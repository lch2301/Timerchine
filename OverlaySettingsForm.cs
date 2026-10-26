using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IntervalTimer;

/// <summary>Edits a private draft; only Save applies it to the display-only game overlay.</summary>
internal sealed class OverlaySettingsForm : TimerPopupForm
{
    public NumericUpDown HorizontalPositionInput { get; } = Number(0, 100);
    public NumericUpDown VerticalPositionInput { get; } = Number(0, 100);
    public NumericUpDown TransparencyInput { get; } = Number(0, 90);
    public NumericUpDown TimeFontSizeInput { get; } = Number(12, 96, 1);
    public NumericUpDown NameFontSizeInput { get; } = Number(8, 48, 1);
    public NumericUpDown NameAlignmentInput { get; } = Number(0, 100);
    public TrackBar HorizontalPositionSlider { get; } = Slider(100);
    public TrackBar VerticalPositionSlider { get; } = Slider(100);
    public TrackBar TransparencySlider { get; } = Slider(90);
    public TrackBar NameAlignmentSlider { get; } = Slider(100);
    public Button SaveButton { get; } = TimerTheme.Button("저장", primary: true);
    public Button DiscardButton { get; } = TimerTheme.Button("취소");
    public Label ErrorLabel { get; } = DialogLayout.Error();
    public Control PreviewPanel => preview;

    public GameOverlayAppearance Draft => new GameOverlayAppearance {
        HorizontalPosition = (int)HorizontalPositionInput.Value,
        VerticalPosition = (int)VerticalPositionInput.Value,
        Transparency = (int)TransparencyInput.Value,
        TimeFontSize = (float)TimeFontSizeInput.Value,
        NameFontSize = (float)NameFontSizeInput.Value,
        NameAlignmentPercent = (int)NameAlignmentInput.Value
    }.Normalized();

    private readonly OverlayPreviewPanel preview = new();

    public OverlaySettingsForm(GameOverlayAppearance initial, Func<GameOverlayAppearance, string?> save)
    {
        var appearance = initial.Normalized();
        HorizontalPositionInput.Value = appearance.HorizontalPosition;
        VerticalPositionInput.Value = appearance.VerticalPosition;
        TransparencyInput.Value = appearance.Transparency;
        TimeFontSizeInput.Value = (decimal)appearance.TimeFontSize;
        NameFontSizeInput.Value = (decimal)appearance.NameFontSize;
        NameAlignmentInput.Value = appearance.NameAlignmentPercent ?? 100;

        DialogLayout.Configure(this, "오버레이 설정", 640);
        var layout = DialogLayout.Create(6);
        layout.Controls.Add(DialogLayout.Header("게임 화면 표시", "오버레이 설정",
            "위치와 글자 크기를 미리 확인하고 저장하세요."), 0, 0);

        var previewCard = DialogLayout.Section(2);
        previewCard.Controls.Add(DialogLayout.SectionTitle("미리보기"), 0, 0);
        preview.Dock = DockStyle.Fill;
        preview.Margin = Padding.Empty;
        preview.MinimumSize = new Size(440, 212);
        preview.Size = new Size(500, 212);
        previewCard.Controls.Add(preview, 0, 1);
        layout.Controls.Add(previewCard, 0, 1);

        var placementCard = DialogLayout.Section(5);
        placementCard.Controls.Add(DialogLayout.SectionTitle("위치 · 투명도"), 0, 0);
        placementCard.Controls.Add(PositionRow("가로 위치", HorizontalPositionSlider, HorizontalPositionInput), 0, 1);
        placementCard.Controls.Add(PositionRow("세로 위치", VerticalPositionSlider, VerticalPositionInput), 0, 2);
        placementCard.Controls.Add(PositionRow("투명도", TransparencySlider, TransparencyInput), 0, 3);
        placementCard.Controls.Add(DialogLayout.Description(
            "가로 0%는 왼쪽, 100%는 오른쪽입니다. 세로 0%는 위쪽, 100%는 아래쪽입니다.\n투명도를 높일수록 글자가 흐려집니다."), 0, 4);
        layout.Controls.Add(placementCard, 0, 2);

        var fontCard = DialogLayout.Section(4);
        fontCard.Controls.Add(DialogLayout.SectionTitle("글자 크기 · 이름 정렬"), 0, 0);
        var fontInputs = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = TimerTheme.Card, Margin = Padding.Empty
        };
        fontInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fontInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fontInputs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fontInputs.Controls.Add(FontInput("남은 시간", TimeFontSizeInput, new Padding(0, 0, 8, 0)), 0, 0);
        fontInputs.Controls.Add(FontInput("타이머 이름", NameFontSizeInput, new Padding(8, 0, 0, 0)), 1, 0);
        fontCard.Controls.Add(fontInputs, 0, 1);
        var alignmentRow = PositionRow("이름 가로 정렬", NameAlignmentSlider, NameAlignmentInput, 106);
        alignmentRow.Margin = new Padding(0, 14, 0, 10);
        fontCard.Controls.Add(alignmentRow, 0, 2);
        fontCard.Controls.Add(DialogLayout.Description(
            "0%는 시간의 왼쪽, 100%는 오른쪽에 맞춥니다.\n50%에서는 이름과 시간 숫자의 실제 가로 중앙이 일치합니다."), 0, 3);
        layout.Controls.Add(fontCard, 0, 3);
        layout.Controls.Add(ErrorLabel, 0, 4);

