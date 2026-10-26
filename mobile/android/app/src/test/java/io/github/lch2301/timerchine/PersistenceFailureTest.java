package io.github.lch2301.timerchine;

import android.app.AlarmManager;
import android.content.Context;
import android.content.Intent;
import android.os.SystemClock;
import android.util.AtomicFile;
import io.github.lch2301.timerchine.core.PresetLibrary;
import io.github.lch2301.timerchine.core.TimerEngine;
import io.github.lch2301.timerchine.core.TimerSpec;
import io.github.lch2301.timerchine.core.TimerState;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;
import org.json.JSONObject;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.Shadows;
import org.robolectric.annotation.Config;
import org.robolectric.annotation.Implementation;
import org.robolectric.annotation.Implements;
import org.robolectric.annotation.LooperMode;
import org.robolectric.annotation.RealObject;
import org.robolectric.shadow.api.Shadow;
import org.robolectric.shadows.ShadowAlarmManager;
import org.robolectric.shadows.ShadowSystemClock;
import org.robolectric.util.ReflectionHelpers;
import static org.junit.Assert.*;

/** Real I/O failures must not discard the current library or system deadlines. */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35, shadows = PersistenceFailureTest.FailingAtomicFileShadow.class)
@LooperMode(LooperMode.Mode.PAUSED)
public class PersistenceFailureTest {
    private static final String PRIMARY = "timerchine-state.json";
    private static final String PREVIOUS = "timerchine-state.previous.json";
    private Context context;
    private AlarmManager alarms;

    @Before public void open() throws Exception {
        context = RuntimeEnvironment.getApplication();
        File build = new File(context.getCacheDir(), "persistence-build.apk");
        try (ZipOutputStream zip = new ZipOutputStream(new FileOutputStream(build))) {
            for (String entry : new String[]{"classes.dex", "resources.arsc"}) {
                zip.putNextEntry(new ZipEntry(entry));
                zip.write(("Persistence regression " + entry).getBytes(StandardCharsets.UTF_8));
                zip.closeEntry();
            }
        }
        context.getApplicationInfo().sourceDir = build.getAbsolutePath();
        for (String name : new String[]{PRIMARY, PREVIOUS})
            for (String suffix : new String[]{"", ".new", ".bak"}) context.deleteFile(name + suffix);
        context.getSharedPreferences("timerchine-runtime", 0).edit().clear().commit();
        ReflectionHelpers.setStaticField(AppStore.class, "currentBuildId", null);
        ReflectionHelpers.setStaticField(TimerController.class, "bootChecked", false);
        FailingAtomicFileShadow.reset();
        alarms = context.getSystemService(AlarmManager.class);
        ShadowAlarmManager.setCanScheduleExactAlarms(true);
        TimerController.read(context);
    }

    @After public void close() { FailingAtomicFileShadow.reset(); }

    @Test public void failedMutationsRestoreBothOriginalDeadlinesAndGenerations() throws Exception {
        Snapshot original = runningPair();
        ShadowSystemClock.advanceBy(Duration.ofSeconds(7));
        String id = original.timers.get(0).spec.id;
        TimerSpec changedDuration = original.timers.get(0).spec.copy();
        changedDuration.durationSeconds += 30;
        Runnable[] mutations = {
            () -> TimerController.stop(context, id),
            () -> TimerController.start(context, id),
            () -> TimerController.removeTimer(context, id),
            () -> TimerController.stopAll(context),
            () -> TimerController.all(context),
            () -> TimerController.shutdown(context),
            () -> TimerController.updateTimer(context, changedDuration)
        };
        byte[] originalBytes = Files.readAllBytes(stateFile(PRIMARY).toPath());
        for (int i = 0; i < mutations.length; i++) {
            FailingAtomicFileShadow.failWriteName = PRIMARY;
            assertThrows("Mutation " + i + " must report the failed write", IllegalStateException.class, mutations[i]::run);
            assertArrayEquals("Mutation " + i + " must retain the saved state", originalBytes,
                    Files.readAllBytes(stateFile(PRIMARY).toPath()));
            assertReservations(original);
        }
    }

