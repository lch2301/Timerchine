package io.github.lch2301.timerchine;

import android.content.Context;
import android.content.Intent;
import android.os.SystemClock;
import android.provider.Settings;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

import io.github.lch2301.timerchine.core.TimerEngine;
import io.github.lch2301.timerchine.core.TimerSpec;
import io.github.lch2301.timerchine.core.TimerState;

/** All timer mutations read the latest stored state before writing it back. */
public final class TimerController {
    public static final String ACTION_STATE_CHANGED = "io.github.lch2301.timerchine.STATE_CHANGED";
    private static boolean bootChecked;
    private TimerController() { }

    public static synchronized Snapshot read(Context context) {
        ensureBootIdentity(context);
        Snapshot snapshot = AppStore.load(context);
        long now = SystemClock.elapsedRealtime();
        for (TimerState state : snapshot.timers)
            state.remainingSeconds = TimerEngine.remainingSeconds(state, now);
        return snapshot;
    }

    public static boolean canScheduleExact(Context context) {
        return AlarmScheduler.canScheduleExact(context);
    }

    public static synchronized void addTimer(Context context) {
        ensureBootIdentity(context);
        Snapshot snapshot = AppStore.load(context);
        TimerState timer = new TimerState();
        timer.spec.name = "타이머 " + (snapshot.timers.size() + 1);
        snapshot.timers.add(timer);
        save(context, snapshot);
    }

    public static synchronized void removeTimer(Context context, String timerId) {
        ensureBootIdentity(context);
        PlaybackService.captureCursor(timerId);
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        AlarmScheduler.cancel(context, timerId);
        snapshot.timers.removeIf(state -> state.spec.id.equals(timerId));
        PlaybackService.forgetProgress(timerId);
        saveWithAlarmRecovery(context, snapshot, before);
        PlaybackService.refresh(context);
    }

    public static synchronized void updateTimer(Context context, TimerSpec replacement) {
        ensureBootIdentity(context);
        if (replacement == null) return;
        PlaybackService.captureCursor(replacement.id);
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        TimerState state = find(snapshot, replacement.id);
        if (state == null) return;
        boolean sourceChanged = !state.spec.hasSameAudioSource(replacement);
        boolean modeChanged = state.spec.reverse != replacement.reverse;
        boolean timingChanged = state.spec.durationSeconds != replacement.durationSeconds;
        state.spec = replacement.copy();
        state.spec.durationSeconds = TimerEngine.clampDuration(state.spec.durationSeconds);
        state.spec.volume = Math.max(0, Math.min(100, state.spec.volume));
        if (sourceChanged || modeChanged) {
            state.playbackSourceKey = "";
            state.playbackPositionMs = 0;
            state.playlistIndex = 0;
            state.playbackOrder.clear();
            state.playbackError = "";
            state.playbackFailed = false;
            PlaybackService.forgetProgress(state.spec.id);
        }
        if (timingChanged && TimerEngine.isActive(state)) {
            TimerEngine.stop(state, SystemClock.elapsedRealtime());
            AlarmScheduler.cancel(context, state.spec.id);
        }
        if (modeChanged && state.phase == TimerState.Phase.ALARMING && state.spec.reverse)
            state.phase = TimerState.Phase.COMPLETED;
        if (timingChanged && !TimerEngine.isActive(state)) {
            state.remainingSeconds = state.spec.durationSeconds;
            state.remainingExactMs = state.spec.durationSeconds * 1000L;
            state.phase = TimerState.Phase.IDLE;
        }
        saveWithAlarmRecovery(context, snapshot, before);
        PlaybackService.refresh(context);
    }

    public static synchronized void start(Context context, String timerId) {
        ensureBootIdentity(context);
        PlaybackService.captureCursor(timerId);
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        TimerState state = find(snapshot, timerId);
        if (state == null) return;
        requireExactIfNeeded(context, state.spec.durationSeconds);
        if (!state.spec.reverse) resetPlayback(state);
        TimerEngine.start(state, SystemClock.elapsedRealtime(), System.currentTimeMillis());
        AlarmScheduler.cancel(context, timerId);
        if (!AlarmScheduler.schedule(context, state)) schedulingFailed(state);
        snapshot.recoveryNotice = "";
        saveWithAlarmRecovery(context, snapshot, before);
        if (state.playbackError.isEmpty()) PlaybackService.playActionChime(context, true);
        PlaybackService.refresh(context);
    }

