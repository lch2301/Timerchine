using System.Drawing;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace IntervalTimer;

internal class MultiTimerForm : Form, IMessageFilter
{
    private readonly SettingsStore store;
    private readonly AppSettings settings;
    private readonly List<TimerCard> cards = [];
    private readonly Dictionary<int, TimerCard> hotkeyTargets = [];
    private readonly Dictionary<Guid, int> hotkeyIds = [];
    private int nextHotkeyId = 100;
    private readonly ActionChimePlayer actionChime = new();
    private readonly WinTimer ticker = new() { Interval = 33 };
    private readonly TimerScrollPanel scroller = new() { AutoScroll = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TimerCardCanvas cardList = new() {
        Location = Point.Empty, Margin = Padding.Empty
    };
    private readonly Button stopButton = TimerTheme.Button("전체 시작");
    private readonly Label timerSummary = TimerTheme.Caption("나의 타이머");
    private readonly Label commonHotkeyLink = new KeycapButton() {
        AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = TimerTheme.Muted,
        Cursor = Cursors.Hand,
        Margin = new Padding(14, 0, 0, 0)
    };
    private readonly TimeProvider clock;
    private readonly Func<IAlarmPlayer> alarmFactory;
    private GlobalHotkeys? hotkeys;
    private bool settingsOpen;
    private bool hotkeysActive;
    private bool resizingCards;
    private bool layoutInitialized;
    private NotifyIcon? trayIcon;
    private ContextMenuStrip? trayMenu;
    private bool exitRequested;
    private FormWindowState restoreWindowState = FormWindowState.Normal;
    private GameOverlayController? gameOverlay;

    public MultiTimerForm(SettingsStore? store = null, TimeProvider? clock = null, Func<IAlarmPlayer>? alarmFactory = null)
    {
        SuspendLayout();
        this.store = store ?? new SettingsStore(SettingsStore.DefaultPath);
        this.clock = clock ?? TimeProvider.System;
        this.alarmFactory = alarmFactory ?? (() => new AlarmPlayer());
        settings = this.store.Load();
        ExternalLinkPolicy.UsageConfirmed = settings.ExternalLinksConfirmed;
        Text = TimerBranding.AppName;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 650);
        MinimumSize = new Size(560, 690);
        BackColor = TimerTheme.Background;
        ForeColor = TimerTheme.Foreground;
        Font = TimerTypography.Create(10F);
        Icon = TimerBranding.AppIcon;
        TimerTheme.ApplyWindow(this);
        DoubleBuffered = true;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Swap the former 34px brand mark and 38px header action heights.
        // Keep these as design units so WinForms scales both groups with DPI.
        var logo = new TimerLogo { Size = new Size(38, 38), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
        brand.Controls.Add(logo, 0, 0);
        var title = TimerTheme.Caption(TimerBranding.KoreanAppName);
        title.ForeColor = TimerTheme.Foreground;
        title.Font = TimerTypography.Create(18F, FontStyle.Bold);
        title.AutoSize = false;
        title.Dock = DockStyle.Fill;
        title.AutoEllipsis = true;
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.MinimumSize = new Size(0, TextRenderer.MeasureText(title.Text, title.Font).Height);
        title.Margin = Padding.Empty;
        brand.Controls.Add(title, 1, 0);
        header.Controls.Add(brand, 0, 0);
        var addButton = TimerTheme.Button("타이머 추가");
        addButton.AccessibleName = "타이머 추가";
        addButton.Margin = new Padding(6, 0, 6, 0);
        addButton.Font = TimerTypography.Create(9F, FontStyle.Bold);
        addButton.Padding = new Padding(10, 6, 10, 6);
        addButton.MinimumSize = new Size(0, 34);
        addButton.Anchor = AnchorStyles.Right;
        TimerTheme.SetIcon(addButton, TimerGlyph.Add);
        addButton.Click += (_, _) => AddTimer();
        header.Controls.Add(addButton, 1, 0);
        var visualSettings = TimerTheme.Button("설정");
        visualSettings.Margin = Padding.Empty;
        visualSettings.Font = TimerTypography.Create(9F, FontStyle.Bold);
        visualSettings.Padding = new Padding(10, 6, 10, 6);
        visualSettings.MinimumSize = new Size(0, 34);
        visualSettings.Anchor = AnchorStyles.Right;
        TimerTheme.SetIcon(visualSettings, TimerGlyph.Settings);
        visualSettings.Click += (_, _) => OpenVisualSettings();
        header.Controls.Add(visualSettings, 2, 0);
        layout.Controls.Add(header, 0, 0);
        timerSummary.Font = TimerTypography.Create(9F, FontStyle.Bold);
        timerSummary.Margin = new Padding(2, 0, 0, 8);
        layout.Controls.Add(timerSummary, 0, 1);

        scroller.Controls.Add(cardList);
        scroller.SizeChanged += (_, _) => ResizeCards();
        DpiChanged += (_, _) => {
            if (IsHandleCreated) BeginInvoke(new Action(() => { if (!IsDisposed && !Disposing) ResizeCards(); }));
        };
        layout.Controls.Add(scroller, 0, 2);
        var footer = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        commonHotkeyLink.Text = settings.StopHotkey.ToString();
        commonHotkeyLink.Click += (_, _) => OpenHotkeySettings(null);
        footer.Controls.Add(commonHotkeyLink, 1, 0);
        stopButton.Dock = DockStyle.Fill;
        stopButton.Margin = Padding.Empty;
        stopButton.Padding = new Padding(16, 9, 16, 9);
        TimerTheme.SetIcon(stopButton, TimerGlyph.Play);
        stopButton.Click += (_, _) => CycleAllTimers();
        footer.Controls.Add(stopButton, 0, 0);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);

        foreach (var configuration in settings.Timers) AppendCard(configuration);
        ticker.Tick += (_, _) => {
            foreach (var card in cards) card.Tick();
        };
        Shown += (_, _) => {
            ResizeCards();
            InitializeHotkeys();
            ticker.Start();
            gameOverlay ??= new GameOverlayController(() => cards.Select(card => new OverlayTimerRow(
                card.Configuration.Name, card.Session.RemainingSeconds)).ToArray(), settings.ShowGameOverlay,
                appearance: () => settings.OverlayAppearance);
        };
        AutoScaleDimensions = new SizeF(96F, 96F);
        ResumeLayout(performLayout: true);
        layoutInitialized = true;
        Application.AddMessageFilter(this);
    }

