using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IntervalTimer;

internal readonly record struct HourglassScene(double UpperSand, double LowerSand,
    float Rotation, bool Flowing, double AnimationTime)
{
    public static HourglassScene From(TimerSession session)
    {
        if (session.State == TimerState.Idle) return new(0, 1, 0, false, 0);
        var duration = session.DurationSeconds;
        var elapsed = session.ElapsedSeconds;
        if (duration <= 0 || session.State == TimerState.Alarming || elapsed >= duration)
            return new(0, 1, 0, false, elapsed);
        var flipDuration = Math.Min(.7, duration * .25);
        if (elapsed < flipDuration)
        {
            var progress = elapsed / flipDuration;
            var eased = progress * progress * (3 - 2 * progress);
            return new(0, 1, (float)(180 * eased), false, elapsed);
        }
        var transferred = Math.Clamp((elapsed - flipDuration) / (duration - flipDuration), 0, 1);
        return new(1 - transferred, transferred, 0,
            session.State == TimerState.Running && transferred < 1, elapsed);
    }
}

internal sealed class HourglassReadout : Control
{
    public TimerSession? Session { get; set; }
    public bool Compact { get; set; }

    public HourglassReadout()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = TimerTheme.Card;
        ForeColor = TimerTheme.Foreground;
        Font = TimerTypography.Create(11F, FontStyle.Bold);
        AccessibleName = "모래시계 타이머";
        AccessibleRole = AccessibleRole.Graphic;
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Math.Max(0, proposedSize.Width),
        (int)Math.Round((Compact ? 72 : 224) * DeviceDpi / 96d));

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleDescription = Text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Session is null || Width < 4 || Height < 4) return;
        var scene = HourglassScene.From(Session);
        if (!Compact) { Draw(e.Graphics, ClientRectangle, scene, Text, Font, ForeColor); return; }
        var artWidth = Math.Min(Height, (int)(Width * .34));
        Draw(e.Graphics, new Rectangle(0, 0, artWidth, Height), scene, "", Font, ForeColor);
        var captionBounds = new Rectangle(artWidth + 2, 0, Math.Max(1, Width - artWidth - 2), Height);
        using var captionFont = TimerTypography.Create(22F);
        var measured = TextRenderer.MeasureText(e.Graphics, Text, captionFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        using var fitted = new Font(captionFont.FontFamily, captionFont.Size * Math.Min(1F,
            captionBounds.Width / (float)Math.Max(1, measured.Width)), FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, Text, fitted, captionBounds, ForeColor,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
    }

    internal static void Draw(Graphics graphics, Rectangle bounds, HourglassScene scene,
        string caption, Font font, Color foreground)
    {
        var saved = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var captionHeight = string.IsNullOrEmpty(caption) ? 0 : TextRenderer.MeasureText(caption, font).Height + 8;
        var drawingHeight = Math.Max(1, bounds.Height - captionHeight);
        // Reserve the entire diagonal so the frame stays within its slot during the flip.
        var scale = Math.Max(.01f, Math.Min((bounds.Width - 10) / 214f, (drawingHeight - 6) / 214f));
        graphics.TranslateTransform(bounds.Left + bounds.Width / 2f, bounds.Top + drawingHeight / 2f);
        graphics.ScaleTransform(scale, scale);

        using (var haloPath = new GraphicsPath())
        {
            haloPath.AddEllipse(-90, -90, 180, 180);
            using var halo = new PathGradientBrush(haloPath) {
                CenterColor = Color.FromArgb(26, 112, 224, 197),
                SurroundColors = [Color.FromArgb(0, 112, 224, 197)]
            };
            graphics.FillPath(halo, haloPath);
        }
        using (var shadowPath = new GraphicsPath())
        {
            shadowPath.AddEllipse(-66, 79, 132, 16);
            using var shadow = new PathGradientBrush(shadowPath) {
                CenterColor = Color.FromArgb(76, 0, 0, 0),
                SurroundColors = [Color.Transparent]
            };
            graphics.FillPath(shadow, shadowPath);
        }
        graphics.RotateTransform(scene.Rotation);

        // Both chambers and caps are symmetric, so the end of the flip meets the flowing state smoothly.
        using var glass = new GraphicsPath();
        glass.AddLines([new PointF(-39, -61), new(39, -61), new(39, -55), new(5, -3),
            new(5, 3), new(39, 55), new(39, 61), new(-39, 61), new(-39, 55),
            new(-5, 3), new(-5, -3), new(-39, -55)]);
        glass.CloseFigure();
        using var glassFill = new LinearGradientBrush(new PointF(-39, 0), new PointF(39, 0),
            Color.FromArgb(33, 57, 61), Color.FromArgb(22, 36, 44));
        graphics.FillPath(glassFill, glass);

        var glassState = graphics.Save();
        graphics.SetClip(glass, CombineMode.Intersect);
        using (var reflectedLight = new LinearGradientBrush(new PointF(-35, -60), new PointF(20, 60),
            Color.FromArgb(35, 166, 245, 224), Color.FromArgb(0, 166, 245, 224)))
            graphics.FillPolygon(reflectedLight, [new PointF(-34, -62), new(-19, -62),
                new(21, 62), new(7, 62)]);
        graphics.Restore(glassState);

        using var upper = new GraphicsPath();
        upper.AddPolygon([new PointF(-36, -57), new(36, -57), new(4, -3), new(-4, -3)]);
        using var lower = new GraphicsPath();
        lower.AddPolygon([new PointF(-4, 3), new(4, 3), new(36, 57), new(-36, 57)]);
        const double sideSlope = 64d / 54;
        const double sandArea = 2160;
        var upperArea = sandArea * Math.Clamp(scene.UpperSand, 0, 1);
        var upperHeight = (-8 + Math.Sqrt(64 + 2 * sideSlope * upperArea)) / sideSlope;
        var upperSurface = (float)(-3 - upperHeight);
        var lowerArea = sandArea * Math.Clamp(scene.LowerSand, 0, 1);
        var lowerHeight = (72 - Math.Sqrt(Math.Max(0, 5184 - 2 * sideSlope * lowerArea))) / sideSlope;
        var lowerSurface = (float)(57 - lowerHeight);

        using var sand = new LinearGradientBrush(new PointF(-35, -60), new PointF(35, 60),
            Color.FromArgb(255, 223, 165), Color.FromArgb(223, 163, 80));
        using var sandSurface = new Pen(Color.FromArgb(200, 255, 234, 189), 1.1f);
        var clipped = graphics.Save();
        graphics.SetClip(upper, CombineMode.Intersect);
        if (scene.UpperSand > 0)
        {
            graphics.FillRectangle(sand, -40, upperSurface, 80, -upperSurface + 1);
            graphics.DrawLine(sandSurface, -40, upperSurface + .5f, 40, upperSurface + .5f);
        }
        graphics.Restore(clipped);
        clipped = graphics.Save();
        graphics.SetClip(lower, CombineMode.Intersect);
        if (scene.LowerSand > 0)
        {
            graphics.FillRectangle(sand, -40, lowerSurface, 80, 62 - lowerSurface);
            var mound = (float)(7 * Math.Sin(Math.PI * Math.Clamp(scene.LowerSand, 0, 1)));
            graphics.FillPolygon(sand, [new PointF(-16, lowerSurface + 1),
                new(0, lowerSurface - mound), new(16, lowerSurface + 1)]);
            graphics.DrawLines(sandSurface, [new PointF(-36, lowerSurface + .5f),
                new(-16, lowerSurface + .5f), new(0, lowerSurface - mound + .5f),
                new(16, lowerSurface + .5f), new(36, lowerSurface + .5f)]);

            // Fixed highlights give the sand texture without making a paused timer shimmer.
            using var sparkle = new SolidBrush(Color.FromArgb(95, 255, 240, 203));
            for (var i = 0; i < 15; i++)
            {
                var x = -31 + (i * 19 % 62);
                var y = 12 + (i * 17 % 43);
                if (y > lowerSurface + 2) graphics.FillEllipse(sparkle, x, y, .9f, .9f);
            }
        }
        graphics.Restore(clipped);

        if (scene.Flowing)
        {
            using var streamGlow = new Pen(Color.FromArgb(32, 255, 218, 153), 4f);
            graphics.DrawLine(streamGlow, 0, -3, 0, Math.Max(4, lowerSurface));
            using var stream = new Pen(Color.FromArgb(230, 255, 222, 162), 1.15f);
            graphics.DrawLine(stream, 0, -3, 0, Math.Max(4, lowerSurface));
            using var grain = new SolidBrush(Color.FromArgb(255, 237, 193));
            var phase = scene.AnimationTime * 3.4;
            for (var i = 0; i < 5; i++)
            {
                var position = (phase + i / 5d) % 1;
                var y = (float)(3 + position * Math.Max(1, lowerSurface - 3));
                var x = (float)(Math.Sin(scene.AnimationTime * 13 + i) * .9);
                graphics.FillEllipse(grain, x - 1, y - 1, 2, 2);
            }
        }

        using var edge = new Pen(Color.FromArgb(130, 129, 184, 183), 1.5f);
        graphics.DrawPath(edge, glass);
        using var gleam = new Pen(Color.FromArgb(115, 207, 250, 239), 1.3f) {
            StartCap = LineCap.Round, EndCap = LineCap.Round
        };
        graphics.DrawLine(gleam, -33, -53, -12, -19);
        graphics.DrawLine(gleam, 33, 53, 12, 19);
        using var neck = new Pen(Color.FromArgb(90, 166, 230, 213), .85f);
        graphics.DrawLine(neck, -5, 0, 5, 0);

        using var frame = new Pen(Color.FromArgb(54, 79, 85), 5) {
            StartCap = LineCap.Round, EndCap = LineCap.Round
        };
        using var frameLight = new Pen(Color.FromArgb(115, 133, 170, 171), 1);
        graphics.DrawLine(frame, -49, -68, -49, 68);
        graphics.DrawLine(frame, 49, -68, 49, 68);
        graphics.DrawLine(frameLight, -50, -67, -50, 67);
        graphics.DrawLine(frameLight, 48, -67, 48, 67);
        DrawCap(graphics, new RectangleF(-59, -78, 118, 11));
        DrawCap(graphics, new RectangleF(-59, 67, 118, 11));
        graphics.Restore(saved);

        if (captionHeight > 0)
        {
            var captionBounds = new Rectangle(bounds.Left, bounds.Bottom - captionHeight, bounds.Width, captionHeight);
            TextRenderer.DrawText(graphics, caption, font, captionBounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    private static void DrawCap(Graphics graphics, RectangleF bounds)
    {
        using var path = new GraphicsPath();
        const float diameter = 6;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        using var metal = new LinearGradientBrush(bounds, Color.FromArgb(84, 123, 124),
            Color.FromArgb(38, 62, 70), LinearGradientMode.Vertical);
        graphics.FillPath(metal, path);
        using var edge = new Pen(Color.FromArgb(87, 139, 137), .8f);
        graphics.DrawPath(edge, path);
        using var accent = new Pen(Color.FromArgb(180, 142, 235, 208), 1.1f) {
            StartCap = LineCap.Round, EndCap = LineCap.Round
        };
        graphics.DrawLine(accent, bounds.Left + 10, bounds.Top + 1.5f, bounds.Right - 10, bounds.Top + 1.5f);
        using var fastening = new SolidBrush(Color.FromArgb(136, 172, 170));
        graphics.FillEllipse(fastening, bounds.Left + 7, bounds.Top + 4.4f, 2, 2);
        graphics.FillEllipse(fastening, bounds.Right - 9, bounds.Top + 4.4f, 2, 2);
    }
}
