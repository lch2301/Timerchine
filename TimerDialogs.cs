using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IntervalTimer;

internal class DeleteConfirmationForm : TimerPopupForm
{
    public DeleteConfirmationForm(string timerName)
    {
        DialogLayout.Configure(this, "타이머 삭제", 480);
        var layout = DialogLayout.Create(3);
        layout.Controls.Add(DialogLayout.Header("삭제 확인", "타이머를 삭제할까요?", timerName), 0, 0);
        layout.Controls.Add(DialogLayout.Description("재생 중인 알람이 중단되고, 이 타이머의 설정이 삭제됩니다."), 0, 1);
        var actions = new FlowLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0, 24, 0, 0)
        };
        var delete = TimerTheme.Button("삭제", primary: true);
        delete.BackColor = TimerTheme.Warm;
        delete.DialogResult = DialogResult.Yes;
        delete.Margin = Padding.Empty;
        TimerTheme.SetIcon(delete, TimerGlyph.Delete);
        var cancel = TimerTheme.Button("취소");
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(0, 0, 10, 0);
        actions.Controls.Add(delete);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 2);
        AcceptButton = cancel;
        CancelButton = cancel;
        Shown += (_, _) => cancel.Focus();
        DialogLayout.Complete(this, layout);
    }
}

internal class VisualSettingsForm : TimerPopupForm
{
    public CheckBox HourglassCheckBox { get; } = new PopupCheckBox() {
        Text = "모래시계 표시", AutoSize = true, Margin = new Padding(0, 0, 0, 12)
    };
    public CheckBox TrayCheckBox { get; } = new PopupCheckBox() {
        Text = "창을 닫으면 트레이로 최소화", AutoSize = true, Margin = new Padding(0, 0, 0, 12)
    };
    public CheckBox ExternalLinkCheckBox { get; } = new PopupCheckBox() {
        Text = "다운로드 및 개인 알람 이용 권한을 확인했습니다.",
        AutoSize = true, Margin = Padding.Empty, Enabled = ExternalLinkPolicy.IsEnabled
    };

    public VisualSettingsForm(bool enabled, bool minimizeToTray, bool externalLinksConfirmed,
        Func<bool, bool, bool, string?> save)
    {
        DialogLayout.Configure(this, "설정", 560);
        var layout = DialogLayout.Create(7);
        layout.Controls.Add(DialogLayout.Header("앱 설정", "설정", "타이머 표시와 창을 닫는 방식을 선택하세요."), 0, 0);
        var card = DialogLayout.Section(2);
        HourglassCheckBox.Checked = enabled;
        card.Controls.Add(HourglassCheckBox, 0, 0);
        card.Controls.Add(DialogLayout.Description(
            "시작할 때 모래시계가 뒤집히고, 시간에 맞춰 모래가 흐릅니다.\n모든 타이머에 적용되며 남은 시간도 함께 표시됩니다."), 0, 1);
        layout.Controls.Add(card, 0, 1);
        var trayCard = DialogLayout.Section(2);
        TrayCheckBox.Checked = minimizeToTray;
        trayCard.Controls.Add(TrayCheckBox, 0, 0);
        trayCard.Controls.Add(DialogLayout.Description(
            "닫기(X)나 최소화 버튼을 누르면 트레이로 보냅니다.\n타이머와 알람, 단축키는 계속 동작합니다."), 0, 1);
        layout.Controls.Add(trayCard, 0, 2);
        var license = new LinkLabel {
            Text = "글꼴 · 오디오 엔진 라이선스", AutoSize = true,
            LinkColor = TimerTheme.Muted, ActiveLinkColor = TimerTheme.Accent,
            VisitedLinkColor = TimerTheme.Muted, Margin = new Padding(0, 0, 0, 12)
        };
        license.LinkClicked += (_, _) => { using var dialog = new FontLicenseForm(); dialog.ShowDialog(this); };
        var informationLinks = new FlowLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        license.Margin = new Padding(0, 0, 18, 0);
        informationLinks.Controls.Add(license);
        var usage = new LinkLabel {
            Text = "외부 링크 이용 안내", AutoSize = true,
            LinkColor = TimerTheme.Muted, ActiveLinkColor = TimerTheme.Accent,
            VisitedLinkColor = TimerTheme.Muted, Margin = Padding.Empty
        };
        usage.LinkClicked += (_, _) => {
            using var dialog = new FontLicenseForm("외부 링크 이용 안내", ExternalLinkPolicy.UsageNotice);
            dialog.ShowDialog(this);
        };
        var externalLinkCard = DialogLayout.Section(2);
        usage.Margin = Padding.Empty;
        ExternalLinkCheckBox.Checked = externalLinksConfirmed;
        ExternalLinkCheckBox.Margin = new Padding(0, 0, 0, 12);
        externalLinkCard.Controls.Add(ExternalLinkCheckBox, 0, 0);
        externalLinkCard.Controls.Add(usage, 0, 1);
        layout.Controls.Add(externalLinkCard, 0, 3);
        layout.Controls.Add(informationLinks, 0, 4);
        var error = DialogLayout.Error();
        layout.Controls.Add(error, 0, 5);
        layout.Controls.Add(DialogLayout.Actions(this, () => {
            var message = save(HourglassCheckBox.Checked, TrayCheckBox.Checked, ExternalLinkCheckBox.Checked);
            if (message is not null) { error.Text = message; return; }
            DialogResult = DialogResult.OK;
            Close();
        }), 0, 6);
        DialogLayout.Complete(this, layout);
    }
}

