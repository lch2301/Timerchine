using System.Drawing;
using System.Windows.Forms;

namespace IntervalTimer;

// The same three fields display the countdown and edit its duration. Only a
// completed edit changes Value; countdown updates never change the saved value.
internal sealed class TimerDurationInput : UserControl
{
    public const int MaximumSeconds = 99 * 3600 + 59 * 60 + 59;
    private readonly TextBox[] parts = new TextBox[3];
    private readonly TimerTimeSeparator[] separators = new TimerTimeSeparator[2];
    private bool synchronizing;
    private bool dirty;
    private bool readOnly;
    private bool fitting;
    private bool showEditableHint;
    private int value;
    // WinForms can keep the old Font reference when a newly assigned font is
    // value-equal. Retain every assigned font until all child HWNDs are disposed.
    private readonly Dictionary<(int QuarterPoints, FontStyle Style), Font> fittedFonts = new();
    public bool IsEditing { get; private set; }
    public int Value => value;
    public bool ShowEditableHint
    {
        get => showEditableHint;
        set { if (showEditableHint == value) return; showEditableHint = value; ApplyInputAppearance(); }
    }
    public event Action<bool>? CommitRequested;
    public event Action? TextLayoutChanged;
    public int ContentWidth { get; private set; }
    public Rectangle TextBounds { get; private set; }
    public int PreferredContentWidth
    {
        get { var (part, colon) = MeasureFields(Font); return part * 3 + colon * 2; }
    }

    public bool ReadOnly
    {
        get => readOnly;
        set
        {
            readOnly = value;
            foreach (var part in parts)
            {
                part.ReadOnly = value;
                part.TabStop = !value;
                part.Cursor = value ? Cursors.Default : Cursors.IBeam;
            }
            ApplyInputAppearance();
        }
    }

