using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NAudio.Wave;

namespace IntervalTimer;

internal static class AudioDecoder
{
    private static readonly string BinaryHash = EmbeddedMediaIntegrity.GetHash("ffmpeg.exe");
    private static readonly string ProbeHash = EmbeddedMediaIntegrity.GetHash("ffprobe.exe");
    private static readonly Lazy<string> Executable = new(() => ExtractExecutable("ffmpeg.exe", BinaryHash));
    private static readonly Lazy<string> ProbeExecutable = new(() => ExtractExecutable("ffprobe.exe", ProbeHash));
    public static string ExecutablePath => Executable.Value;
    public static string LicenseText
    {
        get
        {
            var assembly = typeof(AudioDecoder).Assembly;
            var notices = assembly.GetManifestResourceNames()
                .Where(n => n.Contains(".Media.", StringComparison.Ordinal) && n.EndsWith(".txt", StringComparison.Ordinal))
                .OrderBy(n => n.EndsWith("NOTICE.txt", StringComparison.Ordinal) ? 0 : 1)
                .Select(n => {
                    using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                    return reader.ReadToEnd();
                });
            return string.Join("\n\n────────────────────────────\n\n", notices);
        }
    }

    public static async Task<double> ProbeDurationAsync(string path, CancellationToken cancellation)
    {
        try
        {
            if (OnlineAlarmSource.IsLink(path))
            {
                var preparedAudio = await OnlineAlarmSource.PrepareAsync(path, cancellation).ConfigureAwait(false);
                if (preparedAudio.DurationSeconds > 0) return preparedAudio.DurationSeconds;
                path = preparedAudio.Path;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var token = timeout.Token;
            token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(ProbeExecutable.Value) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] {
                "-v", "error", "-protocol_whitelist", "file,pipe,crypto,data", "-select_streams", "a:0",
                "-show_entries", "stream=duration:stream_tags=DURATION:format=duration", "-of", "json", "-i", Path.GetFullPath(path)
            }) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            token.ThrowIfCancellationRequested();
            if (!process.Start()) return 0;
            using var registration = token.Register(() => {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            });
            var json = process.StandardOutput.ReadToEndAsync(token);
            var errors = process.StandardError.ReadToEndAsync(token);
            await Task.WhenAll(json, errors, process.WaitForExitAsync(token)).ConfigureAwait(false);
            if (process.ExitCode != 0) return 0;
            using var document = JsonDocument.Parse(await json.ConfigureAwait(false));
            var root = document.RootElement;
            if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0) return 0;
            var audio = streams[0];
            if (audio.TryGetProperty("duration", out var length) && ParseSeconds(length) is > 0 and var seconds) return seconds;
            if (audio.TryGetProperty("tags", out var tags) && tags.TryGetProperty("DURATION", out var tag)
                && TimeSpan.TryParse(tag.GetString(), CultureInfo.InvariantCulture, out var span) && span.TotalSeconds > 0)
                return span.TotalSeconds;
            return root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out length)
                ? ParseSeconds(length) : 0;
        }
        catch { return 0; } // Missing metadata never prevents playback.
    }

    private static double ParseSeconds(JsonElement value)
        => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds > 0 ? seconds : 0;

    internal static string ExtractExecutable(string filename, string expectedHash)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntervalTimer", "Media", BinaryHash[..16]);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, filename);
        if (IsOriginal(target, expectedHash)) return target;
        var temporary = Path.Combine(folder, $"{filename}-{Guid.NewGuid():N}.tmp");
        try
        {
            var assembly = typeof(AudioDecoder).Assembly;
            var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Media." + filename, StringComparison.Ordinal));
            using (var source = assembly.GetManifestResourceStream(name)!)
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(true);
            }
            if (!IsOriginal(temporary, expectedHash)) throw new InvalidDataException("내장 오디오 디코더를 불러올 수 없습니다.");
            File.Move(temporary, target, overwrite: true);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool IsOriginal(string path, string expectedHash)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)) == expectedHash;
    }
}

