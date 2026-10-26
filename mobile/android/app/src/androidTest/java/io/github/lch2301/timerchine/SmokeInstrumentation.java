package io.github.lch2301.timerchine;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Context;
import android.content.Intent;
import android.media.MediaPlayer;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.ParcelFileDescriptor;
import android.os.SystemClock;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.lang.reflect.Field;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import org.json.JSONObject;

import io.github.lch2301.timerchine.core.PlaylistPreset;
import io.github.lch2301.timerchine.core.PresetLibrary;
import io.github.lch2301.timerchine.core.TimerEngine;
import io.github.lch2301.timerchine.core.TimerState;
import io.github.lch2301.timerchine.core.Track;

/** Device tests using only the Android framework; never touches the release app. */
public final class SmokeInstrumentation extends Instrumentation {
    private Context target;
    private Activity activity;
    private String toneUri;
    private int current;
    private String activeTest = "initialization";
    private static final int TEST_COUNT = 6;

    @Override public void onCreate(Bundle arguments) {
        super.onCreate(arguments);
        start();
    }

    @Override public void onStart() {
        Bundle result = new Bundle();
        int resultCode = Activity.RESULT_OK;
        try {
            target = getTargetContext();
            check(target.getPackageName().endsWith(".debug"), "Device tests require the separate .debug app.");
            shell("input keyevent KEYCODE_WAKEUP");
            shell("wm dismiss-keyguard");
            if (Build.VERSION.SDK_INT >= 33)
                shell("pm grant " + target.getPackageName() + " android.permission.POST_NOTIFICATIONS");
            else if (Build.VERSION.SDK_INT >= 31)
                shell("appops set " + target.getPackageName() + " SCHEDULE_EXACT_ALARM allow");
            check(TimerController.canScheduleExact(target), "Exact-alarm permission is required for device tests.");
            toneUri = createTone();
            runCase("closeNormalDeadlinesWhileScreenOff", this::normalDeadlines);
            runCase("reverseAudioContinuesInBackgroundAndEnds", this::reverseDeadline);
            runCase("restartRetainsMusicCursorAndResetsCountdown", this::cursorRestart);
            runCase("normalAlarmAlwaysStartsAtTrackBeginning", this::normalRestartsFromBeginning);
            runCase("selectedPresetAndPlaylistSurviveStoreReload", this::presetPersistence);
            runCase("appUpgradeKeepsSettingsAndBackupRecoversSettings", this::upgradeAndBackup);
            result.putString("stream", "\nOK (" + TEST_COUNT + " tests)\n");
        } catch (Throwable failure) {
            Bundle failed = status(activeTest);
            failed.putString("stack", Log.getStackTraceString(failure));
            failed.putString("stream", Log.getStackTraceString(failure));
            sendStatus(-2, failed);
            result.putString("stream", "\nFAILURES!!!\n" + Log.getStackTraceString(failure));
            resultCode = Activity.RESULT_CANCELED;
        } finally {
            if (target != null && target.getPackageName().endsWith(".debug")) {
                try { runOnMainSync(() -> TimerController.stopAll(target)); } catch (Throwable ignored) { }
                try { shell("input keyevent KEYCODE_WAKEUP"); } catch (Throwable ignored) { }
            }
        }
        finish(resultCode, result);
    }

    private void runCase(String name, CheckedRunnable action) throws Exception {
        current++;
        activeTest = name;
        sendStatus(1, status(name));
        action.run();
        sendStatus(0, status(name));
    }

    private Bundle status(String name) {
        Bundle status = new Bundle();
        status.putString("id", "Timerchine");
        status.putString("class", getClass().getName());
        status.putString("test", name);
        status.putInt("numtests", TEST_COUNT);
        status.putInt("current", current);
        return status;
    }

