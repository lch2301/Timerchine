using System.Text.Json;

namespace IntervalTimer;

internal static class EmbeddedMediaIntegrity
{
    private static readonly Lazy<Dictionary<string, string>> Hashes = new(() => {
        using var source = typeof(EmbeddedMediaIntegrity).Assembly.GetManifestResourceStream("IntervalTimer.Media.integrity.json")
            ?? throw new InvalidDataException("내장 오디오 도구의 무결성 정보를 찾을 수 없습니다.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(source)
            ?? throw new InvalidDataException("내장 오디오 도구의 무결성 정보를 읽을 수 없습니다.");
    });

    public static string GetHash(string name)
    {
        if (!Hashes.Value.TryGetValue(name, out var hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("내장 오디오 도구의 무결성 정보가 올바르지 않습니다.");
        return hash.ToUpperInvariant();
    }
}
