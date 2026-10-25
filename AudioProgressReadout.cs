using System.Drawing.Drawing2D;

namespace IntervalTimer;

internal sealed class AudioProgressReadout : Control
{
    private static readonly Color Honey = Color.FromArgb(255, 207, 95);
    private static readonly Color Orange = Color.FromArgb(247, 157, 66);
    private double fraction;
    private string remaining = "재생 대기";

    public AudioProgressReadout()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = TimerTheme.Card;
        Font = TimerTypography.Create(7.5F);
        AutoSize = true;
        Height = 20;
        TabStop = false;
        AccessibleName = "역알람 오디오 남은 시간";
    }

    public void UpdateProgress(AlarmAudioProgress audio, AlarmPlaybackStatus status)
    {
        var next = audio.HasDuration ? FormatRemaining(audio.RemainingSeconds)
            : status == AlarmPlaybackStatus.Preparing ? "확인 중"
            : status == AlarmPlaybackStatus.DeviceError ? "오디오 확인 필요" : "재생 대기";
        var nextFraction = audio.Fraction;
        if (next == remaining && Math.Abs(nextFraction - fraction) * Width < .5) return;
        fraction = nextFraction;
        remaining = next;
        AccessibleDescription = $"오디오 남은 시간 {remaining}";
        Invalidate();
    }

    private static string FormatRemaining(double seconds)
    {
        var total = (long)Math.Ceiling(seconds);
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}";
    }

    public override Size GetPreferredSize(Size proposedSize)
        => new(Math.Max(1, proposedSize.Width), (int)Math.Round(20 * DeviceDpi / 96d));

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 1 || Height < 1) return;
        var barHeight = Math.Max(2, (int)Math.Round(3 * DeviceDpi / 96d));
        var labelBounds = new Rectangle(0, 0, Width, Height - barHeight - (int)Math.Round(2 * DeviceDpi / 96d));
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(e.Graphics, "오디오 남은 시간", Font, labelBounds, TimerTheme.Muted, flags);
        TextRenderer.DrawText(e.Graphics, remaining, Font, labelBounds, Honey, flags | TextFormatFlags.Right);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0, Height - barHeight, Width, barHeight);
        using var track = TimerTheme.Rounded(bounds, barHeight / 2f);
        using var background = new SolidBrush(TimerTheme.Elevated);
        e.Graphics.FillPath(background, track);
        if (fraction <= 0) return;
        // Clip a full-width gradient: progress starts at the left and grows warm.
        var saved = e.Graphics.Save();
        e.Graphics.SetClip(new RectangleF(0, bounds.Y, (float)(Width * fraction), barHeight));
        using var fill = new LinearGradientBrush(bounds, Honey, Orange, LinearGradientMode.Horizontal);
        e.Graphics.FillPath(fill, track);
        e.Graphics.Restore(saved);
    }
}
