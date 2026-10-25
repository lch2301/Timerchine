using System.Collections.Concurrent;
using System.Text.Json;
using NAudio.Wave;

namespace IntervalTimer;

internal sealed class PlaylistAudio(IEnumerable<AlarmPlaylistItem> items, bool shuffle)
{
    public AlarmPlaylistItem[] Items { get; } = items.Select(item => item with { }).ToArray();
    public bool Shuffle { get; } = shuffle;
    public static string Identity(TimerSettings settings) => "playlist:" + JsonSerializer.Serialize(
        new { settings.ShufflePlaylist, Items = settings.AlarmPlaylist.Select(item => new { item.Path, item.ExternalLink }) });
}

// Every file is decoded to the same PCM format. Track changes happen at actual
// EOF, including files with absent/incorrect duration metadata.
internal sealed class PlaylistWaveStream : WaveStream
{
    private readonly PlaylistAudio playlist;
    private readonly CancellationToken cancellation;
    private readonly WaveFormat format = new(48_000, 16, 2);
    private readonly ConcurrentQueue<Segment> segments = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<double>>> durations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<int> order = new();
    private WaveStream? source;
    private Segment? current;
    private int lastIndex = -1;
    private long position;
    private long trackBytes;
    private bool disposed;

    private sealed class Segment(double start)
    {
        public double Start { get; } = start;
        public double Duration;
    }

    public PlaylistWaveStream(PlaylistAudio playlist, CancellationToken cancellation)
    {
        this.playlist = playlist;
        this.cancellation = cancellation;
        OpenNext();
    }

    public override WaveFormat WaveFormat => format;
    public override long Length => 0;
    public override long Position { get => position; set => throw new NotSupportedException(); }

    private int NextIndex()
    {
        if (order.Count == 0)
        {
            var indices = Enumerable.Range(0, playlist.Items.Length).ToArray();
            if (playlist.Shuffle)
            {
                Random.Shared.Shuffle(indices);
                if (indices.Length > 1 && indices[0] == lastIndex)
                    (indices[0], indices[1]) = (indices[1], indices[0]);
            }
            foreach (var index in indices) order.Enqueue(index);
        }
        return order.Dequeue();
    }

    private void OpenNext()
    {
        cancellation.ThrowIfCancellationRequested();
        source?.Dispose();
        source = null;
        if (current is not null) Volatile.Write(ref current.Duration, trackBytes / (double)format.AverageBytesPerSecond);
        for (var attempt = 0; attempt < playlist.Items.Length; attempt++)
        {
            var index = NextIndex();
            var item = playlist.Items[index];
            DecodedAlarmFile? decoded = null;
            try
            {
                var resolvedFile = item.ExternalLink
                    ? OnlineAlarmSource.PrepareAsync(item.Path, cancellation).GetAwaiter().GetResult().Path
                    : item.Path;
                decoded = new DecodedAlarmFile(resolvedFile, cancellation);
                var prefix = new byte[4096];
                var count = decoded.Read(prefix, 0, prefix.Length);
                if (count == 0) throw new InvalidDataException();
                source = new PrefixedWaveStream(decoded, prefix, count);
                decoded = null;
                trackBytes = 0;
                lastIndex = index;
                var segment = new Segment(position / (double)format.AverageBytesPerSecond);
                current = segment;
                segments.Enqueue(segment);
                // Keep history bounded even for normal alarms whose progress is hidden.
                while (segments.Count > 65536) segments.TryDequeue(out _);
                var durationTask = durations.GetOrAdd(item.Path, file => new Lazy<Task<double>>(
                    () => Task.Run(() => AudioDecoder.ProbeDurationAsync(file, cancellation)))).Value;
                _ = durationTask.ContinueWith(completed => {
                    // EOF is authoritative; do not overwrite a completed track.
                    if (completed.Status == TaskStatus.RanToCompletion && completed.Result > 0)
                        Interlocked.CompareExchange(ref segment.Duration, completed.Result, 0);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* A moved/damaged item must not prevent other files from playing. */ }
            finally { decoded?.Dispose(); }
        }
        throw new InvalidDataException("재생 목록에서 재생할 수 있는 오디오 파일을 찾지 못했습니다.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var total = 0;
        while (total < count)
        {
            cancellation.ThrowIfCancellationRequested();
            int read;
            try { read = source!.Read(buffer, offset + total, count - total); }
            catch (OperationCanceledException) { throw; }
            catch { OpenNext(); continue; }
            if (read == 0) { OpenNext(); continue; }
            total += read;
            trackBytes += read;
            position += read;
        }
        return total;
    }

    public AlarmAudioProgress GetAudibleProgress(double seconds)
    {
        Segment? audible = null;
        foreach (var segment in segments)
        {
            if (segment.Start > seconds) break;
            audible = segment;
        }
        if (audible is null) return default;
        while (segments.TryPeek(out var head) && head != audible) segments.TryDequeue(out _);
        var duration = Volatile.Read(ref audible.Duration);
        return new(Math.Clamp(seconds - audible.Start, 0, duration > 0 ? duration : double.MaxValue), duration);
    }

    protected override void Dispose(bool disposing)
    {
        disposed = true;
        if (disposing) source?.Dispose();
        base.Dispose(disposing);
    }
}
