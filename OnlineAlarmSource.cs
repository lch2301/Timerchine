using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntervalTimer;

internal readonly record struct PreparedOnlineAudio(string Path, double DurationSeconds, string Title = "");
internal readonly record struct OnlineMediaDetails(string Title, string ThumbnailUrl);
internal readonly record struct OnlineDownloadProgress(double? Percent, string Status,
    long DownloadedBytes = 0, long? TotalBytes = null, bool Estimated = false);

internal static class OnlineAlarmSource
{
    private static readonly string ExtractorHash = EmbeddedMediaIntegrity.GetHash("yt-dlp.exe");
    private static readonly string RuntimeHash = EmbeddedMediaIntegrity.GetHash("deno.exe");
    private static readonly Lazy<string> Extractor = new(() => AudioDecoder.ExtractExecutable("yt-dlp.exe", ExtractorHash));
    private static readonly Lazy<string> Runtime = new(() => AudioDecoder.ExtractExecutable("deno.exe", RuntimeHash));
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PreparationLocks = new();
    private static readonly ConcurrentDictionary<string, OnlineMediaDetails> Details = new(StringComparer.Ordinal);
    private const string ProgressMarker = "__TIMERCHINE_PROGRESS__";
    private const string ProgressTemplate = "download:" + ProgressMarker
        + "{\"status\":%(progress.status|null)j,\"downloaded_bytes\":%(progress.downloaded_bytes|null)j,"
        + "\"total_bytes\":%(progress.total_bytes|null)j,\"total_bytes_estimate\":%(progress.total_bytes_estimate|null)j}";
    private sealed record CacheEntry(string FileName, double DurationSeconds,
        int PolicyRevision = 0, DateTimeOffset? PreparedAtUtc = null, string Title = "");

    public static bool IsLink(string value)
        => OnlineAudioPolicy.IsPublicFileUrl(value);

    public static PreparedOnlineAudio? GetPreparedAudio(string url)
        => ExternalLinkPolicy.IsEnabled && IsLink(url) ? TryReadCache(CacheFolder(url)) : null;

    private static string CacheFolder(string url)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntervalTimer", "OnlineAudio", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))));

    public static async Task<string> GetTitleAsync(string url, CancellationToken cancellation)
        => (await GetDetailsAsync(url, cancellation).ConfigureAwait(false)).Title;

