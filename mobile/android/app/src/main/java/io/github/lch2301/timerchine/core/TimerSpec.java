package io.github.lch2301.timerchine.core;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

public final class TimerSpec {
    public String id = UUID.randomUUID().toString();
    public String name = "타이머 1";
    public int durationSeconds;
    public int volume = 50;
    public boolean reverse;
    public boolean usePlaylist;
    public boolean shuffle;
    public String presetId = PresetLibrary.DEFAULT_ID;
    public String soundUri = "";
    public String soundTitle = "기본 알람음";
    public List<Track> playlist = new ArrayList<>();

    public TimerSpec copy() {
        TimerSpec result = new TimerSpec();
        result.id = id;
        result.name = name;
        result.durationSeconds = durationSeconds;
        result.volume = volume;
        result.reverse = reverse;
        result.usePlaylist = usePlaylist;
        result.shuffle = shuffle;
        result.presetId = presetId;
        result.soundUri = soundUri;
        result.soundTitle = soundTitle;
        if (playlist != null) for (Track item : playlist) if (item != null) result.playlist.add(item.copy());
        return result;
    }

    /** Playback identity includes mode plus every desktop source setting, even dormant ones. */
    public String sourceKey() {
        StringBuilder key = new StringBuilder(reverse ? "reverse|" : "normal|");
        key.append(sourceConfigurationKey());
        return key.toString();
    }

    /** Desktop sourceChanged excludes mode, titles, thumbnails, volume and preset ID. */
    public boolean hasSameAudioSource(TimerSpec other) {
        return other != null && sourceConfigurationKey().equals(other.sourceConfigurationKey());
    }

    private String sourceConfigurationKey() {
        StringBuilder key = new StringBuilder();
        appendPart(key, soundUri);
        key.append(usePlaylist ? '1' : '0').append('|').append(shuffle ? '1' : '0').append('|');
        if (playlist != null) for (Track item : playlist) {
            if (item == null) continue;
            appendPart(key, item.uri);
            key.append(item.remote ? '1' : '0').append('|');
        }
        return key.toString();
    }

    private static void appendPart(StringBuilder target, String value) {
        String text = value == null ? "" : value;
        target.append(text.length()).append(':').append(text).append('|');
    }
}
