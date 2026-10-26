using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;

namespace IntervalTimer;

internal static class OverlayChecks
{
    private static string output = "";
    private static readonly List<string> passed = [];
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pass(string name) { passed.Add(name); Console.WriteLine("PASS " + name); }
    private static GraphicsProgram Program(string executable = "custom-game.exe", string windowClass = "CustomGame",
        string path = @"C:\Games\custom-game.exe", string original = "", bool? gpu = true,
        bool windows = false, params string[] modules) => new(executable, path, original, windowClass,
            new HashSet<string>(modules, StringComparer.OrdinalIgnoreCase), gpu, windows);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args[0] == "--fixture") return FixtureProcess(args[1]);
        if (args[0] == "--probe") return ProbeProcess(int.Parse(args[1]), args[2]);
        output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Policy(); Render(); Appearance(); NameAlignment(); NativeWindow(); Detection(); Settings(); AppearanceDialog(); FailedAppearanceSave(); IntegratedForegroundSwitching();
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { Passed = passed }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
    }

    private static int ProbeProcess(int pid, string destination)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var process = Process.GetProcessById(pid);
        using var detector = new GameWindowDetector();
        using var gpu = new Gpu3DActivity(); gpu.Sample(); Thread.Sleep(750); gpu.Sample();
        var window = process.MainWindowHandle;
        string? moduleFailure = null; int? count = null;
        try { count = process.Modules.Count; } catch (Exception ex) { moduleFailure = ex.GetType().Name + ": " + ex.Message; }
        var target = detector.ProbeWindow(window);
        var report = new { Process = process.ProcessName, WindowClass = OverlayNative.WindowClass(window),
            Image = OverlayNative.ProcessImagePath((uint)pid), Modules = count, ModuleFailure = moduleFailure,
            Active3D = gpu.IsActive((uint)pid), Detected = target is not null, Target = target?.ToString() };
        File.WriteAllText(destination, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        return target is null ? 1 : 0;
    }

    private static void Policy()
    {
        foreach (var api in new[] { "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll", "vulkan-1.dll" })
            Require(GameOverlayPolicy.ShouldDisplay(Program(modules: [api])), "Active native graphics window not detected: " + api);
        Require(!GameOverlayPolicy.ShouldDisplay(Program(modules: ["dxgi.dll"])), "DXGI by itself is not 3D rendering evidence");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(gpu: true)), "GPU activity without a 3D API must not display");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(gpu: false, modules: ["d3d11.dll"])), "A loaded API alone is not enough for an unknown program");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(gpu: null, modules: ["d3d11.dll"])), "No-counter fallback must require game evidence");
        Pass("Direct3D/OpenGL/Vulkan eligibility and actual 3D work for unknown native programs");

        foreach (var browser in new[] { "chrome.exe", "msedge.exe", "firefox.exe", "whale.exe", "brave.exe", "opera_gx.exe", "vivaldi.exe" })
            Require(!GameOverlayPolicy.ShouldDisplay(Program(executable: browser, windowClass: "GLFW30", modules: ["d3d12.dll"])), "Browser got an overlay: " + browser);
        Require(!GameOverlayPolicy.ShouldDisplay(Program(executable: "renamed.exe", original: "chrome.exe", modules: ["d3d11.dll"])), "Renamed browser not excluded");
        foreach (var browserClass in new[] { "Chrome_WidgetWin_1", "MozillaWindowClass", "IEFrame" })
            Require(!GameOverlayPolicy.ShouldDisplay(Program(windowClass: browserClass, path: @"C:\Steam\steamapps\common\test\test.exe", modules: ["opengl32.dll"])), "Browser window class exclusion was overridden");
        Pass("Browsers remain hidden even with high GPU use, fullscreen/game paths and renamed executables");

        foreach (var desktop in new[] { "explorer.exe", "dwm.exe", "SystemSettings.exe", "Taskmgr.exe", "devenv.exe", "winword.exe", "steam.exe", "blender.exe" })
            Require(!GameOverlayPolicy.ShouldDisplay(Program(executable: desktop, modules: ["d3d11.dll"])), "Desktop app got an overlay: " + desktop);
        foreach (var framework in new[] { "WindowsForms10.Window.8", "HwndWrapper[app]", "ApplicationFrameWindow" })
            Require(!GameOverlayPolicy.ShouldDisplay(Program(windowClass: framework, modules: ["d3d11.dll"])), "Ordinary desktop framework got an overlay");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(windows: true, modules: ["d3d11.dll"])), "Windows component got an overlay");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(modules: ["d3d11.dll", "d2d1.dll"])), "Direct2D desktop composition was treated as a game");
        Pass("Windows shell, desktop applications, launchers and 2D UI composition stay hidden");

        foreach (var engine in new[] { "UnityWndClass", "UnrealWindow", "SDL_app", "GLFW30", "Godot", "Valve001" })
            Require(GameOverlayPolicy.ShouldDisplay(Program(windowClass: engine, gpu: false, modules: ["opengl32.dll"])), "Static game menu should remain eligible: " + engine);
        Require(GameOverlayPolicy.ShouldDisplay(Program(path: @"C:\Steam\steamapps\common\Example\game.exe", gpu: null, modules: ["d3d11.dll"])), "Game evidence fallback failed");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(windowClass: "UnityWndClass", gpu: true)), "Game hint must not bypass missing graphics API");
        Pass("Known game engines and game installation paths remain visible at static menus; a 3D API is always required");
        Require(GameOverlayPolicy.ShouldDisplay(Program(windowClass: "UnityWndClass", gpu: false) with { ModulesReadable = false }), "Protected Unity game was discarded because modules could not be inspected");
        Require(GameOverlayPolicy.ShouldDisplay(Program(gpu: true) with { ModulesReadable = false }), "OS-confirmed 3D GPU work must survive denied module access");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(gpu: false) with { ModulesReadable = false }), "Unknown desktop window cannot qualify without graphics evidence");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(windowClass: "Chrome_WidgetWin_1", gpu: true) with { ModulesReadable = false }), "Protected browser class must still be excluded");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(executable: "chrome.exe", windowClass: "UnityWndClass", gpu: true) with { ModulesReadable = false }), "Browser executable exclusion must precede fallback");
        Require(!GameOverlayPolicy.ShouldDisplay(Program(windowClass: "HwndWrapper[app]", gpu: true) with { ModulesReadable = false }), "WPF desktop composition was treated as a protected game");
        Pass("Denied module access uses game-engine windows or OS 3D GPU activity while keeping browsers and ordinary desktop UI excluded");
    }

    private static void Render()
    {
        OverlayTimerRow[] rows = [new("다음 일정", 3723), new("음악이 멈출 때", 187)];
        foreach (var dpi in new[] { 96, 144, 192 })
        {
            using var image = GameOverlayForm.Render(new Size(1920, 1080), dpi, rows);
            var maxAlpha = 0; var opaque = 0; var transparent = 0;
            for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
            {
                var a = image.GetPixel(x, y).A; maxAlpha = Math.Max(maxAlpha, a);
                if (a == 255) opaque++; if (a == 0) transparent++;
            }
            Require(maxAlpha > 100 && opaque == 0 && transparent > image.Width * image.Height / 2, "Overlay must be legible translucent text on a transparent background");
            image.Save(Path.Combine(output, $"overlay-transparent-{dpi}.png"));
            using var preview = new Bitmap(960, 540);
            using var g = Graphics.FromImage(preview);
            using var backdrop = new LinearGradientBrush(new Rectangle(0, 0, 960, 540), Color.FromArgb(38, 57, 76), Color.FromArgb(12, 24, 35), 35);
            g.FillRectangle(backdrop, 0, 0, 960, 540);
            using var lines = new Pen(Color.FromArgb(45, 137, 177, 181), 1);
            for (var x = 0; x < 960; x += 80) g.DrawLine(lines, x, 0, x, 540);
            for (var y = 0; y < 540; y += 60) g.DrawLine(lines, 0, y, 960, y);
            var previewLocation = GameOverlayForm.LocationFor(new Rectangle(0, 0, 960, 540), image.Size, dpi, new GameOverlayAppearance());
            if (dpi == 96) g.DrawImage(image, new Rectangle(previewLocation, image.Size),
                new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
            if (dpi == 96) preview.Save(Path.Combine(output, "overlay-layout-preview.png"));
        }
        using var crowded = GameOverlayForm.Render(new Size(800, 600), 192, Enumerable.Range(0, 30).Select(i => new OverlayTimerRow(new string('가', 80), i * 99)).ToArray());
        Require(crowded.Width <= 800 && crowded.Height <= 600, "Many timers must stay inside the game client");
        Pass("Translucent right-aligned names/times render at 96/144/192 DPI; long names and 30 timers fit");
    }

    private static void NativeWindow()
    {
        var foreground = OverlayNative.GetForegroundWindow();
        using var overlay = new GameOverlayForm();
        overlay.Present(new Rectangle(20000, 20000, 800, 600), 96, [new("테스트", 90)],
            new GameOverlayAppearance { VerticalPosition = 50 });
        Application.DoEvents();
        Require(overlay.Visible, "Overlay window failed to show");
        Require(OverlayNative.GetForegroundWindow() == foreground, "Showing the overlay stole focus");
        var style = GetWindowLongPtrW(overlay.Handle, -20).ToInt64();
        Require((style & 0x080800A0) == 0x080800A0, "Layered/noactivate/transparent/toolwindow styles missing");
        Require(SendMessageW(overlay.Handle, 0x84, 0, 0) == -1 && SendMessageW(overlay.Handle, 0x21, 0, 0) == 3, "Overlay intercepted input");
        GetWindowRect(overlay.Handle, out var rectangle);
        Require(rectangle.Right == 20800 - 18, "Overlay is not aligned to the right edge of the game");
        Require(Math.Abs((rectangle.Top + rectangle.Bottom) / 2 - 20300) <= 1, "Overlay is not vertically centered");
        var appearance = new GameOverlayAppearance { HorizontalPosition = 0, VerticalPosition = 0,
            Transparency = 65, TimeFontSize = 40, NameFontSize = 20 };
        overlay.Present(new Rectangle(20000, 20000, 800, 600), 96, [new("테스트", 90)], appearance);
        Application.DoEvents(); GetWindowRect(overlay.Handle, out var customized);
        using var customizedBitmap = GameOverlayForm.Render(new Size(800, 600), 96, [new("테스트", 90)], appearance);
        var customizedLocation = GameOverlayForm.LocationFor(new Rectangle(20000, 20000, 800, 600), customizedBitmap.Size, 96, appearance);
        Require(customized.Left == customizedLocation.X && customized.Top == customizedLocation.Y
            && customized.Right - customized.Left == customizedBitmap.Width && customized.Bottom - customized.Top == customizedBitmap.Height,
            "Changing appearance with unchanged rows must redraw and reposition the native overlay");
        Require(OverlayNative.GetForegroundWindow() == foreground && overlay.Controls.Count == 0,
            "Appearance customization must keep the game overlay passive and focus-free");
        overlay.Present(new Rectangle(20000, 20000, 800, 600), 96, []);
        Require(!overlay.Visible, "No timers should hide the overlay");
        Pass("Native layered window stays passive during appearance changes, repositions correctly and hides for an empty timer list");
    }

    private static void Appearance()
    {
        var defaults = new GameOverlayAppearance().Normalized();
        Require(defaults.HorizontalPosition == 100 && defaults.VerticalPosition == 30 && defaults.Transparency == 50
            && defaults.TimeFontSize == 15 && defaults.NameFontSize == 13
            && defaults.NameAlignment == OverlayNameAlignment.Right && defaults.NameAlignmentPercent == 100,
            "Appearance defaults must match the requested position, opacity, fonts and right name position");
        var bounded = new GameOverlayAppearance { HorizontalPosition = -1, VerticalPosition = 200,
            Transparency = -10, TimeFontSize = 1000, NameFontSize = 0 }.Normalized();
        Require(bounded.HorizontalPosition == 0 && bounded.VerticalPosition == 100 && bounded.Transparency == 0
            && bounded.TimeFontSize == 96 && bounded.NameFontSize == 8, "Appearance normalization bounds mismatch");
        var invalid = new GameOverlayAppearance { TimeFontSize = float.NaN, NameFontSize = float.PositiveInfinity }.Normalized();
        Require(float.IsFinite(invalid.TimeFontSize) && float.IsFinite(invalid.NameFontSize)
            && invalid.TimeFontSize is >= 12 and <= 96 && invalid.NameFontSize is >= 8 and <= 48,
            "Non-finite font values must not reach the renderer");
        Require((defaults with { NameAlignment = (OverlayNameAlignment)(-1) }).Normalized().NameAlignment == OverlayNameAlignment.Right,
            "Invalid name alignment must fall back to the previous right alignment");
        Require((defaults with { NameAlignmentPercent = -1 }).Normalized().NameAlignmentPercent == 0
            && (defaults with { NameAlignmentPercent = 101 }).Normalized().NameAlignmentPercent == 100,
            "Continuous name position must stay between zero and one hundred");

        var bounds = new Rectangle(-1600, 70, 1280, 720);
        var size = new Size(350, 132);
        var defaultPosition = GameOverlayForm.LocationFor(bounds, size, 96, defaults);
        Require(defaultPosition == new Point(bounds.Right - 18 - size.Width,
            bounds.Top + 18 + (int)Math.Round((bounds.Height - size.Height - 36) * .3)),
            "Default position must use the right edge and 30 percent vertical travel with its edge margin");
        foreach (var dpi in new[] { 96, 144, 192 })
        foreach (var horizontal in new[] { 0, 50, 100 })
        foreach (var vertical in new[] { 0, 50, 100 })
        {
            var point = GameOverlayForm.LocationFor(bounds, size, dpi,
                defaults with { HorizontalPosition = horizontal, VerticalPosition = vertical });
            var positioned = new Rectangle(point, size);
            Require(bounds.Contains(positioned), "Custom position escaped the game client on a secondary monitor");
            if (horizontal == 0) Require(point.X < bounds.Left + bounds.Width / 4, "Left position did not move to the left");
            if (horizontal == 100) Require(positioned.Right > bounds.Right - bounds.Width / 4, "Right position did not move to the right");
            if (vertical == 0) Require(point.Y < bounds.Top + bounds.Height / 4, "Top position did not move to the top");
            if (vertical == 100) Require(positioned.Bottom > bounds.Bottom - bounds.Height / 4, "Bottom position did not move to the bottom");
        }
        Pass("Appearance defaults, finite bounds and all corner/center positions remain inside the game at multiple DPIs");

        OverlayTimerRow[] rows = [new("사용자 지정 이름", 3723), new("남은 시간", 90)];
        long previousAlpha = long.MaxValue;
        foreach (var transparency in new[] { 0, 20, 50, 90 })
        {
            using var image = GameOverlayForm.Render(new Size(1920, 1080), 96, rows, defaults with { Transparency = transparency });
            long alpha = 0;
            for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++) alpha += image.GetPixel(x, y).A;
            Require(alpha > 0 && alpha < previousAlpha, "Increasing transparency must make all overlay text less opaque while retaining visible text");
            previousAlpha = alpha;
            image.Save(Path.Combine(output, $"overlay-custom-transparency-{transparency}.png"));
        }
        using var small = GameOverlayForm.Render(new Size(1920, 1080), 96, rows,
            defaults with { TimeFontSize = 12, NameFontSize = 8 });
        using var largeTime = GameOverlayForm.Render(new Size(1920, 1080), 96, rows,
            defaults with { TimeFontSize = 96, NameFontSize = 8 });
        using var largeName = GameOverlayForm.Render(new Size(1920, 1080), 96, rows,
            defaults with { TimeFontSize = 12, NameFontSize = 48 });
        Require(largeTime.Height > small.Height && largeName.Height > small.Height,
            "Independent number/name font sizes must affect the overlay layout");
        foreach (var dpi in new[] { 96, 144, 192, 384 })
        foreach (var count in new[] { 1, 2, 30 })
        {
            var client = new Size(800, 600);
            using var image = GameOverlayForm.Render(client, dpi,
                Enumerable.Range(0, count).Select(i => new OverlayTimerRow(new string('가', 80), 99 * 3600 + 59 * 60 + 59)).ToArray(),
                defaults with { TimeFontSize = 96, NameFontSize = 48 });
            Require(image.Width > 0 && image.Height > 0 && image.Width <= client.Width && image.Height <= client.Height,
                "Largest fonts and long/many timers must fit inside the game client at every DPI");
        }
        largeTime.Save(Path.Combine(output, "overlay-custom-large-time.png"));
        largeName.Save(Path.Combine(output, "overlay-custom-large-name.png"));
        Pass("Transparency consistently changes alpha; independent name/time sizes reflow and maximum fonts with 30 timers stay bounded");
    }

    private static void NameAlignment()
    {
        var cases = new[] {
            (Id: "short", Name: "알림", Time: 32f, NameSize: 12f),
            (Id: "long", Name: "오버레이 타이머 이름 위치 설정", Time: 15f, NameSize: 24f),
            (Id: "large-name", Name: "다음 일정", Time: 12f, NameSize: 48f)
        };
        var geometry = new List<object>();
        foreach (var test in cases)
        foreach (var dpi in new[] { 96, 144, 192 })
        {
            OverlayTimerRow[] rows = [new(test.Name, 3723)];
            var appearance = new GameOverlayAppearance { TimeFontSize = test.Time, NameFontSize = test.NameSize,
                NameAlignmentPercent = 100 };
            using var reference = GameOverlayForm.Render(new Size(1920, 1080), dpi, rows, appearance);
            bool NameInk(Color color) => color.A > 32 && color.R > 160 && Math.Abs(color.G - color.R) < 25 && Math.Abs(color.B - color.G) < 25;
            bool TimeInk(Color color) => color.A > 32 && color.G > color.R + 40 && color.B > color.R + 20;
            var referenceTime = InkBounds(reference, TimeInk);
            Require(referenceTime.Width > 0, "Time digits must remain visible: " + test.Id);
            foreach (var percent in new[] { 0, 25, 50, 75, 100 })
            {
                using var image = GameOverlayForm.Render(new Size(1920, 1080), dpi, rows,
                    appearance with { NameAlignmentPercent = percent });
                Require(image.Size == reference.Size, "Moving only the name must not change the overlay dimensions");
                var name = InkBounds(image, NameInk); var time = InkBounds(image, TimeInk);
                Require(name.Width > 0 && time == referenceTime, "Name and time ink must remain visible while time stays fixed");
                var expectedLeft = time.Left + (time.Width - name.Width) * percent / 100d;
                Require(Math.Abs(name.Left - expectedLeft) <= 2,
                    $"Actual name/time ink is not aligned at {percent}% for {test.Id}, DPI {dpi}: name {name}, time {time}, expected left {expectedLeft}");
                for (var y = time.Top; y < reference.Height; y++) for (var x = 0; x < reference.Width; x++)
                    Require(image.GetPixel(x, y) == reference.GetPixel(x, y),
                        "Name position modified a pixel in the fixed time display: " + test.Id);
                geometry.Add(new { test.Id, Dpi = dpi, Percent = percent, Name = name, Time = time, ExpectedLeft = expectedLeft });
                image.Save(Path.Combine(output, $"overlay-name-position-{test.Id}-{dpi}-{percent}.png"));
            }
        }
        File.WriteAllText(Path.Combine(output, "name-position-geometry.json"), JsonSerializer.Serialize(geometry, new JsonSerializerOptions { WriteIndented = true }));
        Pass("Continuous 0/25/50/75/100 name positions match actual time ink edges/centers for short/long names, independent fonts and multiple DPIs; all time pixels stay fixed");

        static Rectangle InkBounds(Bitmap image, Func<Color, bool> matches)
        {
            var left = image.Width; var top = image.Height; var right = -1; var bottom = -1;
            for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
                if (matches(image.GetPixel(x, y))) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }
    }

    private static void Detection()
    {
        using (var sampler = new Gpu3DActivity())
        {
            sampler.Sample(); Thread.Sleep(150); sampler.Sample();
            var entries = (Dictionary<uint, bool>)typeof(Gpu3DActivity).GetField("activity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sampler)!;
            File.WriteAllText(Path.Combine(output, "gpu-counter-capability.json"), JsonSerializer.Serialize(new { ProcessSamples = entries.Count }));
        }
        using var game = new GraphicsFixture("GLFW30", true);
        using var browser = new GraphicsFixture("Chrome_WidgetWin_1", false);
        using var detector = new GameWindowDetector(-1);
        Require(detector.ProbeWindow(game.Handle) is not null, "Live OpenGL rendering window was not detected");
        Require(detector.ProbeWindow(browser.Handle) is null, "Browser class in the same graphics process must be excluded");
        using var selfDetector = new GameWindowDetector();
        Require(selfDetector.ProbeWindow(game.Handle) is null, "App's own windows must be excluded");
        ShowWindow(game.Handle, 0);
        Require(detector.ProbeWindow(game.Handle) is null, "Hidden game must not get an overlay");
        Pass("Live OpenGL context is detected; browser-like, hidden and app-owned windows are rejected");
    }

    private static void Settings()
    {
        var store = new SettingsStore(Path.Combine(output, "settings.json"));
        var value = new AppSettings(); Require(value.ShowGameOverlay, "Overlay default must be enabled");
        value.ShowGameOverlay = false; store.Save(value); Require(!store.Load().ShowGameOverlay, "Overlay preference did not persist");
        var opened = 0;
        using var settings = new VisualSettingsForm(true, true, false, false, (_, _, _, _) => null, () => opened++);
        settings.Opacity = 0; settings.Show(); Application.DoEvents(); settings.PerformLayout(); Application.DoEvents();
        File.WriteAllText(Path.Combine(output, "settings-layout.json"), JsonSerializer.Serialize(new { settings.Bounds, Screen.PrimaryScreen!.WorkingArea }));
        Require(!settings.GameOverlayCheckBox.Checked && settings.GameOverlayCheckBox.Text == "게임 오버레이 표시", "Settings checkbox mismatch");
        Require(settings.OverlaySettingsButton.Text == "오버레이 설정" && settings.OverlaySettingsButton.Parent is not null
            && settings.OverlaySettingsButton.Parent == settings.GameOverlayCheckBox.Parent,
            "Overlay settings button missing from the settings card");
        settings.OverlaySettingsButton.PerformClick();
        Require(opened == 1, "Overlay settings button did not open customization");
        using var image = new Bitmap(settings.Width, settings.Height); settings.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, "settings-overlay.png"));
        typeof(VisualSettingsForm).GetMethod("FitHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [600]);
        Application.DoEvents();
        Require(settings.Height <= 600 && settings.AutoScroll, "Settings must fit a short monitor and allow scrolling");
        Require(settings.AutoScrollPosition.Y == 0, "Short settings should open at the first setting");
        using var compact = new Bitmap(settings.Width, settings.Height); settings.DrawToBitmap(compact, new Rectangle(Point.Empty, compact.Size)); compact.Save(Path.Combine(output, "settings-overlay-short-screen.png"));
        Pass("Overlay default/opt-out persistence and customization button integrate with the existing scrollable settings dialog");
    }

    private static void AppearanceDialog()
    {
        var initial = new GameOverlayAppearance { HorizontalPosition = 70, VerticalPosition = 35,
            Transparency = 42, TimeFontSize = 30, NameFontSize = 15, NameAlignmentPercent = 50 };
        var saveCount = 0; GameOverlayAppearance? saved = null;
        using (var dialog = new OverlaySettingsForm(initial, value => { saveCount++; saved = value; return null; }))
        {
            dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
            Require(dialog.Draft == initial && dialog.NameAlignmentSlider.Value == 50 && dialog.NameAlignmentInput.Value == 50,
                "Appearance popup did not display the saved values and continuous name position");
            dialog.HorizontalPositionInput.Value = 0; dialog.VerticalPositionSlider.Value = 100;
            dialog.TransparencySlider.Value = 75; dialog.TimeFontSizeInput.Value = 44;
            dialog.NameFontSizeInput.Value = 20;
            Require(dialog.HorizontalPositionSlider.Value == 0 && dialog.VerticalPositionInput.Value == 100
                && dialog.TransparencyInput.Value == 75, "Appearance sliders and numeric controls are not synchronized");
            using var previewBefore = new Bitmap(dialog.PreviewPanel.Width, dialog.PreviewPanel.Height);
            dialog.PreviewPanel.DrawToBitmap(previewBefore, new Rectangle(Point.Empty, previewBefore.Size));
            dialog.NameAlignmentSlider.Value = 25; Application.DoEvents();
            using var previewAfter = new Bitmap(dialog.PreviewPanel.Width, dialog.PreviewPanel.Height);
            dialog.PreviewPanel.DrawToBitmap(previewAfter, new Rectangle(Point.Empty, previewAfter.Size));
            var changed = false;
            for (var y = 0; y < previewBefore.Height && !changed; y++) for (var x = 0; x < previewBefore.Width; x++)
                if (previewBefore.GetPixel(x, y) != previewAfter.GetPixel(x, y)) { changed = true; break; }
            Require(changed && dialog.Draft.NameAlignmentPercent == 25 && dialog.NameAlignmentInput.Value == 25,
                "Moving the name slider must update the numeric value, draft and preview immediately");
            Require(saveCount == 0 && initial.HorizontalPosition == 70 && initial.TimeFontSize == 30
                && initial.NameAlignmentPercent == 50,
                "Editing a draft must not save or mutate the initial appearance");
            using var snapshot = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(snapshot, new Rectangle(Point.Empty, snapshot.Size));
            snapshot.Save(Path.Combine(output, "overlay-settings-popup.png"));
            typeof(OverlaySettingsForm).GetMethod("FitHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [600]);
            Application.DoEvents();
            Require(dialog.Height <= 600 && dialog.AutoScroll && dialog.AutoScrollPosition.Y == 0,
                "Appearance popup must fit short monitors, scroll and start at the top");
            using var compact = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(compact, new Rectangle(Point.Empty, compact.Size));
            compact.Save(Path.Combine(output, "overlay-settings-popup-short-screen.png"));
            dialog.ScrollControlIntoView(dialog.SaveButton); Application.DoEvents();
            var saveArea = dialog.RectangleToClient(dialog.SaveButton.RectangleToScreen(dialog.SaveButton.ClientRectangle));
            Require(saveArea.Top >= 0 && saveArea.Bottom <= dialog.ClientRectangle.Bottom,
                "Save action must remain reachable by scrolling the short appearance popup");
            dialog.DiscardButton.PerformClick(); Application.DoEvents();
            Require(saveCount == 0 && dialog.DialogResult == DialogResult.Cancel, "Cancel must discard appearance changes");
        }
        using (var dialog = new OverlaySettingsForm(initial, value => { saveCount++; saved = value; return null; }))
        {
            dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
            dialog.HorizontalPositionInput.Value = 12; dialog.VerticalPositionInput.Value = 85;
            dialog.TransparencyInput.Value = 10; dialog.TimeFontSizeInput.Value = 36.5m; dialog.NameFontSizeInput.Value = 18.5m;
            dialog.NameAlignmentInput.Value = 37;
            Require(dialog.NameAlignmentSlider.Value == 37, "Numeric name position must update its slider");
            dialog.SaveButton.PerformClick(); Application.DoEvents();
            Require(saveCount == 1 && saved == new GameOverlayAppearance { HorizontalPosition = 12,
                VerticalPosition = 85, Transparency = 10, TimeFontSize = 36.5f, NameFontSize = 18.5f,
                NameAlignmentPercent = 37 }
                && dialog.DialogResult == DialogResult.OK, "Save must commit exactly the selected appearance once");
        }
        using (var dialog = new OverlaySettingsForm(new GameOverlayAppearance(), _ => "검증용 저장 오류"))
        {
            dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
            Require(dialog.NameAlignmentSlider.Value == 100 && dialog.NameAlignmentInput.Value == 100
                && dialog.Draft.NameAlignmentPercent == 100,
                "A fresh appearance popup must default its continuous name position to the right edge");
            using var defaults = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(defaults, new Rectangle(Point.Empty, defaults.Size));
            defaults.Save(Path.Combine(output, "overlay-settings-popup-default.png"));
            dialog.SaveButton.PerformClick(); Application.DoEvents();
            Require(dialog.Visible && dialog.DialogResult != DialogResult.OK && dialog.ErrorLabel.Text.Contains("검증용 저장 오류"),
                "A failed save must keep the appearance draft open and display its error");
            dialog.DiscardButton.PerformClick();
        }
        Pass("Appearance popup loads and synchronizes continuous name position, updates its preview, isolates cancel, commits once and retains the draft on save errors");
    }

    private static void FailedAppearanceSave()
    {
        var blocked = Path.Combine(output, "blocked-settings.json"); Directory.CreateDirectory(blocked);
        using var main = new MultiTimerForm(new SettingsStore(blocked), alarmFactory: () => new SilentAlarm());
        var settings = (AppSettings)typeof(MultiTimerForm).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var before = JsonSerializer.Serialize(settings);
        var original = settings.OverlayAppearance;
        var save = typeof(MultiTimerForm).GetMethod("SaveOverlayAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = save.Invoke(main, [new GameOverlayAppearance { HorizontalPosition = 0, TimeFontSize = 55 }]);
        Require(error is string { Length: > 0 } && ReferenceEquals(settings.OverlayAppearance, original)
            && JsonSerializer.Serialize(settings) == before,
            "Failed appearance persistence must return an error and restore the exact prior settings");
        using var popup = new OverlaySettingsForm(original, _ => null);
        var enter = Message.Create(popup.TimeFontSizeInput.Handle, 0x0100, (nint)Keys.Enter, 0);
        var space = Message.Create(popup.DiscardButton.Handle, 0x0100, (nint)Keys.Space, 0);
        Require(!((IMessageFilter)main).PreFilterMessage(ref enter) && !((IMessageFilter)main).PreFilterMessage(ref space),
            "Main timer keyboard filtering must allow Enter/Space in the appearance popup");
        using var visual = new VisualSettingsForm(true, true, false, true, (_, _, _, _) => null, () => { });
        var buttonSpace = Message.Create(visual.OverlaySettingsButton.Handle, 0x0100, (nint)Keys.Space, 0);
        Require(!((IMessageFilter)main).PreFilterMessage(ref buttonSpace), "Main timer keyboard filter blocked the overlay settings button");
        Pass("Failed appearance saves roll back all settings; main keyboard filtering allows popup editing and settings-button activation");
    }

    private sealed class GraphicsFixture : IDisposable
    {
        private readonly WindowProcedure procedure = DefWindowProcW;
        private readonly string className;
        private nint context, dc;
        public nint Handle { get; }
        public GraphicsFixture(string name, bool graphics, bool noActivate = true)
        {
            className = name;
            var cls = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = procedure, Name = className, Instance = GetModuleHandleW(null), Style = 0x20 };
            Require(RegisterClassExW(ref cls) != 0, "Fixture class failed to register");
            Handle = CreateWindowExW(noActivate ? 0x08000080u : 0x80u, className, "Overlay graphics verification", 0x90000000, 20000, 20000, 800, 600, 0, 0, cls.Instance, 0);
            Require(Handle != 0, "Fixture window creation failed");
            if (!graphics) return;
            dc = GetDC(Handle);
            var format = new PixelFormat { Size = (ushort)Marshal.SizeOf<PixelFormat>(), Version = 1, Flags = 0x25, ColorBits = 32, DepthBits = 24 };
            var chosen = ChoosePixelFormat(dc, ref format);
            Require(chosen != 0 && SetPixelFormat(dc, chosen, ref format), "OpenGL fixture pixel format failed");
            context = wglCreateContext(dc); Require(context != 0 && wglMakeCurrent(dc, context), "OpenGL fixture context failed");
            glClearColor(0.1f, 0.2f, 0.3f, 1); glClear(0x4000); SwapBuffers(dc);
        }
        public void DrawFrame() { glClear(0x4000); SwapBuffers(dc); }
        public void Dispose() { if (context != 0) { wglMakeCurrent(0, 0); wglDeleteContext(context); } if (dc != 0) ReleaseDC(Handle, dc); DestroyWindow(Handle); UnregisterClassW(className, GetModuleHandleW(null)); }
    }

    private static int FixtureProcess(string directory)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var game = new GraphicsFixture("GLFW30", true);
        using var browser = new GraphicsFixture("Chrome_WidgetWin_1", false);
        File.WriteAllText(Path.Combine(directory, "fixture.json"), JsonSerializer.Serialize(new { Game = game.Handle.ToInt64(), Browser = browser.Handle.ToInt64() }));
        var until = Environment.TickCount64 + 20_000;
        while (Environment.TickCount64 < until)
        {
            if (File.Exists(Path.Combine(directory, "exit.request"))) return 0;
            game.DrawFrame();
            Application.DoEvents(); Thread.Sleep(16);
        }
        return 0;
    }

    private static void IntegratedForegroundSwitching()
    {
        // Foreground selection is injected: unattended checks must not steal focus from the user.
        // Detection still inspects actual windows/modules in a separate OpenGL process.
        nint selectedWindow = 0, game = 0, browser = 0;
        var directory = Path.Combine(output, "integration"); Directory.CreateDirectory(directory);
        foreach (var name in new[] { "fixture.json", "exit.request" }) File.Delete(Path.Combine(directory, name));
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new AppSettings { ShowGameOverlay = true, MinimizeToTray = true,
            Timers = [new() { Name = "게임 타이머", DurationSeconds = 120 }] });
        using var main = new MultiTimerForm(store, alarmFactory: () => new SilentAlarm());
        main.Opacity = 0; main.ShowInTaskbar = false; main.Show(); Application.DoEvents();
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
        var existing = Field<GameOverlayController>(main, "gameOverlay"); existing.Dispose();
        var cards = Field<List<TimerCard>>(main, "cards"); cards[0].Begin(120);
        var mainSettings = Field<AppSettings>(main, "settings");
        var controller = new GameOverlayController(() => cards.Select(card => new OverlayTimerRow(card.Configuration.Name, card.Session.RemainingSeconds)).ToArray(), true,
            _ => new Rectangle(20000, 20000, 800, 600), () => Volatile.Read(ref selectedWindow), () => mainSettings.OverlayAppearance);
        typeof(MultiTimerForm).GetField("gameOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, controller);
        var overlay = Field<GameOverlayForm>(controller, "window");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--fixture"); info.ArgumentList.Add(directory);
        using var child = Process.Start(info)!;
        try
        {
            Wait(() => File.Exists(Path.Combine(directory, "fixture.json")), "Fixture process startup", 4000);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "fixture.json")));
            game = (nint)json.RootElement.GetProperty("Game").GetInt64();
            browser = (nint)json.RootElement.GetProperty("Browser").GetInt64();
            Command("game");
            Wait(() => overlay.Visible, "Selected game must show the overlay", 5000);
            var before = cards[0].Session.RemainingSeconds;
            typeof(MultiTimerForm).GetMethod("SendToTray", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            Wait(() => cards[0].Session.RemainingSeconds < before, "Timer must continue while in the tray", 2000);
            Require(!main.Visible && overlay.Visible, "Tray minimize hid the game overlay");
            var customized = new GameOverlayAppearance { HorizontalPosition = 0, VerticalPosition = 100,
                Transparency = 45, TimeFontSize = 32, NameFontSize = 16, NameAlignmentPercent = 37 }.Normalized();
            var saveAppearance = typeof(MultiTimerForm).GetMethod("SaveOverlayAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Require(saveAppearance.Invoke(main, [customized]) is null && store.Load().OverlayAppearance == customized,
                "Main app must persist appearance changes while retaining its saved timers");
            Wait(() => { GetWindowRect(overlay.Handle, out var current); return current.Left < 20200 && current.Bottom > 20450; },
                "Appearance changes must update the running overlay while minimized to tray", 2000);
            Require(overlay.Controls.Count == 0 && store.Load().Timers[0].Name == "게임 타이머",
                "Customizing the overlay changed the saved timer or added overlay controls");
            Command("browser"); Wait(() => !overlay.Visible, "Browser switch must hide the overlay", 2000);
            Command("game"); Wait(() => overlay.Visible, "Returning to game must restore the overlay", 4000);
            var save = typeof(MultiTimerForm).GetMethod("SaveVisualSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Require(save.Invoke(main, [true, true, false, false]) is null, "Overlay setting save failed");
            Require(!overlay.Visible && !store.Load().ShowGameOverlay, "Disabling the overlay must hide and persist immediately");
            Require(save.Invoke(main, [true, true, false, true]) is null, "Overlay enable save failed");
            Wait(() => overlay.Visible, "Enabling the overlay must resume detection", 4000);
            Command("exit"); Wait(() => child.HasExited && !overlay.Visible, "Game process exit must hide overlay", 3000);
            main.Dispose(); Require(overlay.IsDisposed, "App exit must dispose the overlay");
            Pass("Separate OpenGL process: passive appearance updates in tray, countdown, browser switch, settings toggle, process exit and cleanup");
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
        }
        void Command(string command) {
            if (command is "game" or "browser") Volatile.Write(ref selectedWindow, command == "game" ? game : browser);
            else if (command == "exit") File.WriteAllText(Path.Combine(directory, "exit.request"), "");
        }
        static void Wait(Func<bool> condition, string message, int timeout)
        {
            var deadline = Environment.TickCount64 + timeout;
            while (!condition() && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Sleep(15); }
            Require(condition(), message);
        }
    }

    private sealed class SilentAlarm : IAlarmPlayer
    {
        public bool Start(string path, int volume) => false;
        public void SetVolume(int volume) { }
        public void Stop() { }
        public void Dispose() { }
    }
    private delegate nint WindowProcedure(nint window, uint message, nint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass {
        public uint Size, Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background;
        public string? Menu; public string Name; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PixelFormat {
        public ushort Size, Version; public uint Flags; public byte Type, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift, AlphaBits, AlphaShift, AccumBits, AccumRed, AccumGreen, AccumBlue, AccumAlpha, DepthBits, StencilBits, AuxBuffers, LayerType, Reserved; public uint LayerMask, VisibleMask, DamageMask;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint extended, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint window, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(nint dc, ref PixelFormat format);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(nint dc, int format, ref PixelFormat descriptor);
    [DllImport("gdi32.dll")] private static extern bool SwapBuffers(nint dc);
    [DllImport("opengl32.dll")] private static extern nint wglCreateContext(nint dc);
    [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(nint dc, nint context);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(nint context);
    [DllImport("opengl32.dll")] private static extern void glClearColor(float red, float green, float blue, float alpha);
    [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
}