    @Test public void failedFirstStartRemovesItsUncommittedSystemAlarm() {
        Snapshot state = AppStore.load(context);
        state.timers.get(0).spec.durationSeconds = 60;
        AppStore.save(context, state);
        FailingAtomicFileShadow.failWriteName = PRIMARY;
        assertThrows(IllegalStateException.class, () -> TimerController.start(context, state.timers.get(0).spec.id));
        assertEquals(TimerState.Phase.IDLE, AppStore.load(context).timers.get(0).phase);
        assertTrue(Shadows.shadowOf(alarms).getScheduledAlarms().isEmpty());
    }

    @Test public void buildResetWriteFailureCannotSelectAnOlderPresetLibrary() throws Exception {
        Snapshot latest = AppStore.load(context);
        PresetLibrary.create(latest.presets, "새로 만든 프리셋");
        AppStore.save(context, latest);
        JSONObject oldBuild = new JSONObject(new String(Files.readAllBytes(stateFile(PRIMARY).toPath()), StandardCharsets.UTF_8));
        oldBuild.put("buildId", "previous compiled build");
        Files.write(stateFile(PRIMARY).toPath(), oldBuild.toString().getBytes(StandardCharsets.UTF_8));
        byte[] primary = Files.readAllBytes(stateFile(PRIMARY).toPath());
        byte[] previous = Files.readAllBytes(stateFile(PREVIOUS).toPath());
        FailingAtomicFileShadow.failWriteName = PREVIOUS;
        assertThrows(IllegalStateException.class, () -> AppStore.load(context));
        assertArrayEquals(primary, Files.readAllBytes(stateFile(PRIMARY).toPath()));
        assertArrayEquals(previous, Files.readAllBytes(stateFile(PREVIOUS).toPath()));
        Snapshot reset = AppStore.load(context);
        assertEquals("새로 만든 프리셋", reset.presets.get(1).name);
    }

    @Test public void corruptPrimaryStillRecoversItsValidatedBackup() throws Exception {
        Snapshot before = AppStore.load(context);
        before.timers.get(0).spec.volume = 63;
        AppStore.save(context, before);
        Snapshot latest = before.copy();
        latest.timers.get(0).spec.volume = 91;
        AppStore.save(context, latest);
        Files.write(stateFile(PRIMARY).toPath(), "damaged JSON".getBytes(StandardCharsets.UTF_8));
        Snapshot recovered = AppStore.load(context);
        assertEquals(63, recovered.timers.get(0).spec.volume);
        assertFalse(recovered.recoveryNotice.isEmpty());
    }

    @Test public void silentlyFailedFinalRenameIsReportedInsteadOfSaved() throws Exception {
        Snapshot before = AppStore.load(context);
        byte[] originalBytes = Files.readAllBytes(stateFile(PRIMARY).toPath());
        before.timers.get(0).spec.volume = 77;
        FailingAtomicFileShadow.dropPrimaryRename = true;
        assertThrows(IllegalStateException.class, () -> AppStore.save(context, before));
        assertArrayEquals(originalBytes, Files.readAllBytes(stateFile(PRIMARY).toPath()));
    }

    @Test public void failedReadbackRestoresTheGenerationActuallyCommittedToDisk() {
        Snapshot original = runningPair();
        String id = original.timers.get(0).spec.id;
        FailingAtomicFileShadow.failReadAfterPrimaryCommit = true;
        assertThrows(IllegalStateException.class, () -> TimerController.start(context, id));
        Snapshot committed = AppStore.load(context);
        assertEquals(original.timers.get(0).generation + 1, committed.timers.get(0).generation);
        assertReservations(committed);
    }

    private File stateFile(String name) { return new File(context.getFilesDir(), name); }

