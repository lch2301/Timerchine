using System.Text.Json;

namespace IntervalTimer;

internal sealed record PlaylistPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<AlarmPlaylistItem> Items { get; set; } = [];
    public bool Shuffle { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => $"{Name}  ·  파일 {Items.Count}개";
}

// Presets are a shared library, independent of a timer and the app's compiled version.
internal sealed class PlaylistPresetStore(string path)
{
    public const string DefaultName = "기본 프리셋";
    private static readonly Guid DefaultId = new("44be04bb-f9e7-443b-9230-7d9556be84cb");
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "playlist-presets.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<PlaylistPreset> Load()
    {
        var presets = LoadStored();
        if (!presets.Any(preset => preset.Name == DefaultName))
        {
            presets.Insert(0, new PlaylistPreset { Id = DefaultId, Name = DefaultName });
            Save(presets);
        }
        return presets;
    }

    private List<PlaylistPreset> LoadStored()
    {
        var found = false;
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            found = true;
            try
            {
                var presets = JsonSerializer.Deserialize<List<PlaylistPreset>>(File.ReadAllText(candidate))
                    ?? throw new JsonException();
                var ids = new HashSet<Guid>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var preset in presets)
                {
                    if (preset is null || preset.Id == Guid.Empty || !ids.Add(preset.Id)
                        || string.IsNullOrWhiteSpace(preset.Name) || !names.Add(preset.Name.Trim())) throw new JsonException();
                    preset.Name = preset.Name.Trim();
                    preset.Items ??= [];
                    preset.Items = preset.Items.Where(item => item is not null).Select(item => item with {
                        Path = item.Path ?? "", Title = item.Title ?? "", ThumbnailPath = item.ThumbnailPath ?? "",
                        ExternalLink = item.ExternalLink || OnlineAlarmSource.IsLink(item.Path ?? "")
                    }).ToList();
                }
                return presets;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        if (found) throw new IOException("프리셋을 읽을 수 없습니다. 프리셋 파일과 백업을 확인하세요.");
        return [];
    }

    public PlaylistPreset Create(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("프리셋 이름을 입력하세요.");
        if (name.Length > 80) throw new ArgumentException("프리셋 이름은 80자까지 입력할 수 있습니다.");
        var presets = Load();
        if (presets.Any(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("이미 사용 중인 프리셋 이름입니다. 다른 이름을 입력하세요.");
        var created = new PlaylistPreset { Name = name };
        presets.Add(created);
        Save(presets);
        return created;
    }

    public void SavePlaylist(Guid id, IEnumerable<AlarmPlaylistItem> items, bool shuffle)
    {
        var presets = Load();
        var preset = presets.FirstOrDefault(preset => preset.Id == id)
            ?? throw new IOException("선택한 프리셋을 찾을 수 없습니다. 프리셋을 다시 선택하세요.");
        preset.Items = items.Select(item => item with { }).ToList();
        preset.Shuffle = shuffle;
        Save(presets);
    }

    private void Save(List<PlaylistPreset> presets)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".playlist-presets-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, presets, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
