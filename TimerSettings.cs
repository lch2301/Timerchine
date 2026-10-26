using System.Text.Json;
using System.Windows.Forms;

namespace IntervalTimer;

internal sealed class TimerSettings
{
    public static string DefaultName(int number) => $"타이머 {number}";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int DurationSeconds { get; set; } = 0;
    public string AlarmPath { get; set; } = "";
    public int Volume { get; set; } = 50;
    public bool ReverseAlarm { get; set; }
    public bool UsePlaylist { get; set; }
    public List<AlarmPlaylistItem> AlarmPlaylist { get; set; } = [];
    public bool ShufflePlaylist { get; set; }
    // Null selects the default on first use; Guid.Empty means explicitly deselected.
    public Guid? PlaylistPresetId { get; set; }
    public HotkeyBinding StartHotkey { get; set; } = new();
}

internal sealed class AppSettings
{
    public int Version { get; set; } = 5;
    public string BuildId { get; set; } = AppBuild.Id;
    public bool UseHourglass { get; set; } = true;
    public bool ShowGameOverlay { get; set; } = true;
    public GameOverlayAppearance OverlayAppearance { get; set; } = new();
    // A stored preference and the first-close explanation are separate states.
    // Saving another setting must never count as accepting that explanation.
    public bool? MinimizeToTray { get; set; }
    public bool CloseBehaviorConfirmed { get; set; }
    public bool ExternalLinksConfirmed { get; set; }
    public List<TimerSettings> Timers { get; set; } = [new()];
    public HotkeyBinding StopHotkey { get; set; } = new();

    public void Normalize()
    {
        OverlayAppearance = (OverlayAppearance ?? new()).Normalized();
        Timers ??= [];
        var ids = new HashSet<Guid>();
        for (var i = 0; i < Timers.Count; i++)
        {
            Timers[i] ??= new();
            var timer = Timers[i];
            timer.Name = string.IsNullOrWhiteSpace(timer.Name) ? TimerSettings.DefaultName(i + 1) : timer.Name.Trim();
            if (timer.Name.Length > 80) timer.Name = timer.Name[..80];
            if (timer.Id == Guid.Empty || !ids.Add(timer.Id))
            {
                timer.Id = Guid.NewGuid();
                ids.Add(timer.Id);
            }
            timer.DurationSeconds = Math.Clamp(timer.DurationSeconds, 0, TimerDurationInput.MaximumSeconds);
            timer.Volume = Math.Clamp(timer.Volume, 0, 100);
            timer.AlarmPath ??= "";
            timer.AlarmPlaylist ??= [];
            timer.AlarmPlaylist = timer.AlarmPlaylist.Where(item => item is not null)
                .Select(item => item with { Path = item.Path?.Trim() ?? "",
                    Title = (item.Title ?? "").Trim() is var title && title.Length > 500 ? title[..500] : title,
                    ThumbnailPath = item.ThumbnailPath?.Trim() ?? "",
                    ExternalLink = item.ExternalLink || OnlineAlarmSource.IsLink(item.Path ?? "") }).ToList();
            if (timer.StartHotkey is not { IsValid: true }) timer.StartHotkey = new();
        }
        if (StopHotkey is not { IsValid: true }) StopHotkey = new();
        Version = 5;
    }
}

internal sealed record AlarmPlaylistItem
{
    public string Path { get; set; } = "";
    public bool ExternalLink { get; set; }
    public string Title { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
}

internal sealed record AlarmSoundSettings(string Path, int Volume, bool ReverseAlarm,
    bool UsePlaylist, List<AlarmPlaylistItem> Playlist, bool Shuffle, Guid? PresetId = null)
{
    public static AlarmSoundSettings From(TimerSettings settings) => new(settings.AlarmPath,
        settings.Volume, settings.ReverseAlarm, settings.UsePlaylist,
        settings.AlarmPlaylist.Select(item => item with { }).ToList(), settings.ShufflePlaylist, settings.PlaylistPresetId);

    public void ApplyTo(TimerSettings settings)
    {
        settings.AlarmPath = Path;
        settings.Volume = Math.Clamp(Volume, 0, 100);
        settings.ReverseAlarm = ReverseAlarm;
        settings.UsePlaylist = UsePlaylist;
        settings.AlarmPlaylist = Playlist.Select(item => item with { }).ToList();
        settings.ShufflePlaylist = Shuffle;
        settings.PlaylistPresetId = PresetId;
    }
}

internal sealed class SettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntervalTimer", "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var json = File.ReadAllText(candidate);
                // BuildId is diagnostic metadata, never a reason to discard saved
                // values. Missing new fields use their property defaults; Normalize
                // carries older documents into the current settings schema.
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new JsonException();
                settings.Normalize();
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                // An interrupted or corrupt primary file can still use the last complete backup.
            }
        }
        return Defaults();
    }

    public void Save(AppSettings settings)
    {
        settings.BuildId = AppBuild.Id;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static AppSettings Defaults()
    {
        var settings = new AppSettings();
        settings.Normalize();
        return settings;
    }
}

internal static class AppBuild
{
    // Diagnostic identity only. A different binary must still restore saved settings.
    public static string Id { get; } = typeof(AppSettings).Assembly.ManifestModule.ModuleVersionId.ToString("N");
}
