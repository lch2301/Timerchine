using WinTimer = System.Windows.Forms.Timer;
using System.Runtime.InteropServices;

namespace IntervalTimer;

/// <summary>Rendering stays on the UI thread; process/GPU inspection never blocks a countdown.</summary>
internal sealed class GameOverlayController : IDisposable
{
    private readonly Func<IReadOnlyList<OverlayTimerRow>> rows;
    private readonly GameOverlayForm window = new();
    private readonly WinTimer pulse = new() { Interval = 100 };
    private readonly CancellationTokenSource lifetime = new();
    private readonly OverlayNative.WinEventCallback foregroundChanged;
    private readonly SynchronizationContext ui;
    private readonly Task worker;
    private readonly Func<nint, Rectangle> monitorBounds;
    private readonly Func<nint> foregroundWindow;
    private readonly Func<GameOverlayAppearance> appearance;
    private nint foregroundHook;
    private GameOverlayTarget? target;
    private volatile bool enabled;
    private bool disposed;

    public GameOverlayController(Func<IReadOnlyList<OverlayTimerRow>> rows, bool enabled,
        Func<nint, Rectangle>? monitorBounds = null, Func<nint>? foregroundWindow = null,
        Func<GameOverlayAppearance>? appearance = null)
    {
        this.rows = rows; this.enabled = enabled;
        this.monitorBounds = monitorBounds ?? (handle => Screen.FromHandle(handle).Bounds);
        this.foregroundWindow = foregroundWindow ?? OverlayNative.GetForegroundWindow;
        this.appearance = appearance ?? (() => new GameOverlayAppearance());
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        foregroundChanged = (_, _, _, _, _, _, _) => ui.Post(_ => {
            if (!disposed) { Volatile.Write(ref target, null); window.Hide(); }
        }, null);
        foregroundHook = OverlayNative.SetWinEventHook(3, 3, 0, foregroundChanged, 0, 0, 0);
        pulse.Tick += (_, _) => Update();
        pulse.Start();
        worker = Task.Run(() => InspectLoop(lifetime.Token));
    }

    public void SetEnabled(bool value)
    {
        enabled = value;
        if (!value) { Volatile.Write(ref target, null); window.Hide(); }
        else Update();
    }

    private async Task InspectLoop(CancellationToken cancellation)
    {
        using var detector = new GameWindowDetector();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(650));
        try
        {
            do
            {
                if (enabled)
                {
                    GameOverlayTarget? found = null;
                    try { found = detector.ProbeWindow(foregroundWindow()); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
                    if (!cancellation.IsCancellationRequested && enabled) Volatile.Write(ref target, found);
                }
            } while (await timer.WaitForNextTickAsync(cancellation));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { Volatile.Write(ref target, null); }
    }

    private void Update()
    {
        if (disposed) return;
        var current = Volatile.Read(ref target);
        if (!enabled || current is null || foregroundWindow() != current.Window
            || !OverlayNative.IsWindowVisible(current.Window) || OverlayNative.IsIconic(current.Window)
            || !OverlayNative.TryClientBounds(current.Window, out var bounds)
            || OverlayNative.GetWindowThreadProcessId(current.Window, out var pid) == 0 || pid != current.ProcessId)
        { window.Hide(); return; }
        try
        {
            // Clip to the game's current monitor. A window dragged partly offscreen must not spill onto the desktop.
            bounds.Intersect(monitorBounds(current.Window));
            window.Present(bounds, OverlayNative.Dpi(current.Window), rows(), appearance());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or ExternalException)
        { window.Hide(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; enabled = false;
        if (foregroundHook != 0) OverlayNative.UnhookWinEvent(foregroundHook);
        foregroundHook = 0;
        pulse.Stop(); pulse.Dispose();
        lifetime.Cancel();
        _ = worker.ContinueWith(finished => { _ = finished.Exception; lifetime.Dispose(); }, TaskScheduler.Default);
        window.Dispose();
        // The worker owns and closes its PDH query after its current inspection.
    }
}
