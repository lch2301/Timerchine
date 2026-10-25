using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IntervalTimer;

internal static class TimerBranding
{
    public const string AppName = "Timerchine";
    public const string KoreanAppName = "타이머신";
    private static readonly Lazy<Icon> icon = new(CreateAppIcon);
    public static Icon AppIcon => icon.Value;

    internal static void DrawMark(Graphics graphics, RectangleF bounds, bool tile = true)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var size = Math.Min(bounds.Width, bounds.Height);
        graphics.TranslateTransform(bounds.X + (bounds.Width - size) / 2,
            bounds.Y + (bounds.Height - size) / 2);
        graphics.ScaleTransform(size / 96, size / 96);

        if (tile)
        {
            using var tilePath = RoundRectangle(new RectangleF(2, 2, 92, 92), 22);
            using var tileFill = new LinearGradientBrush(new PointF(0, 0), new PointF(96, 96),
                Color.FromArgb(35, 51, 57), Color.FromArgb(16, 25, 33));
            graphics.FillPath(tileFill, tilePath);
            using var border = new Pen(Color.FromArgb(66, 98, 99), 1.4f);
            graphics.DrawPath(border, tilePath);
        }

        using var silhouette = new GraphicsPath();
        silhouette.StartFigure();
        silhouette.AddLine(29, 26, 67, 26);
        silhouette.AddBezier(67, 26, 67, 38, 56, 42, 51, 48);
        silhouette.AddBezier(51, 48, 56, 54, 67, 58, 67, 70);
        silhouette.AddLine(67, 70, 29, 70);
        silhouette.AddBezier(29, 70, 29, 58, 40, 54, 45, 48);
        silhouette.AddBezier(45, 48, 40, 42, 29, 38, 29, 26);
        silhouette.CloseFigure();
        using var glassFill = new LinearGradientBrush(new PointF(24, 24), new PointF(72, 72),
            Color.FromArgb(38, 112, 224, 197), Color.FromArgb(8, 112, 224, 197));
        graphics.FillPath(glassFill, silhouette);
        using var rim = new Pen(Color.FromArgb(153, 239, 216), 3.5f) {
            LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round
        };
        graphics.DrawPath(rim, silhouette);

        using var sand = new LinearGradientBrush(new PointF(36, 35), new PointF(59, 66),
            Color.FromArgb(191, 251, 231), Color.FromArgb(91, 207, 177));
        graphics.FillPolygon(sand, [new PointF(36, 34), new(60, 34), new(48, 43)]);
        graphics.FillPolygon(sand, [new PointF(35, 64), new(48, 54), new(61, 64)]);
        using var stream = new Pen(Color.FromArgb(149, 235, 208), 1.6f);
        graphics.DrawLine(stream, 48, 45, 48, 55);
        using var cap = new Pen(Color.FromArgb(112, 224, 197), 4.4f) {
            StartCap = LineCap.Round, EndCap = LineCap.Round
        };
        graphics.DrawLine(cap, 26, 20, 70, 20);
        graphics.DrawLine(cap, 26, 76, 70, 76);
        graphics.Restore(state);
    }

    private static GraphicsPath RoundRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Icon CreateAppIcon()
    {
        using var resource = typeof(TimerBranding).Assembly
            .GetManifestResourceStream("IntervalTimer.Assets.IntervalTimer.ico");
        if (resource is not null)
        {
            using var embedded = new Icon(resource, 64, 64);
            return (Icon)embedded.Clone();
        }

        // The drawing fallback also supplies a real icon in source-only design previews.
        using var bitmap = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(bitmap)) DrawMark(graphics, new RectangleF(0, 0, 64, 64));
        var handle = bitmap.GetHicon();
        try
        {
            using var generated = Icon.FromHandle(handle);
            return (Icon)generated.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}

internal sealed class TimerLogo : Control
{
    public TimerLogo()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(40, 40);
        Margin = new Padding(0, 0, 12, 0);
        AccessibleName = $"{TimerBranding.KoreanAppName} 로고";
        AccessibleRole = AccessibleRole.Graphic;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TimerBranding.DrawMark(e.Graphics, ClientRectangle);
    }
}
