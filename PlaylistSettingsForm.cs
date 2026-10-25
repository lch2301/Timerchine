using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed class PlaylistSettingsForm : TimerPopupForm
{
    private readonly List<PlaylistFileCard> cards = [];
    private readonly TimerScrollPanel scroller = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    private readonly Panel cardList = new() { Margin = Padding.Empty };
    private readonly PopupCheckBox shuffle = new() { Text = "무작위 재생", AutoSize = true, BackColor = TimerTheme.Background };
    private readonly Label error = DialogLayout.Error();
    private readonly Label empty = DialogLayout.Description("파일 추가 버튼을 눌러 알람 파일을 등록하세요.");
    private readonly PlaylistPresetStore presetStore;
    private readonly Label presetCaption = new PresetCaptionLabel() {
        Text = "프리셋: 선택 안 함", AutoSize = false, AutoEllipsis = true,
        Dock = DockStyle.Fill, Height = 24, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = TimerTheme.Muted, Margin = new Padding(0, 6, 12, 0)
    };
    private bool arranging;
    private bool initialized;
    private bool preparing;
    private bool resourcesDisposed;
    private int downloadAttempt;
    private readonly CancellationTokenSource preparation = new();
    private Control? headerArea;
    private Control? actions;
    public List<AlarmPlaylistItem> Items { get; private set; } = [];
    public bool Shuffle => shuffle.Checked;
    public Guid? SelectedPresetId { get; private set; }

    public PlaylistSettingsForm(IEnumerable<AlarmPlaylistItem> items, bool random, Guid? selectedPresetId = null,
        PlaylistPresetStore? presetStore = null)
    {
        this.presetStore = presetStore ?? new PlaylistPresetStore(PlaylistPresetStore.DefaultPath);
        DialogLayout.Configure(this, "재생 목록 설정", 720);
        AutoSize = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(580, 420);
        ClientSize = new Size(720, 650);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 5,
            BackColor = TimerTheme.Background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 0, 0, 14)
        };
        headerArea = header;
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label {
            Text = "재생 목록", Font = TimerTypography.Create(22, FontStyle.Bold),
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty
        };
        var titleArea = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty
        };
        titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleArea.Controls.Add(title, 0, 0);
        titleArea.Controls.Add(presetCaption, 0, 1);
        header.Controls.Add(titleArea, 0, 0);
        var tools = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tools.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var presets = TimerTheme.Button("프리셋");
        TimerTheme.SetIcon(presets, TimerGlyph.Folder);
        presets.Margin = new Padding(0, 0, 8, 0);
        presets.Anchor = AnchorStyles.Right;
        presets.Click += (_, _) => OpenPresets();
        tools.Controls.Add(presets, 0, 0);
        tools.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var add = TimerTheme.Button("파일 추가");
        TimerTheme.SetIcon(add, TimerGlyph.Add);
        add.Margin = Padding.Empty;
        add.Anchor = AnchorStyles.Right;
        add.Click += (_, _) => { AddCard(new()); scroller.Reveal(cards[^1]); };
        tools.Controls.Add(add, 1, 0);
        shuffle.Checked = random;
        shuffle.Font = TimerTypography.Create(9);
        shuffle.Anchor = AnchorStyles.Right;
        shuffle.Margin = new Padding(0, 9, 0, 0);
        tools.Controls.Add(shuffle, 0, 1);
        tools.SetColumnSpan(shuffle, 2);
        header.Controls.Add(tools, 1, 0);
        layout.Controls.Add(header, 0, 0);
        var help = DialogLayout.Description("파일 또는 외부 링크를 등록하세요. 마지막 항목 뒤에는 목록을 반복합니다.");
        help.Margin = new Padding(0, 0, 0, 14);
        help.MaximumSize = Size.Empty;
        layout.Controls.Add(help, 0, 1);
        cardList.Controls.Add(empty);
        empty.Margin = Padding.Empty;
        scroller.Controls.Add(cardList);
        scroller.SizeChanged += (_, _) => ArrangeCards();
        layout.Controls.Add(scroller, 0, 2);
        error.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(error, 0, 3);
        layout.SizeChanged += (_, _) => {
            var width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal);
            help.MaximumSize = error.MaximumSize = new Size(width, 0);
        };
        actions = DialogLayout.Actions(this, SavePlaylist);
        layout.Controls.Add(actions, 0, 4);
        foreach (var item in items) AddCard(item with { });
        try
        {
            var availablePresets = this.presetStore.Load();
            var selected = selectedPresetId is null
                ? availablePresets.First(preset => preset.Name == PlaylistPresetStore.DefaultName)
                : availablePresets.FirstOrDefault(preset => preset.Id == selectedPresetId);
            if (selected is not null) SetSelectedPreset(selected);
            else if (selectedPresetId == Guid.Empty) SetSelectedPreset(null);
            else if (selectedPresetId != Guid.Empty)
                error.Text = "이전에 선택한 프리셋이 없습니다. 프리셋을 다시 선택하세요.";
        }
        catch (Exception ex) { error.Text = ex.Message; }
        DialogLayout.Complete(this, layout);
        initialized = true;
        Shown += (_, _) => ArrangeCards();
    }

    private void OpenPresets()
    {
        if (preparing) return;
        CommitEditorsOutside(null);
        using var dialog = new PlaylistPresetsForm(presetStore, SelectedPresetId);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ApplyPreset(dialog.SelectedPreset, dialog.PresetCreated);
    }

    private void SetSelectedPreset(PlaylistPreset? preset)
    {
        // Null means a new timer; Guid.Empty records an explicit selection removal.
        SelectedPresetId = preset?.Id ?? Guid.Empty;
        presetCaption.Text = preset is null ? "프리셋: 선택 안 함" : $"프리셋: {preset.Name}";
        presetCaption.AccessibleName = presetCaption.Text;
    }

    private void ApplyPreset(PlaylistPreset? preset, bool created)
    {
        SetSelectedPreset(preset);
        error.Text = "";
        // Creating a name selects its destination without capturing or replacing the draft.
        if (preset is null || created) return;
        foreach (var card in cards) { cardList.Controls.Remove(card); card.Dispose(); }
        cards.Clear();
        scroller.AutoScrollPosition = Point.Empty;
        foreach (var item in preset.Items) AddCard(item with { });
        shuffle.Checked = preset.Shuffle;
        ArrangeCards();
    }

    private void AddCard(AlarmPlaylistItem item)
    {
        var card = new PlaylistFileCard(item);
        card.LayoutRequested += ArrangeCards;
        card.ErrorOccurred += message => error.Text = message;
        card.DeleteRequested += () => {
            cards.Remove(card); cardList.Controls.Remove(card); card.Dispose();
            ArrangeCards();
        };
        cards.Add(card);
        cardList.Controls.Add(card);
        if (initialized && card.DeviceDpi != 96) card.Scale(new SizeF(card.DeviceDpi / 96F, card.DeviceDpi / 96F));
        ArrangeCards();
    }

    private void ArrangeCards()
    {
        if (arranging || scroller.IsDisposed) return;
        arranging = true;
        try
        {
            var gap = (int)Math.Round(8 * DeviceDpi / 96d);
            for (var pass = 0; pass < 3; pass++)
            {
                var width = Math.Max(1, scroller.ClientSize.Width - 2);
                var y = 0;
                for (var i = 0; i < cards.Count; i++)
                {
                    var card = cards[i];
                    card.SetNumber(i + 1);
                    card.SetBounds(1, y, width, card.PreferredHeight(width));
                    y += card.Height + gap;
                }
                empty.Visible = cards.Count == 0;
                empty.MaximumSize = new Size(width, 0);
                empty.Location = new Point(0, gap);
                cardList.Size = new Size(width + 2, cards.Count == 0 ? empty.PreferredHeight + gap * 2 : y - gap);
                var before = scroller.ClientSize.Width;
                scroller.PerformLayout();
                if (before == scroller.ClientSize.Width) break;
            }
        }
        finally { arranging = false; }
    }

    private async void SavePlaylist()
    {
        if (preparing) return;
        CommitEditorsOutside(null);
        error.Text = "";
        if (cards.Count == 0) { error.Text = "알람으로 사용할 파일을 한 개 이상 등록하세요."; return; }
        var items = new List<AlarmPlaylistItem>();
        for (var i = 0; i < cards.Count; i++)
        {
            var item = cards[i].ReadItem();
            if (item.ExternalLink && (!ExternalLinkPolicy.IsEnabled || !ExternalLinkPolicy.UsageConfirmed))
            {
                error.Text = ExternalLinkPolicy.ConfirmationRequiredMessage;
                scroller.Reveal(cards[i]);
                return;
            }
            if (item.ExternalLink ? !OnlineAlarmSource.IsLink(item.Path) : !File.Exists(item.Path))
            {
                error.Text = item.ExternalLink ? $"파일 {i + 1}에 http 또는 https로 시작하는 영상·오디오 링크를 입력하세요."
                    : $"파일 {i + 1}의 경로를 확인하고 실제 파일을 선택하세요.";
                scroller.Reveal(cards[i]);
                return;
            }
            if (!item.ExternalLink) item.Path = Path.GetFullPath(item.Path);
            items.Add(item);
        }
        var token = preparation.Token;
        var attempt = ++downloadAttempt;
        try
        {
            SetPreparing(true);
            for (var i = 0; i < items.Count; i++)
            {
                var card = cards[i];
                if (items[i].ExternalLink)
                {
                    card.ReportProgress(new OnlineDownloadProgress(null, "다운로드 준비 중"));
                    scroller.Reveal(card);
                    var reporter = new Progress<OnlineDownloadProgress>(value => {
                        if (IsDisposed || Disposing || token.IsCancellationRequested || !preparing || attempt != downloadAttempt) return;
                        card.ReportProgress(value);
                    });
                    error.Text = $"파일 {i + 1}의 오디오를 준비하고 있습니다. 취소하면 중단됩니다.";
                    var prepared = await Task.Run(() => OnlineAlarmSource.PrepareAsync(items[i].Path, token, reporter), token);
                    if (IsDisposed || Disposing || token.IsCancellationRequested) return;
                    card.ApplyTitle(items[i].Path, prepared.Title);
                }
                try
                {
                    var source = card.ReadItem().Path;
                    var preview = await Task.Run(() => MediaThumbnailStore.LoadAsync(source, items[i].ExternalLink, token), token);
                    if (IsDisposed || Disposing || token.IsCancellationRequested) return;
                    card.ApplyPreview(source, items[i].ExternalLink, preview);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { /* A missing thumbnail must never block an alarm from saving. */ }
                var updated = card.ReadItem();
                items[i].Title = updated.Title;
                items[i].ThumbnailPath = updated.ThumbnailPath;
            }
            if (SelectedPresetId is Guid presetId && presetId != Guid.Empty)
                presetStore.SavePlaylist(presetId, items, Shuffle);
            Items = items;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing) error.Text = ex.Message;
        }
        finally { if (!IsDisposed && !Disposing) SetPreparing(false); }
    }

    private void SetPreparing(bool value)
    {
        preparing = value;
        cardList.Enabled = !value;
        if (headerArea is not null) headerArea.Enabled = !value;
        if (actions is not null)
            foreach (var button in actions.Controls.OfType<Button>())
                if (button.DialogResult != DialogResult.Cancel) button.Enabled = !value;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        preparation.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            preparation.Cancel();
            preparation.Dispose();
        }
        base.Dispose(disposing);
    }

    public bool IsEditorTarget(Control? control) => cards.Any(card => card.IsEditorTarget(control));
    public bool IsPathTarget(Control? control) => cards.Any(card => card.IsPathTarget(control));
    public void CommitEditorsOutside(Control? target)
    {
        foreach (var card in cards) if (!card.IsEditorTarget(target)) card.CommitEditors();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Enter)
        {
            foreach (var card in cards) card.CommitEditors();
            ActiveControl = null;
            return true;
        }
        if ((keyData & Keys.KeyCode) == Keys.Space && !cards.Any(card => card.PathHasFocus)) return true;
        return base.ProcessCmdKey(ref message, keyData);
    }
}

