package io.github.lch2301.timerchine;

import io.github.lch2301.timerchine.core.PlaylistPreset;
import io.github.lch2301.timerchine.core.PresetLibrary;
import io.github.lch2301.timerchine.core.TimerState;
import java.util.ArrayList;
import java.util.List;

public final class Snapshot {
    public List<TimerState> timers = new ArrayList<>();
    public List<PlaylistPreset> presets = new ArrayList<>();
    public boolean useHourglass = true;
    public boolean externalLinksConfirmed;
    public Boolean minimizeToBackground;
    public boolean closeBehaviorConfirmed;
    public String recoveryNotice = "";

    public Snapshot() {
        timers.add(new TimerState());
        PresetLibrary.ensureDefault(presets);
    }

    public Snapshot copy() {
        Snapshot result = new Snapshot();
        result.timers.clear();
        result.presets.clear();
        if (timers != null) for (TimerState timer : timers) if (timer != null) result.timers.add(timer.copy());
        if (presets != null) for (PlaylistPreset preset : presets) if (preset != null) result.presets.add(preset.copy());
        result.useHourglass = useHourglass;
        result.externalLinksConfirmed = externalLinksConfirmed;
        result.minimizeToBackground = minimizeToBackground;
        result.closeBehaviorConfirmed = closeBehaviorConfirmed;
        result.recoveryNotice = recoveryNotice;
        return result;
    }
}
