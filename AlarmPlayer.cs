using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace IntervalTimer;

internal enum AlarmPlaybackStatus { Stopped, Preparing, Playing, Fallback, DeviceError, Paused }

internal readonly record struct AlarmAudioProgress(double PositionSeconds, double DurationSeconds)
{
    public bool HasDuration => double.IsFinite(DurationSeconds) && DurationSeconds > 0;
    public double Fraction => HasDuration ? Math.Clamp(PositionSeconds / DurationSeconds, 0, 1) : 0;
    public double RemainingSeconds => HasDuration ? Math.Max(0, DurationSeconds - PositionSeconds) : 0;
}

internal interface IAlarmPlayer : IDisposable
{
    bool Start(string path, int volume);
    bool Start(TimerSettings settings) => Start(settings.AlarmPath, settings.Volume);
    void SetVolume(int volume);
    void Stop();
    void Pause() => Stop();
    bool Resume(string path, int volume) => Start(path, volume);
    bool Resume(TimerSettings settings) => Resume(settings.AlarmPath, settings.Volume);
    AlarmPlaybackStatus PlaybackStatus => AlarmPlaybackStatus.Playing;
    AlarmAudioProgress AudioProgress => default;
}

internal sealed class AlarmPlayer : IAlarmPlayer
{
    private readonly object gate = new();
    private PlaybackRun? current;
    private int status;
    public AlarmPlaybackStatus PlaybackStatus => (AlarmPlaybackStatus)Volatile.Read(ref status);
    public AlarmAudioProgress AudioProgress
    {
        get
        {
            PlaybackRun? run;
            lock (gate) run = current;
            // Never hold the owner lock while reading the output device.
            return run?.GetAudioProgress() ?? default;
        }
    }

    public bool Start(string path, int volume)
        => StartSource(path, volume, null);

    public bool Start(TimerSettings settings)
        => settings.UsePlaylist ? StartSource(PlaylistAudio.Identity(settings), settings.Volume,
            new PlaylistAudio(settings.AlarmPlaylist, settings.ShufflePlaylist)) : Start(settings.AlarmPath, settings.Volume);

    private bool StartSource(string path, int volume, PlaylistAudio? playlist)
    {
        Stop();
        var run = new PlaybackRun(this, volume, path, playlist);
        lock (gate)
        {
            current = run;
            Volatile.Write(ref status, (int)AlarmPlaybackStatus.Preparing);
            run.Begin(path);
        }
        return playlist is null && !string.IsNullOrWhiteSpace(path) && !OnlineAlarmSource.IsLink(path) && !File.Exists(path);
    }

    public void Pause()
    {
        PlaybackRun? run;
        lock (gate) run = current;
        try { run?.Pause(); }
        catch { Stop(); } // A failed audio device must still fall silent.
    }

    public bool Resume(string path, int volume)
        => ResumeSource(path, volume, null);

    public bool Resume(TimerSettings settings)
        => settings.UsePlaylist ? ResumeSource(PlaylistAudio.Identity(settings), settings.Volume,
            new PlaylistAudio(settings.AlarmPlaylist, settings.ShufflePlaylist)) : Resume(settings.AlarmPath, settings.Volume);

    private bool ResumeSource(string path, int volume, PlaylistAudio? playlist)
    {
        PlaybackRun? run;
        lock (gate) run = current;
        if (run is not null && string.Equals(run.SourcePath, path, StringComparison.OrdinalIgnoreCase)
            && run.TryResume(Math.Clamp(volume, 0, 100)))
            return run.ReadyStatus == AlarmPlaybackStatus.Fallback;
        return StartSource(path, volume, playlist);
    }