    private void normalDeadlines() throws Exception {
        Snapshot snapshot = new Snapshot();
        TimerState first = timer("첫 번째 알람", 4, false);
        TimerState second = timer("두 번째 알람", 6, false);
        snapshot.timers.clear();
        snapshot.timers.add(first);
        snapshot.timers.add(second);
        replace(snapshot);
        foreground();
        runOnMainSync(() -> {
            TimerController.start(target, first.spec.id);
            TimerController.start(target, second.spec.id);
            activity.moveTaskToBack(true);
        });
        shell("input keyevent KEYCODE_SLEEP");
        await(() -> phase(first.spec.id) == TimerState.Phase.ALARMING
                && phase(second.spec.id) == TimerState.Phase.ALARMING, 15_000L,
                "Two closely spaced normal deadlines did not both fire with the screen off.");
        await(() -> playerPosition(first.spec.id) >= 0 && playerPosition(second.spec.id) >= 0,
                5_000L, "Both alarms must actually be playing, not merely marked ALARMING.");
        target.sendBroadcast(new Intent(target, TimerReceiver.class).setAction(TimerReceiver.ACTION_STOP_ALL));
        await(() -> !active(first.spec.id) && !active(second.spec.id)
                && playerPosition(first.spec.id) < 0 && playerPosition(second.spec.id) < 0,
                5_000L, "The notification stop-all action did not end both alarms.");
    }

    private void reverseDeadline() throws Exception {
        TimerState reverse = timer("역알람 종료", 7, true);
        Snapshot snapshot = new Snapshot();
        snapshot.timers.clear();
        snapshot.timers.add(reverse);
        replace(snapshot);
        foreground();
        runOnMainSync(() -> TimerController.start(target, reverse.spec.id));
        await(() -> playerPosition(reverse.spec.id) >= 0, 5_000L, "Reverse audio did not start.");
        runOnMainSync(() -> activity.moveTaskToBack(true));
        shell("input keyevent KEYCODE_SLEEP");
        SystemClock.sleep(1_000L);
        check(playerPosition(reverse.spec.id) >= 0, "Reverse audio stopped when the app went into the background.");
        await(() -> phase(reverse.spec.id) == TimerState.Phase.COMPLETED
                && playerPosition(reverse.spec.id) < 0, 12_000L,
                "Reverse audio did not stop at its deadline with the screen off.");
    }

    private void cursorRestart() throws Exception {
        TimerState reverse = timer("음악 위치 복구", 30, true);
        Snapshot snapshot = new Snapshot();
        snapshot.timers.clear();
        snapshot.timers.add(reverse);
        replace(snapshot);
        foreground();
        runOnMainSync(() -> TimerController.start(target, reverse.spec.id));
        await(() -> playerPosition(reverse.spec.id) >= 1_000, 6_000L, "Music cursor did not advance.");
        runOnMainSync(() -> TimerController.stop(target, reverse.spec.id));
        int savedPosition = find(reverse.spec.id).playbackPositionMs;
        check(savedPosition >= 800, "Stopping did not checkpoint the music cursor.");
        SystemClock.sleep(500L);
        runOnMainSync(() -> TimerController.start(target, reverse.spec.id));
        await(() -> playerPosition(reverse.spec.id) >= savedPosition - 200, 5_000L,
                "Restart did not resume the saved track position.");
        check(find(reverse.spec.id).remainingSeconds >= 27,
                "Restart must begin a full configured countdown while retaining music position.");
        runOnMainSync(() -> TimerController.stop(target, reverse.spec.id));
    }

    private void presetPersistence() throws Exception {
        Snapshot snapshot = AppStore.load(target);
        PlaylistPreset preset = PresetLibrary.create(snapshot.presets, "기기 테스트 프리셋");
        Track track = new Track();
        track.uri = toneUri;
        track.title = "테스트 음원";
        preset.items.add(track);
        preset.shuffle = true;
        TimerState timer = snapshot.timers.get(0);
        timer.spec.presetId = preset.id;
        timer.spec.usePlaylist = true;
        timer.spec.playlist.clear();
        timer.spec.playlist.add(track.copy());
        timer.spec.shuffle = true;
        runOnMainSync(() -> AppStore.save(target, snapshot));
        Snapshot reloaded = AppStore.load(target);
        PlaylistPreset restored = PresetLibrary.find(reloaded.presets, preset.id);
        check(restored != null && restored.shuffle && restored.items.size() == 1
                && toneUri.equals(restored.items.get(0).uri), "Preset tracks did not survive a disk reload.");
        check(preset.id.equals(reloaded.timers.get(0).spec.presetId), "The selected preset was not preserved.");
        check(PresetLibrary.find(reloaded.presets, PresetLibrary.DEFAULT_ID) != null,
                "The default preset must remain available.");
    }

