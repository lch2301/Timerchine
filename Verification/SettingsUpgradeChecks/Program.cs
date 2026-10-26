using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace IntervalTimer;

internal static class SettingsUpgradeChecks
{
    private static readonly List<string> passed = [];
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pass(string value) { passed.Add(value); Console.WriteLine("PASS " + value); }
    [STAThread]
    private static int Main(string[] args)
    {
        var folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json"); var store = new SettingsStore(path);
            var presets = new PlaylistPresetStore(Path.Combine(folder, "playlist-presets.json"));
            var preset = presets.Create("업데이트 후에도 유지");
            var track = new AlarmPlaylistItem { Path = "https://example.org/track", Title = "저장한 음악", ExternalLink = true, ThumbnailPath = @"C:\Music\thumbnail.png" };
            presets.SavePlaylist(preset.Id, [track], true);
            var settings = new AppSettings {
                UseHourglass = false, ShowGameOverlay = false, MinimizeToTray = true,
                OverlayAppearance = new() { HorizontalPosition = 22, VerticalPosition = 81,
                    Transparency = 37, TimeFontSize = 32.5f, NameFontSize = 18,
                    NameAlignment = OverlayNameAlignment.Center, NameAlignmentPercent = 37 },
                CloseBehaviorConfirmed = true, ExternalLinksConfirmed = true,
                StopHotkey = new() { Key = Keys.F9, Modifiers = Keys.Control | Keys.Alt },
                Timers = [new() { Name = "첫 번째 저장 타이머", DurationSeconds = 3723, Volume = 73, ReverseAlarm = true,
                    AlarmPath = @"C:\Music\alarm.wav", UsePlaylist = true, ShufflePlaylist = true, AlarmPlaylist = [track], PlaylistPresetId = preset.Id,
                    StartHotkey = new() { Key = Keys.F8, Modifiers = Keys.Control | Keys.Shift } },
                    new() { Name = "직접 지정한 0초", DurationSeconds = 0, PlaylistPresetId = Guid.Empty }]
            };
            store.Save(settings);
            var saved = JsonNode.Parse(File.ReadAllText(path))!;
            saved["BuildId"] = "previous-version-1.2.0"; saved["Version"] = 4;
            var original = saved.ToJsonString(); File.WriteAllText(path, original);
            var restored = store.Load();
            var expected = saved.DeepClone(); expected["Version"] = 5;
            Require(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(restored)), "An update discarded saved values");
            Require(File.ReadAllText(path) == original, "Loading an older build must not rewrite the source file");
            Require(presets.Load().Single(p => p.Id == preset.Id).Items[0].Path == track.Path, "Preset tracks changed");
            store.Save(restored);
            expected["BuildId"] = AppBuild.Id;
            Require(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(path))), "The first save after an update discarded fields");
            Require(restored.OverlayAppearance == settings.OverlayAppearance, "Custom overlay appearance changed across builds");
            Pass("Changed build and older schema preserve all timer/app settings, overlay appearance, hotkeys, playlists, selected presets and explicit zero through load and save");

            var legacy = saved.DeepClone(); legacy.AsObject().Remove("BuildId"); legacy.AsObject().Remove("ShowGameOverlay");
            legacy.AsObject().Remove("OverlayAppearance");
            File.WriteAllText(path, legacy.ToJsonString());
            restored = store.Load();
            Require(restored.ShowGameOverlay && !restored.UseHourglass && restored.Timers[0].Id == settings.Timers[0].Id
                && restored.Timers[0].DurationSeconds == 3723 && restored.Timers[1].PlaylistPresetId == Guid.Empty,
                "Legacy file was reset instead of defaulting only its missing new preference");
            Require(restored.OverlayAppearance == new GameOverlayAppearance().Normalized(), "Legacy overlay appearance must receive its default without resetting other fields");
            var expectedLegacy = saved.DeepClone(); expectedLegacy["Version"] = 5;
            expectedLegacy.AsObject().Remove("BuildId");
            expectedLegacy["BuildId"] = AppBuild.Id;
            expectedLegacy["ShowGameOverlay"] = true;
            expectedLegacy["OverlayAppearance"] = JsonSerializer.SerializeToNode(new GameOverlayAppearance().Normalized());
            Require(JsonNode.DeepEquals(expectedLegacy, JsonSerializer.SerializeToNode(restored)), "Adding default overlay appearance changed an unrelated legacy field");
            Pass("Missing build identity and overlay appearance use defaults while every saved legacy field survives");

            var missingAlignment = saved.DeepClone(); missingAlignment["OverlayAppearance"]!.AsObject().Remove("NameAlignment");
            missingAlignment["OverlayAppearance"]!.AsObject().Remove("NameAlignmentPercent");
            var missingAlignmentJson = missingAlignment.ToJsonString(); File.WriteAllText(path, missingAlignmentJson);
            restored = store.Load();
            var expectedMissingAlignment = missingAlignment.DeepClone(); expectedMissingAlignment["Version"] = 5;
            expectedMissingAlignment["OverlayAppearance"]!["NameAlignment"] = (int)OverlayNameAlignment.Right;
            expectedMissingAlignment["OverlayAppearance"]!["NameAlignmentPercent"] = 100;
            Require(JsonNode.DeepEquals(expectedMissingAlignment, JsonSerializer.SerializeToNode(restored))
                && File.ReadAllText(path) == missingAlignmentJson,
                "Adding default right name alignment must preserve every existing appearance and timer field without rewriting on load");
            var previousDefaults = saved.DeepClone();
            var oldAppearance = new GameOverlayAppearance { HorizontalPosition = 100, VerticalPosition = 50,
                Transparency = 20, TimeFontSize = 21, NameFontSize = 11.5f };
            previousDefaults["OverlayAppearance"] = JsonSerializer.SerializeToNode(oldAppearance);
            previousDefaults["OverlayAppearance"]!.AsObject().Remove("NameAlignment");
            previousDefaults["OverlayAppearance"]!.AsObject().Remove("NameAlignmentPercent");
            File.WriteAllText(path, previousDefaults.ToJsonString()); restored = store.Load();
            Require(restored.OverlayAppearance == oldAppearance.Normalized() && restored.Timers[0].Id == settings.Timers[0].Id,
                "Changing fresh-install defaults must not replace an existing saved appearance, including its previous default values");
            foreach (var alignment in Enum.GetValues<OverlayNameAlignment>())
            {
                var changedBuild = saved.DeepClone(); changedBuild["BuildId"] = "different-build-" + alignment;
                changedBuild["OverlayAppearance"]!["NameAlignment"] = (int)alignment;
                changedBuild["OverlayAppearance"]!.AsObject().Remove("NameAlignmentPercent");
                File.WriteAllText(path, changedBuild.ToJsonString());
                restored = store.Load();
                var expectedAlignment = changedBuild.DeepClone(); expectedAlignment["Version"] = 5;
                var percent = alignment switch { OverlayNameAlignment.Left => 0, OverlayNameAlignment.Center => 50, _ => 100 };
                expectedAlignment["OverlayAppearance"]!["NameAlignmentPercent"] = percent;
                Require(restored.OverlayAppearance.NameAlignment == alignment
                    && restored.OverlayAppearance.NameAlignmentPercent == percent
                    && JsonNode.DeepEquals(expectedAlignment, JsonSerializer.SerializeToNode(restored)),
                    "A changed build discarded name alignment or another saved value: " + alignment);
                store.Save(restored); expectedAlignment["BuildId"] = AppBuild.Id;
                Require(JsonNode.DeepEquals(expectedAlignment, JsonNode.Parse(File.ReadAllText(path))),
                    "Name alignment failed to survive its first save in a changed build: " + alignment);
            }
            Pass("Legacy name alignment migrates left/center/right to 0/50/100 while preserving every saved appearance and timer value across builds");

            foreach (var percent in new[] { 0, 25, 37, 50, 75, 100 })
            {
                var continuous = saved.DeepClone(); continuous["BuildId"] = "different-percent-build-" + percent;
                continuous["OverlayAppearance"]!["NameAlignment"] = (int)OverlayNameAlignment.Left;
                continuous["OverlayAppearance"]!["NameAlignmentPercent"] = percent;
                File.WriteAllText(path, continuous.ToJsonString()); restored = store.Load();
                var expectedContinuous = continuous.DeepClone(); expectedContinuous["Version"] = 5;
                Require(restored.OverlayAppearance.NameAlignmentPercent == percent
                    && JsonNode.DeepEquals(expectedContinuous, JsonSerializer.SerializeToNode(restored)),
                    "The new percent must override the legacy enum while preserving all other saved values: " + percent);
                store.Save(restored); expectedContinuous["BuildId"] = AppBuild.Id;
                Require(JsonNode.DeepEquals(expectedContinuous, JsonNode.Parse(File.ReadAllText(path))),
                    "Continuous name position did not survive save after a build change: " + percent);
            }
            var explicitNull = saved.DeepClone(); explicitNull["OverlayAppearance"]!["NameAlignmentPercent"] = null;
            File.WriteAllText(path, explicitNull.ToJsonString());
            Require(store.Load().OverlayAppearance.NameAlignmentPercent == 50, "A null percent must migrate the stored center alignment to 50");
            var negativePercent = saved.DeepClone(); negativePercent["OverlayAppearance"]!["NameAlignmentPercent"] = -20;
            File.WriteAllText(path, negativePercent.ToJsonString());
            Require(store.Load().OverlayAppearance.NameAlignmentPercent == 0, "A negative stored name position must clamp to zero");
            Pass("Continuous positions round-trip through changed builds, override legacy enum values, migrate null and clamp negative values without resets");

            var nullAppearance = saved.DeepClone(); nullAppearance["OverlayAppearance"] = null;
            File.WriteAllText(path, nullAppearance.ToJsonString());
            restored = store.Load();
            Require(restored.OverlayAppearance == new GameOverlayAppearance().Normalized() && restored.Timers[0].Id == settings.Timers[0].Id,
                "A null overlay appearance must default without discarding timers");
            var outOfRange = saved.DeepClone();
            outOfRange["OverlayAppearance"] = JsonSerializer.SerializeToNode(new GameOverlayAppearance {
                HorizontalPosition = -50, VerticalPosition = 180, Transparency = 110, TimeFontSize = 110,
                NameFontSize = 1, NameAlignment = (OverlayNameAlignment)999, NameAlignmentPercent = 140 });
            File.WriteAllText(path, outOfRange.ToJsonString());
            restored = store.Load();
            Require(restored.OverlayAppearance == new GameOverlayAppearance {
                HorizontalPosition = 0, VerticalPosition = 100, Transparency = 90, TimeFontSize = 96, NameFontSize = 8,
                NameAlignmentPercent = 100 }
                && restored.Timers[0].Id == settings.Timers[0].Id && restored.Timers[0].AlarmPlaylist[0].Path == track.Path,
                "Invalid appearance fields must be bounded independently while preserving timers and playlists");
            Pass("Null appearance receives defaults, out-of-range values are bounded and invalid name alignment returns to right without resetting saved data");

            File.WriteAllText(path + ".bak", original); File.WriteAllText(path, "corrupt JSON");
            restored = store.Load();
            Require(restored.Timers.Count == 2 && restored.Timers[0].Id == settings.Timers[0].Id
                && restored.Timers[0].StartHotkey.Key == Keys.F8 && !restored.ShowGameOverlay,
                "Backup from an older build was ignored");
            Pass("Corrupt primary recovers all settings from a valid older-build backup");

            var empty = saved.DeepClone(); empty["Timers"] = new JsonArray();
            File.WriteAllText(path, empty.ToJsonString());
            Require(store.Load().Timers.Count == 0, "An intentional empty timer list must survive updates");
            Pass("An intentionally empty timer list remains empty across build changes");
            File.WriteAllText(Path.Combine(folder, "checks.json"), JsonSerializer.Serialize(new { Passed = passed }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