    // Runs on a worker. Preserve the first samples rather than seeking compressed
    // input, which is unreliable for containers with missing duration metadata.
    internal static WaveStream OpenSource(string path, out bool usedFallback, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        usedFallback = false;
        if (string.IsNullOrWhiteSpace(path)) return DefaultMelody.Open();
        WaveStream? source = null;
        try
        {
            if (OnlineAlarmSource.IsLink(path))
                path = OnlineAlarmSource.PrepareAsync(path, cancellation).GetAwaiter().GetResult().Path;
            source = new DecodedAlarmFile(path, cancellation);
            var probe = new byte[4096];
            var read = source.Read(probe, 0, probe.Length);
            if (read == 0) throw new InvalidDataException("파일에서 재생할 오디오를 찾을 수 없습니다.");
            return new PrefixedWaveStream(source, probe, read);
        }
        catch (OperationCanceledException) { source?.Dispose(); throw; }
        catch
        {
            source?.Dispose();
            cancellation.ThrowIfCancellationRequested();
            usedFallback = true;
            return DefaultMelody.Open();
        }
    }

    private void UpdateStatus(PlaybackRun run, AlarmPlaybackStatus next)
    {
        lock (gate)
        {
            if (!ReferenceEquals(current, run)) return;
            if (next == AlarmPlaybackStatus.DeviceError) run.MarkFailed();
            // Opening, pausing, and resuming can finish on different threads.
            // Publish the current state, rather than an older caller's snapshot.
            next = run.HasFailed ? AlarmPlaybackStatus.DeviceError
                : run.IsPaused ? AlarmPlaybackStatus.Paused
                : run.IsReady ? run.ReadyStatus : AlarmPlaybackStatus.Preparing;
            Volatile.Write(ref status, (int)next);
        }
    }

    public void SetVolume(int volume)
    {
        lock (gate) current?.SetVolume(Math.Clamp(volume, 0, 100));
    }

    public void Stop()
    {
        PlaybackRun? previous;
        lock (gate)
        {
            previous = current;
            current = null;
            Volatile.Write(ref status, (int)AlarmPlaybackStatus.Stopped);
        }
        previous?.Dispose();
    }

    public void Dispose() => Stop();

    private sealed class PlaybackRun(AlarmPlayer owner, int volume, string path, PlaylistAudio? playlist) : IDisposable
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource cancellation = new();
        private Task? preparation;
        private WaveOutEvent? output;
        private LoopingWaveStream? loop;
        private PlaylistWaveStream? playlistStream;
        private VolumeSampleProvider? gain;
        private int currentVolume = Math.Clamp(volume, 0, 100);
        private int disposed;
        private int usingFallback;
        private int paused;
        private int failed;
        private int ready;
        private int readyStatus = (int)AlarmPlaybackStatus.Playing;
        private double durationHint;
        private long lastDevicePosition;
        private long devicePositionWrap;
        public string SourcePath { get; } = path;
        public bool IsPaused => Volatile.Read(ref paused) != 0;
        public bool IsReady => Volatile.Read(ref ready) != 0;
        public bool HasFailed => Volatile.Read(ref failed) != 0;
        public AlarmPlaybackStatus ReadyStatus => (AlarmPlaybackStatus)Volatile.Read(ref readyStatus);
        public void MarkFailed() => Interlocked.Exchange(ref failed, 1);