    public static synchronized void stop(Context context, String timerId) {
        ensureBootIdentity(context);
        PlaybackService.captureCursor(timerId);
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        TimerState state = find(snapshot, timerId);
        if (state == null) return;
        boolean wasActive = TimerEngine.isActive(state);
        TimerEngine.stop(state, SystemClock.elapsedRealtime());
        if (!state.spec.reverse) resetPlayback(state);
        AlarmScheduler.cancel(context, timerId);
        saveWithAlarmRecovery(context, snapshot, before);
        if (wasActive) PlaybackService.playActionChime(context, false);
        PlaybackService.refresh(context);
    }

    public static synchronized void all(Context context) {
        ensureBootIdentity(context);
        PlaybackService.captureAllCursors();
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        for (TimerState state : snapshot.timers)
            requireExactIfNeeded(context, state.spec.durationSeconds);
        long elapsed = SystemClock.elapsedRealtime();
        long wall = System.currentTimeMillis();
        for (TimerState state : snapshot.timers) {
            if (!state.spec.reverse) resetPlayback(state);
            TimerEngine.start(state, elapsed, wall);
            AlarmScheduler.cancel(context, state.spec.id);
            if (!AlarmScheduler.schedule(context, state)) schedulingFailed(state);
        }
        snapshot.recoveryNotice = "";
        saveWithAlarmRecovery(context, snapshot, before);
        if (!snapshot.timers.isEmpty()) PlaybackService.playActionChime(context, true);
        PlaybackService.refresh(context);
    }

    public static synchronized void stopAll(Context context) {
        ensureBootIdentity(context);
        PlaybackService.captureAllCursors();
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        long now = SystemClock.elapsedRealtime();
        boolean wasActive = false;
        for (TimerState state : snapshot.timers) {
            if (TimerEngine.isActive(state)) {
                wasActive = true;
                TimerEngine.stop(state, now);
                if (!state.spec.reverse) resetPlayback(state);
            }
            AlarmScheduler.cancel(context, state.spec.id);
        }
        saveWithAlarmRecovery(context, snapshot, before);
        if (wasActive) PlaybackService.playActionChime(context, false);
        PlaybackService.refresh(context);
    }

    public static synchronized void updateVolume(Context context, String timerId, int volume) {
        ensureBootIdentity(context);
        Snapshot snapshot = AppStore.load(context);
        TimerState timer = find(snapshot, timerId);
        if (timer == null) return;
        timer.spec.volume = Math.max(0, Math.min(100, volume));
        save(context, snapshot);
        PlaybackService.refresh(context);
    }

    /** The close-X choice to exit fully: no user stop chime, like desktop Dispose. */
    public static synchronized void shutdown(Context context) {
        ensureBootIdentity(context);
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        for (TimerState timer : snapshot.timers) {
            AlarmScheduler.cancel(context, timer.spec.id);
            timer.phase = TimerState.Phase.IDLE;
            timer.remainingSeconds = timer.spec.durationSeconds;
            timer.remainingExactMs = timer.spec.durationSeconds * 1000L;
            timer.deadlineElapsedMs = timer.deadlineWallMs = 0;
            timer.generation++;
            resetPlayback(timer);
        }
        saveWithAlarmRecovery(context, snapshot, before);
        PlaybackService.endSession(context);
    }

    private static void resetPlayback(TimerState timer) {
        timer.playbackPositionMs = 0;
        timer.playlistIndex = 0;
        timer.playbackOrder.clear();
        timer.playbackSourceKey = "";
        timer.playbackError = "";
        timer.playbackFailed = false;
        PlaybackService.forgetProgress(timer.spec.id);
    }