internal sealed class CloseBehaviorForm : TimerPopupForm
{
    public CloseBehaviorForm()
    {
        DialogLayout.Configure(this, "종료 방식 선택", 560);
        var layout = DialogLayout.Create(3);
        layout.Controls.Add(DialogLayout.Header("처음 한 번만 선택", "트레이로 최소화할까요?",
            "창을 닫아도 타이머와 알람, 단축키가 계속 동작합니다."), 0, 0);
        layout.Controls.Add(DialogLayout.Description(
            "선택한 내용은 저장됩니다.\n설정 창에서 언제든 트레이 최소화 여부를 변경할 수 있습니다."), 0, 1);
        var actions = new FlowLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0, 24, 0, 0)
        };
        var tray = TimerTheme.Button("트레이로 보내기", primary: true);
        tray.DialogResult = DialogResult.Yes;
        tray.Margin = Padding.Empty;
        var exit = TimerTheme.Button("완전 종료");
        exit.DialogResult = DialogResult.No;
        exit.Margin = new Padding(0, 0, 10, 0);
        var cancel = TimerTheme.Button("취소");
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(0, 0, 10, 0);
        actions.Controls.Add(tray);
        actions.Controls.Add(exit);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 2);
        AcceptButton = tray;
        CancelButton = cancel;
        DialogLayout.Complete(this, layout);
    }
}

internal sealed class FontLicenseForm : TimerPopupForm
{
    public FontLicenseForm(string? title = null, string? contents = null)
    {
        SuspendLayout();
        Text = title ?? "오픈소스 라이선스";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = TimerTypography.Create(10F);
        ClientSize = new Size(640, 480);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MinimizeBox = false;
        BackColor = TimerTheme.Background;
        Padding = new Padding(20);
        var license = new TextBox {
            Text = (contents ?? (TimerTypography.LicenseText + "\n\n" + AudioDecoder.LicenseText)).ReplaceLineEndings(Environment.NewLine),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill, BackColor = TimerTheme.Card, ForeColor = TimerTheme.Foreground,
            BorderStyle = BorderStyle.None
        };
        Controls.Add(license);
        TimerTheme.ApplyWindow(this);
        AutoScaleDimensions = new SizeF(96F, 96F);
        ResumeLayout(true);
    }
}

internal class SingleHotkeySettingsForm : TimerPopupForm
{
    public HotkeyCaptureBox KeyInput { get; }

    public SingleHotkeySettingsForm(string target, HotkeyBinding binding, Func<HotkeyBinding, string?> save)
    {
        DialogLayout.Configure(this, "단축키 설정", 560);
        var layout = DialogLayout.Create(4);
        layout.Controls.Add(DialogLayout.Header("키보드 설정", "단축키", target), 0, 0);
        var card = DialogLayout.Section(3);
        card.Controls.Add(DialogLayout.SectionTitle("실행 키"), 0, 0);
        var row = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1,
            BackColor = TimerTheme.Card, Margin = new Padding(0, 4, 0, 16)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        KeyInput = new HotkeyCaptureBox(binding) {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0),
            TextAlign = HorizontalAlignment.Center, Font = TimerTypography.Create(12F, FontStyle.Bold)
        };
        TimerTheme.StyleTextBox(KeyInput);
        var keyField = DialogLayout.InputFrame(KeyInput);
        keyField.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        keyField.Margin = new Padding(0, 0, 12, 0);
        row.Controls.Add(keyField, 0, 0);
        var clear = TimerTheme.Button("해제");
        clear.Anchor = AnchorStyles.Left;
        clear.Margin = Padding.Empty;
        clear.Click += (_, _) => KeyInput.SetBinding(new());
        row.Controls.Add(clear, 1, 0);
        card.Controls.Add(row, 0, 1);
        card.Controls.Add(DialogLayout.Description(
            "입력칸을 클릭하고 원하는 키를 누르세요.\nCtrl · Alt · Shift 키를 조합할 수 있습니다."), 0, 2);
        layout.Controls.Add(card, 0, 1);
        var error = DialogLayout.Error();
        layout.Controls.Add(error, 0, 2);
        var actions = DialogLayout.Actions(this, () => {
            var message = save(KeyInput.Binding);
            if (message is not null) { error.Text = message; return; }
            DialogResult = DialogResult.OK;
            Close();
        });
        layout.Controls.Add(actions, 0, 3);
        DialogLayout.Complete(this, layout);
    }
}