        public void Begin(string path)
        {
            lock (gate)
            {
                if (Volatile.Read(ref disposed) != 0) return;
                var requestToken = cancellation.Token;
                preparation = Task.Run(async () => {
                    // Metadata and decoding run independently. Opening a large file
                    // must not hold up the UI or delay the first audible sample.
                    var durationTask = playlist is not null || string.IsNullOrWhiteSpace(path) ? Task.FromResult(0d)
                        : Task.Run(() => AudioDecoder.ProbeDurationAsync(path, requestToken));
                    try
                    {
                        bool fallback;
                        WaveStream source;
                        if (playlist is null) source = OpenSource(path, out fallback, requestToken);
                        else
                        {
                            try { source = new PlaylistWaveStream(playlist, requestToken); fallback = false; }
                            catch (OperationCanceledException) { throw; }
                            catch { source = DefaultMelody.Open(); fallback = true; }
                        }
                        if (fallback || (playlist is null && string.IsNullOrWhiteSpace(path))) Interlocked.Exchange(ref usingFallback, 1);
                        StartOutput(source, fallback ? AlarmPlaybackStatus.Fallback : AlarmPlaybackStatus.Playing);
                    }
                    catch (OperationCanceledException) { }
                    catch
                    {
                        ReleaseOutput();
                        if (!requestToken.IsCancellationRequested) owner.UpdateStatus(this, AlarmPlaybackStatus.DeviceError);
                    }
                    finally { Volatile.Write(ref durationHint, await durationTask.ConfigureAwait(false)); }
                });
            }
        }

        private void StartOutput(WaveStream source, AlarmPlaybackStatus next)
        {
            LoopingWaveStream? candidateLoop = null;
            WaveOutEvent? candidateOutput = null;
            try
            {
                candidateLoop = new LoopingWaveStream(source);
                candidateOutput = new WaveOutEvent();
                lock (gate)
                {
                    if (Volatile.Read(ref disposed) != 0) return;
                    var provider = new VolumeSampleProvider(candidateLoop.ToSampleProvider()) { Volume = currentVolume / 100f };
                    candidateOutput.Init(provider);
                    candidateOutput.PlaybackStopped += (_, e) => {
                        if (e.Exception is not null && Volatile.Read(ref disposed) == 0)
                            _ = Task.Run(RecoverPlayback);
                    };
                    output = candidateOutput;
                    loop = candidateLoop;
                    playlistStream = source as PlaylistWaveStream;
                    gain = provider;
                    lastDevicePosition = devicePositionWrap = 0;
                    candidateOutput = null;
                    candidateLoop = null;
                    Volatile.Write(ref readyStatus, (int)next);
                    Volatile.Write(ref ready, 1);
                    // An expiry/stop can arrive while the file is still opening.
                    // Retain its decoded beginning without allowing delayed sound.
                    if (!IsPaused) output.Play();
                }
                owner.UpdateStatus(this, next);
            }
            finally
            {
                candidateOutput?.Dispose();
                candidateLoop?.Dispose();
            }
        }

        private void RecoverPlayback()
        {
            if (Volatile.Read(ref disposed) != 0) return;
            if (Interlocked.Exchange(ref usingFallback, 1) != 0)
            {
                owner.UpdateStatus(this, AlarmPlaybackStatus.DeviceError);
                return;
            }
            ReleaseOutput();
            if (Volatile.Read(ref disposed) != 0) return;
            try { StartOutput(DefaultMelody.Open(), AlarmPlaybackStatus.Fallback); }
            catch { ReleaseOutput(); owner.UpdateStatus(this, AlarmPlaybackStatus.DeviceError); }
        }

        public void SetVolume(int value)
        {
            lock (gate)
            {
                currentVolume = value;
                if (gain is not null) gain.Volume = value / 100f;
            }
        }

        public AlarmAudioProgress GetAudioProgress()
        {
            lock (gate)
            {
                var duration = loop?.DurationSeconds ?? 0;
                if (duration <= 0) duration = Volatile.Read(ref durationHint);
                if (output is null || (duration <= 0 && playlistStream is null)) return new(0, duration);
                try
                {
                    // Device position is what has actually been heard. Stream.Position
                    // includes decoded samples that are still waiting in output buffers.
                    var position = output.GetPosition();
                    if (position < lastDevicePosition) devicePositionWrap += 1L << 32;
                    lastDevicePosition = position;
                    var seconds = (position + devicePositionWrap) / (double)output.OutputWaveFormat.AverageBytesPerSecond;
                    return playlistStream is not null ? playlistStream.GetAudibleProgress(seconds) : new(seconds % duration, duration);
                }
                catch { return new(0, duration); }
            }
        }

