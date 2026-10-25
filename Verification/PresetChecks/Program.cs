using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace IntervalTimer;

internal static class PresetChecks
{
    private static string output = "";
    private static readonly List<string> passed = [];
    private static T Field<T>(object instance, string name) => (T)instance.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Call(object instance, string name, params object?[] args) => instance.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pass(string message) { passed.Add(message); Console.WriteLine("PASS " + message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception) { return; }
        throw new Exception("Expected rejection");
    }

    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        Application.SetHighDpiMode(args.Length > 1 && args[1] == "96" ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Run();
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { Passed = passed }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.WriteLine(ex); return 1; }
    }

    private static void Show(Form form)
    {
        form.ShowInTaskbar = false; form.Opacity = 0; form.Show();
        Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
    }
    private static void Picture(Form form, string name)
    {
        Application.DoEvents();
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(Path.Combine(output, name + ".png"));
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls) { yield return control; foreach (var nested in Descendants(control)) yield return nested; }
    }
    private static void Save(PlaylistSettingsForm form, bool success)
    {
        Call(form, "SavePlaylist");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!form.IsDisposed && form.DialogResult != DialogResult.OK && Field<bool>(form, "preparing") && DateTime.UtcNow < deadline)
        { Application.DoEvents(); Thread.Sleep(10); }
        Require(form.DialogResult == DialogResult.OK || form.IsDisposed || !Field<bool>(form, "preparing"), "Save timed out");
        Require((form.DialogResult == DialogResult.OK) == success, "Unexpected save result: " + Field<Label>(form, "error").Text);
    }

    private static void Run()
    {
        var store = new PlaylistPresetStore(Path.Combine(output, "library.json"));
        var defaultPreset = store.Load().Single();
        Require(defaultPreset.Name == "기본 프리셋" && defaultPreset.Items.Count == 0, "Initial default missing");
        Require(new PlaylistPresetStore(Path.Combine(output, "library.json")).Load().Single().Id == defaultPreset.Id, "Default identity changes on reload");
        using (var form = new PlaylistSettingsForm([], false, null, store))
        {
            Show(form); Picture(form, "playlist-initial-default");
            Require(form.SelectedPresetId == defaultPreset.Id && Field<Label>(form, "presetCaption").Text.Contains("기본 프리셋"), "Default not selected initially");
        }
        var emptyPath = Path.Combine(output, "empty-array.json"); File.WriteAllText(emptyPath, "[]");
        Require(new PlaylistPresetStore(emptyPath).Load().Single().Name == "기본 프리셋", "Empty stored library not initialized");
        var legacyPath = Path.Combine(output, "legacy.json");
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(new[] { new PlaylistPreset { Name = "기존 프리셋" } }));
        var legacy = new PlaylistPresetStore(legacyPath);
        Require(legacy.Load().Count == 2 && legacy.Load().Count == 2, "Default migration duplicates or removes legacy presets");
        Pass("Default preset exists and is selected on first open; reload, empty library and legacy migration");
        Reject(() => store.Create("  "));
        var first = store.Create("  작업 음악  ");
        var other = store.Create("휴식 음악");
        Reject(() => store.Create("작업 음악"));
        Reject(() => store.Create(new string('가', 81)));
        var media = Path.Combine(output, "sample.wav");
        File.WriteAllBytes(media, new byte[44]);
        var item = new AlarmPlaylistItem { Path = media, Title = "테스트 음악" };
        var initialStore = new PlaylistPresetStore(Path.Combine(output, "initial-save.json"));
        using (var form = new PlaylistSettingsForm([item], true, null, initialStore))
        {
            Show(form); Save(form, true);
            Require(initialStore.Load().Single().Items.Count == 1 && initialStore.Load().Single().Shuffle,
                "Initial save did not update automatically selected default");
        }
        Pass("Playlist save updates default preset without creating or choosing a preset manually");
        store.SavePlaylist(first.Id, [item], true);
        item.Title = "원본 수정";
        Require(store.Load().Single(p => p.Id == first.Id).Items[0].Title == "테스트 음악", "Preset is not a snapshot");
        Require(store.Load().Single(p => p.Id == other.Id).Items.Count == 0, "Unselected preset changed");
        Pass("Named creation, blank/duplicate/long names, copied items, independent presets and disk reload");

        using (var popup = new PlaylistPresetsForm(store, first.Id))
        {
            Show(popup); Picture(popup, "presets");
            Require(Field<ListBox>(popup, "presets").SelectedItem is PlaylistPreset selected && selected.Id == first.Id, "Current selection lost");
            Call(popup, "LoadPreset");
            Require(popup.SelectedPreset?.Id == first.Id && !popup.PresetCreated, "Load button failed");
        }
        PlaylistPreset created;
        using (var popup = new PlaylistPresetsForm(store, null))
        {
            Show(popup);
            Field<TextBox>(popup, "name").Text = "새 작업 프리셋";
            Call(popup, "CreatePreset");
            Require(popup.PresetCreated && popup.SelectedPreset is not null, "Create button failed");
            created = popup.SelectedPreset!;
        }
        using (var form = new PlaylistSettingsForm([item], false, null, store))
        {
            Show(form);
            var buttons = Descendants(form).OfType<Button>().ToArray();
            var presets = buttons.Single(button => button.Text == "프리셋");
            var add = buttons.Single(button => button.Text == "파일 추가");
            Require(presets.PointToScreen(Point.Empty).X < add.PointToScreen(Point.Empty).X, "Preset button is not left of Add");
            Call(form, "ApplyPreset", created, true);
            Require(Field<List<PlaylistFileCard>>(form, "cards").Count == 1, "Creating preset replaced draft");
            Require(store.Load().Single(p => p.Id == created.Id).Items.Count == 0, "Creation prematurely saved draft");
            Picture(form, "playlist-selected");
            Save(form, true);
            Require(store.Load().Single(p => p.Id == created.Id).Items.Count == 1, "Playlist Save did not write selection");
        }
        Pass("Popup creation/loading, button placement, selection and save-time capture");

        using (var form = new PlaylistSettingsForm([item], true, null, store))
        {
            Show(form);
            var completed = false;
            PlaylistPresetsForm? createdPopup = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            using var driver = new System.Windows.Forms.Timer { Interval = 50 };
            driver.Tick += (_, _) => {
                var popup = Application.OpenForms.OfType<PlaylistPresetsForm>().FirstOrDefault();
                if (popup is null) { if (DateTime.UtcNow > deadline) { driver.Stop(); form.Close(); } return; }
                driver.Stop(); popup.Opacity = 0;
                createdPopup = popup;
                Field<TextBox>(popup, "name").Text = "즉시 선택 확인";
                Descendants(popup).OfType<Button>().Single(button => button.Text == "생성").PerformClick();
                completed = popup.DialogResult == DialogResult.OK;
            };
            driver.Start(); Call(form, "OpenPresets"); driver.Stop();
            var immediate = store.Load().Single(preset => preset.Name == "즉시 선택 확인");
            Require(completed && createdPopup is { Visible: false } && form.SelectedPresetId == immediate.Id,
                "Create did not close popup and select new preset");
            Require(Field<List<PlaylistFileCard>>(form, "cards").Count == 1 && form.Shuffle, "Create replaced draft");
            Require(immediate.Items.Count == 0, "Create saved draft before playlist save");
            Picture(form, "playlist-after-create"); Save(form, true);
            Require(store.Load().Single(preset => preset.Id == immediate.Id).Items.Count == 1, "New selection not saved");
            Require(store.Load().Single(preset => preset.Id == defaultPreset.Id).Items.Count == 0, "New preset save changed default");
        }
        Pass("Create button in nested popup closes it immediately, selects new preset and saves only that preset");

        using (var form = new PlaylistSettingsForm([], false, null, store))
        {
            Show(form); Call(form, "ApplyPreset", store.Load().Single(p => p.Id == first.Id), false);
            Require(form.Shuffle && Field<List<PlaylistFileCard>>(form, "cards").Count == 1, "Load lost order/settings");
            Require(Field<List<PlaylistFileCard>>(form, "cards")[0].ReadItem().Path == media, "Load lost file path");
            Call(form, "AddCard", new AlarmPlaylistItem { Path = media });
            Require(store.Load().Single(p => p.Id == first.Id).Items.Count == 1, "Edits wrote before save");
            form.Close();
            Require(store.Load().Single(p => p.Id == first.Id).Items.Count == 1, "Cancel modified library");
        }
        using (var form = new PlaylistSettingsForm([item], false, first.Id, store))
        {
            Show(form);
            Require(form.SelectedPresetId == first.Id && !form.Shuffle, "Restoration replaced timer draft");
            Call(form, "ApplyPreset", null, false);
            Save(form, true);
            Require(store.Load().Single(p => p.Id == first.Id).Shuffle, "Unselected save modified library");
            Require(form.SelectedPresetId == Guid.Empty, "Explicit deselection not recorded");
        }
        using (var form = new PlaylistSettingsForm([], false, Guid.Empty, store))
        { Require(form.SelectedPresetId == Guid.Empty, "Reopening lost explicit deselection"); }
        using (var form = new PlaylistSettingsForm([new AlarmPlaylistItem { Path = Path.Combine(output, "missing.wav") }], false, first.Id, store))
        {
            Show(form); Save(form, false);
            Require(store.Load().Single(p => p.Id == first.Id).Items[0].Path == media, "Invalid save modified preset");
        }
        using (var form = new PlaylistSettingsForm([], false, first.Id, store))
        { Show(form); Save(form, false); Require(store.Load().Single(p => p.Id == first.Id).Items.Count == 1, "Empty save modified preset"); }
        ExternalLinkPolicy.UsageConfirmed = false;
        using (var form = new PlaylistSettingsForm([new AlarmPlaylistItem { Path = "https://example.com/audio", ExternalLink = true }], false, first.Id, store))
        { Show(form); Save(form, false); Require(store.Load().Single(p => p.Id == first.Id).Items[0].Path == media, "Permission failure modified preset"); }
        Pass("Load metadata and shuffle, draft isolation, cancel, deselection, invalid/empty save protection");

        var cfg = new TimerSettings { PlaylistPresetId = first.Id };
        var alarm = AlarmSoundSettings.From(cfg);
        var restored = new TimerSettings(); alarm.ApplyTo(restored);
        Require(restored.PlaylistPresetId == first.Id, "Alarm settings lost preset association");
        var settingsStore = new SettingsStore(Path.Combine(output, "settings.json"));
        settingsStore.Save(new AppSettings { Timers = [cfg] });
        Require(settingsStore.Load().Timers[0].PlaylistPresetId == first.Id, "Restart lost selection");
        Pass("Preset association survives alarm settings and settings reload");

        var recovered = new PlaylistPresetStore(Path.Combine(output, "recovery.json"));
        var backup = recovered.Create("백업 확인"); recovered.SavePlaylist(backup.Id, [item], false);
        File.WriteAllText(Path.Combine(output, "recovery.json"), "broken");
        Require(recovered.Load().Single(p => p.Id == backup.Id).Name == "백업 확인", "Backup recovery failed");
        File.WriteAllText(Path.Combine(output, "recovery.json.bak"), "broken");
        Reject(() => recovered.Load());
        var blocked = Path.Combine(output, "blocked"); File.WriteAllText(blocked, "file");
        Reject(() => new PlaylistPresetStore(Path.Combine(blocked, "library.json")).Create("실패"));
        var blockedLibraryPath = Path.Combine(output, "blocked-library.json");
        var blockedStore = new PlaylistPresetStore(blockedLibraryPath);
        var blockedPreset = blockedStore.Create("저장 오류 확인");
        blockedStore.SavePlaylist(blockedPreset.Id, [], false);
        File.Move(blockedLibraryPath, blockedLibraryPath + ".original");
        Directory.CreateDirectory(blockedLibraryPath);
        using (var form = new PlaylistSettingsForm([item], false, blockedPreset.Id, blockedStore))
        {
            Show(form); Save(form, false);
            Require(Field<Label>(form, "error").Text.Length > 0 && !form.IsDisposed, "Preset write failure closed dialog");
            Require(form.Items.Count == 0, "Preset write failure accepted timer draft");
        }
        Pass("Backup recovery, corrupt-library and write-failure protection");

        using (var image = new Bitmap(24, 24))
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.White); TimerIcons.Draw(graphics, TimerGlyph.Stop, new RectangleF(0, 0, 24, 24), TimerTheme.StopIcon);
            Require(image.GetPixel(12, 12).ToArgb() == TimerTheme.StopIcon.ToArgb(), "Stop glyph ignores theme color");
            Require(image.GetPixel(7, 7).ToArgb() == TimerTheme.StopIcon.ToArgb(), "Stop glyph is not filled square");
        }
        using (var main = new MultiTimerForm(settingsStore))
        {
            using (var popup = new PlaylistPresetsForm(store, first.Id))
            {
                Show(popup);
                var input = Field<TextBox>(popup, "name");
                var space = Message.Create(input.Handle, 0x0100, (IntPtr)Keys.Space, IntPtr.Zero);
                var enter = Message.Create(input.Handle, 0x0100, (IntPtr)Keys.Enter, IntPtr.Zero);
                Require(!((IMessageFilter)main).PreFilterMessage(ref space) && !((IMessageFilter)main).PreFilterMessage(ref enter), "Main filter blocks preset-name input");
            }
            var card = Field<List<TimerCard>>(main, "cards")[0];
            card.Begin(300);
            Call(main, "UpdateFooter");
            Require(((TimerButton)card.StartButton).Glyph == TimerGlyph.Stop, "Card stop glyph wrong");
            Require(Field<TimerButton>(main, "stopButton").Glyph == TimerGlyph.Stop, "All-stop glyph wrong");
            Show(main); Picture(main, "stop-icons");
        }
        Pass("Theme-colored stop-square rendering and individual/all-stop button state");

        var longName = store.Create(new string('가', 80));
        using (var form = new PlaylistSettingsForm([], false, longName.Id, store))
        {
            Show(form); form.Size = form.MinimumSize; Application.DoEvents(); Picture(form, "playlist-minimum-long-name");
            var header = Field<Control>(form, "headerArea");
            var corner = form.PointToClient(header.PointToScreen(new Point(header.Width, header.Height)));
            Require(corner.X <= form.ClientSize.Width, $"Minimum-width header overflow: corner={corner}, client={form.ClientSize}, bounds={header.Bounds}");
        }
        using (var form = new PlaylistPresetsForm(new PlaylistPresetStore(Path.Combine(output, "empty.json")), null))
        {
            Show(form); Picture(form, "presets-default");
            Require(Field<Button>(form, "load").Enabled && Field<ListBox>(form, "presets").SelectedItem is PlaylistPreset selected
                && selected.Name == "기본 프리셋", "Default not selected in initial popup");
        }
        Pass("Initial popup selects default preset; minimum window with 80-character selected name");
    }
}