internal class AlarmSettingsForm : TimerPopupForm
{
    public RadioButton NormalAlarm { get; } = new PopupRadioButton() { Text = "일반 알람", AutoSize = true };
    public RadioButton ReverseAlarm { get; } = new PopupRadioButton() { Text = "역알람", AutoSize = true };
    public RadioButton DefaultSound { get; } = new PopupRadioButton() { Text = "내장 멜로디", AutoSize = true };
    public RadioButton CustomSound { get; } = new PopupRadioButton() { Text = "내 파일", AutoSize = true };
    public RadioButton ExternalSound { get; } = new PopupRadioButton() {
        Text = ExternalLinkPolicy.DisplayName, Enabled = ExternalLinkPolicy.IsEnabled, AutoSize = true
    };
    public RadioButton PlaylistSound { get; } = new PopupRadioButton() { Text = "재생 목록", AutoSize = true };
    public TextBox PathInput { get; } = new();
    public TextBox LinkInput { get; } = new();
    public TrackBar VolumeInput { get; } = new PopupVolumeTrackBar() { Minimum = 0, Maximum = 100, TickFrequency = 10, TickStyle = TickStyle.None };
    public NumericUpDown VolumeNumber { get; } = new() { Minimum = 0, Maximum = 100 };
    private readonly Func<int, string?> saveVolume;
    private readonly Label error = DialogLayout.Error();
    private readonly ToolTip hints = new();
    private int savedVolume;
    private bool synchronizing;
    private bool committingVolume;
    private bool preparingLink;
    private int downloadAttempt;
    private bool resourcesDisposed;
    private Action? fitSoundCard;
    private readonly CancellationTokenSource linkPreparation = new();

    public AlarmSettingsForm(TimerSettings settings, Func<string, int, bool, string?> save,
        Func<int, string?>? saveVolume = null)
        : this(settings, sound => save(sound.Path, sound.Volume, sound.ReverseAlarm),
            saveVolume ?? (volume => save(settings.AlarmPath, volume, settings.ReverseAlarm))) { }