        public void Pause()
        {
            lock (gate)
            {
                if (Volatile.Read(ref disposed) != 0) return;
                Volatile.Write(ref paused, 1);
                // Keep the output queue and decoder alive. The device resumes at
                // the audible position, including samples already buffered ahead.
                output?.Pause();
            }
            owner.UpdateStatus(this, AlarmPlaybackStatus.Paused);
        }

        public bool TryResume(int value)
        {
            AlarmPlaybackStatus next;
            try
            {
                lock (gate)
                {
                    if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref failed) != 0) return false;
                    currentVolume = value;
                    if (gain is not null) gain.Volume = value / 100f;
                    Volatile.Write(ref paused, 0);
                    output?.Play();
                    next = output is null ? AlarmPlaybackStatus.Preparing : ReadyStatus;
                }
            }
            catch
            {
                owner.UpdateStatus(this, AlarmPlaybackStatus.DeviceError);
                return false;
            }
            owner.UpdateStatus(this, next);
            return true;
        }

        private void ReleaseOutput()
        {
            WaveOutEvent? previousOutput;
            LoopingWaveStream? previousLoop;
            lock (gate)
            {
                previousOutput = output; output = null;
                Volatile.Write(ref ready, 0);
                previousLoop = loop; loop = null;
                playlistStream = null;
                gain = null;
            }
            try { previousOutput?.Stop(); } catch { }
            try { previousOutput?.Dispose(); } catch { }
            try { previousLoop?.Dispose(); } catch { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            // Cancel pending pipe reads before waiting for the audio device to stop.
            cancellation.Cancel();
            ReleaseOutput();
            Task? pending;
            lock (gate) pending = preparation;
            if (pending is null) cancellation.Dispose();
            else _ = pending.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        }
    }
}

internal sealed class LoopingWaveStream(WaveStream source) : WaveStream
{
    private long decodedLength;
    public double DurationSeconds => (source.Length > 0 ? source.Length : Interlocked.Read(ref decodedLength))
        / (double)WaveFormat.AverageBytesPerSecond;
    public override WaveFormat WaveFormat => source.WaveFormat;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = source.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (source.Position == 0) break;
                Interlocked.CompareExchange(ref decodedLength, source.Position, 0);
                source.Position = 0;
            }
            total += read;
        }
        return total;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}

internal static class DefaultMelody
{
    // Original eight-note bell melody, synthesized once from data bundled in the EXE.
    private static readonly byte[] WaveData = Build();
    public static double DurationSeconds => (WaveData.Length - 44) / (44100d * 2);
    public static WaveFileReader Open() => new(new MemoryStream(WaveData, writable: false));

    private static byte[] Build()
    {
        const int sampleRate = 44100;
        var notes = new[] {
            (523.251, .24), (659.255, .24), (783.991, .28), (1046.502, .42),
            (783.991, .24), (659.255, .24), (587.330, .28), (523.251, .50)
        };
        var samples = new List<short>();
        foreach (var (frequency, duration) in notes)
        {
            for (var i = 0; i < (int)(duration * sampleRate); i++)
            {
                var time = i / (double)sampleRate;
                var attack = Math.Min(1, time / .012);
                var release = Math.Min(1, (duration - time) / .045);
                var envelope = attack * release * Math.Exp(-2.6 * time / duration);
                var phase = 2 * Math.PI * frequency * time;
                var tone = .76 * Math.Sin(phase) + .18 * Math.Sin(2 * phase) + .06 * Math.Sin(3 * phase);
                samples.Add((short)(short.MaxValue * .43 * envelope * tone));
            }
            samples.AddRange(new short[(int)(.055 * sampleRate)]);
        }
        samples.AddRange(new short[(int)(.55 * sampleRate)]);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Count * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
        writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Count * 2);
        foreach (var sample in samples) writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }
}