// FFmpeg detects the container and codec from the content. Only decoded PCM goes to
// NAudio: no Windows codec, file extension, video window, or full-file conversion is needed.
internal sealed class DecodedAlarmFile : WaveStream
{
    private readonly string path;
    private readonly CancellationToken cancellation;
    private readonly WaveFormat format = new(48_000, 16, 2);
    private readonly object processGate = new();
    private readonly CancellationTokenRegistration cancellationRegistration;
    private Process? decoder;
    private Stream? pcm;
    private long position;
    private bool disposed;

    public DecodedAlarmFile(string path, CancellationToken cancellation = default)
    {
        this.path = Path.GetFullPath(path);
        this.cancellation = cancellation;
        if (!File.Exists(this.path)) throw new FileNotFoundException("알람 파일을 찾을 수 없습니다.", this.path);
        cancellationRegistration = cancellation.Register(TerminateDecoder);
        try { OpenDecoder(); }
        catch { Dispose(); throw; }
    }

    public override WaveFormat WaveFormat => format;
    // Length is intentionally unknown. Looping uses actual EOF and our decoded byte count.
    public override long Length => 0;
    public override long Position
    {
        get => position;
        set
        {
            if (value != 0) throw new NotSupportedException("Alarm audio can only restart from the beginning.");
            CloseDecoder();
            OpenDecoder();
            position = 0;
        }
    }

    private void OpenDecoder()
    {
        cancellation.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(AudioDecoder.ExecutablePath) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe,crypto,data",
            "-i", path, "-map", "0:a:0", "-vn", "-sn", "-dn", "-threads", "1",
            "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-f", "s16le", "pipe:1"
        }) start.ArgumentList.Add(argument);
        var process = new Process { StartInfo = start };
        lock (processGate)
        {
            cancellation.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            try
            {
                if (!process.Start()) throw new IOException("오디오 디코더를 시작할 수 없습니다.");
                decoder = process;
                pcm = process.StandardOutput.BaseStream;
                // Drain stderr so damaged input cannot fill a pipe and stall audio decoding.
                _ = DrainErrorsAsync(process.StandardError);
            }
            catch { process.Dispose(); throw; }
        }
    }

    private static async Task DrainErrorsAsync(StreamReader errors)
    {
        var buffer = new char[1024];
        try { while (await errors.ReadAsync(buffer).ConfigureAwait(false) > 0) { } }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        cancellation.ThrowIfCancellationRequested();
        var source = pcm ?? throw new ObjectDisposedException(nameof(DecodedAlarmFile));
        var total = 0;
        // Pipe reads can split a PCM frame. Return complete frames to NAudio's converters.
        while (total < count)
        {
            int read;
            try
            {
                read = source.ReadAsync(buffer.AsMemory(offset + total, count - total), cancellation).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(15), cancellation).GetAwaiter().GetResult();
            }
            catch { TerminateDecoder(); throw; }
            if (read == 0) break;
            total += read;
        }
        position += total;
        if (count > 0 && position == 0)
            throw new InvalidDataException("파일에서 재생할 오디오를 찾을 수 없습니다.");
        if (total % format.BlockAlign != 0) throw new InvalidDataException("불완전한 오디오 데이터입니다.");
        return total;
    }

    private void TerminateDecoder()
    {
        lock (processGate)
        {
            try { if (decoder is { HasExited: false }) decoder.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    private void CloseDecoder()
    {
        lock (processGate)
        {
            TerminateDecoder();
            pcm?.Dispose();
            pcm = null;
            decoder?.Dispose();
            decoder = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (processGate) disposed = true;
            cancellationRegistration.Dispose();
            CloseDecoder();
        }
        base.Dispose(disposing);
    }
}

internal sealed class PrefixedWaveStream(WaveStream source, byte[] prefix, int count) : WaveStream
{
    private int prefixOffset;
    private int prefixCount = count;
    private long position;
    public override WaveFormat WaveFormat => source.WaveFormat;
    public override long Length => source.Length;
    public override long Position
    {
        get => position;
        set
        {
            source.Position = value;
            prefixOffset = prefixCount = 0;
            position = value;
        }
    }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var buffered = Math.Min(count, prefixCount - prefixOffset);
        if (buffered > 0) Buffer.BlockCopy(prefix, prefixOffset, buffer, offset, buffered);
        prefixOffset += buffered;
        var total = buffered;
        if (total < count) total += source.Read(buffer, offset + total, count - total);
        position += total;
        return total;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}