    public AlarmSettingsForm(TimerSettings settings, Func<AlarmSoundSettings, string?> save,
        Func<int, string?>? saveVolume = null)
    {
        this.saveVolume = saveVolume ?? (volume => save(AlarmSoundSettings.From(settings) with { Volume = volume }));
        var playlistItems = settings.AlarmPlaylist.Select(item => item with { }).ToList();
        var shufflePlaylist = settings.ShufflePlaylist;
        var playlistPresetId = settings.PlaylistPresetId;
        savedVolume = settings.Volume;
        DialogLayout.Configure(this, "알람 설정", 640);
        var layout = DialogLayout.Create(6);
        layout.Controls.Add(DialogLayout.Header("타이머별 설정", "알람 설정", settings.Name), 0, 0);
        var modeCard = DialogLayout.Section(3);
        modeCard.Controls.Add(DialogLayout.SectionTitle("알람 방식"), 0, 0);
        var modeChoices = new FlowLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true,
            BackColor = TimerTheme.Card, Margin = new Padding(0, 4, 0, 12)
        };
        NormalAlarm.Checked = !settings.ReverseAlarm;
        ReverseAlarm.Checked = settings.ReverseAlarm;
        NormalAlarm.Margin = new Padding(0, 0, 24, 0);
        ReverseAlarm.Margin = Padding.Empty;
        modeChoices.Controls.Add(NormalAlarm);
        modeChoices.Controls.Add(ReverseAlarm);
        modeCard.Controls.Add(modeChoices, 0, 1);
        var modeDescription = DialogLayout.Description("");
        void UpdateModeDescription()
        {
            modeDescription.Text = ReverseAlarm.Checked
                ? "역알람: 타이머 실행 중 재생하고, 중단하거나 0이 되면 일시정지합니다.\n다시 시작하면 중단한 위치부터 이어 재생합니다."
                : "일반 알람: 남은 시간이 0이 되면 알람이 울립니다.";
            if (IsHandleCreated) BeginInvoke(() => FitSettingsToScreen(layout));
        }
        NormalAlarm.CheckedChanged += (_, _) => UpdateModeDescription();
        ReverseAlarm.CheckedChanged += (_, _) => UpdateModeDescription();
        UpdateModeDescription();
        modeCard.Controls.Add(modeDescription, 0, 2);
        layout.Controls.Add(modeCard, 0, 1);
        var soundCard = DialogLayout.Section(8);
        soundCard.AutoSize = false;
        soundCard.Dock = DockStyle.Top;
        var shownSoundRows = new HashSet<int> { 0, 1 };
        var fittingSoundCard = false;
        void FitSoundCard()
        {
            if (fittingSoundCard || soundCard.Controls.Count < 8 || soundCard.IsDisposed) return;
            fittingSoundCard = true;
            soundCard.SuspendLayout();
            try
            {
                var width = Math.Max(1, soundCard.ClientSize.Width - soundCard.Padding.Horizontal);
                var total = soundCard.Padding.Vertical;
                for (var row = 0; row < soundCard.RowCount; row++)
                {
                    var control = soundCard.GetControlFromPosition(0, row)!;
                    var height = shownSoundRows.Contains(row)
                        ? control.GetPreferredSize(new Size(Math.Max(1, width - control.Margin.Horizontal), 0)).Height
                            + control.Margin.Vertical : 0;
                    soundCard.RowStyles[row].SizeType = SizeType.Absolute;
                    soundCard.RowStyles[row].Height = height;
                    total += height;
                }
                if (soundCard.Height != total) soundCard.Height = total;
            }
            finally { soundCard.ResumeLayout(performLayout: true); fittingSoundCard = false; }
        }
        void SetSoundRowVisible(Control control, int row, bool visible)
        {
            if (visible) shownSoundRows.Add(row); else shownSoundRows.Remove(row);
            soundCard.RowStyles[row].SizeType = visible ? SizeType.AutoSize : SizeType.Absolute;
            soundCard.RowStyles[row].Height = 0;
            control.Visible = visible;
        }
        soundCard.Controls.Add(DialogLayout.SectionTitle("알람음"), 0, 0);
        var choices = new FlowLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true,
            BackColor = TimerTheme.Card, Margin = new Padding(0, 4, 0, 16)
        };
        var savedExternalLink = OnlineAlarmSource.IsLink(settings.AlarmPath);
        DefaultSound.Checked = !settings.UsePlaylist && (string.IsNullOrWhiteSpace(settings.AlarmPath) || (savedExternalLink && !ExternalLinkPolicy.IsEnabled));
        ExternalSound.Checked = !settings.UsePlaylist && savedExternalLink && ExternalLinkPolicy.IsEnabled;
        PlaylistSound.Checked = settings.UsePlaylist;
        CustomSound.Checked = !DefaultSound.Checked && !ExternalSound.Checked && !PlaylistSound.Checked;
        DefaultSound.Margin = new Padding(0, 0, 24, 0);
        CustomSound.Margin = new Padding(0, 0, 24, 0);
        ExternalSound.Margin = new Padding(0, 0, 24, 0);
        PlaylistSound.Margin = Padding.Empty;
        choices.Controls.Add(DefaultSound);
        choices.Controls.Add(CustomSound);
        choices.Controls.Add(ExternalSound);
        choices.Controls.Add(PlaylistSound);
        soundCard.Controls.Add(choices, 0, 1);
        var pathRow = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = TimerTheme.Card, Margin = Padding.Empty
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        PathInput.Text = savedExternalLink ? "" : settings.AlarmPath;
        PathInput.PlaceholderText = "알람으로 사용할 파일 경로";
        PathInput.Dock = DockStyle.Fill;
        PathInput.Margin = Padding.Empty;
        TimerTheme.StyleTextBox(PathInput);
        var pathField = DialogLayout.InputFrame(PathInput);
        pathField.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        pathField.Margin = new Padding(0, 0, 12, 0);
        pathRow.Controls.Add(pathField, 0, 0);
        var browse = TimerTheme.Button("파일 선택");
        TimerTheme.SetIcon(browse, TimerGlyph.Folder);
        browse.Anchor = AnchorStyles.Left;
        browse.Margin = Padding.Empty;
        browse.Click += (_, _) => {
            using var dialog = new OpenFileDialog {
                Title = "알람 파일 선택",
                Filter = "모든 파일 (*.*)|*.*",
                CheckFileExists = true, AddExtension = false
            };
            if (File.Exists(PathInput.Text)) dialog.FileName = PathInput.Text;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            PathInput.Text = dialog.FileName;
            CustomSound.Checked = true;
        };
        pathRow.Controls.Add(browse, 1, 0);
        soundCard.Controls.Add(pathRow, 0, 2);
        LinkInput.Text = savedExternalLink ? settings.AlarmPath : "";
        LinkInput.PlaceholderText = "이용 권한이 있는 영상·오디오의 https:// 링크";
        LinkInput.AccessibleName = "알람으로 사용할 외부 링크";
        LinkInput.Margin = Padding.Empty;
        TimerTheme.StyleTextBox(LinkInput);
        var linkRow = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = TimerTheme.Card, Margin = Padding.Empty
        };
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var linkField = DialogLayout.InputFrame(LinkInput);
        linkField.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        linkField.Margin = new Padding(0, 0, 12, 0);
        linkRow.Controls.Add(linkField, 0, 0);
        var openDownload = TimerTheme.Button("");
        TimerTheme.SetIcon(openDownload, TimerGlyph.Folder);
        if (openDownload is TimerButton folderButton) folderButton.IconOnly = true;
        openDownload.Padding = new Padding(8);
        openDownload.Margin = Padding.Empty;
        openDownload.Anchor = AnchorStyles.Left;
        openDownload.AccessibleName = "다운로드된 파일 위치 열기";
        hints.SetToolTip(openDownload, "다운로드된 파일 위치 열기");
        openDownload.Click += (_, _) => {
            try
            {
                var audio = OnlineAlarmSource.GetPreparedAudio(LinkInput.Text.Trim());
                if (audio is null)
                {
                    error.Text = "다운로드된 파일이 없습니다. 링크를 저장해 먼저 오디오를 다운로드하세요.";
                    FitSettingsToScreen(layout);
                    return;
                }
                var start = new ProcessStartInfo {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    UseShellExecute = true
                };
                start.ArgumentList.Add("/select,");
                start.ArgumentList.Add(audio.Value.Path);
                Process.Start(start)?.Dispose();
            }
            catch (Exception ex)
            {
                error.Text = $"다운로드된 파일 위치를 열 수 없습니다.\n{ex.Message}";
                FitSettingsToScreen(layout);
            }
        };
        linkRow.Controls.Add(openDownload, 1, 0);
        soundCard.Controls.Add(linkRow, 0, 3);
        var linkHelp = DialogLayout.Description(
            "유튜브 등 공개된 단일 영상·오디오 링크를 입력하세요.\n" +
            "해당 링크의 오디오를 PC에 다운로드하며, 다운로드가 완료되면 자동으로 해당 파일이 알람으로 설정됩니다.");
        linkHelp.Margin = new Padding(0, 10, 0, 0);
        soundCard.Controls.Add(linkHelp, 0, 4);
        var downloadProgress = new DownloadProgressReadout {
            Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0), Visible = false
        };
        soundCard.Controls.Add(downloadProgress, 0, 5);
        SetSoundRowVisible(downloadProgress, 5, false);
        var permissionWarning = DialogLayout.Error();
        permissionWarning.Text = ExternalLinkPolicy.ConfirmationRequiredMessage;
        permissionWarning.Margin = new Padding(0, 22, 0, 0);
        permissionWarning.Visible = false;
        soundCard.Controls.Add(permissionWarning, 0, 7);
        SetSoundRowVisible(permissionWarning, 7, false);
        var playlistRow = new TableLayoutPanel {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1,
            BackColor = TimerTheme.Card, Margin = Padding.Empty
        };
        playlistRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        playlistRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var playlistButton = TimerTheme.Button("재생 목록 설정");
        TimerTheme.SetIcon(playlistButton, TimerGlyph.Settings);
        playlistButton.Margin = new Padding(0, 0, 14, 0);
        playlistButton.Anchor = AnchorStyles.Left;
        playlistRow.Controls.Add(playlistButton, 0, 0);
        var playlistSummary = DialogLayout.Description("");
        playlistSummary.AutoSize = true;
        playlistSummary.MaximumSize = Size.Empty;
        playlistSummary.Dock = DockStyle.None;
        playlistSummary.Anchor = AnchorStyles.Left;
        playlistSummary.TextAlign = ContentAlignment.MiddleLeft;
        playlistSummary.Margin = Padding.Empty;
        void UpdatePlaylistSummary() => playlistSummary.Text = playlistItems.Count == 0 ? "등록된 파일 없음"
            : $"파일 {playlistItems.Count}개 · {(shufflePlaylist ? "무작위 재생" : "순차 재생")}";
        UpdatePlaylistSummary();
        playlistRow.Controls.Add(playlistSummary, 1, 0);
        playlistButton.Click += (_, _) => {
            using var dialog = new PlaylistSettingsForm(playlistItems, shufflePlaylist, playlistPresetId);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            playlistItems = dialog.Items.Select(item => item with { }).ToList();
            shufflePlaylist = dialog.Shuffle;
            playlistPresetId = dialog.SelectedPresetId;
            UpdatePlaylistSummary();
            FitSettingsToScreen(layout);
        };
        soundCard.Controls.Add(playlistRow, 0, 6);
        fitSoundCard = FitSoundCard;
        soundCard.SizeChanged += (_, _) => FitSoundCard();
        void ShowPermissionWarning()
        {
            error.Text = "";
            SetSoundRowVisible(downloadProgress, 5, false);
            SetSoundRowVisible(permissionWarning, 7, true);
            // AutoSize rows reserve space only while the warning is visible.
            // Reflow the card and dialog before applying the screen-height limit.
            soundCard.PerformLayout();
            layout.PerformLayout();
            FitSettingsToScreen(layout);
            if (AutoScroll) ScrollControlIntoView(permissionWarning);
        }
        void UpdateSoundSource()
        {
            PathInput.Enabled = CustomSound.Checked;
            SetSoundRowVisible(pathRow, 2, !ExternalSound.Checked && !PlaylistSound.Checked);
            SetSoundRowVisible(playlistRow, 6, PlaylistSound.Checked);
            SetSoundRowVisible(linkRow, 3, ExternalSound.Checked);
            SetSoundRowVisible(linkHelp, 4, ExternalSound.Checked);
            SetSoundRowVisible(permissionWarning, 7, false);
            if (!ExternalSound.Checked) SetSoundRowVisible(downloadProgress, 5, false);
            FitSoundCard();
            if (IsHandleCreated) BeginInvoke(() => FitSettingsToScreen(layout));
        }
        DefaultSound.CheckedChanged += (_, _) => UpdateSoundSource();
        CustomSound.CheckedChanged += (_, _) => UpdateSoundSource();
        ExternalSound.CheckedChanged += (_, _) => UpdateSoundSource();
        PlaylistSound.CheckedChanged += (_, _) => UpdateSoundSource();
        UpdateSoundSource();
        layout.Controls.Add(soundCard, 0, 2);
        var volumeCard = DialogLayout.Section(3);
        volumeCard.Controls.Add(DialogLayout.SectionTitle("음량"), 0, 0);
        var volumeRow = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = TimerTheme.Card, Margin = new Padding(0, 4, 0, 12)
        };
        volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        VolumeInput.Value = settings.Volume;
        VolumeInput.AutoSize = false;
        VolumeInput.Height = 30;
        VolumeInput.Dock = DockStyle.Fill;
        VolumeInput.BackColor = TimerTheme.Card;
        VolumeInput.Margin = new Padding(0, 10, 0, 0);
        VolumeNumber.Value = settings.Volume;
        VolumeNumber.Width = 75;
        VolumeNumber.Margin = Padding.Empty;
        VolumeNumber.TextAlign = HorizontalAlignment.Center;
        TimerTheme.StyleNumber(VolumeNumber);
        var volumeField = DialogLayout.InputFrame(VolumeNumber);
        volumeField.Dock = DockStyle.None;
        volumeField.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        volumeField.Margin = new Padding(12, 0, 0, 0);
        VolumeInput.ValueChanged += (_, _) => {
            if (synchronizing) return;
            synchronizing = true;
            VolumeNumber.Value = VolumeInput.Value;
            synchronizing = false;
        };
        VolumeNumber.ValueChanged += (_, _) => {
            if (synchronizing) return;
            synchronizing = true;
            VolumeInput.Value = (int)VolumeNumber.Value;
            synchronizing = false;
        };
        VolumeNumber.Leave += (_, _) => CommitVolumeEdit();
        VolumeInput.Leave += (_, _) => CommitVolumeEdit();
        VolumeInput.MouseCaptureChanged += (_, _) => {
            if (!VolumeInput.Capture) CommitVolumeEdit();
        };
        volumeRow.Controls.Add(VolumeInput, 0, 0);
        volumeRow.Controls.Add(volumeField, 1, 0);
        volumeCard.Controls.Add(volumeRow, 0, 1);
        volumeCard.Controls.Add(DialogLayout.Description("0 무음  ·  50 절반 음량  ·  100 원본 음량"), 0, 2);
        layout.Controls.Add(volumeCard, 0, 3);
        layout.Controls.Add(error, 0, 4);
        Control? actions = null;
        void SetPreparing(bool value)
        {
            preparingLink = value;
            modeCard.Enabled = soundCard.Enabled = volumeCard.Enabled = !value;
            if (actions is not null)
                foreach (var button in actions.Controls.OfType<Button>())
                    if (button.DialogResult != DialogResult.Cancel) button.Enabled = !value;
        }
        actions = DialogLayout.Actions(this, async () => {
            if (preparingLink) return;
            SetSoundRowVisible(permissionWarning, 7, false);
            var external = ExternalSound.Checked;
            var path = DefaultSound.Checked || PlaylistSound.Checked ? "" : external ? LinkInput.Text.Trim() : PathInput.Text.Trim();
            if (PlaylistSound.Checked && (playlistItems.Count == 0 || playlistItems.Any(item =>
                item.ExternalLink ? !OnlineAlarmSource.IsLink(item.Path) : !File.Exists(item.Path))))
            {
                error.Text = "재생 목록 설정에서 사용할 파일이나 외부 링크를 등록하고 확인하세요.";
                FitSettingsToScreen(layout);
                return;
            }
            if (external && !OnlineAlarmSource.IsLink(path))
            {
                error.Text = "http 또는 https로 시작하는 영상·오디오 링크를 입력하세요.";
                return;
            }
            var onlineItems = PlaylistSound.Checked
                ? playlistItems.Where(item => item.ExternalLink).Select(item => item.Path).Distinct().ToList()
                : external ? [path] : new List<string>();
            if (onlineItems.Count > 0 && !ExternalLinkPolicy.UsageConfirmed)
            {
                ShowPermissionWarning();
                return;
            }
            if (CustomSound.Checked && !File.Exists(path))
            {
                error.Text = "알람으로 사용할 파일을 선택하거나 내장 멜로디를 선택하세요.";
                return;
            }
            var volume = (int)VolumeNumber.Value;
            var reverse = ReverseAlarm.Checked;
            var token = linkPreparation.Token;
            try
            {
                if (onlineItems.Count > 0)
                {
                    SetPreparing(true);
                    var attempt = ++downloadAttempt;
                    downloadProgress.UpdateProgress(new OnlineDownloadProgress(null, "다운로드 준비 중"));
                    SetSoundRowVisible(downloadProgress, 5, true);
                    var reporter = new Progress<OnlineDownloadProgress>(value => {
                        if (IsDisposed || Disposing || !preparingLink || attempt != downloadAttempt || token.IsCancellationRequested) return;
                        downloadProgress.UpdateProgress(value);
                    });
                    error.ForeColor = TimerTheme.Muted;
                    error.Text = "링크 오디오를 준비하고 있습니다. 취소하면 중단됩니다.";
                    FitSettingsToScreen(layout);
                    foreach (var link in onlineItems)
                        await Task.Run(() => OnlineAlarmSource.PrepareAsync(link, token, reporter), token);
                    if (IsDisposed || Disposing || token.IsCancellationRequested) return;
                }
                var message = save(new AlarmSoundSettings(path, volume, reverse, PlaylistSound.Checked,
                    playlistItems.Select(item => item with { }).ToList(), shufflePlaylist, playlistPresetId));
                if (message is not null) { error.Text = message; return; }
                savedVolume = volume;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    if (onlineItems.Count > 0 && ex.Message == ExternalLinkPolicy.ConfirmationRequiredMessage)
                        ShowPermissionWarning();
                    else
                    {
                        error.Text = ex.Message;
                        if (onlineItems.Count > 0) downloadProgress.ShowFailure();
                    }
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetPreparing(false);
                    error.ForeColor = Color.FromArgb(255, 160, 160);
                    FitSettingsToScreen(layout);
                }
            }
        });
        layout.Controls.Add(actions, 0, 5);
        DialogLayout.Complete(this, layout);
        Shown += (_, _) => FitSettingsToScreen(layout, resetPosition: true);
    }

    private void FitSettingsToScreen(Control layout, bool resetPosition = false)
    {
        if (IsDisposed || Disposing) return;
        fitSoundCard?.Invoke();
        if (!IsHandleCreated) return;
        var area = MainWorkingArea;
        var margin = (int)Math.Round(16 * DeviceDpi / 96d);
        var maximumHeight = Math.Max(1, area.Height - (Height - ClientSize.Height) - margin * 2);
        if (layout.GetPreferredSize(new Size(ClientSize.Width, 0)).Height <= maximumHeight) return;
        SuspendLayout();
        AutoSize = false;
        AutoScroll = true;
        layout.Dock = DockStyle.Top;
        ClientSize = new Size(ClientSize.Width, maximumHeight);
        ResumeLayout(performLayout: true);
        KeepPopupOnScreen();
        if (resetPosition)
        {
            ActiveControl = ReverseAlarm.Checked ? ReverseAlarm : NormalAlarm;
            AutoScrollPosition = Point.Empty;
        }
    }

    public bool IsVolumeInputTarget(Control? target)
        => (VolumeNumber.ContainsFocus && IsInside(target, VolumeNumber))
            || (VolumeInput.ContainsFocus && IsInside(target, VolumeInput));

    private static bool IsInside(Control? target, Control editor)
    {
        for (var control = target; control is not null; control = control.Parent)
            if (control == editor) return true;
        return false;
    }

    public bool CommitVolumeEdit()
    {
        if (committingVolume || Disposing || IsDisposed) return true;
        committingVolume = true;
        try
        {
            var volume = (int)VolumeNumber.Value; // Validate the complete typed number first.
            if (volume == savedVolume) return true;
            var message = saveVolume(volume);
            if (message is not null) { error.Text = message; return false; }
            savedVolume = volume;
            error.Text = "";
            return true;
        }
        finally { committingVolume = false; }
    }

    public bool CommitVolumeOutside(Control? target)
    {
        if ((!VolumeNumber.ContainsFocus && !VolumeInput.ContainsFocus) || IsVolumeInputTarget(target)) return true;
        if (!CommitVolumeEdit()) return false;
        ActiveControl = null;
        return true;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Enter && (VolumeNumber.ContainsFocus || VolumeInput.ContainsFocus))
        {
            if (CommitVolumeEdit()) ActiveControl = null;
            return true; // Save the volume without triggering the dialog's default Save button.
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        CommitVolumeEdit();
        base.OnDeactivate(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !CommitVolumeEdit()) e.Cancel = true;
        base.OnFormClosing(e);
        if (!e.Cancel) linkPreparation.Cancel();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            linkPreparation.Cancel();
            linkPreparation.Dispose();
            hints.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class DialogLayout
{
    public static void Configure(Form form, string title, int width)
    {
        form.SuspendLayout();
        form.Text = title;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.StartPosition = FormStartPosition.Manual;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.ShowInTaskbar = false;
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.BackColor = TimerTheme.Background;
        form.ForeColor = TimerTheme.Foreground;
        form.Font = TimerTypography.Create(10F);
        form.AutoSize = true;
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        form.MinimumSize = new Size(width, 0);
        TimerTheme.ApplyWindow(form);
    }

    public static void Complete(Form form, Control layout)
    {
        form.Controls.Add(layout);
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.ResumeLayout(false);
        form.PerformLayout();
    }

    public static TableLayoutPanel Create(int rows)
    {
        var table = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(28), ColumnCount = 1, RowCount = rows,
            BackColor = TimerTheme.Background
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table;
    }

    public static Control Header(string category, string title, string description)
    {
        var header = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3,
            Margin = new Padding(0, 0, 0, 24), BackColor = TimerTheme.Background
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++) header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(new Label {
            Text = category, AutoSize = true, ForeColor = TimerTheme.Accent,
            Font = TimerTypography.Create(9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);
        header.Controls.Add(new Label {
            Text = title, AutoSize = true, Font = TimerTypography.Create(22F, FontStyle.Bold),
            ForeColor = TimerTheme.Foreground, Margin = new Padding(0, 0, 0, 8)
        }, 0, 1);
        var caption = Description(description);
        caption.Margin = Padding.Empty;
        header.Controls.Add(caption, 0, 2);
        return header;
    }

    public static TableLayoutPanel Section(int rows)
    {
        var panel = new PopupSectionPanel {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = rows, Padding = new Padding(20),
            Margin = new Padding(0, 0, 0, 14), BackColor = TimerTheme.Card
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return panel;
    }

    public static Label SectionTitle(string text) => new() {
        Text = text, AutoSize = true, Font = TimerTypography.Create(10F, FontStyle.Bold),
        ForeColor = TimerTheme.Foreground, Margin = new Padding(0, 0, 0, 12)
    };

    public static Control InputFrame(Control input)
    {
        var frame = new PopupInputPanel {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 10), BackColor = TimerTheme.Elevated,
            ColumnCount = 1, RowCount = 1
        };
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (input is TextBoxBase text) text.BorderStyle = BorderStyle.None;
        if (input is NumericUpDown number)
        {
            number.BorderStyle = BorderStyle.None;
            foreach (Control child in number.Controls)
                if (child is TextBoxBase edit) edit.BorderStyle = BorderStyle.None;
        }
        input.Dock = DockStyle.Fill;
        input.BackColor = TimerTheme.Elevated;
        frame.Controls.Add(input, 0, 0);
        input.Enter += (_, _) => frame.Invalidate();
        input.Leave += (_, _) => frame.Invalidate();
        return frame;
    }
    public static Label Description(string text) => new() {
        Text = text, AutoSize = true, MaximumSize = new Size(520, 0),
        ForeColor = TimerTheme.Muted, Margin = Padding.Empty
    };
    public static Label Error() => new() {
        AutoSize = true, MaximumSize = new Size(520, 0),
        ForeColor = Color.FromArgb(255, 160, 160), Margin = new Padding(0, 0, 0, 8)
    };
    public static Control Actions(Form form, Action save)
    {
        var row = new FlowLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0, 10, 0, 0)
        };
        var cancel = TimerTheme.Button("취소");
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(0, 0, 10, 0);
        var saveButton = TimerTheme.Button("저장", primary: true);
        TimerTheme.SetIcon(saveButton, TimerGlyph.Check);
        saveButton.Margin = Padding.Empty;
        saveButton.Click += (_, _) => save();
        row.Controls.Add(saveButton);
        row.Controls.Add(cancel);
        form.AcceptButton = saveButton;
        form.CancelButton = cancel;
        form.Shown += (_, _) => saveButton.Focus();
        return row;
    }
}

