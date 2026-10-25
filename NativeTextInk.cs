using System.Drawing;
using System.Windows.Forms;

namespace IntervalTimer;

// Measure visible pixels with the same Windows GDI renderer as the edit fields.
// GDI+ outline metrics and GDI font fallback do not always have the same origin.
internal static class NativeTextInk
{
    private static readonly Dictionary<(string Name, float Size, FontStyle Style, int Dpi, string Text), Rectangle> Cache = new();
    public const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
        | TextFormatFlags.SingleLine | TextFormatFlags.Top;

    public static Rectangle Measure(string text, Font font, int dpi)
    {
        if (string.IsNullOrEmpty(text)) return Rectangle.Empty;
        var key = (font.Name, font.SizeInPoints, font.Style, dpi, text);
        if (Cache.TryGetValue(key, out var ink)) return ink;
        using var probe = new Bitmap(1, 1);
        probe.SetResolution(dpi, dpi);
        using var probeGraphics = Graphics.FromImage(probe);
        var measured = TextRenderer.MeasureText(probeGraphics, text, font, Size.Empty, Flags);
        var width = Math.Max(1, measured.Width + 8);
        var height = Math.Max(1, measured.Height + 8);
        using var bitmap = new Bitmap(width, height);
        bitmap.SetResolution(dpi, dpi);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            TextRenderer.DrawText(graphics, text, font, new Rectangle(0, 0, width, height), Color.White, Color.Black, Flags);
        }
        var left = width; var top = height; var right = -1; var bottom = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R + pixel.G + pixel.B <= 48) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        ink = right >= left ? Rectangle.FromLTRB(left, top, right + 1, bottom + 1) : new Rectangle(Point.Empty, measured);
        if (Cache.Count > 1024) Cache.Clear();
        Cache[key] = ink;
        return ink;
    }
}
