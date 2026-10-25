using System.Drawing;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed class HotkeyCaptureBox : TextBox
{
    public HotkeyBinding Binding { get; private set; } = new();
    public HotkeyCaptureBox(HotkeyBinding binding)
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = TimerTheme.Card;
        ForeColor = TimerTheme.Foreground;
        Font = TimerTypography.Create(11F);
        SetBinding(binding);
    }
    public void SetBinding(HotkeyBinding binding)
    {
        Binding = binding;
        Text = binding.ToString();
        SelectionStart = TextLength;
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        var binding = new HotkeyBinding {
            Key = keyData & Keys.KeyCode,
            Modifiers = keyData & (Keys.Control | Keys.Alt | Keys.Shift)
        };
        if (binding.IsValid && !binding.IsEmpty) SetBinding(binding);
        return true;
    }
}