    /** Restore missing deadlines when returning to the app or updating the APK. */
    public static synchronized void reconcile(Context context) {
        ensureBootIdentity(context);
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        boolean changed = tickSnapshot(snapshot);
        for (TimerState state : snapshot.timers) {
            if (state.phase == TimerState.Phase.RUNNING && !AlarmScheduler.schedule(context, state)) {
                schedulingFailed(state);
                changed = true;
            }
        }
        if (changed) saveWithAlarmRecovery(context, snapshot, before);
        PlaybackService.refresh(context);
    }

    /** Lightweight active-service tick; never repeatedly rewrites system alarms. */
    static synchronized void tick(Context context) {
        ensureBootIdentity(context);
        Snapshot snapshot = AppStore.load(context);
        if (tickSnapshot(snapshot)) {
            save(context, snapshot);
            PlaybackService.refresh(context);
        }
    }

    private static boolean tickSnapshot(Snapshot snapshot) {
        boolean changed = false;
        long now = SystemClock.elapsedRealtime();
        for (TimerState state : snapshot.timers) changed |= TimerEngine.tick(state, now);
        return changed;
    }

    public static synchronized void handleDeadline(Context context, String timerId, long generation) {
        ensureBootIdentity(context);
        Snapshot snapshot = AppStore.load(context);
        TimerState state = find(snapshot, timerId);
        if (state == null || state.generation != generation
                || state.phase != TimerState.Phase.RUNNING) return;
        if (!TimerEngine.tick(state, SystemClock.elapsedRealtime())) {
            AlarmScheduler.schedule(context, state);
            return;
        }
        save(context, snapshot);
        PlaybackService.refresh(context);
    }

    public static synchronized void onClockChanged(Context context) {
        ensureBootIdentity(context);
        Snapshot snapshot = AppStore.load(context);
        Snapshot before = snapshot.copy();
        long now = SystemClock.elapsedRealtime();
        long wall = System.currentTimeMillis();
        tickSnapshot(snapshot);
        for (TimerState state : snapshot.timers) {
            if (state.phase == TimerState.Phase.RUNNING) {
                state.deadlineWallMs = wall + Math.max(0, state.deadlineElapsedMs - now);
                if (!AlarmScheduler.schedule(context, state)) schedulingFailed(state);
            }
        }
        saveWithAlarmRecovery(context, snapshot, before);
        PlaybackService.refresh(context);
    }

    public static synchronized void onReboot(Context context) {
        rememberBoot(context);
        Snapshot snapshot = AppStore.load(context);
        boolean interrupted = false;
        for (TimerState state : snapshot.timers) {
            if (TimerEngine.isActive(state)) {
                state.phase = TimerState.Phase.STOPPED;
                state.remainingSeconds = Math.max(0, Math.min(state.spec.durationSeconds,
                        state.remainingSeconds));
                state.remainingExactMs = Math.max(0, Math.min(state.remainingSeconds * 1000L,
                        state.remainingExactMs < 0 ? state.remainingSeconds * 1000L : state.remainingExactMs));
                state.deadlineElapsedMs = 0;
                state.deadlineWallMs = 0;
                state.generation++;
                interrupted = true;
            }
            AlarmScheduler.cancel(context, state.spec.id);
        }
        if (interrupted)
            snapshot.recoveryNotice = "기기가 재시작되어 실행 중이던 타이머를 중단했습니다. 다시 시작해 주세요.";
        save(context, snapshot);
        // BOOT_COMPLETED must not launch a media-playback foreground service.
    }

    private static void ensureBootIdentity(Context context) {
        if (bootChecked) return;
        bootChecked = true;
        int current = Settings.Global.getInt(context.getContentResolver(), Settings.Global.BOOT_COUNT, -1);
        int previous = context.getSharedPreferences("timerchine-runtime", Context.MODE_PRIVATE)
                .getInt("boot-count", -1);
        if (current >= 0 && previous >= 0 && current != previous) {
            // BOOT_COMPLETED is withheld from force-stopped apps. Check again on
            // the next launch before interpreting an old boot's elapsedRealtime.
            onReboot(context);
        } else rememberBoot(context);
    }

