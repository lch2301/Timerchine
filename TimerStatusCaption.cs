using System.Drawing;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed class TimerStatusCaption : Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (string.IsNullOrEmpty(Text) || Width <= 0 || Height <= 0) return;
        var text = Text;
        while (true)
        {
            if (!AutoEllipsis || TextRenderer.MeasureText(text, Font, Size.Empty, NativeTextInk.Flags).Width <= Width || text.Length <= 1) break;
            text = text.EndsWith('…') ? text[..^2] + "…" : text[..^1] + "…";
        }
        var ink = NativeTextInk.Measure(text, Font, DeviceDpi);
        var top = (Height - ink.Height) / 2 - ink.Top;
        var inkLeft = TextAlign switch {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                => (Width - ink.Width) / 2,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                => Width - ink.Width,
            _ => 0
        };
        var bounds = new Rectangle(inkLeft - ink.Left, top, Width + ink.Left + 8, Math.Max(Font.Height, ink.Bottom) + 8);
        TextRenderer.DrawText(e.Graphics, text, Font, bounds, ForeColor, BackColor,
            NativeTextInk.Flags | TextFormatFlags.PreserveGraphicsClipping);
    }
}