    private void normalRestartsFromBeginning() throws Exception {
        TimerState alarm = timer("기본 알람 위치 초기화", 0, false);
        Snapshot snapshot = new Snapshot();
        snapshot.timers.clear();
        snapshot.timers.add(alarm);
        replace(snapshot);
        foreground();
        runOnMainSync(() -> TimerController.start(target, alarm.spec.id));
        await(() -> playerPosition(alarm.spec.id) >= 1_000, 6_000L, "Normal alarm did not advance.");
        runOnMainSync(() -> TimerController.stop(target, alarm.spec.id));
        check(find(alarm.spec.id).playbackPositionMs == 0, "A normal alarm must discard its previous cursor.");
        runOnMainSync(() -> TimerController.start(target, alarm.spec.id));
        await(() -> playerPosition(alarm.spec.id) >= 0, 5_000L, "Normal alarm restart did not play.");
        check(playerPosition(alarm.spec.id) < 800, "A normal alarm resumed the previous cursor instead of restarting.");
        runOnMainSync(() -> TimerController.stop(target, alarm.spec.id));
    }

    private void upgradeAndBackup() throws Exception {
        runOnMainSync(() -> TimerController.shutdown(target));
        Snapshot snapshot = AppStore.load(target);
        int presetCount = snapshot.presets.size();
        snapshot.timers.get(0).spec.durationSeconds = 37;
        snapshot.minimizeToBackground = false;
        snapshot.closeBehaviorConfirmed = true;
        runOnMainSync(() -> AppStore.save(target, snapshot));
        Snapshot roundTrip = AppStore.load(target);
        check(roundTrip.timers.get(0).spec.durationSeconds == 37
                && Boolean.FALSE.equals(roundTrip.minimizeToBackground) && roundTrip.closeBehaviorConfirmed,
                "Timer and close preferences did not survive a disk reload.");
        File primary = new File(target.getFilesDir(), "timerchine-state.json");
        JSONObject previousBuild = new JSONObject(new String(Files.readAllBytes(primary.toPath()), StandardCharsets.UTF_8));
        previousBuild.put("buildId", "an older compiled build");
        Files.write(primary.toPath(), previousBuild.toString().getBytes(StandardCharsets.UTF_8));
        Snapshot restored = AppStore.load(target);
        check(restored.timers.size() == snapshot.timers.size() && restored.timers.get(0).spec.durationSeconds == 37
                && restored.timers.get(0).spec.id.equals(snapshot.timers.get(0).spec.id)
                && Boolean.FALSE.equals(restored.minimizeToBackground) && restored.closeBehaviorConfirmed,
                "A changed compiled build must preserve timer and app preferences.");
        check(restored.presets.size() == presetCount, "A changed compiled build must preserve the preset library.");
        runOnMainSync(() -> { AppStore.save(target, restored); AppStore.save(target, restored); });
        Files.write(primary.toPath(), new byte[] {'b','r','o','k','e','n'});
        Snapshot recovered = AppStore.load(target);
        check(recovered.presets.size() == presetCount && recovered.recoveryNotice.contains("백업"),
                "A damaged primary state file must recover the validated previous copy.");
        runOnMainSync(() -> AppStore.save(target, recovered));
    }

    private TimerState timer(String name, int seconds, boolean reverse) {
        TimerState timer = new TimerState();
        timer.spec.name = name;
        timer.spec.durationSeconds = seconds;
        timer.spec.reverse = reverse;
        timer.spec.soundUri = toneUri;
        timer.spec.soundTitle = "테스트 음원";
        timer.spec.volume = 20;
        return timer;
    }

