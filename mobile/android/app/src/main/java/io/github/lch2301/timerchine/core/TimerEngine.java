package io.github.lch2301.timerchine.core;

/** Countdown state independent of UI ticks and the lifetime of an Activity. */
public final class TimerEngine {
    public static final int MAXIMUM_SECONDS = 99 * 3600 + 59 * 60 + 59;
    private TimerEngine() { }

    public static void start(TimerState timer, long nowElapsed, long nowWall) {
        int duration = clampDuration(timer.spec.durationSeconds);
        timer.spec.durationSeconds = duration;
        timer.remainingSeconds = duration;
        timer.remainingExactMs = duration * 1000L;
        timer.deadlineElapsedMs = deadline(nowElapsed, duration);
        timer.deadlineWallMs = deadline(nowWall, duration);
        timer.generation++;
        timer.playbackError = "";
        timer.playbackFailed = false;
        timer.phase = duration > 0 ? TimerState.Phase.RUNNING
                : timer.spec.reverse ? TimerState.Phase.COMPLETED : TimerState.Phase.ALARMING;
    }

    public static void stop(TimerState timer, long nowElapsed) {
        timer.remainingExactMs = remainingMilliseconds(timer, nowElapsed);
        timer.remainingSeconds = remainingSeconds(timer, nowElapsed);
        timer.phase = TimerState.Phase.STOPPED;
        timer.generation++;
    }

    /** Returns true only when this call changes a running timer to its end state. */
    public static boolean tick(TimerState timer, long nowElapsed) {
        if (timer.phase != TimerState.Phase.RUNNING) return false;
        timer.remainingSeconds = remainingSeconds(timer, nowElapsed);
        timer.remainingExactMs = remainingMilliseconds(timer, nowElapsed);
        if (timer.remainingSeconds != 0) return false;
        timer.phase = timer.spec.reverse ? TimerState.Phase.COMPLETED : TimerState.Phase.ALARMING;
        return true;
    }

    public static int remainingSeconds(TimerState timer, long nowElapsed) {
        if (timer.phase == TimerState.Phase.RUNNING) {
            if (nowElapsed >= timer.deadlineElapsedMs) return 0;
            long milliseconds = timer.deadlineElapsedMs - nowElapsed;
            if (milliseconds < 0) return MAXIMUM_SECONDS;
            long seconds = milliseconds / 1000L + (milliseconds % 1000L == 0 ? 0 : 1);
            return (int) Math.min(MAXIMUM_SECONDS, seconds);
        }
        if (timer.phase == TimerState.Phase.IDLE) return clampDuration(timer.spec.durationSeconds);
        if (timer.phase == TimerState.Phase.ALARMING || timer.phase == TimerState.Phase.COMPLETED) return 0;
        if (timer.remainingExactMs >= 0)
            return (int) Math.min(MAXIMUM_SECONDS, (timer.remainingExactMs + 999) / 1000);
        return clampDuration(timer.remainingSeconds);
    }

    public static long remainingMilliseconds(TimerState timer, long nowElapsed) {
        if (timer.phase == TimerState.Phase.RUNNING)
            return Math.max(0L, Math.min(MAXIMUM_SECONDS * 1000L, timer.deadlineElapsedMs - nowElapsed));
        if (timer.phase == TimerState.Phase.IDLE) return clampDuration(timer.spec.durationSeconds) * 1000L;
        if (timer.phase == TimerState.Phase.ALARMING || timer.phase == TimerState.Phase.COMPLETED) return 0;
        return timer.remainingExactMs >= 0 ? Math.min(MAXIMUM_SECONDS * 1000L, timer.remainingExactMs)
                : clampDuration(timer.remainingSeconds) * 1000L;
    }

    public static boolean isActive(TimerState timer) {
        return timer.phase == TimerState.Phase.RUNNING || timer.phase == TimerState.Phase.ALARMING;
    }

    public static boolean isAudioActive(TimerState timer) {
        return !timer.playbackFailed && (timer.spec.reverse ? timer.phase == TimerState.Phase.RUNNING
                : timer.phase == TimerState.Phase.ALARMING);
    }

    public static int clampDuration(int seconds) { return Math.max(0, Math.min(MAXIMUM_SECONDS, seconds)); }

    private static long deadline(long now, int seconds) {
        long durationMs = seconds * 1000L;
        return now > Long.MAX_VALUE - durationMs ? Long.MAX_VALUE : now + durationMs;
    }
}
