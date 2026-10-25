using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntervalTimer;

// These are technical eligibility checks, not a determination of copyright or
// of whether a site's signature/challenge mechanism is a statutory TPM.
internal static class OnlineAudioPolicy
{
    public const int Revision = 1;
    // Direct files only: do not pass encrypted HLS/DASH manifests to a downloader.
    public const string FormatSelector = "bestaudio[protocol=https]/bestaudio[protocol=http]/best[protocol=https]/best[protocol=http]";

    public static readonly string[] Restrictions = [
        "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-cache-dir",
        // netrc is opt-in; ignore-config prevents a user's file from enabling it.
        "--no-cookies", "--no-cookies-from-browser",
        "--proxy", "", "--geo-verification-proxy", "", "--no-geo-bypass",
        "--no-allow-unplayable-formats", "--no-playlist", "--playlist-items", "1",
        "--match-filters", "!is_live & !is_upcoming",
        "--format", FormatSelector
    ];

    public static bool IsPublicFileUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

    // Return a single approved stream. The download consumes this exact snapshot
    // instead of extracting the page again and potentially choosing another file.
    public static string ValidateAndPin(string json)
    {
        JsonObject info;
        try
        {
            info = JsonNode.Parse(json) as JsonObject
                ?? throw new IOException("링크의 오디오 정보를 확인할 수 없습니다.");
        }
        catch (JsonException)
        {
            throw new IOException("공개된 단일 콘텐츠의 오디오 정보를 확인할 수 없습니다. 접근 제한과 링크를 확인하세요.");
        }
        string? Text(string name) => info[name]?.GetValueKind() == JsonValueKind.String
            ? info[name]!.GetValue<string>() : null;
        bool Flag(string name) => info[name]?.GetValueKind() == JsonValueKind.True;
        if (info["entries"] is not null || Text("_type") is "playlist" or "multi_video" or "url" or "url_transparent")
            throw new IOException("공개된 단일 영상 또는 오디오 링크만 사용할 수 있습니다.");
        var availability = Text("availability");
        if (availability is not null && availability is not ("public" or "unlisted"))
            throw new IOException("로그인·구독·구매 등 접근 권한이 필요한 콘텐츠는 지원하지 않습니다.");
        if (info["age_limit"] is JsonValue age && age.TryGetValue<double>(out var ageLimit) && ageLimit > 0)
            throw new IOException("연령 제한 콘텐츠는 지원하지 않습니다.");
        if (Flag("is_live") || Flag("is_upcoming") || Text("live_status") is "is_live" or "is_upcoming" or "post_live")
            throw new IOException("실시간 방송이나 아직 준비되지 않은 콘텐츠는 지원하지 않습니다.");
        // Unknown metadata does not establish permission. Explicit DRM indicators,
        // including the extractor's 'maybe' result, are rejected conservatively.
        if (info["has_drm"] is { } drm && drm.GetValueKind() is not (JsonValueKind.False or JsonValueKind.Null))
            throw new IOException("DRM 보호가 감지되거나 의심되는 오디오는 다운로드하지 않습니다.");
        if (info["requested_formats"] is JsonArray { Count: > 0 }
            || info["requested_downloads"] is JsonArray { Count: > 1 }
            || info["fragments"] is JsonArray { Count: > 0 }
            || info["hls_aes"] is not null
            || Text("protocol") is not ("http" or "https")
            || !IsPublicFileUrl(Text("url") ?? ""))
            throw new IOException("보호된 스트림 처리를 피하기 위해 직접 받을 수 있는 HTTP/HTTPS 오디오만 지원합니다.");
        if (Text("acodec") == "none")
            throw new IOException("링크에서 재생할 오디오를 찾을 수 없습니다.");

        // The selected root fields include URL, headers, codec, and format ID.
        // Remove alternative formats so a subsequent selection cannot bypass checks.
        foreach (var key in new[] { "formats", "requested_downloads", "requested_formats", "__postprocessors" })
            info.Remove(key);
        return info.ToJsonString();
    }
}