internal sealed class PresetCaptionLabel : Label
{
    // A long name must fit the available title column rather than widen the header.
    public override Size GetPreferredSize(Size proposedSize) => new(1, Math.Max(Height, Font.Height + 4));

    protected override void OnPaint(PaintEventArgs e) => TextRenderer.DrawText(e.Graphics, Text, Font,
        ClientRectangle, ForeColor, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
            | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
}

internal sealed class PlaylistFileCard : UserControl
{
    private readonly TableLayoutPanel content = DialogLayout.Section(3);
    private readonly Label title = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    private readonly MediaThumbnailView thumbnail = new() { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
    private readonly TextBox path = new() { PlaceholderText = "알람으로 사용할 파일 경로" };
    private readonly RadioButton local = new PopupRadioButton { Text = "내 파일", AutoSize = true, Margin = new Padding(0, 0, 14, 0) };
    private readonly RadioButton external = new PopupRadioButton { Text = ExternalLinkPolicy.DisplayName, AutoSize = true,
        Enabled = ExternalLinkPolicy.IsEnabled, Margin = Padding.Empty };
    private readonly DownloadProgressReadout progress = new() { Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0, 8, 0, 0) };
    private string fileDraft = "";
    private string linkDraft = "";
    private bool externalMode;
    private bool showingProgress;
    private readonly System.Windows.Forms.Timer previewDelay = new() { Interval = 700 };
    private CancellationTokenSource? previewLookup;
    private string titleUrl = "";
    private string resolvedTitle = "";
    private string requestedPreviewSource = "";
    private bool loadingTitle;
    private string thumbnailSource = "";
    private string thumbnailPath = "";
    private int number;
    public event Action? DeleteRequested;
    public event Action? LayoutRequested;
    public event Action<string>? ErrorOccurred;
    public bool PathHasFocus => path.ContainsFocus;