internal class PopupSectionPanel : TableLayoutPanel
{
    public PopupSectionPanel() => DoubleBuffered = true;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TimerTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = PopupDrawing.Rounded(ClientRectangle, 12F * DeviceDpi / 96F);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(TimerTheme.Border);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
    }
}

internal sealed class PopupInputPanel : PopupSectionPanel
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!ContainsFocus) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = PopupDrawing.Rounded(ClientRectangle, 12F * DeviceDpi / 96F);
        using var pen = new Pen(TimerTheme.Accent, Math.Max(1F, DeviceDpi / 96F));
        e.Graphics.DrawPath(pen, shape);
    }
}

internal sealed class PopupCheckBox : CheckBox
{
    public PopupCheckBox()
    {
        // Let WinForms paint the indicator and text together, including DPI changes and focus.
        FlatStyle = FlatStyle.Flat;
        BackColor = TimerTheme.Card;
        ForeColor = TimerTheme.Foreground;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }
}

internal sealed class PopupRadioButton : RadioButton
{
    public PopupRadioButton()
    {
        FlatStyle = FlatStyle.Flat;
        BackColor = TimerTheme.Card;
        ForeColor = TimerTheme.Foreground;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }
}

internal sealed class PopupVolumeTrackBar : TrackBar
{
    public PopupVolumeTrackBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnValueChanged(EventArgs e) { base.OnValueChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!IsHandleCreated) return;
        var channel = new NativeRect();
        var thumb = new NativeRect();
        SendMessage(Handle, 0x0400 + 26, IntPtr.Zero, ref channel);
        SendMessage(Handle, 0x0400 + 25, IntPtr.Zero, ref thumb);
        var scale = DeviceDpi / 96F;
        var x = (thumb.Left + thumb.Right) / 2F;
        var y = (thumb.Top + thumb.Bottom) / 2F;
        var halfThumb = (thumb.Right - thumb.Left) / 2F;
        var left = channel.Left + halfThumb;
        var right = channel.Right - halfThumb;
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = new Pen(TimerTheme.Border, 4F * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(TimerTheme.Accent, 4F * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(track, left, y, right, y);
        if (x > left) e.Graphics.DrawLine(active, left, y, Math.Min(x, right), y);
        var radius = 7F * scale;
        using var fill = new SolidBrush(Enabled ? TimerTheme.Accent : TimerTheme.Muted);
        e.Graphics.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(TimerTheme.Accent, scale);
            radius += 4F * scale;
            e.Graphics.DrawEllipse(focus, x - radius, y - radius, radius * 2, radius * 2);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref NativeRect rectangle);
}

internal static class PopupDrawing
{
    public static GraphicsPath Rounded(Rectangle rectangle, float radius)
    {
        var rect = new RectangleF(rectangle.X + .5F, rectangle.Y + .5F,
            Math.Max(1, rectangle.Width - 1F), Math.Max(1, rectangle.Height - 1F));
        var diameter = Math.Min(radius * 2F, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

}
