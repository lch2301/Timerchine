using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed record HotkeyBinding
{
    public Keys Key { get; init; }
    public Keys Modifiers { get; init; }
    [JsonIgnore]
    public bool IsEmpty => Key == Keys.None;

    [JsonIgnore]
    public bool IsValid => IsEmpty || ((int)Key > 0 && (int)Key < 255
        && Key is not (Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin)
        && (Modifiers & ~(Keys.Control | Keys.Alt | Keys.Shift)) == 0);

    public override string ToString()
    {
        if (IsEmpty) return "지정 안 함";
        var parts = new List<string>();
        if ((Modifiers & Keys.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Keys.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Keys.Shift) != 0) parts.Add("Shift");
        parts.Add(new KeysConverter().ConvertToString(Key) ?? Key.ToString());
        return string.Join(" + ", parts);
    }
}

internal sealed class GlobalHotkeys : NativeWindow, IDisposable
{
    public const int StartId = 1;
    public const int StopId = 2;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private readonly HashSet<int> registered = new();
    public event Action<int>? Pressed;

    public GlobalHotkeys()
    {
        // A message-only window receives hotkeys without focusing the timer.
        CreateHandle(new CreateParams { Caption = $"{TimerBranding.AppName}.Hotkeys", Parent = new IntPtr(-3) });
    }

    public bool TryRegister(HotkeyBinding start, HotkeyBinding stop, out string error)
        => TryRegister([new(StartId, "개별 타이머 동작", start), new(StopId, "전체 타이머 동작", stop)], out error);

    public bool TryRegister(IReadOnlyList<HotkeyRegistration> bindings, out string error)
    {
        if (bindings.Any(entry => !entry.Binding.IsValid) || bindings.Select(entry => entry.Id).Distinct().Count() != bindings.Count)
        {
            error = "사용할 수 없는 키 조합입니다. 단축키를 다시 지정하세요.";
            return false;
        }
        var duplicate = bindings.Where(entry => !entry.Binding.IsEmpty)
            .GroupBy(entry => (entry.Binding.Key, entry.Binding.Modifiers)).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            error = $"{string.Join(", ", duplicate.Select(entry => entry.Name))}: 같은 단축키를 지정할 수 없습니다.";
            return false;
        }

        UnregisterAll();
        foreach (var entry in bindings)
        {
            var id = entry.Id;
            var binding = entry.Binding;
            if (binding.IsEmpty) continue;
            uint modifiers = ModNoRepeat;
            if ((binding.Modifiers & Keys.Alt) != 0) modifiers |= 0x0001;
            if ((binding.Modifiers & Keys.Control) != 0) modifiers |= 0x0002;
            if ((binding.Modifiers & Keys.Shift) != 0) modifiers |= 0x0004;
            if (!RegisterHotKey(Handle, id, modifiers, (uint)binding.Key))
            {
                var code = Marshal.GetLastWin32Error();
                UnregisterAll();
                error = $"{entry.Name} ({binding}): 등록할 수 없습니다. 다른 프로그램에서 사용 중이거나 Windows에서 예약한 키일 수 있습니다.";
                if (code != 1409) error += $"\n{new Win32Exception(code).Message}";
                return false;
            }
            registered.Add(id);
        }
        error = "";
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in registered) UnregisterHotKey(Handle, id);
        registered.Clear();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && registered.Contains(message.WParam.ToInt32()))
        {
            // An explicitly configured global key must not bypass the local
            // Enter/Space policy while our app or one of its dialogs is focused.
            var key = (Keys)((message.LParam.ToInt64() >> 16) & 0xffff);
            if (key is Keys.Enter or Keys.Space)
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
                if (foregroundProcess == (uint)Environment.ProcessId) return;
            }
            Pressed?.Invoke(message.WParam.ToInt32());
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

internal sealed record HotkeyRegistration(int Id, string Name, HotkeyBinding Binding);