    public PlaylistFileCard(AlarmPlaylistItem item)
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = TimerTheme.Background;
        ForeColor = TimerTheme.Foreground;
        Font = TimerTypography.Create(10);
        DoubleBuffered = true;
        content.Padding = new Padding(12);
        content.Margin = Padding.Empty;
        content.Dock = DockStyle.Fill;
        content.ColumnCount = 2;
        content.ColumnStyles.Clear();
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(thumbnail, 0, 0);
        content.SetRowSpan(thumbnail, 2);
        var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        title.Font = TimerTypography.Create(10, FontStyle.Bold);
        title.Height = TextRenderer.MeasureText("파일", title.Font).Height + 4;
        header.Controls.Add(title, 0, 0);
        var sources = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right,
            BackColor = TimerTheme.Card, Margin = new Padding(8, 0, 0, 0), Font = TimerTypography.Create(9) };
        sources.Controls.Add(local);
        sources.Controls.Add(external);
        header.Controls.Add(sources, 1, 0);
        var delete = TimerTheme.Button("삭제");
        TimerTheme.SetIcon(delete, TimerGlyph.Delete);
        delete.Font = TimerTypography.Create(8.5F);
        delete.Padding = new Padding(6, 3, 6, 3);
        delete.Margin = new Padding(10, 0, 0, 0);
        delete.Anchor = AnchorStyles.Right;
        delete.Click += (_, _) => DeleteRequested?.Invoke();
        header.Controls.Add(delete, 2, 0);
        content.Controls.Add(header, 1, 0);
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        path.Text = item.Path;
        path.Margin = Padding.Empty;
        TimerTheme.StyleTextBox(path);
        var frame = DialogLayout.InputFrame(path);
        frame.Padding = new Padding(10, 7, 10, 7);
        frame.Margin = new Padding(0, 0, 10, 0);
        frame.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        row.Controls.Add(frame, 0, 0);
        var browse = TimerTheme.Button("파일 선택");
        TimerTheme.SetIcon(browse, TimerGlyph.Folder);
        browse.Margin = Padding.Empty;
        browse.Padding = new Padding(10, 6, 10, 6);
        browse.Anchor = AnchorStyles.Right;
        browse.Click += (_, _) => {
            if (externalMode)
            {
                try
                {
                    var audio = OnlineAlarmSource.GetPreparedAudio(path.Text.Trim());
                    if (audio is null)
                    {
                        ErrorOccurred?.Invoke("다운로드된 파일이 없습니다. 목록을 저장해 먼저 오디오를 다운로드하세요.");
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
                catch (Exception ex) { ErrorOccurred?.Invoke($"다운로드된 파일 위치를 열 수 없습니다.\n{ex.Message}"); }
                return;
            }
            using var picker = new OpenFileDialog { Title = "재생 목록 파일 선택", Filter = "모든 파일 (*.*)|*.*", CheckFileExists = true, AddExtension = false };
            if (File.Exists(path.Text)) picker.FileName = path.Text;
            if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
            path.Text = picker.FileName;
            UpdateTitle();
        };
        path.Leave += (_, _) => CommitEditors();
        row.Controls.Add(browse, 1, 0);
        content.Controls.Add(row, 1, 1);
        content.Controls.Add(progress, 1, 2);
        externalMode = item.ExternalLink || OnlineAlarmSource.IsLink(item.Path);
        if (externalMode) linkDraft = item.Path; else fileDraft = item.Path;
        void UpdateSource()
        {
            var next = external.Checked;
            if (externalMode != next)
            {
                CancelPreviewLookup();
                previewDelay.Stop();
                thumbnailSource = thumbnailPath = "";
                thumbnail.SetImage("");
                if (externalMode) linkDraft = path.Text; else fileDraft = path.Text;
                externalMode = next;
                path.Text = next ? linkDraft : fileDraft;
            }
            path.PlaceholderText = next ? "영상·오디오의 https:// 링크 · 저장 시 다운로드" : "알람으로 사용할 파일 경로";
            path.AccessibleName = next ? "재생 목록 외부 링크" : "재생 목록 파일 경로";
            browse.Text = next ? "파일 위치" : "파일 선택";
            browse.AccessibleName = next ? "다운로드된 파일 위치 열기" : "알람 파일 선택";
            showingProgress = false;
            progress.Visible = false;
            UpdateTitle();
            if (IsHandleCreated) FetchPreview();
            LayoutRequested?.Invoke();
        }
        local.Checked = !externalMode;
        external.Checked = externalMode;
        external.CheckedChanged += (_, _) => UpdateSource();
        UpdateSource();
        if (externalMode && !string.IsNullOrWhiteSpace(item.Title))
        {
            titleUrl = path.Text.Trim();
            resolvedTitle = item.Title.Trim();
        }
        if (MediaThumbnailStore.IsCachedImage(item.ThumbnailPath ?? ""))
        {
            thumbnailSource = path.Text.Trim();
            thumbnailPath = item.ThumbnailPath ?? "";
            thumbnail.SetImage(thumbnailPath);
            if (!externalMode || resolvedTitle.Length > 0) requestedPreviewSource = thumbnailSource;
        }
        path.TextChanged += (_, _) => {
            CancelPreviewLookup();
            previewDelay.Stop();
            titleUrl = resolvedTitle = requestedPreviewSource = "";
            thumbnailSource = thumbnailPath = "";
            thumbnail.SetImage("");
            loadingTitle = false;
            UpdateTitle();
            if (externalMode ? OnlineAlarmSource.IsLink(path.Text.Trim()) && ExternalLinkPolicy.UsageConfirmed : File.Exists(path.Text.Trim()))
                previewDelay.Start();
        };
        previewDelay.Tick += (_, _) => { previewDelay.Stop(); FetchPreview(); };
        Controls.Add(content);
        AutoScaleDimensions = new SizeF(96F, 96F);
        ResumeLayout(true);
    }

    public void SetNumber(int value) { number = value; UpdateTitle(); }
    private void UpdateTitle()
    {
        var value = path.Text.Trim();
        var caption = externalMode
            ? titleUrl == value && resolvedTitle.Length > 0 ? resolvedTitle : loadingTitle ? "제목 확인 중…" : "외부 링크"
            : Path.GetFileName(value);
        title.Text = $"파일 {number}" + (value.Length == 0 ? "" : $" · {caption}");
        title.AccessibleName = title.Text;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(new Action(FetchPreview));
    }

    private async void FetchPreview()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        var url = path.Text.Trim();
        var mode = externalMode;
        if (mode ? !ExternalLinkPolicy.UsageConfirmed || !OnlineAlarmSource.IsLink(url) : !File.Exists(url)) return;
        if (requestedPreviewSource == url) return;
        CancelPreviewLookup();
        using var lookup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        previewLookup = lookup;
        requestedPreviewSource = url;
        loadingTitle = mode && (titleUrl != url || resolvedTitle.Length == 0);
        UpdateTitle();
        try
        {
            var value = await Task.Run(() => MediaThumbnailStore.LoadAsync(url, mode, lookup.Token), lookup.Token);
            if (IsDisposed || Disposing || !ReferenceEquals(previewLookup, lookup) || mode != externalMode || path.Text.Trim() != url) return;
            ApplyPreview(url, mode, value);
        }
        catch (OperationCanceledException) { }
        catch { /* Metadata failure leaves a neutral caption and does not block playback. */ }
        finally
        {
            if (ReferenceEquals(previewLookup, lookup))
            {
                previewLookup = null;
                loadingTitle = false;
                if (!IsDisposed && !Disposing) UpdateTitle();
            }
        }
    }

