using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed class TimerCard : UserControl
{
    public TimerSettings Configuration { get; }
    public TimerSession Session { get; }
    public TimerDurationInput TimeInput { get; } = new();
    public TextBox NameInput { get; } = new();
    public Button EditNameButton => nameLabel;
    public Button StartButton { get; } = TimerTheme.Button("시작", primary: true);
    public Button AlarmSettingsButton { get; } = TimerTheme.Button("알람 설정");
    public Button DeleteButton { get; } = TimerTheme.Button("삭제");
    public Label HotkeyLink { get; } = new KeycapButton();
    private readonly HourglassReadout hourglass = new();
    private readonly Panel displaySlot = new() { Dock = DockStyle.Fill };
    private readonly TimerProgressLine progress = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
    private readonly AudioProgressReadout audioProgress = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = false };
    private readonly Panel nameRow;
    private readonly Label statusLabel = new TimerStatusCaption();
    private readonly TimerNameCaption nameLabel = new();
    private readonly Panel nameSlot = new() {
        AutoSize = false, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom, Margin = Padding.Empty
    };
    private readonly IAlarmPlayer alarm;
    private readonly TableLayoutPanel content;
    private readonly string defaultName;
    private readonly ToolTip hints = new();
    private bool editingName;
    private bool committingName;
    private bool useHourglass;
    private bool fittingName;
    private bool fittingTime;
    private bool finishingSeconds;
    private bool showingAudioProgress;
    private int measuredHeight;
    private (int Width, int Dpi, int ArtDpi, bool Reverse, bool Hourglass)? heightKey;
    private (int Width, int Dpi, int ArtDpi, bool Reverse, bool Hourglass) HeightKey(int width)
        => (width, DeviceDpi, hourglass.DeviceDpi, Configuration.ReverseAlarm, useHourglass);
    public bool NeedsHeightUpdate => measuredHeight < 1 || heightKey != HeightKey(Width);
    public event Action<TimerCard>? ActionRequested;
    public event Action<TimerCard>? AlarmSettingsRequested;
    public event Action<TimerCard>? HotkeySettingsRequested;
    public event Action<TimerCard>? DeleteRequested;
    public event Action? StateChanged;
    public Func<bool>? SaveConfiguration { get; set; }
    public int PendingSeconds => TimeInput.PendingSeconds;

    public TimerCard(TimerSettings configuration, int number, TimeProvider? clock = null, IAlarmPlayer? alarm = null,
        bool useHourglass = true)
    {
        SuspendLayout();
        Configuration = configuration;
        defaultName = TimerSettings.DefaultName(number);
        if (string.IsNullOrWhiteSpace(configuration.Name)) configuration.Name = defaultName;
        Session = new TimerSession(clock);
        Session.Reset(configuration.DurationSeconds);
        this.alarm = alarm ?? new AlarmPlayer();
        AutoScaleMode = AutoScaleMode.Inherit;
        AutoSize = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = TimerTheme.Card;
        ForeColor = TimerTheme.Foreground;
        Font = TimerTypography.Create(10F);
        Margin = new Padding(0, 0, 0, 12);

        content = new TableLayoutPanel {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 4, Padding = new Padding(10), Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < content.RowCount; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        nameRow = new Panel {
            Dock = DockStyle.Fill, AutoSize = false, Height = 24,
            Margin = new Padding(8, 0, 10, 0)
        };
        nameRow.SizeChanged += (_, _) => FitNameLabel();
        NameInput.Text = configuration.Name;
        NameInput.MaxLength = 80;
        NameInput.AccessibleName = "타이머 이름";
        NameInput.Font = TimerTypography.Create(11F, FontStyle.Bold);
        TimerTheme.StyleTextBox(NameInput);
        NameInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        NameInput.Margin = Padding.Empty;
        NameInput.Visible = false;
        NameInput.Leave += (_, _) => { if (editingName) CommitName(); };
        NameInput.KeyDown += (_, e) => {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            CommitName();
            StartButton.Focus();
        };
        nameLabel.Text = configuration.Name;
        nameLabel.Font = NameInput.Font;
        nameLabel.ForeColor = TimerTheme.Foreground;
        nameLabel.AutoSize = false;
        nameLabel.Dock = DockStyle.Fill;
        nameLabel.Margin = Padding.Empty;
        nameLabel.TextAlign = ContentAlignment.MiddleLeft;
        nameLabel.HandleCreated += (_, _) => FitNameLabel();
        nameSlot.Controls.Add(nameLabel);
        nameSlot.Controls.Add(NameInput);
        nameRow.Controls.Add(nameSlot);
        EditNameButton.Click += (_, _) => BeginNameEdit();
        header.Controls.Add(nameRow, 0, 0);
        HotkeyLink.AutoSize = false;
        HotkeyLink.Anchor = AnchorStyles.Right;
        HotkeyLink.TextAlign = ContentAlignment.MiddleRight;
        HotkeyLink.AutoEllipsis = true;
        HotkeyLink.ForeColor = TimerTheme.Muted;
        HotkeyLink.Cursor = Cursors.Hand;
        HotkeyLink.AccessibleName = "타이머 단축키 설정";
        HotkeyLink.Margin = Padding.Empty;
        HotkeyLink.Click += (_, _) => HotkeySettingsRequested?.Invoke(this);
        header.Controls.Add(HotkeyLink, 1, 0);
        AlarmSettingsButton.Font = TimerTypography.Create(8.5F);
        AlarmSettingsButton.Padding = new Padding(4, 3, 4, 3);
        AlarmSettingsButton.Margin = new Padding(8, 0, 0, 0);
        AlarmSettingsButton.Anchor = AnchorStyles.Right;
        TimerTheme.SetIcon(AlarmSettingsButton, TimerGlyph.Bell);
        if (AlarmSettingsButton is TimerButton alarmButton) alarmButton.Quiet = true;
        AlarmSettingsButton.ForeColor = TimerTheme.Muted;
        AlarmSettingsButton.Click += (_, _) => AlarmSettingsRequested?.Invoke(this);
        header.Controls.Add(AlarmSettingsButton, 2, 0);
        DeleteButton.Font = TimerTypography.Create(8.5F);
        DeleteButton.Padding = new Padding(5, 3, 5, 3);
        DeleteButton.Margin = new Padding(8, 0, 0, 0);
        DeleteButton.Anchor = AnchorStyles.Right;
        DeleteButton.ForeColor = TimerTheme.Muted;
        DeleteButton.AccessibleName = "타이머 삭제";
        if (DeleteButton is TimerButton delete) delete.Quiet = true;
        TimerTheme.SetIcon(DeleteButton, TimerGlyph.Delete);
        DeleteButton.Click += (_, _) => DeleteRequested?.Invoke(this);
        header.Controls.Add(DeleteButton, 3, 0);
        content.Controls.Add(header, 0, 0);

        var countdown = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 4, 0, 4)
        };
        countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 76));
        countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        TimeInput.EndEdit(configuration.DurationSeconds);
        TimeInput.CommitRequested += moveFocus => {
            if (!Disposing && !IsDisposed && CommitSecondsEdit() && moveFocus) StartButton.Focus();
        };
        hints.SetToolTip(TimeInput, "시·분·초를 직접 수정한 뒤 엔터를 누르거나 다른 곳을 클릭하세요.");
        statusLabel.Font = TimerTypography.Create(11F, FontStyle.Bold);
        statusLabel.AutoSize = false;
        statusLabel.Dock = DockStyle.None;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.AutoEllipsis = true;
        statusLabel.TextChanged += (_, _) => hints.SetToolTip(statusLabel, statusLabel.Text);
        statusLabel.Margin = new Padding(0, 0, 4, 0);
        statusLabel.Visible = false;
        hourglass.Session = Session;
        hourglass.Compact = true;
        hourglass.Dock = DockStyle.Fill;
        hourglass.Visible = false;
        displaySlot.Margin = Padding.Empty;
        displaySlot.Controls.Add(hourglass);
        displaySlot.Controls.Add(TimeInput);
        displaySlot.Controls.Add(statusLabel);
        TimeInput.TextLayoutChanged += FitTimeInput;
        displaySlot.SizeChanged += (_, _) => FitTimeInput();
        countdown.Controls.Add(displaySlot, 0, 0);
        StartButton.AutoSize = false;
        StartButton.Dock = DockStyle.None;
        StartButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        StartButton.Margin = new Padding(8, 0, 0, 0);
        StartButton.Padding = new Padding(10, 8, 10, 8);
        TimerTheme.SetIcon(StartButton, TimerGlyph.Play);
        StartButton.Click += (_, _) => ActionRequested?.Invoke(this);
        countdown.Controls.Add(StartButton, 1, 0);
        content.Controls.Add(countdown, 0, 1);
        content.Controls.Add(audioProgress, 0, 2);
        content.Controls.Add(progress, 0, 3);
        Controls.Add(content);
        FitCompactFields();
        HandleCreated += (_, _) => FitCompactFields();
        SetHourglassMode(useHourglass);
        UpdateHotkey();
        ResumeLayout(performLayout: true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TimerTheme.Background);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = TimerTheme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 16 * DeviceDpi / 96f);
        using var fill = new SolidBrush(TimerTheme.Card);
        using var border = new Pen(TimerTheme.Border);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
    }

    private void FitCompactFields()
    {
        measuredHeight = 0;
        var nameHeight = Math.Max(NameInput.PreferredHeight, EditNameButton.PreferredSize.Height);
        nameRow.MinimumSize = new Size(0, nameHeight);
        nameRow.Height = nameHeight;
        nameSlot.MinimumSize = new Size(0, nameHeight);
        HotkeyLink.MinimumSize = new Size(0, Math.Max(NameInput.PreferredHeight,
            HotkeyLink.Font.Height + HotkeyLink.Padding.Vertical + 2));
        FitHotkeyLabel();
        FitNameLabel();
        UpdateDisplayHeight();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (content is null || nameRow is null || Disposing || IsDisposed) return;
        FitHotkeyLabel();
        FitNameLabel();
    }

    private void FitNameLabel()
    {
        if (fittingName) return;
        fittingName = true;
        try
        {
            // One canvas owns both the name and its immediately adjacent edit icon.
            var height = Math.Max(1, nameRow.ClientSize.Height);
            var available = Math.Max(1, nameRow.ClientSize.Width);
            nameLabel.MaximumSize = new Size(available, 0);
            var width = editingName ? available : Math.Min(available, nameLabel.GetPreferredSize(Size.Empty).Width);
            nameSlot.SetBounds(0, 0, width, height);
            NameInput.SetBounds(0, Math.Max(0, (height - NameInput.PreferredHeight) / 2), width, NameInput.PreferredHeight);
            nameSlot.PerformLayout();
        }
        finally { fittingName = false; }
    }

    private void FitHotkeyLabel()
    {
        var actionsWidth = DeleteButton.PreferredSize.Width + DeleteButton.Margin.Horizontal
            + AlarmSettingsButton.PreferredSize.Width + AlarmSettingsButton.Margin.Horizontal;
        var available = Math.Max(1, (Width - content.Padding.Horizontal - actionsWidth) * 42 / 100
            - HotkeyLink.Margin.Horizontal);
        var natural = HotkeyLink.GetPreferredSize(Size.Empty);
        HotkeyLink.Size = new Size(Math.Min(available, natural.Width), HotkeyLink.MinimumSize.Height);
    }

    private void UpdateDisplayHeight()
    {
        // Retain the established, DPI-scaled readout minimum from the original
        // card layout instead of shrinking it during handle/DPI initialization.
        var height = Math.Max(displaySlot.MinimumSize.Height, (int)Math.Round(72 * DeviceDpi / 96d));
        displaySlot.MinimumSize = new Size(0, height);
        displaySlot.Height = height;
        FitTimeInput();
    }

    private void FitTimeInput()
    {
        if (fittingTime || Disposing || IsDisposed) return;
        fittingTime = true;
        try
        {
            // Reduce the previous fill-height button by another third. Use the
            // actual laid-out body height, not an assumed 72px row or extra margins.
            var previousInset = (int)Math.Round(17 * DeviceDpi / 96d);
            var previousHeight = Math.Max(1, displaySlot.ClientSize.Height - previousInset * 2);
            var buttonHeight = Math.Max(1, (int)Math.Round(previousHeight * 2d / 3d));
            if (StartButton.Height != buttonHeight) StartButton.Height = buttonHeight;
            var gap = (int)Math.Round(8 * DeviceDpi / 96d);
            var statusGap = (int)Math.Round(12 * DeviceDpi / 96d);
            var artWidth = useHourglass ? Math.Min(displaySlot.Height, (int)(displaySlot.Width * .34)) + gap : 0;
            var available = Math.Max(1, displaySlot.ClientSize.Width - artWidth);
            var statusMinimum = TextRenderer.MeasureText("실행 중", statusLabel.Font).Width;
            // The next table cell starts immediately after displaySlot; include
            // the button's left margin so the status midpoint uses its real edge.
            var buttonLeft = displaySlot.ClientSize.Width + StartButton.Margin.Left;
            var timeWidth = Math.Min(TimeInput.PreferredContentWidth,
                Math.Max(1, available + StartButton.Margin.Left - statusGap * 2 - statusMinimum));
            TimeInput.SetBounds(artWidth, 0, timeWidth, displaySlot.ClientSize.Height);
            var statusLeft = artWidth + TimeInput.ContentWidth + statusGap;
            var line = TimeInput.TextBounds;
            statusLabel.SetBounds(statusLeft, line.Top, Math.Max(1, buttonLeft - statusGap - statusLeft), line.Height);
            TimeInput.BringToFront();
            statusLabel.BringToFront();
        }
        finally { fittingTime = false; }
    }

    public void SetHourglassMode(bool enabled)
    {
        measuredHeight = 0;
        useHourglass = enabled;
        hourglass.Visible = enabled;
        TimeInput.Font = TimerTypography.CreateTime(enabled ? 22F : 28F);
        TimeInput.BringToFront();
        UpdateDisplayHeight();
        RefreshReadout();
    }

    private void RefreshReadout()
    {
        TimeInput.ShowEditableHint = !Session.IsActive;
        TimeInput.DisplaySeconds(Session.State == TimerState.Idle ? PendingSeconds : Session.RemainingSeconds);
        hourglass.Text = "";
        UpdateAudioProgress();
        progress.Fraction = Session.State == TimerState.Idle ? (PendingSeconds > 0 ? 1 : 0)
            : Session.DurationSeconds > 0 ? Session.RemainingExact / Session.DurationSeconds : 0;
        progress.ForeColor = Session.State == TimerState.Alarming ? TimerTheme.Warm
            : Session.State == TimerState.Stopped ? TimerTheme.Muted : TimerTheme.Accent;
        statusLabel.ForeColor = Session.State == TimerState.Alarming ? TimerTheme.Warm
            : Session.State == TimerState.Running ? TimerTheme.Accent : TimerTheme.Muted;
        TimerTheme.SetIcon(StartButton, Session.IsActive ? TimerGlyph.Stop
            : Session.State == TimerState.Stopped ? TimerGlyph.Restart : TimerGlyph.Play);
        var primary = !Session.IsActive || Session.State == TimerState.Alarming;
        if (StartButton is TimerButton button) button.Primary = primary;
        StartButton.BackColor = Session.State == TimerState.Alarming ? TimerTheme.Warm
            : primary ? TimerTheme.Accent : TimerTheme.Elevated;
        StartButton.ForeColor = primary ? TimerTheme.AccentDark : TimerTheme.Foreground;
        if (useHourglass) hourglass.Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        FitCompactFields();
    }

    public bool CommitSecondsEdit(bool persist = true)
    {
        if (finishingSeconds || Session.IsActive) return true;
        finishingSeconds = true;
        try
        {
            var seconds = PendingSeconds;
            var previous = Configuration.DurationSeconds;
            // Save once when editing finishes; keystrokes never write settings.
            if (persist && previous != seconds)
            {
                Configuration.DurationSeconds = seconds;
                if (SaveConfiguration?.Invoke() == false)
                {
                    Configuration.DurationSeconds = previous;
                    return false;
                }
            }
            var changed = seconds != TimeInput.Value;
            TimeInput.EndEdit(seconds);
            if (changed)
            {
                Session.Reset(seconds);
                StartButton.Text = "시작";
                statusLabel.Text = "";
                statusLabel.Visible = false;
            }
            RefreshReadout();
            if (changed) StateChanged?.Invoke();
            return true;
        }
        finally { finishingSeconds = false; }
    }

    public bool CommitEditorsOutside(Control? target, bool moveFocus = false)
    {
        if (Disposing || IsDisposed) return true;
        if (editingName && !IsInside(target, NameInput) && !CommitName()) return false;
        if (TimeInput.IsEditing && !IsInside(target, TimeInput))
        {
            if (!CommitSecondsEdit()) return false;
            if (moveFocus && TimeInput.ContainsFocus) StartButton.Focus();
        }
        return true;
    }

    public bool IsTimerInputTarget(Control? target)
        => (editingName && NameInput.ContainsFocus && IsInside(target, NameInput))
            || (TimeInput.IsEditing && TimeInput.ContainsFocus && IsInside(target, TimeInput));

    private static bool IsInside(Control? target, Control editor)
    {
        for (var control = target; control is not null; control = control.Parent)
            if (control == editor) return true;
        return false;
    }

    public void BeginNameEdit()
    {
        if (editingName) return;
        editingName = true;
        NameInput.Text = Configuration.Name;
        nameLabel.Visible = false;
        NameInput.Visible = true;
        NameInput.BringToFront();
        NameInput.Focus();
        NameInput.SelectAll();
        FitNameLabel();
        StateChanged?.Invoke();
    }

    private void EndNameEdit()
    {
        editingName = false;
        nameLabel.Text = Configuration.Name;
        NameInput.Text = Configuration.Name;
        NameInput.Visible = false;
        nameLabel.Visible = true;
        FitNameLabel();
        StateChanged?.Invoke();
    }

    public int PreferredHeight(int width, int viewportHeight = int.MaxValue)
    {
        if (measuredHeight > 0 && heightKey == HeightKey(width)) return measuredHeight;
        Width = width;
        UpdateDisplayHeight();
        FitHotkeyLabel();
        content.Width = width;
        content.PerformLayout();
        FitNameLabel();
        measuredHeight = content.GetPreferredSize(new Size(width, 0)).Height;
        heightKey = HeightKey(width);
        return measuredHeight;
    }

    public void MatchLayoutOf(TimerCard reference)
    {
        if (ReferenceEquals(this, reference)) return;
        // Initial cards pass through the form's automatic DPI scaling; cards
        // constructed later do not. Use the laid-out initial card's metrics,
        // including its readout minimum, rather than scaling those values again.
        var changed = false;
        void Match(Control target, Control source)
        {
            if (target.Padding != source.Padding) { target.Padding = source.Padding; changed = true; }
            if (target.Margin != source.Margin) { target.Margin = source.Margin; changed = true; }
            if (target.MinimumSize != source.MinimumSize) { target.MinimumSize = source.MinimumSize; changed = true; }
        }

        content.SuspendLayout();
        try
        {
            Match(content, reference.content);
            Match(nameRow, reference.nameRow);
            Match(nameSlot, reference.nameSlot);
            Match(HotkeyLink, reference.HotkeyLink);
            Match(AlarmSettingsButton, reference.AlarmSettingsButton);
            Match(DeleteButton, reference.DeleteButton);
            Match(StartButton, reference.StartButton);
            Match(displaySlot, reference.displaySlot);
            Match(progress, reference.progress);
            Match(audioProgress, reference.audioProgress);
            for (var row = 0; row < 2; row++)
                Match(content.GetControlFromPosition(0, row)!, reference.content.GetControlFromPosition(0, row)!);
            if (changed)
            {
                measuredHeight = 0;
                UpdateDisplayHeight();
                FitHotkeyLabel();
                FitNameLabel();
            }
        }
        finally { content.ResumeLayout(performLayout: changed); }
    }

    public bool CommitName()
    {
        if (committingName) return true;
        committingName = true;
        try
        {
            var text = string.IsNullOrWhiteSpace(NameInput.Text) ? defaultName : NameInput.Text.Trim();
            var previous = Configuration.Name;
            if (text != previous)
            {
                Configuration.Name = text;
                if (SaveConfiguration?.Invoke() == false)
                {
                    Configuration.Name = previous;
                    EndNameEdit();
                    return false;
                }
            }
            EndNameEdit();
            return true;
        }
        finally { committingName = false; }
    }

    public void Begin(int seconds)
    {
        CommitSecondsEdit(persist: false); // The start action already committed its duration.
        if (Configuration.ReverseAlarm) alarm.Pause();
        else alarm.Stop();
        Session.Start(seconds);
        TimeInput.ReadOnly = true;
        StartButton.Text = "중단";
        statusLabel.Text = "실행 중";
        statusLabel.Visible = true;
        RefreshReadout();
        StateChanged?.Invoke();
        if (seconds == 0) Tick();
        else if (Configuration.ReverseAlarm) PlayAlarm();
    }

    public void StopCurrent() => FinishCurrent("중단");

    private void FinishCurrent(string status)
    {
        if (Configuration.ReverseAlarm) alarm.Pause();
        else alarm.Stop();
        Session.StopCurrent();
        TimeInput.ReadOnly = false;
        StartButton.Text = "재시작";
        RefreshReadout();
        statusLabel.Text = status;
        statusLabel.Visible = true;
        StateChanged?.Invoke();
    }

    public void Tick()
    {
        if (Configuration.ReverseAlarm) UpdateAudioProgress();
        if (!Session.IsActive) return;
        if (Session.Tick())
        {
            if (Configuration.ReverseAlarm) FinishCurrent("완료");
            else PlayAlarm();
        }
        else if (ShouldPlayAlarm) UpdateAlarmStatus();
        RefreshReadout();
    }

    private bool ShouldPlayAlarm => Configuration.ReverseAlarm
        ? Session.State == TimerState.Running && Session.RemainingExact > 0
        : Session.State == TimerState.Alarming;

    private void UpdateAudioProgress()
    {
        if (showingAudioProgress != Configuration.ReverseAlarm)
        {
            showingAudioProgress = Configuration.ReverseAlarm;
            audioProgress.Visible = showingAudioProgress;
            measuredHeight = 0;
        }
        if (!Configuration.ReverseAlarm) return;
        var audio = alarm.AudioProgress;
        if (!audio.HasDuration && !Configuration.UsePlaylist && string.IsNullOrWhiteSpace(Configuration.AlarmPath))
            audio = new AlarmAudioProgress(0, DefaultMelody.DurationSeconds);
        audioProgress.UpdateProgress(audio, alarm.PlaybackStatus);
    }

    private void ApplyAlarmMode()
    {
        // A mode change must not carry a normal alarm's position into reverse mode.
        alarm.Stop();
        if (Session.State == TimerState.Running)
        {
            if (Session.RemainingExact <= 0) { Tick(); return; }
            if (Configuration.ReverseAlarm) PlayAlarm();
            else
            {
                statusLabel.Text = "실행 중";
            }
        }
        else if (Session.State == TimerState.Alarming && Configuration.ReverseAlarm)
            FinishCurrent("완료");
        RefreshReadout();
        StateChanged?.Invoke();
    }

    private void PlayAlarm()
    {
        statusLabel.Visible = true;
        try
        {
            if (Configuration.ReverseAlarm) alarm.Resume(Configuration);
            else alarm.Start(Configuration);
            UpdateAlarmStatus();
        }
        catch { statusLabel.Text = "오디오 장치 확인"; }
        StateChanged?.Invoke();
    }

    private void UpdateAlarmStatus()
    {
        var text = alarm.PlaybackStatus switch {
            AlarmPlaybackStatus.Preparing => "알람 준비 중",
            AlarmPlaybackStatus.Fallback => "기본 멜로디 재생",
            AlarmPlaybackStatus.DeviceError => "오디오 장치 확인",
            _ => Configuration.ReverseAlarm ? "역알람 재생 중" : "알람 재생 중"
        };
        if (statusLabel.Text == text) return;
        statusLabel.Text = text;
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (Configuration.ReverseAlarm) alarm.Pause();
        else alarm.Stop();
        Session.Stop();
        TimeInput.ReadOnly = false;
        StartButton.Text = "시작";
        RefreshReadout();
        statusLabel.Text = "";
        statusLabel.Visible = false;
        StateChanged?.Invoke();
    }

    public void UpdateHotkey()
    {
        HotkeyLink.Text = Configuration.StartHotkey.IsEmpty ? "단축키 미지정" : Configuration.StartHotkey.ToString();
        FitHotkeyLabel();
        hints.SetToolTip(HotkeyLink, HotkeyLink.Text);
    }

    public void ApplyAlarmSettings(bool pathChanged, bool modeChanged = false)
    {
        if (modeChanged)
            ApplyAlarmMode();
        else if (pathChanged)
        {
            alarm.Stop(); // A new file always starts at its own beginning, even after a pause.
            if (ShouldPlayAlarm) PlayAlarm();
        }
        else alarm.SetVolume(Configuration.Volume);
        UpdateAudioProgress();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { alarm.Dispose(); hints.Dispose(); }
        base.Dispose(disposing);
    }
}