        var actions = new FlowLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0, 10, 0, 0)
        };
        TimerTheme.SetIcon(SaveButton, TimerGlyph.Check);
        SaveButton.Margin = Padding.Empty;
        DiscardButton.Margin = new Padding(0, 0, 10, 0);
        DiscardButton.DialogResult = DialogResult.Cancel;
        SaveButton.Click += (_, _) => {
            // Validate the active numeric edit before reading the draft (including typed decimal values).
            ValidateChildren();
            var message = save(Draft);
            if (message is not null) { ErrorLabel.Text = message; return; }
            ErrorLabel.Text = "";
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.Add(SaveButton);
        actions.Controls.Add(DiscardButton);
        AcceptButton = SaveButton;
        CancelButton = DiscardButton;
        layout.Controls.Add(actions, 0, 5);

        Synchronize(HorizontalPositionSlider, HorizontalPositionInput);
        Synchronize(VerticalPositionSlider, VerticalPositionInput);
        Synchronize(TransparencySlider, TransparencyInput);
        Synchronize(NameAlignmentSlider, NameAlignmentInput);
        foreach (var input in new[] { HorizontalPositionInput, VerticalPositionInput, TransparencyInput, TimeFontSizeInput, NameFontSizeInput, NameAlignmentInput })
            input.ValueChanged += (_, _) => UpdatePreview();
        Shown += (_, _) => {
            ActiveControl = HorizontalPositionInput;
            AutoScrollPosition = Point.Empty;
        };
        UpdatePreview();
        DialogLayout.Complete(this, layout);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitHeight(MainWorkingArea.Height - (int)Math.Round(24 * DeviceDpi / 96d));
    }

    private void FitHeight(int maximum)
    {
        if (Height <= maximum || Controls.Count == 0) return;
        AutoSize = false;
        AutoScroll = true;
        Controls[0].Dock = DockStyle.Top;
        Height = Math.Max(240, maximum);
        AutoScrollMargin = new Size(0, 12);
        PerformLayout();
        ActiveControl = HorizontalPositionInput;
        AutoScrollPosition = Point.Empty;
        KeepPopupOnScreen();
    }

    private void UpdatePreview() { preview.Appearance = Draft; preview.Invalidate(); }

    private static NumericUpDown Number(int minimum, int maximum, int decimals = 0)
    {
        var number = new NumericUpDown {
            Minimum = minimum, Maximum = maximum, DecimalPlaces = decimals,
            Increment = decimals == 0 ? 1 : .5m, TextAlign = HorizontalAlignment.Right,
            Width = 64, Margin = Padding.Empty
        };
        TimerTheme.StyleNumber(number);
        return number;
    }

    private static TrackBar Slider(int maximum) => new PopupVolumeTrackBar {
        Minimum = 0, Maximum = maximum, TickStyle = TickStyle.None,
        AutoSize = false, Height = 28, Dock = DockStyle.Fill,
        SmallChange = 1, LargeChange = 10, Margin = new Padding(0, 0, 10, 0)
    };

    private static void Synchronize(TrackBar slider, NumericUpDown input)
    {
        slider.Value = (int)input.Value;
        slider.ValueChanged += (_, _) => input.Value = slider.Value;
        input.ValueChanged += (_, _) => {
            if (slider.Value != (int)input.Value) slider.Value = (int)input.Value;
        };
    }

    private static Control PositionRow(string caption, TrackBar slider, NumericUpDown number, int captionWidth = 82)
    {
        var row = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = TimerTheme.Card, Margin = new Padding(0, 0, 0, 10)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, captionWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = TimerTheme.Caption(caption);
        label.Anchor = AnchorStyles.Left;
        row.Controls.Add(label, 0, 0);
        slider.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        row.Controls.Add(slider, 1, 0);
        var numberWithUnit = new TableLayoutPanel {
            AutoSize = true, ColumnCount = 2, RowCount = 1, BackColor = TimerTheme.Card,
            Anchor = AnchorStyles.Right, Margin = Padding.Empty
        };
        numberWithUnit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        numberWithUnit.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        numberWithUnit.Controls.Add(DialogLayout.InputFrame(number), 0, 0);
        var unit = TimerTheme.Caption("%");
        unit.Margin = new Padding(6, 0, 0, 0);
        numberWithUnit.Controls.Add(unit, 1, 0);
        row.Controls.Add(numberWithUnit, 2, 0);
        return row;
    }

    private static Control FontInput(string caption, NumericUpDown number, Padding margin)
    {
        var layout = new TableLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = TimerTheme.Card, Margin = margin
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = TimerTheme.Caption(caption + " (pt)");
        label.Margin = new Padding(0, 0, 0, 6);
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(DialogLayout.InputFrame(number), 0, 1);
        return layout;
    }

    private sealed class OverlayPreviewPanel : Panel
    {
        private static readonly IReadOnlyList<OverlayTimerRow> SampleRows = [
            new("타이머 1", 12 * 60 + 34), new("타이머 2", 3 * 60 + 20)
        ];
        public GameOverlayAppearance Appearance { get; set; } = new();

        public OverlayPreviewPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = TimerTheme.Background;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width <= 1 || ClientSize.Height <= 1) return;
            var area = ClientRectangle;
            using var gradient = new LinearGradientBrush(area, Color.FromArgb(34, 52, 59),
                Color.FromArgb(12, 20, 29), 30F);
            e.Graphics.FillRectangle(gradient, area);
            using var grid = new Pen(Color.FromArgb(24, TimerTheme.Muted));
            var spacing = Math.Max(20, (int)Math.Round(40 * DeviceDpi / 96d));
            for (var x = spacing; x < Width; x += spacing) e.Graphics.DrawLine(grid, x, 0, x, Height);
            for (var y = spacing; y < Height; y += spacing) e.Graphics.DrawLine(grid, 0, y, Width, y);
            // Keep the backing bitmap and preview coordinates in physical pixels at every DPI.
            using var bitmap = GameOverlayForm.Render(area.Size, DeviceDpi, SampleRows, Appearance);
            var location = GameOverlayForm.LocationFor(area, bitmap.Size, DeviceDpi, Appearance);
            e.Graphics.DrawImage(bitmap, new Rectangle(location, bitmap.Size), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel);
            using var border = new Pen(TimerTheme.Border);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
    }
}