    private static void rememberBoot(Context context) {
        bootChecked = true;
        int current = Settings.Global.getInt(context.getContentResolver(), Settings.Global.BOOT_COUNT, -1);
        context.getSharedPreferences("timerchine-runtime", Context.MODE_PRIVATE)
                .edit().putInt("boot-count", current).commit();
    }

    static synchronized void checkpoint(Context context, String timerId, long generation,
            String sourceKey, int positionMs, int playlistIndex, List<Integer> order) {
        Snapshot snapshot = AppStore.load(context);
        TimerState state = find(snapshot, timerId);
        if (state == null || state.generation != generation
                || !state.spec.sourceKey().equals(sourceKey)) return;
        state.playbackSourceKey = sourceKey;
        state.playbackPositionMs = Math.max(0, positionMs);
        state.playlistIndex = Math.max(0, playlistIndex);
        state.playbackOrder = new ArrayList<>(order);
        state.remainingSeconds = TimerEngine.remainingSeconds(state, SystemClock.elapsedRealtime());
        state.remainingExactMs = TimerEngine.remainingMilliseconds(state, SystemClock.elapsedRealtime());
        AppStore.save(context, snapshot);
    }

    static synchronized void audioError(Context context, String timerId, long generation,
            String message, boolean stopTimer) {
        Snapshot snapshot = AppStore.load(context);
        TimerState state = find(snapshot, timerId);
        if (state == null || state.generation != generation) return;
        state.playbackError = message;
        if (stopTimer) state.playbackFailed = true;
        save(context, snapshot);
    }

    static synchronized void serviceStartFailed(Context context, String message) {
        Snapshot snapshot = AppStore.load(context);
        for (TimerState state : snapshot.timers) {
            if (!TimerEngine.isAudioActive(state)) continue;
            state.playbackError = message;
            state.playbackFailed = true;
        }
        save(context, snapshot);
    }

    static TimerState find(Snapshot snapshot, String id) {
        if (id == null) return null;
        for (TimerState state : snapshot.timers) if (id.equals(state.spec.id)) return state;
        return null;
    }

    private static void requireExactIfNeeded(Context context, int durationSeconds) {
        if (durationSeconds > 0 && !canScheduleExact(context))
            throw new IllegalStateException("정확한 타이머를 사용하려면 ‘알람 및 리마인더’ 권한을 허용해 주세요.");
    }

    private static void schedulingFailed(TimerState state) {
        TimerEngine.stop(state, SystemClock.elapsedRealtime());
        state.playbackError = "알람을 예약하지 못했습니다. ‘알람 및 리마인더’ 권한을 확인해 주세요.";
    }

    private static void save(Context context, Snapshot snapshot) {
        AppStore.save(context, snapshot);
        context.sendBroadcast(new Intent(ACTION_STATE_CHANGED).setPackage(context.getPackageName()));
    }

    /** Keep system reservations consistent with disk if an atomic state write fails. */
    private static void saveWithAlarmRecovery(Context context, Snapshot snapshot, Snapshot before) {
        try {
            AppStore.save(context, snapshot);
        } catch (RuntimeException failure) {
            Snapshot persisted = before;
            try { persisted = AppStore.load(context); }
            catch (RuntimeException unreadable) { failure.addSuppressed(unreadable); }
            Set<String> ids = new HashSet<>();
            for (TimerState timer : before.timers) ids.add(timer.spec.id);
            for (TimerState timer : snapshot.timers) ids.add(timer.spec.id);
            for (TimerState timer : persisted.timers) ids.add(timer.spec.id);
            for (String id : ids) {
                try { AlarmScheduler.cancel(context, id); }
                catch (RuntimeException error) { failure.addSuppressed(error); }
            }
            for (TimerState timer : persisted.timers) {
                if (timer.phase != TimerState.Phase.RUNNING) continue;
                try {
                    if (!AlarmScheduler.schedule(context, timer))
                        failure.addSuppressed(new IllegalStateException("기존 타이머 예약을 복원하지 못했습니다."));
                } catch (RuntimeException error) { failure.addSuppressed(error); }
            }
            throw failure;
        }
        context.sendBroadcast(new Intent(ACTION_STATE_CHANGED).setPackage(context.getPackageName()));
    }
}
