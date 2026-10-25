using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed class CountdownReadout : Control
{
    public CountdownReadout()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ForeColor = TimerTheme.Foreground;
        BackColor = TimerTheme.Card;
        AccessibleName = "남은 시간";
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var height = TextRenderer.MeasureText("00:00:00", Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height + (int)Math.Round(25 * DeviceDpi / 96d);
        return new Size(Math.Max(0, proposedSize.Width), height);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleDescription = Text; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
        const TextFormatFlags measureFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var unitHeight = (int)Math.Round(20 * DeviceDpi / 96d);
        var measured = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, measureFlags);
        var scale = Math.Min(1, Math.Min((ClientSize.Width - 2d) / Math.Max(1, measured.Width),
            (ClientSize.Height - unitHeight - 2d) / Math.Max(1, measured.Height)));
        using var fitted = new Font(Font.FontFamily, Math.Max(1f, Font.Size * (float)scale), Font.Style, Font.Unit);
        using var unitFont = TimerTypography.Create(7.5F);
        var parts = Text.Split(':');
        var units = new[] { "시간", "분", "초" };
        var digitSize = TextRenderer.MeasureText(e.Graphics, "00", fitted, Size.Empty, measureFlags);
        var colonWidth = TextRenderer.MeasureText(e.Graphics, ":", fitted, Size.Empty, measureFlags).Width;
        var top = Math.Max(0, (Height - unitHeight - digitSize.Height) / 2);
        var x = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            TextRenderer.DrawText(e.Graphics, parts[i], fitted, new Rectangle(x, top, digitSize.Width, digitSize.Height),
                ForeColor, measureFlags | TextFormatFlags.HorizontalCenter);
            if (i < units.Length) TextRenderer.DrawText(e.Graphics, units[i], unitFont,
                new Rectangle(x, top + digitSize.Height, digitSize.Width, unitHeight), TimerTheme.Muted,
                measureFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += digitSize.Width;
            if (i < parts.Length - 1)
            {
                TextRenderer.DrawText(e.Graphics, ":", fitted, new Rectangle(x, top, colonWidth, digitSize.Height),
                    TimerTheme.Blend(TimerTheme.Muted, TimerTheme.Card, .3f), measureFlags);
                x += colonWidth;
            }
        }
    }
}

internal sealed class TimerProgressLine : Control
{
    private double fraction;
    public double Fraction
    {
        get => fraction;
        set { var next = Math.Clamp(value, 0, 1); if (Math.Abs(next - fraction) < .0001) return; fraction = next; Invalidate(); }
    }
    public TimerProgressLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = TimerTheme.Card;
        ForeColor = TimerTheme.Accent;
        Height = 4;
        TabStop = false;
    }
    public override Size GetPreferredSize(Size proposedSize) => new(Math.Max(1, proposedSize.Width), (int)Math.Round(4 * DeviceDpi / 96d));
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 1 || Height < 1) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = TimerTheme.Rounded(new RectangleF(0, 0, Width, Height), Height / 2f);
        using var background = new SolidBrush(TimerTheme.Elevated);
        e.Graphics.FillPath(background, track);
        if (fraction <= 0) return;
        using var bar = TimerTheme.Rounded(new RectangleF(0, 0, Math.Max(Height, (float)(Width * fraction)), Height), Height / 2f);
        using var fill = new SolidBrush(ForeColor);
        e.Graphics.FillPath(fill, bar);
    }
}
