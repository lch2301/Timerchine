using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace IntervalTimer;

// Measure and display the same text pixels, then draw the edit outline on the
// same canvas. Separate native-control font origins cannot shift the name.
internal sealed class TimerNameCaption : Button
{
    private Bitmap? textImage;
    private (string Text, Font Font, int Dpi, int Width, Color Fore, Color Back)? imageKey;
    private bool hovered;
    private const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
        | TextFormatFlags.SingleLine | TextFormatFlags.Top;

    public TimerNameCaption()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = TimerTheme.Card;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        AccessibleName = "이름 편집";
    }

    private int IconSpace => (int)Math.Ceiling(28 * DeviceDpi / 96f);

    public override Size GetPreferredSize(Size proposedSize)
    {
        using var probe = new Bitmap(1, 1);
        probe.SetResolution(DeviceDpi, DeviceDpi);
        using var graphics = Graphics.FromImage(probe);
        var text = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, TextFlags);
        var width = text.Width + 2 + IconSpace;
        if (MaximumSize.Width > 0) width = Math.Min(width, MaximumSize.Width);
        return new Size(width, Math.Max(text.Height, (int)Math.Ceiling(22 * DeviceDpi / 96f)));
    }

    private void PrepareText(int width)
    {
        var key = (Text, Font, DeviceDpi, width, ForeColor, BackColor);
        if (imageKey == key) return;
        textImage?.Dispose();
        textImage = null;
        imageKey = key;
        if (string.IsNullOrEmpty(Text) || width < 1) return;
        using var probe = new Bitmap(1, 1);
        probe.SetResolution(DeviceDpi, DeviceDpi);
        using var probeGraphics = Graphics.FromImage(probe);
        var measured = TextRenderer.MeasureText(probeGraphics, Text, Font, Size.Empty, TextFlags);
        using var raster = new Bitmap(width, Math.Max(1, measured.Height + 8));
        raster.SetResolution(DeviceDpi, DeviceDpi);
        using (var graphics = Graphics.FromImage(raster))
        {
            graphics.Clear(BackColor);
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(Point.Empty, raster.Size),
                ForeColor, BackColor, TextFlags | TextFormatFlags.EndEllipsis);
        }
        var left = raster.Width; var top = raster.Height; var right = -1; var bottom = -1;
        for (var y = 0; y < raster.Height; y++)
            for (var x = 0; x < raster.Width; x++)
            {
                var pixel = raster.GetPixel(x, y);
                if (Math.Abs(pixel.R - BackColor.R) + Math.Abs(pixel.G - BackColor.G)
                    + Math.Abs(pixel.B - BackColor.B) <= 8) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        if (right >= left)
            textImage = raster.Clone(Rectangle.FromLTRB(left, top, right + 1, bottom + 1), PixelFormat.Format32bppPArgb);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var scale = DeviceDpi / 96f;
        PrepareText(Math.Max(0, ClientSize.Width - IconSpace));
        var textTop = (int)Math.Round((ClientSize.Height - (textImage?.Height ?? 0)) / 2f);
        var center = textImage is null ? ClientSize.Height / 2f : textTop + textImage.Height / 2f;
        if (textImage is not null)
        {
            // DrawImageUnscaled(image, x, y) still uses the image's physical size:
            // GDI+ rescales it when bitmap DPI and paint/buffer DPI differ. Keep
            // source and destination explicitly in pixels, matching icon layout.
            var pixels = new Rectangle(0, 0, textImage.Width, textImage.Height);
            var target = new Rectangle(0, textTop, textImage.Width, textImage.Height);
            e.Graphics.DrawImage(textImage, target, pixels, GraphicsUnit.Pixel);
        }
        var iconX = (textImage?.Width ?? 0) + 7 * scale;
        var iconSize = 18 * scale;
        if (hovered)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = TimerTheme.Rounded(new RectangleF(iconX - 4 * scale,
                center - 12 * scale, 26 * scale, 24 * scale), 6 * scale);
            using var fill = new SolidBrush(TimerTheme.Blend(BackColor, Color.White, .08f));
            e.Graphics.FillPath(fill, shape);
        }
        TimerIcons.Draw(e.Graphics, TimerGlyph.Edit,
            new RectangleF(iconX, center - iconSize / 2f, iconSize, iconSize), TimerTheme.Accent);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { textImage?.Dispose(); textImage = null; }
        base.Dispose(disposing);
    }
}
