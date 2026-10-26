package io.github.lch2301.timerchine.core;

import java.util.ArrayList;
import java.util.List;

public final class TimerState {
    public enum Phase { IDLE, RUNNING, ALARMING, STOPPED, COMPLETED }

    public TimerSpec spec = new TimerSpec();
    public Phase phase = Phase.IDLE;
    public long deadlineElapsedMs;
    public long deadlineWallMs;
    public long generation;
    public int remainingSeconds;
    public long remainingExactMs = -1;
    public int playbackPositionMs;
    public int playlistIndex;
    public List<Integer> playbackOrder = new ArrayList<>();
    public String playbackSourceKey = "";
    public String playbackError = "";
    public boolean playbackFailed;

    public TimerState copy() {
        TimerState result = new TimerState();
        result.spec = spec == null ? new TimerSpec() : spec.copy();
        result.phase = phase;
        result.deadlineElapsedMs = deadlineElapsedMs;
        result.deadlineWallMs = deadlineWallMs;
        result.generation = generation;
        result.remainingSeconds = remainingSeconds;
        result.remainingExactMs = remainingExactMs;
        result.playbackPositionMs = playbackPositionMs;
        result.playlistIndex = playlistIndex;
        if (playbackOrder != null) result.playbackOrder.addAll(playbackOrder);
        result.playbackSourceKey = playbackSourceKey;
        result.playbackError = playbackError;
        result.playbackFailed = playbackFailed;
        return result;
    }
}