    public void ApplyTitle(string url, string value)
    {
        if (!externalMode || path.Text.Trim() != url || string.IsNullOrWhiteSpace(value)) return;
        CancelPreviewLookup();
        previewDelay.Stop();
        titleUrl = url;
        resolvedTitle = value.Trim();
        loadingTitle = false;
        UpdateTitle();
    }

    public void ApplyPreview(string source, bool external, MediaPreview value)
    {
        if (IsDisposed || Disposing || external != externalMode || path.Text.Trim() != source) return;
        CancelPreviewLookup();
        previewDelay.Stop();
        if (external && !string.IsNullOrWhiteSpace(value.Title))
        {
            titleUrl = source;
            resolvedTitle = value.Title;
        }
        thumbnailSource = source;
        thumbnailPath = MediaThumbnailStore.IsCachedImage(value.ThumbnailPath) ? value.ThumbnailPath : "";
        thumbnail.SetImage(thumbnailPath);
        requestedPreviewSource = source;
        UpdateTitle();
    }

    private void CancelPreviewLookup()
    {
        previewLookup?.Cancel();
        previewLookup = null;
        loadingTitle = false;
        requestedPreviewSource = "";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            previewDelay.Dispose();
            CancelPreviewLookup();
        }
        base.Dispose(disposing);
    }
    public void ReportProgress(OnlineDownloadProgress value)
    {
        progress.UpdateProgress(value);
        if (showingProgress) return;
        showingProgress = true;
        progress.Visible = true;
        LayoutRequested?.Invoke();
    }
    public int PreferredHeight(int width) => content.GetPreferredSize(new Size(width, 0)).Height;
    public AlarmPlaylistItem ReadItem()
    {
        CommitEditors();
        return new() { Path = path.Text.Trim(), ExternalLink = externalMode,
            Title = externalMode && titleUrl == path.Text.Trim() ? resolvedTitle : "",
            ThumbnailPath = thumbnailSource == path.Text.Trim() ? thumbnailPath : "" };
    }
    public void CommitEditors() { if (IsDisposed || Disposing) return; previewDelay.Stop(); UpdateTitle(); FetchPreview(); }
    private static bool Inside(Control? control, Control editor)
    {
        for (; control is not null; control = control.Parent) if (control == editor) return true;
        return false;
    }
    public bool IsEditorTarget(Control? control) => Inside(control, path);
    public bool IsPathTarget(Control? control) => Inside(control, path);
}