    bool IMessageFilter.PreFilterMessage(ref Message message)
    {
        // Apply to this UI thread's main window and dialogs before WinForms can
        // activate a focused button, link, checkbox, menu item, or default button.
        if (message.Msg is 0x0100 or 0x0101 or 0x0102 or 0x0104 or 0x0105 or 0x0106)
        {
            var key = (Keys)message.WParam.ToInt32();
            if (key is Keys.Enter or Keys.Space)
            {
                var input = Control.FromChildHandle(message.HWnd);
                if (key == Keys.Enter && input?.FindForm() is AlarmSettingsForm alarmDialog
                    && alarmDialog.IsVolumeInputTarget(input)) return false;
                if (input?.FindForm() is PlaylistPresetsForm) return false;
                if (input?.FindForm() is OverlaySettingsForm) return false;
                if (input?.FindForm() is VisualSettingsForm visualDialog
                    && input == visualDialog.OverlaySettingsButton) return false;
                if (input?.FindForm() is PlaylistSettingsForm playlistDialog
                    && ((key == Keys.Enter && playlistDialog.IsEditorTarget(input))
                        || (key == Keys.Space && playlistDialog.IsPathTarget(input)))) return false;
                return !cards.Any(card => card.IsTimerInputTarget(input));
            }
        }
        // A label or blank panel does not take keyboard focus, so Leave alone cannot
        // commit an edit. Catch clicks throughout our window before their actions run.
        if (message.Msg is not (0x0201 or 0x0204 or 0x0207 or 0x020B or 0x00A1)) return false;
        var target = Control.FromChildHandle(message.HWnd);
        if (target?.FindForm() is PlaylistSettingsForm playlistSettings)
        {
            playlistSettings.CommitEditorsOutside(target);
            return false;
        }
        if (target?.FindForm() is AlarmSettingsForm alarmSettings)
            return !alarmSettings.CommitVolumeOutside(target);
        if (target?.FindForm() != this) return false;
        foreach (var card in cards.ToArray())
            if (!card.CommitEditorsOutside(target, moveFocus: true)) return true;
        return false;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        if (layoutInitialized && !Disposing)
            foreach (var card in cards.ToArray()) card.CommitEditorsOutside(null);
        base.OnDeactivate(e);
    }

