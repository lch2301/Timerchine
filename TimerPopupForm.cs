using System.Drawing;
using System.Windows.Forms;

namespace IntervalTimer;

// Modal windows are newly anchored to the main window on every opening.
// Nested settings dialogs use the root owner rather than a moved parent popup.
internal class TimerPopupForm : Form
{
    private bool positioning;
    private bool opened;

    protected Rectangle MainWorkingArea => Screen.FromRectangle(MainBounds()).WorkingArea;

    private Rectangle MainBounds()
    {
        Form? root = Owner;
        while (root?.Owner is { } parent) root = parent;
        if (root is null) return Screen.FromControl(this).WorkingArea;
        return root.WindowState == FormWindowState.Minimized ? root.RestoreBounds : root.Bounds;
    }

    protected override void OnLoad(EventArgs e)
    {
        StartPosition = FormStartPosition.Manual;
        base.OnLoad(e);
        PlacePopup(anchorToMain: true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); // Let dialog-specific layout and scrolling finish first.
        opened = true;
        PlacePopup(anchorToMain: true);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (opened && Visible) PlacePopup(anchorToMain: false);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (!IsHandleCreated || Disposing) return;
        BeginInvoke(new Action(() => {
            if (!IsDisposed && !Disposing) PlacePopup(anchorToMain: false);
        }));
    }

    protected void KeepPopupOnScreen() => PlacePopup(anchorToMain: false);

    private void PlacePopup(bool anchorToMain)
    {
        if (positioning || IsDisposed || Disposing || WindowState != FormWindowState.Normal) return;
        positioning = true;
        try
        {
            var anchor = MainBounds();
            var work = anchorToMain ? Screen.FromRectangle(anchor).WorkingArea : Screen.FromControl(this).WorkingArea;
            var inset = (int)Math.Round(12 * DeviceDpi / 96D);
            var safe = Rectangle.Inflate(work, -Math.Min(inset, Math.Min(work.Width, work.Height) / 4),
                -Math.Min(inset, Math.Min(work.Width, work.Height) / 4));
            var x = anchorToMain ? anchor.Left + (anchor.Width - Width) / 2 : Left;
            var y = anchorToMain ? anchor.Top + (anchor.Height - Height) / 2 : Top;
            // Adjust coordinates only. Oversized dialogs keep their existing size;
            // anchoring their top edge still leaves the title bar available to drag.
            Location = new Point(Math.Clamp(x, safe.Left, Math.Max(safe.Left, safe.Right - Width)),
                Math.Clamp(y, safe.Top, Math.Max(safe.Top, safe.Bottom - Height)));
        }
        finally { positioning = false; }
    }
}
