namespace IntervalTimer;

internal static class ExternalLinkPolicy
{
    private static int usageConfirmed;
    public static bool UsageConfirmed
    {
        get => Volatile.Read(ref usageConfirmed) != 0;
        set => Volatile.Write(ref usageConfirmed, value ? 1 : 0);
    }
    public const string ConfirmationRequiredMessage =
        "앱 설정에서 외부 링크 안내를 확인하고 다운로드 및 개인 알람 이용 권한을 확인해주세요.";
    // A build choice, rather than a user preference: a distribution build can
    // disable both the UI and execution without deleting the implementation.
#if TIMERCHINE_EXTERNAL_LINKS
    public static bool IsEnabled { get; } = true;
#else
    public static bool IsEnabled { get; } = false;
#endif
    public static string DisplayName => IsEnabled ? "외부 링크" : "외부 링크(미지원)";
    public static string UsageNotice
    {
        get
        {
            var assembly = typeof(ExternalLinkPolicy).Assembly;
            var name = assembly.GetManifestResourceNames()
                .Single(n => n.EndsWith(".Legal.ExternalLinkUsage.ko.txt", StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            return reader.ReadToEnd();
        }
    }
}