    private void AppendCard(TimerSettings configuration)
    {
        var card = new TimerCard(configuration, cards.Count + 1, clock, alarmFactory(), settings.UseHourglass);
        // Cards added later need the same design-to-DPI scaling the form gave its initial cards.
        if (layoutInitialized && DeviceDpi != 96)
            card.Scale(new SizeF(DeviceDpi / 96F, DeviceDpi / 96F));
        card.SaveConfiguration = SaveSettings;
        card.ActionRequested += CycleTimer;
        card.HotkeySettingsRequested += card => OpenHotkeySettings(card);
        card.AlarmSettingsRequested += OpenAlarmSettings;
        card.DeleteRequested += DeleteTimer;
        card.StateChanged += () => {
            UpdateFooter();
            if (card.NeedsHeightUpdate) ResizeCards();
        };
        cards.Add(card);
        cardList.Controls.Add(card);
        UpdateFooter();
        ResizeCards();
    }

    private void ResizeCards()
    {
        if (resizingCards) return;
        resizingCards = true;
        var changed = false;
        scroller.SuspendLayout();
        cardList.SuspendLayout();
        try
        {
            // Count and scrollbar visibility do not change the card dimensions.
            var gap = (int)Math.Round(8 * DeviceDpi / 96d);
            var contentWidth = scroller.StableContentWidth;
            var inset = (int)Math.Round(2 * DeviceDpi / 96d);
            var width = Math.Max(1, contentWidth - inset * 2);
            var y = 0;
            foreach (var card in cards)
            {
                if (layoutInitialized && IsHandleCreated && Visible)
                    card.MatchLayoutOf(cards[0]);
                var height = card.PreferredHeight(width);
                var bounds = new Rectangle(inset, y, width, height);
                if (card.Bounds != bounds)
                {
                    card.Bounds = bounds;
                    changed = true;
                }
                y += height + gap;
            }
            var size = new Size(contentWidth, Math.Max(0, y - gap));
            if (cardList.Size != size) { cardList.Size = size; changed = true; }
        }
        finally
        {
            cardList.ResumeLayout(performLayout: true);
            scroller.ResumeLayout(performLayout: true);
            resizingCards = false;
        }
        if (changed) scroller.Invalidate(invalidateChildren: true);
    }

    private void AddTimer()
    {
        var configuration = new TimerSettings { Name = TimerSettings.DefaultName(settings.Timers.Count + 1) };
        settings.Timers.Add(configuration);
        if (!SaveSettings()) { settings.Timers.Remove(configuration); return; }
        AppendCard(configuration);
        if (hotkeys is not null)
        {
            hotkeysActive = RegisterHotkeys(out var error);
            if (!hotkeysActive) ShowHotkeyError(error);
        }
        scroller.Reveal(cards[^1]);
    }

