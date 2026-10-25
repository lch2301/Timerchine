using System.Drawing.Drawing2D;

namespace IntervalTimer;

internal sealed class DownloadProgressReadout : Control
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 40 };
    private OnlineDownloadProgress progress = new(null, "다운로드 준비 중");
    private float phase;
    private bool failed;
    private bool releasing;

    public DownloadProgressReadout()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = TimerTypography.Create(8.5F);
        BackColor = TimerTheme.Card;
        AutoSize = true;
        Height = 36;
        TabStop = false;
        AccessibleName = "오디오 다운로드 진행률";
        animation.Tick += (_, _) => { phase = (phase + .025f) % 1; Invalidate(); };
    }

    public void UpdateProgress(OnlineDownloadProgress value)
    {
        progress = value;
        failed = false;
        AccessibleDescription = $"{value.Status} {ProgressText()}";
        UpdateAnimation();
        Invalidate();
    }

    public void ShowFailure()
    {
        failed = true;
        progress = progress with { Status = "다운로드 실패" };
        AccessibleDescription = $"{progress.Status} {ProgressText()}";
        UpdateAnimation();
        Invalidate();
    }

    private string ProgressText()
    {
        if (progress.Percent is { } percent)
        {
            var value = Math.Floor(Math.Clamp(percent, 0, 100) * 10) / 10;
            var size = progress.TotalBytes is { } total ? $" · {Bytes(progress.DownloadedBytes)} / {Bytes(total)}" : "";
            return $"{(progress.Estimated ? "약 " : "")}{value:0.0}%{size}";
        }
        return progress.DownloadedBytes > 0 ? $"{Bytes(progress.DownloadedBytes)} · 전체 크기 확인 중" : "전체 크기 확인 중";
    }

    private static string Bytes(long value) => value >= 1024 * 1024 ? $"{value / (1024d * 1024):0.0} MB" : $"{value / 1024d:0.0} KB";

    private void UpdateAnimation()
    {
        if (!releasing) animation.Enabled = Visible && progress.Percent is null && !failed;
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimation(); }

    public override Size GetPreferredSize(Size proposedSize)
        => new(Math.Max(1, proposedSize.Width), (int)Math.Round(36 * DeviceDpi / 96d));

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 1 || Height < 1) return;
        var color = failed ? TimerTheme.Warm : TimerTheme.Accent;
        var barHeight = Math.Max(2, (int)Math.Round(6 * DeviceDpi / 96d));
        var labelBounds = new Rectangle(0, 0, Width, Height - barHeight - (int)Math.Round(6 * DeviceDpi / 96d));
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(e.Graphics, progress.Status, Font, labelBounds, TimerTheme.Muted, flags);
        TextRenderer.DrawText(e.Graphics, ProgressText(), Font, labelBounds, color, flags | TextFormatFlags.Right);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = TimerTheme.Rounded(new RectangleF(0, Height - barHeight, Width, barHeight), barHeight / 2f);
        using var background = new SolidBrush(TimerTheme.Elevated);
        using var fill = new SolidBrush(color);
        e.Graphics.FillPath(background, track);
        var state = e.Graphics.Save();
        e.Graphics.SetClip(track);
        if (progress.Percent is { } percent)
            e.Graphics.FillRectangle(fill, 0, Height - barHeight, (float)(Width * Math.Clamp(percent / 100d, 0, 1)), barHeight);
        else if (!failed)
            e.Graphics.FillRectangle(fill, (phase * 1.3f - .3f) * Width, Height - barHeight, Width * .3f, barHeight);
        e.Graphics.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !releasing) { releasing = true; animation.Dispose(); }
        base.Dispose(disposing);
    }
}