    public static async Task<OnlineMediaDetails> GetDetailsAsync(string url, CancellationToken cancellation)
    {
        if (!ExternalLinkPolicy.IsEnabled || !IsLink(url) || !ExternalLinkPolicy.UsageConfirmed) return new("", "");
        if (Details.TryGetValue(url, out var known)) return known;
        var folder = CacheFolder(url);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        var preparation = PreparationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await preparation.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (Details.TryGetValue(url, out known)) return known;
            var detailsFile = Path.Combine(folder, "details.json");
            try
            {
                var stored = JsonSerializer.Deserialize<OnlineMediaDetails>(await File.ReadAllTextAsync(detailsFile, cancellation).ConfigureAwait(false));
                if (!string.IsNullOrWhiteSpace(stored.Title))
                {
                    var thumbnailUrl = stored.ThumbnailUrl ?? "";
                    Details[url] = new(CleanTitle(stored.Title), IsLink(thumbnailUrl) ? thumbnailUrl : "");
                    return Details[url];
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            var cached = ReadCacheEntry(folder);
            Directory.CreateDirectory(folder);
            var inspected = await InspectAsync(folder, url, cancellation).ConfigureAwait(false);
            _ = OnlineAudioPolicy.ValidateAndPin(inspected);
            var details = ReadDetails(inspected);
            // Older downloaded audio remains usable; only enrich its metadata.
            if (cached is not null && details.Title.Length > 0)
            {
                var metadata = Path.Combine(folder, "source.json");
                var temporary = metadata + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(cached with { Title = details.Title }), cancellation).ConfigureAwait(false);
                    File.Move(temporary, metadata, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            await StoreDetailsAsync(folder, details, cancellation).ConfigureAwait(false);
            Details[url] = details;
            return details;
        }
        finally { preparation.Release(); }
    }

    private static async Task<string> InspectAsync(string folder, string url, CancellationToken cancellation)
    {
        var inspection = CreateStart(folder);
        foreach (var argument in new[] { "--dump-single-json", "--simulate", "--", url })
            inspection.ArgumentList.Add(argument);
        return await RunExtractorAsync(inspection, null, cancellation, metadata: true).ConfigureAwait(false);
    }

    private static string CleanTitle(string? value)
    {
        var title = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length <= 500 ? title : title[..500];
    }

    private static string ReadTitle(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
            ? CleanTitle(title.GetString()) : "";
    }

    private static OnlineMediaDetails ReadDetails(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var thumbnail = root.TryGetProperty("thumbnail", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
        if (!IsLink(thumbnail) && root.TryGetProperty("thumbnails", out var images) && images.ValueKind == JsonValueKind.Array)
            foreach (var image in images.EnumerateArray().Reverse())
                if (image.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && IsLink(url.GetString() ?? ""))
                { thumbnail = url.GetString()!; break; }
        return new(ReadTitle(json), IsLink(thumbnail) ? thumbnail : "");
    }

    private static async Task StoreDetailsAsync(string folder, OnlineMediaDetails details, CancellationToken cancellation)
    {
        var file = Path.Combine(folder, "details.json");
        var temporary = file + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(details), cancellation).ConfigureAwait(false);
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<PreparedOnlineAudio> PrepareAsync(string url, CancellationToken cancellation,
        IProgress<OnlineDownloadProgress>? progress = null)
    {
        if (!ExternalLinkPolicy.IsEnabled)
            throw new NotSupportedException("이 버전에서는 외부 링크 기능을 지원하지 않습니다.");
        if (!IsLink(url)) throw new ArgumentException("http 또는 https로 시작하는 링크를 입력하세요.");
        if (!ExternalLinkPolicy.UsageConfirmed)
            throw new InvalidOperationException(ExternalLinkPolicy.ConfirmationRequiredMessage);
        cancellation.ThrowIfCancellationRequested();
        progress?.Report(new(null, "다운로드 준비 중"));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        var folder = CacheFolder(url);
        var preparation = PreparationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await preparation.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (TryReadCache(folder) is { } cached)
            {
                progress?.Report(new(100, "다운로드 완료"));
                return cached;
            }
            Directory.CreateDirectory(folder);
            var token = cancellation;
            progress?.Report(new(null, "접근 제한·오디오 형식 확인 중"));
            var inspected = await InspectAsync(folder, url, token).ConfigureAwait(false);
            var pinned = OnlineAudioPolicy.ValidateAndPin(inspected);
            var title = ReadTitle(inspected);
            var details = ReadDetails(inspected);
            await StoreDetailsAsync(folder, details, token).ConfigureAwait(false);
            Details[url] = details;
            // This transient file contains signed URLs. Delete it on every normal
            // completion, failure, and cancellation; do not include it in notices/logs.
            var snapshot = Path.Combine(folder, $".source-{Guid.NewGuid():N}.json");
            string downloaded;
            try
            {
                await File.WriteAllTextAsync(snapshot, pinned, token).ConfigureAwait(false);
                var start = CreateStart(folder);
                foreach (var argument in new[] {
                    "--progress", "--newline", "--progress-delta", "0.2", "--progress-template", ProgressTemplate,
                    "--paths", folder, "--output", "audio.%(ext)s", "--print", "after_move:filepath",
                    "--no-simulate", "--load-info-json", snapshot
                }) start.ArgumentList.Add(argument);
                downloaded = await RunExtractorAsync(start, progress, token).ConfigureAwait(false);
            }
            finally { if (File.Exists(snapshot)) File.Delete(snapshot); }
            var reported = downloaded.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?.Trim();
            var path = string.IsNullOrEmpty(reported) ? "" : Path.GetFullPath(reported, folder);
            if (string.IsNullOrEmpty(path) || !string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(path).StartsWith("audio.", StringComparison.Ordinal) || !File.Exists(path))
                throw new IOException("링크에서 재생할 오디오를 찾을 수 없습니다. 공개된 단일 영상 또는 오디오 링크를 확인하세요.");
            progress?.Report(new(100, "오디오 확인 중"));
            // Validate the actual decoded sound before accepting a link as an alarm.
            using (var source = new DecodedAlarmFile(path, token))
            {
                var samples = new byte[4096];
                if (source.Read(samples, 0, samples.Length) == 0) throw new IOException("링크에 재생할 오디오가 없습니다.");
            }
            var duration = await AudioDecoder.ProbeDurationAsync(path, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var metadata = Path.Combine(folder, "source.json");
            var temporary = metadata + ".tmp";
            await File.WriteAllTextAsync(temporary,
                JsonSerializer.Serialize(new CacheEntry(Path.GetFileName(path), duration,
                    OnlineAudioPolicy.Revision, DateTimeOffset.UtcNow, title)), token).ConfigureAwait(false);
            File.Move(temporary, metadata, overwrite: true);
            progress?.Report(new(100, "다운로드 완료"));
            return new(path, duration, title);
        }
        finally { preparation.Release(); }
    }

    private static ProcessStartInfo CreateStart(string folder)
    {
        var start = new ProcessStartInfo(Extractor.Value) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = folder
        };
        foreach (var argument in OnlineAudioPolicy.Restrictions) start.ArgumentList.Add(argument);
        foreach (var argument in new[] {
            "--no-warnings", "--no-colors", "--encoding", "utf-8", "--socket-timeout", "15",
            "--retries", "2", "--fragment-retries", "2", "--js-runtimes", "deno:" + Runtime.Value,
            "--ffmpeg-location", AudioDecoder.ExecutablePath
        }) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task<string> RunExtractorAsync(ProcessStartInfo start,
        IProgress<OnlineDownloadProgress>? progress, CancellationToken cancellation, bool metadata = false)
    {
        cancellation.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("외부 링크 오디오를 준비할 수 없습니다.");
        using var registration = cancellation.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        var output = ReadOutputAsync(process.StandardOutput, progress, cancellation, metadata);
        var errors = ReadOutputAsync(process.StandardError, progress, cancellation);
        await Task.WhenAll(output, errors, process.WaitForExitAsync(cancellation)).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException(FailureMessage(await errors.ConfigureAwait(false)));
        return await output.ConfigureAwait(false);
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, IProgress<OnlineDownloadProgress>? progress,
        CancellationToken cancellation, bool metadata = false)
    {
        var output = new StringBuilder();
        var overflow = false;
        while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
        {
            var marker = line.IndexOf(ProgressMarker, StringComparison.Ordinal);
            if (!metadata && marker >= 0)
            {
                try
                {
                    using var document = JsonDocument.Parse(line[(marker + ProgressMarker.Length)..]);
                    var data = document.RootElement;
                    static long ReadBytes(JsonElement data, string key)
                    {
                        if (!data.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number) return 0;
                        if (value.TryGetInt64(out var number)) return Math.Max(0, number);
                        return value.TryGetDouble(out var estimate) && double.IsFinite(estimate) && estimate > 0 && estimate < long.MaxValue
                            ? (long)estimate : 0;
                    }
                    var downloaded = ReadBytes(data, "downloaded_bytes");
                    var total = ReadBytes(data, "total_bytes");
                    var estimated = total <= 0;
                    if (estimated) total = ReadBytes(data, "total_bytes_estimate");
                    var finished = data.TryGetProperty("status", out var status) && status.GetString() == "finished";
                    double? percent = finished ? 100 : total > 0 ? Math.Clamp(downloaded * 100d / total, 0, 100) : null;
                    progress?.Report(new(percent, finished ? "오디오 확인 중" : "다운로드 중",
                        downloaded, total > 0 ? total : null, estimated && !finished));
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
                continue;
            }
            var limit = metadata ? 16 * 1024 * 1024 : 64 * 1024;
            if (metadata && (overflow || output.Length + (long)line.Length > limit)) overflow = true;
            else output.AppendLine(line);
            if (!metadata && output.Length > limit) output.Remove(0, output.Length - limit);
        }
        if (overflow) throw new IOException("링크의 오디오 정보가 너무 복잡해 확인할 수 없습니다. 단일 콘텐츠 링크를 입력하세요.");
        return output.ToString();
    }

    private static CacheEntry? ReadCacheEntry(string folder)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(Path.Combine(folder, "source.json")));
            return entry is { PolicyRevision: OnlineAudioPolicy.Revision }
                ? entry : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }

    private static PreparedOnlineAudio? TryReadCache(string folder)
    {
        try
        {
            var entry = ReadCacheEntry(folder);
            if (entry is null || string.IsNullOrEmpty(entry.FileName) || Path.GetFileName(entry.FileName) != entry.FileName
                || !entry.FileName.StartsWith("audio.", StringComparison.Ordinal)) return null;
            var path = Path.Combine(folder, entry.FileName);
            return new FileInfo(path) is { Exists: true, Length: > 0 }
                ? new(path, double.IsFinite(entry.DurationSeconds) ? Math.Max(0, entry.DurationSeconds) : 0, CleanTitle(entry.Title)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }

    private static string FailureMessage(string details)
    {
        if (details.Contains("DRM", StringComparison.OrdinalIgnoreCase))
            return "DRM 보호가 감지된 콘텐츠는 지원하지 않습니다. 보호를 해제하거나 우회하지 않습니다.";
        if (details.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
            return "직접 다운로드할 수 있는 오디오를 찾지 못했습니다. DRM·암호화 스트림과 접근 제한 콘텐츠는 지원하지 않습니다.";
        if (details.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase))
            return "지원하지 않는 링크입니다. 영상이나 오디오가 있는 페이지의 주소를 입력하세요.";
        return "링크 오디오를 준비하지 못했습니다. 인터넷 연결과 링크를 확인하세요.\n로그인·연령·지역 제한이 있는 콘텐츠와 실시간 방송은 지원하지 않습니다.";
    }
}