    private bool SaveSettings()
    {
        try { store.Save(settings); return true; }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"설정을 저장할 수 없습니다.\n{ex.Message}", "설정 저장 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void DeleteTimer(TimerCard card)
    {
        if (settingsOpen || !cards.Contains(card)) return;
        var confirmed = false;
        RunSettingsDialog(() => {
            using var dialog = new DeleteConfirmationForm(card.Configuration.Name);
            confirmed = dialog.ShowDialog(this) == DialogResult.Yes;
        });
        if (!confirmed) return;
        var index = cards.IndexOf(card);
        if (index < 0) return;
        var configuration = settings.Timers[index];
        settings.Timers.RemoveAt(index);
        if (!SaveSettings()) { settings.Timers.Insert(index, configuration); return; }

        cards.RemoveAt(index);
        hotkeyIds.Remove(configuration.Id);
        cardList.Controls.Remove(card);
        card.Dispose(); // Stops this timer's audio without touching the remaining timers.
        hotkeyTargets.Clear();
        if (hotkeys is not null)
        {
            hotkeysActive = RegisterHotkeys(out var error);
            if (!hotkeysActive) ShowHotkeyError(error);
        }
        UpdateFooter();
        ResizeCards();
    }

    private void CycleTimer(TimerCard card)
    {
        if (card.Session.IsActive)
        {
            card.StopCurrent();
            actionChime.PlayStop();
            return;
        }
        var previous = card.Configuration.DurationSeconds;
        var seconds = card.PendingSeconds;
        card.Configuration.DurationSeconds = seconds;
        // Enter/blur may already have saved this value. A hotkey can still start an
        // actively edited value, which is committed here exactly once.
        if (previous != seconds && !SaveSettings())
        {
            card.Configuration.DurationSeconds = previous;
            return;
        }
        card.Begin(seconds);
        actionChime.PlayStart();
        UpdateFooter();
    }

    private void CycleAllTimers()
    {
        if (cards.Count == 0) return;
        if (cards.Any(card => card.Session.IsActive))
        {
            foreach (var card in cards.Where(card => card.Session.IsActive)) card.StopCurrent();
            actionChime.PlayStop();
            UpdateFooter();
            return;
        }
        var previous = cards.Select(card => card.Configuration.DurationSeconds).ToList();
        foreach (var card in cards) card.Configuration.DurationSeconds = card.PendingSeconds;
        // Commit every duration once before starting any timer, so the batch is all-or-nothing.
        if (cards.Where((card, index) => card.Configuration.DurationSeconds != previous[index]).Any() && !SaveSettings())
        {
            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].Configuration.DurationSeconds = previous[i];
            }
            return;
        }
        foreach (var card in cards) card.Begin(card.Configuration.DurationSeconds);
        actionChime.PlayStart();
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        var active = cards.Count(card => card.Session.IsActive);
        stopButton.Enabled = cards.Count > 0;
        var stopped = cards.Any(card => card.Session.State == TimerState.Stopped);
        stopButton.Text = active > 0 ? "전체 중단" : stopped ? "전체 재시작" : "전체 시작";
        TimerTheme.SetIcon(stopButton, active > 0 ? TimerGlyph.Stop : stopped ? TimerGlyph.Restart : TimerGlyph.Play);
        timerSummary.Text = active > 0 ? $"나의 타이머  ·  {cards.Count}개 중 {active}개 실행 중" : $"나의 타이머  ·  {cards.Count}개";
    }

    private List<HotkeyRegistration> Registrations(IReadOnlyList<HotkeyBinding>? starts = null, HotkeyBinding? stop = null)
    {
        var bindings = new List<HotkeyRegistration>();
        hotkeyTargets.Clear();
        for (var i = 0; i < cards.Count; i++)
        {
            if (!hotkeyIds.TryGetValue(cards[i].Configuration.Id, out var id))
                hotkeyIds[cards[i].Configuration.Id] = id = nextHotkeyId++;
            bindings.Add(new(id, $"{cards[i].Configuration.Name} 시작 / 중단 / 재시작", starts?[i] ?? cards[i].Configuration.StartHotkey));
            hotkeyTargets[id] = cards[i];
        }
        bindings.Add(new(GlobalHotkeys.StopId, "전체 시작 / 중단 / 재시작", stop ?? settings.StopHotkey));
        return bindings;
    }

    private void InitializeHotkeys()
    {
        if (hotkeys is not null) return;
        hotkeys = new GlobalHotkeys();
        hotkeys.Pressed += id => {
            if (settingsOpen) return;
            if (id == GlobalHotkeys.StopId) CycleAllTimers();
            else if (hotkeyTargets.TryGetValue(id, out var card)) CycleTimer(card);
        };
        hotkeysActive = RegisterHotkeys(out var error);
        if (!hotkeysActive) ShowHotkeyError(error);
    }

    private bool RegisterHotkeys(out string error)
    {
        if (hotkeys is null) { error = ""; return false; }
        return hotkeys.TryRegister(Registrations(), out error);
    }

    private void OpenHotkeySettings(TimerCard? card)
    {
        if (hotkeys is null || settingsOpen) return;
        var target = card is null ? "전체 시작 / 전체 중단 / 전체 재시작" : $"{card.Configuration.Name} · 시작 / 중단 / 재시작";
        var binding = card?.Configuration.StartHotkey ?? settings.StopHotkey;
        RunSettingsDialog(() => {
            using var dialog = new SingleHotkeySettingsForm(target, binding, key => SaveSingleHotkey(card, key));
            dialog.ShowDialog(this);
        });
    }

    private void OpenAlarmSettings(TimerCard card)
    {
        if (settingsOpen) return;
        RunSettingsDialog(() => {
            using var dialog = new AlarmSettingsForm(card.Configuration,
                sound => SaveAlarmConfiguration(card, sound),
                volume => SaveAlarmSettings(card, card.Configuration.AlarmPath, volume, card.Configuration.ReverseAlarm));
            dialog.ShowDialog(this);
        });
    }

    private void OpenVisualSettings()
    {
        if (settingsOpen) return;
        RunSettingsDialog(() => {
            using var dialog = new VisualSettingsForm(settings.UseHourglass, settings.MinimizeToTray ?? false,
                settings.ExternalLinksConfirmed, settings.ShowGameOverlay, SaveVisualSettings, OpenOverlaySettings);
            dialog.ShowDialog(this);
        });
    }

    private void OpenOverlaySettings()
    {
        var owner = Form.ActiveForm ?? this;
        using var dialog = new OverlaySettingsForm(settings.OverlayAppearance, SaveOverlayAppearance);
        dialog.ShowDialog(owner);
    }

    private string? SaveOverlayAppearance(GameOverlayAppearance appearance)
    {
        var previous = settings.OverlayAppearance;
        settings.OverlayAppearance = appearance.Normalized();
        try { store.Save(settings); }
        catch (Exception ex)
        {
            settings.OverlayAppearance = previous;
            return $"오버레이 설정을 저장할 수 없습니다.\n{ex.Message}";
        }
        return null;
    }

    private string? SaveVisualSettings(bool enabled, bool minimizeToTray, bool externalLinksConfirmed, bool showGameOverlay)
    {
        var previous = settings.UseHourglass;
        var previousTray = settings.MinimizeToTray;
        var previousConfirmation = settings.ExternalLinksConfirmed;
        var previousOverlay = settings.ShowGameOverlay;
        settings.UseHourglass = enabled;
        settings.MinimizeToTray = minimizeToTray;
        settings.ExternalLinksConfirmed = externalLinksConfirmed;
        settings.ShowGameOverlay = showGameOverlay;
        try { store.Save(settings); }
        catch (Exception ex)
        {
            settings.UseHourglass = previous;
            settings.MinimizeToTray = previousTray;
            settings.ExternalLinksConfirmed = previousConfirmation;
            settings.ShowGameOverlay = previousOverlay;
            return $"설정을 저장할 수 없습니다.\n{ex.Message}";
        }
        ExternalLinkPolicy.UsageConfirmed = settings.ExternalLinksConfirmed;
        foreach (var card in cards) card.SetHourglassMode(enabled);
        gameOverlay?.SetEnabled(settings.ShowGameOverlay);
        ResizeCards();
        return null;
    }

    private void RunSettingsDialog(Action show)
    {
        settingsOpen = true;
        hotkeys?.UnregisterAll();
        hotkeysActive = false;
        try { show(); }
        finally
        {
            settingsOpen = false;
            if (hotkeys is not null && !hotkeysActive)
            {
                hotkeysActive = RegisterHotkeys(out var error);
                if (!hotkeysActive) ShowHotkeyError(error);
            }
            foreach (var card in cards) card.UpdateHotkey();
            commonHotkeyLink.Text = settings.StopHotkey.ToString();
            ResizeCards();
        }
    }

    private string? SaveSingleHotkey(TimerCard? card, HotkeyBinding binding)
    {
        if (hotkeys is null) return "단축키 기능을 초기화하지 못했습니다.";
        var starts = settings.Timers.Select(timer => timer.StartHotkey).ToList();
        var stop = settings.StopHotkey;
        var index = card is null ? -1 : cards.IndexOf(card);
        if (card is not null && index < 0) return "타이머를 다시 확인하세요.";
        if (card is null) stop = binding;
        else starts[index] = binding;
        if (!hotkeys.TryRegister(Registrations(starts, stop), out var error)) return error;
        hotkeysActive = true;
        var previous = settings.Timers.Select(timer => timer.StartHotkey).ToList();
        var previousStop = settings.StopHotkey;
        for (var i = 0; i < starts.Count; i++) settings.Timers[i].StartHotkey = starts[i];
        settings.StopHotkey = stop;
        try { store.Save(settings); return null; }
        catch (Exception ex)
        {
            for (var i = 0; i < previous.Count; i++) settings.Timers[i].StartHotkey = previous[i];
            settings.StopHotkey = previousStop;
            hotkeys.UnregisterAll();
            hotkeysActive = false;
            return $"단축키를 저장할 수 없습니다.\n{ex.Message}";
        }
    }

    private string? SaveAlarmSettings(TimerCard card, string path, int volume, bool reverseAlarm)
        => SaveAlarmConfiguration(card, AlarmSoundSettings.From(card.Configuration) with {
            Path = path, Volume = Math.Clamp(volume, 0, 100), ReverseAlarm = reverseAlarm
        });

    private string? SaveAlarmConfiguration(TimerCard card, AlarmSoundSettings sound)
    {
        var previous = AlarmSoundSettings.From(card.Configuration);
        var sourceChanged = previous.Path != sound.Path || previous.UsePlaylist != sound.UsePlaylist
            || previous.Shuffle != sound.Shuffle
            || !previous.Playlist.Select(item => (item.Path, item.ExternalLink))
                .SequenceEqual(sound.Playlist.Select(item => (item.Path, item.ExternalLink)));
        if (!sourceChanged && previous.Volume == sound.Volume && previous.ReverseAlarm == sound.ReverseAlarm
            && previous.Playlist.SequenceEqual(sound.Playlist)) return null;
        sound.ApplyTo(card.Configuration);
        try { store.Save(settings); }
        catch (Exception ex)
        {
            previous.ApplyTo(card.Configuration);
            return $"알람 설정을 저장할 수 없습니다.\n{ex.Message}";
        }
        card.ApplyAlarmSettings(sourceChanged, previous.ReverseAlarm != sound.ReverseAlarm);
        return null;
    }

    private void ShowHotkeyError(string error) => MessageBox.Show(this,
        $"{error}\n\n타이머의 단축키 표시를 클릭해 변경할 수 있습니다.", "단축키 등록 안내",
        MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
            foreach (var card in cards.ToArray())
                if (!card.CommitEditorsOutside(null)) { e.Cancel = true; break; }
        base.OnFormClosing(e);
        // Windows shutdown and an explicit Exit command must be able to end the process.
        if (e.Cancel || exitRequested || e.CloseReason != CloseReason.UserClosing) return;
        if (settingsOpen) { e.Cancel = true; return; }
        if (!settings.CloseBehaviorConfirmed)
        {
            var choice = DialogResult.Cancel;
            RunSettingsDialog(() => {
                using var dialog = new CloseBehaviorForm();
                choice = dialog.ShowDialog(this);
            });
            if (choice is not (DialogResult.Yes or DialogResult.No)) { e.Cancel = true; return; }
            var previousTray = settings.MinimizeToTray;
            var previousConfirmation = settings.CloseBehaviorConfirmed;
            settings.MinimizeToTray = choice == DialogResult.Yes;
            settings.CloseBehaviorConfirmed = true;
            if (!SaveSettings())
            {
                settings.MinimizeToTray = previousTray;
                settings.CloseBehaviorConfirmed = previousConfirmation;
                e.Cancel = true;
                return;
            }
        }
        if (settings.MinimizeToTray == true)
        {
            e.Cancel = true;
            SendToTray();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized)
            restoreWindowState = WindowState;
        else if (settings is { MinimizeToTray: true } && !settingsOpen && !exitRequested)
            SendToTray();
    }

    private void SendToTray()
    {
        if (trayIcon is null)
        {
            trayMenu = new ContextMenuStrip { Font = TimerTypography.Create(10F) };
            trayMenu.Items.Add("열기", null, (_, _) => RestoreFromTray());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("종료", null, (_, _) => {
                exitRequested = true;
                Close();
            });
            trayIcon = new NotifyIcon {
                Icon = TimerBranding.AppIcon, Text = TimerBranding.KoreanAppName, ContextMenuStrip = trayMenu
            };
            trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        }
        trayIcon.Visible = true;
        Hide();
    }

    private void RestoreFromTray()
    {
        WindowState = restoreWindowState;
        Show();
        if (trayIcon is not null) trayIcon.Visible = false;
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
            ticker.Stop();
            ticker.Dispose();
            gameOverlay?.Dispose();
            hotkeys?.Dispose();
            actionChime.Dispose();
            if (trayIcon is not null) { trayIcon.Visible = false; trayIcon.Dispose(); }
            trayMenu?.Dispose();
        }
        base.Dispose(disposing);
    }
}