    private void replace(Snapshot snapshot) {
        runOnMainSync(() -> {
            TimerController.stopAll(target);
            AppStore.save(target, snapshot);
        });
        waitForIdleSync();
    }

    private void foreground() throws Exception {
        shell("input keyevent KEYCODE_WAKEUP");
        shell("wm dismiss-keyguard");
        Intent open = new Intent().setClassName(target.getPackageName(), MainActivity.class.getName())
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        activity = startActivitySync(open);
        check(activity != null, "Failed to open the app before user-initiated playback.");
        waitForIdleSync();
        SystemClock.sleep(300L);
    }

    private TimerState find(String id) {
        for (TimerState timer : TimerController.read(target).timers)
            if (id.equals(timer.spec.id)) return timer;
        throw new AssertionError("Missing timer: " + id);
    }

    private TimerState.Phase phase(String id) { return find(id).phase; }
    private boolean active(String id) { return TimerEngine.isActive(find(id)); }

    /** Check real MediaPlayer output on its owning main thread. */
    private int playerPosition(String id) {
        AtomicInteger position = new AtomicInteger(-1);
        runOnMainSync(() -> {
            try {
                Field singleton = PlaybackService.class.getDeclaredField("instance");
                singleton.setAccessible(true);
                Object service = singleton.get(null);
                if (service == null) return;
                Field slotField = PlaybackService.class.getDeclaredField("slots");
                slotField.setAccessible(true);
                Object slot = ((Map<?, ?>) slotField.get(service)).get(id);
                if (slot == null) return;
                Field mediaField = slot.getClass().getDeclaredField("player");
                mediaField.setAccessible(true);
                MediaPlayer player = (MediaPlayer) mediaField.get(slot);
                if (player != null && player.isPlaying()) position.set(player.getCurrentPosition());
            } catch (ReflectiveOperationException | IllegalStateException unavailable) {
                position.set(-1);
            }
        });
        return position.get();
    }

    private String createTone() throws Exception {
        int sampleRate = 16_000;
        int sampleCount = sampleRate * 20;
        int dataBytes = sampleCount * 2;
        ByteBuffer wav = ByteBuffer.allocate(44 + dataBytes).order(ByteOrder.LITTLE_ENDIAN);
        wav.put(new byte[] {'R','I','F','F'}).putInt(36 + dataBytes)
                .put(new byte[] {'W','A','V','E','f','m','t',' '}).putInt(16)
                .putShort((short) 1).putShort((short) 1).putInt(sampleRate)
                .putInt(sampleRate * 2).putShort((short) 2).putShort((short) 16)
                .put(new byte[] {'d','a','t','a'}).putInt(dataBytes);
        for (int i = 0; i < sampleCount; i++)
            wav.putShort((short) (Math.sin(i * 2 * Math.PI * 440 / sampleRate) * 2_000));
        File file = new File(target.getFilesDir(), "instrumentation-tone.wav");
        try (FileOutputStream out = new FileOutputStream(file)) { out.write(wav.array()); }
        return Uri.fromFile(file).toString();
    }

    private void shell(String command) throws Exception {
        try (ParcelFileDescriptor.AutoCloseInputStream in = new ParcelFileDescriptor.AutoCloseInputStream(
                getUiAutomation().executeShellCommand(command))) {
            byte[] buffer = new byte[1024];
            while (in.read(buffer) >= 0) { /* Drain the shell command before proceeding. */ }
        }
    }

    private void await(Check condition, long timeoutMs, String failure) throws Exception {
        long deadline = SystemClock.elapsedRealtime() + timeoutMs;
        while (SystemClock.elapsedRealtime() < deadline) {
            if (condition.test()) return;
            SystemClock.sleep(100L);
        }
        throw new AssertionError(failure);
    }

    private static void check(boolean condition, String failure) {
        if (!condition) throw new AssertionError(failure);
    }

    private interface Check { boolean test() throws Exception; }
    private interface CheckedRunnable { void run() throws Exception; }
}
