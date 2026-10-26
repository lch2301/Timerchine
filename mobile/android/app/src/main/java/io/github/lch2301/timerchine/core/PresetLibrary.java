package io.github.lch2301.timerchine.core;

import java.util.ArrayList;
import java.util.List;

public final class PresetLibrary {
    public static final String DEFAULT_ID = "44be04bb-f9e7-443b-9230-7d9556be84cb";
    public static final String DEFAULT_NAME = "기본 프리셋";
    private PresetLibrary() { }

    public static PlaylistPreset ensureDefault(List<PlaylistPreset> presets) {
        PlaylistPreset existing = find(presets, DEFAULT_ID);
        if (existing != null) return existing;
        for (PlaylistPreset preset : presets) {
            if (preset != null && DEFAULT_NAME.equals(preset.name)) {
                preset.id = DEFAULT_ID;
                return preset;
            }
        }
        PlaylistPreset preset = new PlaylistPreset();
        preset.id = DEFAULT_ID;
        preset.name = DEFAULT_NAME;
        presets.add(0, preset);
        return preset;
    }

    public static PlaylistPreset create(List<PlaylistPreset> presets, String name) {
        String trimmed = name == null ? "" : name.trim();
        if (trimmed.isEmpty()) throw new IllegalArgumentException("프리셋 이름을 입력하세요.");
        if (trimmed.length() > 80) throw new IllegalArgumentException("프리셋 이름은 80자까지 입력할 수 있습니다.");
        ensureDefault(presets);
        for (PlaylistPreset preset : presets) {
            if (preset != null && trimmed.equalsIgnoreCase(preset.name == null ? "" : preset.name.trim()))
                throw new IllegalArgumentException("이미 사용 중인 프리셋 이름입니다. 다른 이름을 입력하세요.");
        }
        PlaylistPreset created = new PlaylistPreset();
        created.name = trimmed;
        presets.add(created);
        return created;
    }

    public static void savePlaylist(List<PlaylistPreset> presets, String id, List<Track> items, boolean shuffle) {
        PlaylistPreset preset = find(presets, id);
        if (preset == null) throw new IllegalArgumentException("선택한 프리셋을 찾을 수 없습니다. 프리셋을 다시 선택하세요.");
        List<Track> saved = new ArrayList<>();
        if (items != null) for (Track item : items) if (item != null) saved.add(item.copy());
        preset.items = saved;
        preset.shuffle = shuffle;
    }

    public static PlaylistPreset find(List<PlaylistPreset> presets, String id) {
        if (presets == null || id == null) return null;
        for (PlaylistPreset preset : presets) if (preset != null && id.equals(preset.id)) return preset;
        return null;
    }
}
