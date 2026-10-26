using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IntervalTimer;

internal sealed record GraphicsProgram(string Executable, string Path, string OriginalName,
    string WindowClass, IReadOnlySet<string> Modules, bool? Has3DActivity, bool IsWindowsComponent,
    bool ModulesReadable = true);

/// <summary>Shared policy, kept separate from process access so exclusions can be verified.</summary>
internal static class GameOverlayPolicy
{
    private static readonly HashSet<string> ExcludedExecutables = new(StringComparer.OrdinalIgnoreCase) {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "chromium", "thorium",
        "iexplore", "browser", "whale", "whale_browser", "waterfox", "librewolf", "zen", "arc", "floorp",
        "explorer", "dwm", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp",
        "applicationframehost", "systemsettings", "textinputhost", "lockapp", "logonui", "taskmgr",
        "sihost", "runtimebroker", "shellexperiencehost", "widgets", "widgetservice", "msedgewebview2",
        "discord", "slack", "teams", "ms-teams", "code", "devenv", "notepad", "notepad++", "obs64",
        "obs32", "vlc", "mpv", "wmplayer", "potplayermini64", "potplayermini", "photoshop", "blender",
        "unity", "unityhub", "unrealeditor", "ue4editor", "winword", "excel", "powerpnt", "outlook",
        "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "riotclientservices",
        "eadesktop", "origin", "ubisoftconnect", "upc", "goggalaxy", "galaxyclient", "timerchine"
    };
    private static readonly HashSet<string> GraphicsModules = new(StringComparer.OrdinalIgnoreCase) {
        "d3d9.dll", "d3d10.dll", "d3d10_1.dll", "d3d11.dll", "d3d12.dll", "d3d12core.dll",
        "opengl32.dll", "vulkan-1.dll"
    };
    private static readonly string[] BrowserClasses = ["Chrome_WidgetWin", "Mozilla", "Chrome_RenderWidget", "Internet Explorer", "IEFrame"];
    private static readonly string[] DesktopClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "DV2ControlHost", "XamlExplorerHostIslandWindow"];
    private static string Stem(string name) => System.IO.Path.GetFileNameWithoutExtension(name).ToLowerInvariant();

    public static bool ShouldDisplay(GraphicsProgram program)
    {
        if (program.IsWindowsComponent || ExcludedExecutables.Contains(Stem(program.Executable))
            || ExcludedExecutables.Contains(Stem(program.OriginalName))) return false;
        if (BrowserClasses.Concat(DesktopClasses).Any(prefix => program.WindowClass.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return false;
        if (!program.Modules.Any(GraphicsModules.Contains))
        {
            // Protected games can refuse module enumeration. OS GPU counters and
            // native game-engine window classes do not need access to game memory.
            if (program.ModulesReadable) return false;
            return HasEngineWindow(program.WindowClass)
                || !IsDesktopFramework(program) && program.Has3DActivity == true;
        }
        // Known engines/installed games also qualify while paused or on a static menu.
        if (HasGameEvidence(program)) return true;
        // Windows UI frameworks often load Direct3D for ordinary 2D composition.
        if (IsDesktopFramework(program)) return false;
        // An unfamiliar native graphics window must actually submit 3D GPU work.
        return program.Has3DActivity == true;
    }

    private static bool IsDesktopFramework(GraphicsProgram program) =>
        program.WindowClass.StartsWith("WindowsForms", StringComparison.OrdinalIgnoreCase)
            || program.WindowClass.StartsWith("HwndWrapper", StringComparison.OrdinalIgnoreCase)
            || program.WindowClass is "ApplicationFrameWindow" or "WinUIDesktopWin32WindowClass"
            || program.Modules.Any(m => m.Equals("d2d1.dll", StringComparison.OrdinalIgnoreCase)
                || m.Equals("Windows.UI.Xaml.dll", StringComparison.OrdinalIgnoreCase)
                || m.Equals("Microsoft.UI.Xaml.dll", StringComparison.OrdinalIgnoreCase));

    private static bool HasEngineWindow(string windowClass) =>
        new[] { "UnityWndClass", "UnrealWindow", "SDL_app", "GLFW", "Godot", "Valve001", "Source2", "CryENGINE", "LWJGL" }
            .Any(prefix => windowClass.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool HasGameEvidence(GraphicsProgram program)
    {
        var path = program.Path.Replace('/', '\\');
        if (new[] { "\\steamapps\\common\\", "\\Epic Games\\", "\\GOG Games\\", "\\XboxGames\\" }
            .Any(part => path.Contains(part, StringComparison.OrdinalIgnoreCase))) return true;
        if (HasEngineWindow(program.WindowClass)) return true;
        return program.Modules.Any(m => m.Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase)
            || m.StartsWith("lwjgl", StringComparison.OrdinalIgnoreCase)
            || m.Equals("cryengine.dll", StringComparison.OrdinalIgnoreCase))
            || (path.Contains("\\Binaries\\Win", StringComparison.OrdinalIgnoreCase)
                && program.Executable.Contains("Shipping", StringComparison.OrdinalIgnoreCase))
            || Stem(program.Executable).StartsWith("robloxplayer", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record GameOverlayTarget(nint Window, uint ProcessId, Rectangle ClientBounds, int Dpi);

internal sealed class GameWindowDetector(int? ownProcessId = null) : IDisposable
{
    private readonly int ownProcessId = ownProcessId ?? Environment.ProcessId;
    private readonly Gpu3DActivity gpu = new();
    private readonly Dictionary<uint, (long Stamp, nint Window, GraphicsProgram Program)> cache = [];
    private readonly Dictionary<uint, long> recentActivity = [];

    public GameOverlayTarget? Probe() => ProbeWindow(OverlayNative.GetForegroundWindow());

    internal GameOverlayTarget? ProbeWindow(nint window)
    {
        gpu.Sample();
        if (window == 0 || !OverlayNative.IsWindowVisible(window) || OverlayNative.IsIconic(window)) return null;
        OverlayNative.GetWindowThreadProcessId(window, out var pid);
        if (pid == 0 || pid == ownProcessId || !OverlayNative.TryClientBounds(window, out var bounds)
            || bounds.Width < 160 || bounds.Height < 100) return null;
        var windowClass = OverlayNative.WindowClass(window);
        var now = Environment.TickCount64;
        try
        {
            if (!cache.TryGetValue(pid, out var entry) || entry.Window != window || now - entry.Stamp > 2000)
            {
                using var process = Process.GetProcessById((int)pid);
                // Limited-information queries remain available for many processes
                // which deliberately refuse PROCESS_VM_READ/module enumeration.
                var path = OverlayNative.ProcessImagePath(pid);
                var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var modulesReadable = false;
                try
                {
                    path = process.MainModule?.FileName ?? path;
                    foreach (ProcessModule module in process.Modules) modules.Add(module.ModuleName);
                    modulesReadable = modules.Count > 0;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { }
                var original = string.IsNullOrEmpty(path) ? "" : FileVersionInfo.GetVersionInfo(path).OriginalFilename ?? "";
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
                entry = (now, window, new GraphicsProgram(process.ProcessName, path, original, windowClass, modules, null,
                    path.StartsWith(windows, StringComparison.OrdinalIgnoreCase), modulesReadable));
                cache[pid] = entry;
                foreach (var stale in cache.Where(pair => now - pair.Value.Stamp > 30_000).Select(pair => pair.Key).ToArray())
                { cache.Remove(stale); recentActivity.Remove(stale); }
            }
            var active = gpu.IsActive(pid);
            if (active == true) recentActivity[pid] = now;
            if (recentActivity.TryGetValue(pid, out var last) && now - last <= 3000) active = true;
            var program = entry.Program with { WindowClass = windowClass, Has3DActivity = active };
            if (!GameOverlayPolicy.ShouldDisplay(program)) return null;
            return new(window, pid, bounds, OverlayNative.Dpi(window));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException
            or System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            cache.Remove(pid); recentActivity.Remove(pid);
            return null;
        }
    }

    public void Dispose() => gpu.Dispose();
}

/// <summary>Windows GPU counters; one background sample, independent of UI/countdown timing.</summary>
internal sealed class Gpu3DActivity : IDisposable
{
    private nint query, counter;
    private bool initialized, primed;
    private readonly Dictionary<uint, bool> activity = [];

    public bool? IsActive(uint pid) => activity.TryGetValue(pid, out var value) ? value : null;

    public void Sample()
    {
        activity.Clear();
        if (!initialized)
        {
            initialized = true;
            if (PdhOpenQueryW(null, 0, out query) != 0) { query = 0; return; }
            if (PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", 0, out counter) != 0)
            { Dispose(); return; }
        }
        if (query == 0 || PdhCollectQueryData(query) != 0) return;
        if (!primed) { primed = true; return; }
        uint size = 0, count = 0;
        const uint moreData = 0x800007D2, format = 0x8200;
        if (PdhGetFormattedCounterArrayW(counter, format, ref size, out count, 0) != moreData || size == 0 || size > 32 * 1024 * 1024) return;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, format, ref size, out count, buffer) != 0) return;
            var itemSize = Marshal.SizeOf<CounterItem>();
            if ((long)count * itemSize > size) return;
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * itemSize);
                if (item.Value.Status > 1) continue;
                var name = Marshal.PtrToStringUni(item.Name) ?? "";
                if (!name.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase) || !name.StartsWith("pid_", StringComparison.Ordinal)) continue;
                var end = name.IndexOf('_', 4);
                if (end < 0 || !uint.TryParse(name.AsSpan(4, end - 4), out var pid)) continue;
                activity[pid] = (activity.TryGetValue(pid, out var previous) && previous)
                    || double.IsFinite(item.Value.Number) && item.Value.Number > 0.001;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        if (query != 0) PdhCloseQuery(query);
        query = 0; counter = 0;
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Number;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public nint Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nuint data, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query, string path, nuint data, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bytes, out uint count, nint items);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}

internal static class OverlayNative
{
    public delegate void WinEventCallback(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder value, int length);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    public static string WindowClass(nint window) { var value = new StringBuilder(256); GetClassNameW(window, value, value.Capacity); return value.ToString(); }
    public static int Dpi(nint window) => (int)Math.Clamp(GetDpiForWindow(window), 96u, 384u);
    public static string ProcessImagePath(uint pid)
    {
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0) return "";
        try
        {
            var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
            return QueryFullProcessImageNameW(process, 0, path, ref length) ? path.ToString() : "";
        }
        finally { CloseHandle(process); }
    }
    public static bool TryClientBounds(nint window, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!GetClientRect(window, out var rect)) return false;
        var start = new Point(rect.Left, rect.Top); var end = new Point(rect.Right, rect.Bottom);
        if (!ClientToScreen(window, ref start) || !ClientToScreen(window, ref end)) return false;
        bounds = Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
        return bounds.Width > 0 && bounds.Height > 0;
    }
}
