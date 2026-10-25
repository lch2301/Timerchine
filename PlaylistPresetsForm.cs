using System.Drawing;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed class PlaylistPresetsForm : TimerPopupForm
{
    private readonly PlaylistPresetStore store;
    private readonly ListBox presets = new() {
        Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None,
        BackColor = TimerTheme.Elevated, ForeColor = TimerTheme.Foreground,
        DisplayMember = nameof(PlaylistPreset.DisplayName), HorizontalScrollbar = true
    };
    private readonly TextBox name = new() { PlaceholderText = "새 프리셋 이름", MaxLength = 80 };
    private readonly Label error = DialogLayout.Error();
    private readonly Button load = TimerTheme.Button("불러오기", primary: true);
    public PlaylistPreset? SelectedPreset { get; private set; }
    public bool PresetCreated { get; private set; }

    public PlaylistPresetsForm(PlaylistPresetStore store, Guid? selectedId)
    {
        this.store = store;
        DialogLayout.Configure(this, "재생 목록 프리셋", 600);
        AutoSize = false;
        ClientSize = new Size(600, 540);
        var layout = DialogLayout.Create(5);
        layout.AutoSize = false;
        layout.RowStyles[1].SizeType = SizeType.Percent;
        layout.RowStyles[1].Height = 100;
        layout.Controls.Add(DialogLayout.Header("재생 목록", "프리셋",
            "프리셋을 만들거나 불러온 뒤, 재생 목록 창에서 저장하세요."), 0, 0);
        presets.Margin = new Padding(0, 0, 0, 16);
        presets.Font = TimerTypography.Create(11);
        presets.ItemHeight = 32;
        presets.SelectedIndexChanged += (_, _) => load.Enabled = presets.SelectedItem is PlaylistPreset;
        presets.DoubleClick += (_, _) => LoadPreset();
        layout.Controls.Add(presets, 0, 1);
        var createRow = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        createRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        createRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        TimerTheme.StyleTextBox(name);
        name.AccessibleName = "새 프리셋 이름";
        var frame = DialogLayout.InputFrame(name);
        frame.Margin = new Padding(0, 0, 10, 0);
        createRow.Controls.Add(frame, 0, 0);
        var create = TimerTheme.Button("생성");
        TimerTheme.SetIcon(create, TimerGlyph.Add);
        create.Margin = Padding.Empty;
        create.Anchor = AnchorStyles.Right;
        create.Click += (_, _) => CreatePreset();
        createRow.Controls.Add(create, 1, 0);
        layout.Controls.Add(createRow, 0, 2);
        error.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(error, 0, 3);
        var actions = new FlowLayoutPanel {
            AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0, 16, 0, 0)
        };
        load.Margin = Padding.Empty;
        TimerTheme.SetIcon(load, TimerGlyph.Folder);
        load.Click += (_, _) => LoadPreset();
        var close = TimerTheme.Button("취소");
        close.DialogResult = DialogResult.Cancel;
        close.Margin = new Padding(0, 0, 10, 0);
        var clear = TimerTheme.Button("선택 해제");
        clear.Margin = new Padding(0, 0, 10, 0);
        clear.Click += (_, _) => { SelectedPreset = null; DialogResult = DialogResult.OK; Close(); };
        actions.Controls.Add(load);
        actions.Controls.Add(close);
        actions.Controls.Add(clear);
        layout.Controls.Add(actions, 0, 4);
        AcceptButton = load;
        CancelButton = close;
        DialogLayout.Complete(this, layout);
        try
        {
            foreach (var preset in store.Load()) presets.Items.Add(preset);
            presets.SelectedItem = presets.Items.Cast<PlaylistPreset>().FirstOrDefault(preset => preset.Id == selectedId)
                ?? presets.Items.Cast<PlaylistPreset>().FirstOrDefault(preset => preset.Name == PlaylistPresetStore.DefaultName);
            if (presets.SelectedIndex < 0 && presets.Items.Count > 0) presets.SelectedIndex = 0;
        }
        catch (Exception ex) { error.Text = ex.Message; }
        load.Enabled = presets.SelectedItem is PlaylistPreset;
        Shown += (_, _) => { if (presets.Items.Count == 0) name.Focus(); else presets.Focus(); };
    }

    private void CreatePreset()
    {
        try
        {
            SelectedPreset = store.Create(name.Text);
            PresetCreated = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { error.Text = ex.Message; }
    }

    private void LoadPreset()
    {
        if (presets.SelectedItem is not PlaylistPreset selected) return;
        SelectedPreset = selected;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Enter && name.ContainsFocus) { CreatePreset(); return true; }
        return base.ProcessCmdKey(ref message, keyData);
    }
}
