package io.github.lch2301.timerchine.core;

import org.junit.Test;
import static org.junit.Assert.*;

public class TimerEngineTest {
    @Test public void delayedTicksUseElapsedDeadlineAndCeilSeconds() {
        TimerState timer = timer(10, false);
        TimerEngine.start(timer, 5_000, 100_000);
        assertEquals(15_000, timer.deadlineElapsedMs);
        assertEquals(110_000, timer.deadlineWallMs);
        assertFalse(TimerEngine.tick(timer, 8_001));
        assertEquals(7, TimerEngine.remainingSeconds(timer, 8_001));
        assertEquals(1, TimerEngine.remainingSeconds(timer, 14_999));
        assertTrue(TimerEngine.tick(timer, 26_000));
        assertEquals(TimerState.Phase.ALARMING, timer.phase);
        assertTrue(TimerEngine.isAudioActive(timer));
        assertTrue(TimerEngine.isActive(timer));
        assertFalse(TimerEngine.tick(timer, 99_000));
        assertEquals(0, TimerEngine.remainingSeconds(timer, 99_000));
    }

    @Test public void reverseCompletionStopsSoundAndLeavesCompletedState() {
        TimerState timer = timer(10, true);
        TimerEngine.start(timer, 1_000, 1_000);
        assertTrue(TimerEngine.isAudioActive(timer));
        assertTrue(TimerEngine.tick(timer, 11_000));
        assertEquals(TimerState.Phase.COMPLETED, timer.phase);
        assertFalse(TimerEngine.isAudioActive(timer));
        assertFalse(TimerEngine.isActive(timer));
    }

    @Test public void stoppingPreservesTheExactFractionForHourglassStillness() {
        TimerState timer = timer(10, true);
        TimerEngine.start(timer, 1_000, 1_000);
        TimerEngine.stop(timer, 1_350);
        assertEquals(10, timer.remainingSeconds);
        assertEquals(9_650, timer.remainingExactMs);
        assertEquals(9_650, TimerEngine.remainingMilliseconds(timer, 500_000));
        assertEquals(10, TimerEngine.remainingSeconds(timer, 500_000));
        TimerState copy = timer.copy();
        assertEquals(9_650, copy.remainingExactMs);
        TimerEngine.start(timer, 2_000, 2_000);
        assertEquals(10_000, timer.remainingExactMs);
    }

    @Test public void stopPreservesAudioCursorButRestartUsesFullConfiguredDuration() {
        TimerState timer = timer(120, true);
        timer.playbackPositionMs = 41_000;
        timer.playlistIndex = 2;
        timer.playbackOrder.add(2);
        timer.playbackSourceKey = "saved-source";
        TimerEngine.start(timer, 10_000, 100_000);
        assertEquals(1, timer.generation);
        TimerEngine.stop(timer, 70_500);
        assertEquals(60, timer.remainingSeconds);
        assertEquals(2, timer.generation);
        assertEquals(TimerState.Phase.STOPPED, timer.phase);
        assertEquals(60, TimerEngine.remainingSeconds(timer, 1_000_000));
        TimerEngine.start(timer, 80_000, 170_000);
        assertEquals(120, timer.remainingSeconds);
        assertEquals(200_000, timer.deadlineElapsedMs);
        assertEquals(3, timer.generation);
        assertEquals(41_000, timer.playbackPositionMs);
        assertEquals(2, timer.playlistIndex);
        assertEquals("saved-source", timer.playbackSourceKey);
        assertEquals(Integer.valueOf(2), timer.playbackOrder.get(0));
    }

    @Test public void zeroStartsHaveImmediateModeSpecificBehavior() {
        TimerState normal = timer(0, false);
        TimerState reverse = timer(0, true);
        TimerEngine.start(normal, 1, 1);
        TimerEngine.start(reverse, 1, 1);
        assertEquals(TimerState.Phase.ALARMING, normal.phase);
        assertTrue(TimerEngine.isAudioActive(normal));
        assertEquals(TimerState.Phase.COMPLETED, reverse.phase);
        assertFalse(TimerEngine.isAudioActive(reverse));
    }