    private Snapshot runningPair() {
        Snapshot state = AppStore.load(context);
        state.timers.get(0).spec.durationSeconds = 60;
        TimerState second = new TimerState();
        second.spec.durationSeconds = 90;
        state.timers.add(second);
        for (TimerState timer : state.timers)
            TimerEngine.start(timer, SystemClock.elapsedRealtime(), System.currentTimeMillis());
        AppStore.save(context, state);
        for (TimerState timer : state.timers) assertTrue(AlarmScheduler.schedule(context, timer));
        assertReservations(state);
        return state;
    }

    private void assertReservations(Snapshot expected) {
        Map<String, Intent> scheduled = new HashMap<>();
        Map<String, Long> triggers = new HashMap<>();
        for (ShadowAlarmManager.ScheduledAlarm alarm : Shadows.shadowOf(alarms).getScheduledAlarms()) {
            Intent intent = Shadows.shadowOf(alarm.operation).getSavedIntent();
            scheduled.put(intent.getStringExtra(TimerReceiver.EXTRA_TIMER_ID), intent);
            triggers.put(intent.getStringExtra(TimerReceiver.EXTRA_TIMER_ID), alarm.getTriggerAtMs());
        }
        int count = 0;
        for (TimerState timer : expected.timers) {
            if (timer.phase != TimerState.Phase.RUNNING) continue;
            count++;
            Intent intent = scheduled.get(timer.spec.id);
            assertNotNull("Saved running timer keeps its system alarm", intent);
            assertEquals(timer.generation, intent.getLongExtra(TimerReceiver.EXTRA_GENERATION, -1));
            long expectedWall = System.currentTimeMillis() + Math.max(1L,
                    timer.deadlineElapsedMs - SystemClock.elapsedRealtime());
            assertTrue("The original remaining interval is restored without restarting the countdown",
                    Math.abs(triggers.get(timer.spec.id) - expectedWall) < 1_000L);
        }
        assertEquals("No uncommitted generation or deleted timer remains reserved", count, scheduled.size());
    }

    @Implements(AtomicFile.class)
    public static final class FailingAtomicFileShadow {
        @RealObject private AtomicFile atomicFile;
        static String failWriteName;
        static boolean dropPrimaryRename, failReadAfterPrimaryCommit, failNextPrimaryRead;
        static void reset() {
            failWriteName = null;
            dropPrimaryRename = failReadAfterPrimaryCommit = failNextPrimaryRead = false;
        }
        @Implementation protected FileOutputStream startWrite() throws IOException {
            if (atomicFile.getBaseFile().getName().equals(failWriteName)) {
                failWriteName = null;
                throw new IOException("Simulated unavailable storage");
            }
            return Shadow.directlyOn(atomicFile, AtomicFile.class, "startWrite");
        }
        @Implementation protected byte[] readFully() throws IOException {
            if (PRIMARY.equals(atomicFile.getBaseFile().getName()) && failNextPrimaryRead) {
                failNextPrimaryRead = false;
                throw new IOException("Simulated transient post-commit read failure");
            }
            return Shadow.directlyOn(atomicFile, AtomicFile.class, "readFully");
        }
        @Implementation protected void finishWrite(FileOutputStream stream) {
            if (stream == null) return;
            try {
                stream.getFD().sync();
                stream.close();
                File base = atomicFile.getBaseFile();
                boolean primary = PRIMARY.equals(base.getName());
                if (primary && dropPrimaryRename) { dropPrimaryRename = false; return; }
                File replacement = new File(base.getPath() + ".new");
                if (replacement.isFile()) Files.move(replacement.toPath(), base.toPath(), StandardCopyOption.REPLACE_EXISTING);
                Files.deleteIfExists(new File(base.getPath() + ".bak").toPath());
                if (primary && failReadAfterPrimaryCommit) {
                    failReadAfterPrimaryCommit = false;
                    failNextPrimaryRead = true;
                }
            } catch (IOException failure) { throw new IllegalStateException(failure); }
        }
    }
}