    public TimerDurationInput()
    {
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = TimerTheme.Card;
        ForeColor = TimerTheme.Foreground;
        Font = TimerTypography.CreateTime(28);
        TabStop = false;
        Margin = Padding.Empty;
        for (var i = 0; i < parts.Length; i++)
        {
            var index = i;
            var part = new TextBox {
                Text = "00", BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center,
                BackColor = BackColor, ForeColor = ForeColor, Margin = Padding.Empty, MaxLength = 32,
                AccessibleName = i == 0 ? "타이머 시간" : i == 1 ? "타이머 분" : "타이머 초",
                ShortcutsEnabled = true, TabIndex = i
            };
            parts[i] = part;
            Controls.Add(part);
            part.Enter += (_, _) => {
                if (ReadOnly) return;
                if (!IsEditing) { IsEditing = true; dirty = false; }
                part.SelectAll();
                BeginInvoke(new Action(() => { if (!IsDisposed && part.Focused) part.SelectAll(); }));
            };
            part.TextChanged += (_, _) => {
                if (synchronizing || ReadOnly) return;
                MarkInputReceived();
                synchronizing = true;
                try
                {
                    var pasted = part.Text.Split(new[] { ':', '：' });
                    if (pasted.Length == 3)
                    {
                        for (var n = 0; n < 3; n++) parts[n].Text = ParsePart(pasted[n], n).ToString("00");
                        part.SelectionStart = part.Text.Length;
                    }
                    else if (part.Text.Length > 0)
                    {
                        var bounded = ParsePart(part.Text, index);
                        if (!int.TryParse(part.Text, out var entered) || entered != bounded)
                        {
                            part.Text = bounded.ToString("00");
                            part.SelectionStart = part.Text.Length;
                        }
                    }
                }
                finally { synchronizing = false; }
            };
            part.Leave += (_, _) => {
                if (ReadOnly) return;
                synchronizing = true;
                try { part.Text = ParsePart(part.Text, index).ToString("00"); }
                finally { synchronizing = false; }
                if (!IsHandleCreated || Disposing) return;
                BeginInvoke(new Action(() => {
                    if (!IsDisposed && !Disposing && !ContainsFocus && IsEditing) CommitRequested?.Invoke(false);
                }));
            };
            part.KeyDown += (_, e) => {
                if (ReadOnly) return;
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    CommitRequested?.Invoke(true);
                }
                else if (e.KeyCode is Keys.Up or Keys.Down)
                {
                    e.SuppressKeyPress = true;
                    part.Text = Math.Clamp(ParsePart(part.Text, index) + (e.KeyCode == Keys.Up ? 1 : -1), 0, index == 0 ? 99 : 59).ToString("00");
                    part.SelectAll();
                }
                else if (e.KeyCode == Keys.Right && index < 2 && part.SelectionLength == 0 && part.SelectionStart == part.Text.Length)
                { e.SuppressKeyPress = true; parts[index + 1].Focus(); }
                else if (e.KeyCode == Keys.Left && index > 0 && part.SelectionLength == 0 && part.SelectionStart == 0)
                { e.SuppressKeyPress = true; parts[index - 1].Focus(); }
            };
            part.KeyPress += (_, e) => {
                if (e.KeyChar is ':' or '：')
                {
                    e.Handled = true;
                    if (!ReadOnly && index < 2) parts[index + 1].Focus();
                }
                else if (!char.IsControl(e.KeyChar) && e.KeyChar is not (>= '0' and <= '9')) e.Handled = true;
            };
            if (i < 2)
            {
                var separator = new TimerTimeSeparator {
                    ForeColor = ForeColor, BackColor = BackColor, Margin = Padding.Empty, TabStop = false
                };
                separators[i] = separator;
                separator.Click += (_, _) => { if (!ReadOnly) parts[index + 1].Focus(); };
                Controls.Add(separator);
            }
        }
        SizeChanged += (_, _) => FitFields();
        FontChanged += (_, _) => FitFields();
        BackColorChanged += (_, _) => ApplyInputAppearance();
        ForeColorChanged += (_, _) => ApplyInputAppearance();
    }

    private void MarkInputReceived()
    {
        IsEditing = dirty = true;
        ApplyInputAppearance();
    }

    private void ApplyInputAppearance()
    {
        var highlight = showEditableHint && !ReadOnly;
        foreach (var part in parts)
        {
            if (part is null) continue;
            part.BackColor = highlight ? TimerTheme.TimeInputHint : BackColor;
            part.ForeColor = highlight ? TimerTheme.StopIcon : ForeColor;
            part.Cursor = ReadOnly ? Cursors.Default : highlight ? Cursors.Hand : Cursors.IBeam;
        }
    }

    private static int ParsePart(string text, int index)
    {
        var maximum = index == 0 ? 99 : 59;
        text = text.Trim();
        if (long.TryParse(text, out var number)) return (int)Math.Clamp(number, 0, maximum);
        return text.Length > 0 && text.All(c => c is >= '0' and <= '9') ? maximum : 0;
    }

    public int PendingSeconds => IsEditing && dirty
        ? ParsePart(parts[0].Text, 0) * 3600 + ParsePart(parts[1].Text, 1) * 60 + ParsePart(parts[2].Text, 2)
        : value;

    public void EndEdit(int seconds)
    {
        value = Math.Clamp(seconds, 0, MaximumSeconds);
        IsEditing = dirty = false;
        DisplaySeconds(value);
        ApplyInputAppearance();
    }

    public void DisplaySeconds(int seconds)
    {
        if (IsEditing) return;
        seconds = Math.Clamp(seconds, 0, MaximumSeconds);
        synchronizing = true;
        try
        {
            var values = new[] { seconds / 3600, seconds / 60 % 60, seconds % 60 };
            for (var i = 0; i < parts.Length; i++)
            {
                var text = values[i].ToString("00");
                if (parts[i].Text != text) parts[i].Text = text;
            }
        }
        finally { synchronizing = false; }
        AccessibleName = $"타이머 {TimerSession.Format(seconds)}";
    }

    private void FitFields()
    {
        if (fitting || Disposing || IsDisposed || parts.Any(part => part is null) || Width <= 0 || Height <= 0) return;
        fitting = true;
        try
        {
            var factor = Math.Min(1F, Math.Max(.1F, Width / (float)Math.Max(1, PreferredContentWidth)));
            var quarterPoints = Math.Max(28, (int)Math.Floor(Font.Size * factor * 4));
            Font next;
            int partWidth, colonWidth;
            while (true)
            {
                var key = (quarterPoints, Font.Style);
                if (!fittedFonts.TryGetValue(key, out next!))
                {
                    next = TimerTypography.CreateTime(quarterPoints / 4F);
                    fittedFonts.Add(key, next);
                }
                (partWidth, colonWidth) = MeasureFields(next);
                if (partWidth * 3 + colonWidth * 2 <= Width || quarterPoints == 28) break;
                quarterPoints--;
            }
            foreach (Control child in Controls) child.Font = next;
            var lineHeight = parts[0].PreferredHeight;
            var y = Math.Max(0, (Height - lineHeight) / 2);
            ContentWidth = partWidth * 3 + colonWidth * 2;
            var numberInk = NativeTextInk.Measure("00", next, DeviceDpi);
            var colonInk = NativeTextInk.Measure(":", next, DeviceDpi);
            var colonCorrection = (int)Math.Round((colonInk.Top + colonInk.Height / 2D)
                - (numberInk.Top + numberInk.Height / 2D));
            var x = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i].SetBounds(x, y, partWidth, lineHeight);
                x += partWidth;
                if (i < 2) { separators[i].SetBounds(x, y - colonCorrection, colonWidth, lineHeight); x += colonWidth; }
            }
            var origin = parts[0].IsHandleCreated ? parts[0].GetPositionFromCharIndex(0).Y : 0;
            TextBounds = new Rectangle(0, y + origin + numberInk.Top, ContentWidth, numberInk.Height);
        }
        finally { fitting = false; }
        TextLayoutChanged?.Invoke();
    }

    private (int Part, int Colon) MeasureFields(Font font)
    {
        var padding = (int)Math.Ceiling(4 * DeviceDpi / 96F);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var part = TextRenderer.MeasureText("00", font, Size.Empty, flags).Width + padding;
        var colon = Math.Max((int)Math.Ceiling(8 * DeviceDpi / 96F),
            TextRenderer.MeasureText(":", font, Size.Empty, flags).Width + padding);
        return (part, colon);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); FitFields(); }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(new Action(() => { if (!IsDisposed && !Disposing) FitFields(); }));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var font in fittedFonts.Values) font.Dispose();
            fittedFonts.Clear();
        }
    }
}

// The same native renderer and centered slots as the digits, with a vertical
// offset derived from the font outlines to align their visible centers.
internal sealed class TimerTimeSeparator : TextBox
{
    public TimerTimeSeparator()
    {
        AccessibleName = ":";
        AccessibleRole = AccessibleRole.StaticText;
        Text = ":";
        BorderStyle = BorderStyle.None;
        TextAlign = HorizontalAlignment.Center;
        ReadOnly = true;
        TabStop = false;
    }
}