    @Test public void failedAudioDoesNotStopTheClockAndRestartRetriesIt() {
        TimerState reverse = timer(10, true);
        TimerEngine.start(reverse, 1_000, 1_000);
        reverse.playbackFailed = true;
        reverse.playbackError = "오디오 장치 확인";
        assertTrue(TimerEngine.isActive(reverse));
        assertFalse(TimerEngine.isAudioActive(reverse));
        assertEquals(5, TimerEngine.remainingSeconds(reverse, 6_000));
        assertTrue(TimerEngine.tick(reverse, 11_000));
        assertEquals(TimerState.Phase.COMPLETED, reverse.phase);
        TimerEngine.start(reverse, 20_000, 20_000);
        assertFalse(reverse.playbackFailed);
        assertEquals("", reverse.playbackError);
        assertTrue(TimerEngine.isAudioActive(reverse));
        TimerState alarm = timer(0, false);
        TimerEngine.start(alarm, 0, 0);
        alarm.playbackFailed = true;
        assertTrue(TimerEngine.isActive(alarm));
        assertEquals(TimerState.Phase.ALARMING, alarm.phase);
        assertFalse(TimerEngine.isAudioActive(alarm));
    }

    @Test public void durationBoundsMatchDesktopFields() {
        TimerState timer = timer(Integer.MAX_VALUE, false);
        TimerEngine.start(timer, 0, 0);
        assertEquals(359_999, timer.spec.durationSeconds);
        assertEquals(359_999_000, timer.deadlineElapsedMs);
        timer.spec.durationSeconds = -5;
        TimerEngine.start(timer, 0, 0);
        assertEquals(0, timer.spec.durationSeconds);
        assertEquals(TimerState.Phase.ALARMING, timer.phase);
    }

    @Test public void sourceIdentityOnlyChangesWhenPlaybackNeedsReset() {
        TimerSpec spec = new TimerSpec();
        spec.usePlaylist = true;
        spec.playlist.add(new Track("content://one|two", "원래 제목", false));
        String original = spec.sourceKey();
        spec.name = "이름 변경";
        spec.durationSeconds = 100;
        spec.volume = 7;
        spec.presetId = "another preset";
        spec.playlist.get(0).title = "바뀐 제목";
        assertEquals(original, spec.sourceKey());
        spec.reverse = true;
        assertNotEquals(original, spec.sourceKey());
        spec.reverse = false;
        spec.shuffle = true;
        assertNotEquals(original, spec.sourceKey());
        spec.shuffle = false;
        spec.playlist.get(0).remote = true;
        assertNotEquals(original, spec.sourceKey());
        spec.playlist.get(0).remote = false;
        spec.playlist.get(0).uri = "content://different";
        assertNotEquals(original, spec.sourceKey());
    }

    @Test public void allDesktopSourceSettingsResetPlaybackIncludingDormantFields() {
        for (boolean usePlaylist : new boolean[] {false, true}) {
            TimerSpec source = new TimerSpec();
            source.usePlaylist = usePlaylist;
            source.soundUri = "content://single";
            source.playlist.add(new Track("content://playlist-one", "first", false));
            TimerSpec changed = source.copy();
            changed.soundUri = "content://other-single";
            assertFalse(source.hasSameAudioSource(changed));
            assertNotEquals(source.sourceKey(), changed.sourceKey());
            changed = source.copy();
            changed.shuffle = !source.shuffle;
            assertFalse(source.hasSameAudioSource(changed));
            changed = source.copy();
            changed.playlist.get(0).uri = "content://other-playlist";
            assertFalse(source.hasSameAudioSource(changed));
            changed = source.copy();
            changed.playlist.get(0).remote = true;
            assertFalse(source.hasSameAudioSource(changed));
            changed = source.copy();
            changed.reverse = !source.reverse;
            assertTrue(source.hasSameAudioSource(changed));
            assertNotEquals(source.sourceKey(), changed.sourceKey());
            changed = source.copy();
            changed.soundTitle = "changed title";
            changed.playlist.get(0).title = "renamed";
            changed.playlist.get(0).thumbnailPath = "/another/thumbnail.png";
            changed.presetId = "another-preset";
            changed.volume = 12;
            assertTrue(source.hasSameAudioSource(changed));
            assertEquals(source.sourceKey(), changed.sourceKey());
        }
    }

    private static TimerState timer(int duration, boolean reverse) {
        TimerState timer = new TimerState();
        timer.spec.durationSeconds = duration;
        timer.spec.reverse = reverse;
        return timer;
    }
}
