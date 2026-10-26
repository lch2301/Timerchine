package io.github.lch2301.timerchine.core;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

public final class PlaylistPreset {
    public String id = UUID.randomUUID().toString();
    public String name = "";
    public List<Track> items = new ArrayList<>();
    public boolean shuffle;

    public PlaylistPreset copy() {
        PlaylistPreset result = new PlaylistPreset();
        result.id = id;
        result.name = name;
        result.shuffle = shuffle;
        if (items != null) for (Track item : items) if (item != null) result.items.add(item.copy());
        return result;
    }
}
