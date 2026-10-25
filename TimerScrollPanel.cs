using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed class TimerScrollPanel : Panel
{
    private Control? lastFocused;

    public TimerScrollPanel() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
        | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

    // Reserve the scrollbar gutter even when the list does not yet need it.
    public int StableContentWidth => Math.Max(1, ClientSize.Width
        - (VerticalScroll.Visible ? 0 : GetSystemMetricsForDpi(2, (uint)DeviceDpi)));

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        // Repaint the viewport and native child controls after Windows scrolls
        // copied pixels, including the exposed gaps between rounded cards.
        Invalidate(invalidateChildren: true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!SystemInformation.HighContrast && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            // Theme only this native scrollbar; Windows retains scrolling and keyboard behavior.
            try { SetWindowTheme(Handle, "DarkMode_Explorer", null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string? classes);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    protected override Point ScrollToControl(Control activeControl)
    {
        var focused = FocusedDescendant(this);
        // Reflow must not pull the view back to an old focused field after the user scrolls.
        // A new keyboard focus target still scrolls into view for Tab navigation.
        if (focused is not null && focused != lastFocused)
        {
            lastFocused = focused;
            return base.ScrollToControl(focused);
        }
        return DisplayRectangle.Location;
    }

    public void Reveal(Control target)
    {
        var bounds = target.Bounds;
        Control? ancestor = target.Parent;
        while (ancestor is not null && ancestor != this)
        {
            bounds.Offset(ancestor.Location);
            ancestor = ancestor.Parent;
        }
        if (ancestor != this) return;
        var y = -AutoScrollPosition.Y;
        if (bounds.Top < 0 || bounds.Height > ClientSize.Height) y += bounds.Top;
        else if (bounds.Bottom > ClientSize.Height) y += bounds.Bottom - ClientSize.Height;
        lastFocused = FocusedDescendant(this);
        AutoScrollPosition = new Point(-AutoScrollPosition.X, Math.Max(0, y));
    }

    private static Control? FocusedDescendant(Control root)
    {
        if (root.Focused) return root;
        foreach (Control child in root.Controls)
        {
            if (!child.ContainsFocus) continue;
            return FocusedDescendant(child);
        }
        return null;
    }
}

internal sealed class TimerCardCanvas : Panel
{
    public TimerCardCanvas() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
        | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Compose this canvas and its native edit controls as one frame.
            parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return parameters;
        }
    }
}
