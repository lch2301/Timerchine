using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace IntervalTimer;

internal static class TimerTypography
{
    // Keep both GDI registrations and GDI+ font data alive for the process lifetime.
    // Native edit controls/TextRenderer use GDI; the custom readouts use GDI+.
    private static readonly PrivateFontCollection Fonts = new();
    private static readonly PrivateFontCollection TimeFonts = new();
    private static readonly List<IntPtr> FontMemory = [];
    private static readonly List<IntPtr> Registrations = [];
    private static readonly FontFamily RegularFamily;
    private static readonly FontFamily BoldFamily;
    private static readonly FontFamily TimeFamily;

    static TimerTypography()
    {
        foreach (var file in new[] { "NanumSquareRoundR.ttf", "NanumSquareRoundB.ttf" })
        {
            using var resource = OpenResource(file);
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var memory = Marshal.AllocCoTaskMem(bytes.Length);
            FontMemory.Add(memory);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var count = 0u;
            var registration = AddFontMemResourceEx(memory, (uint)bytes.Length, IntPtr.Zero, ref count);
            if (registration == IntPtr.Zero) throw new InvalidOperationException("내장 글꼴을 불러올 수 없습니다.");
            Registrations.Add(registration);
            Fonts.AddMemoryFont(memory, bytes.Length);
        }
        var families = Fonts.Families;
        RegularFamily = families.FirstOrDefault(f => f.Name.Contains("Regular", StringComparison.OrdinalIgnoreCase))
            ?? families.First();
        BoldFamily = families.FirstOrDefault(f => f.Name.Contains("Bold", StringComparison.OrdinalIgnoreCase))
            ?? RegularFamily;
        using var timeResource = OpenResource("NunitoTimer-Bold.ttf");
        using var timeBuffer = new MemoryStream();
        timeResource.CopyTo(timeBuffer);
        var timeBytes = timeBuffer.ToArray();
        var timeMemory = Marshal.AllocCoTaskMem(timeBytes.Length);
        FontMemory.Add(timeMemory);
        Marshal.Copy(timeBytes, 0, timeMemory, timeBytes.Length);
        var timeCount = 0u;
        var timeRegistration = AddFontMemResourceEx(timeMemory, (uint)timeBytes.Length, IntPtr.Zero, ref timeCount);
        if (timeRegistration == IntPtr.Zero) throw new InvalidOperationException("시간 표시 글꼴을 불러올 수 없습니다.");
        Registrations.Add(timeRegistration);
        TimeFonts.AddMemoryFont(timeMemory, timeBytes.Length);
        TimeFamily = TimeFonts.Families.First();
    }

    public static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        var family = style.HasFlag(FontStyle.Bold) ? BoldFamily : RegularFamily;
        var availableStyle = family.IsStyleAvailable(style) ? style : FontStyle.Regular;
        return new Font(family, size, availableStyle, GraphicsUnit.Point);
    }

    public static Font CreateTime(float size) => new(TimeFamily, size,
        TimeFamily.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);

    public static string LicenseText
    {
        get
        {
            using var reader = new StreamReader(OpenResource("OFL.txt"));
            using var timeReader = new StreamReader(OpenResource("Nunito-OFL.txt"));
            using var notice = new StreamReader(OpenResource("Nunito-NOTICE.txt"));
            return reader.ReadToEnd() + "\n\n" + notice.ReadToEnd() + "\n\n" + timeReader.ReadToEnd();
        }
    }

    private static Stream OpenResource(string filename)
    {
        var assembly = typeof(TimerTypography).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + filename, StringComparison.Ordinal));
        return assembly.GetManifestResourceStream(name)!;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint length, IntPtr reserved, ref uint count);
}
