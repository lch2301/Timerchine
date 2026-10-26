package io.github.lch2301.timerchine.core;

import io.github.lch2301.timerchine.Snapshot;
import java.util.ArrayList;
import java.util.List;
import org.junit.Test;
import static org.junit.Assert.*;

public class PresetLibraryTest {
    @Test public void firstLaunchHasOneSelectedDefaultPreset() {
        Snapshot snapshot = new Snapshot();
        assertEquals(1, snapshot.presets.size());
        assertEquals(PresetLibrary.DEFAULT_NAME, snapshot.presets.get(0).name);
        assertEquals(PresetLibrary.DEFAULT_ID, snapshot.timers.get(0).spec.presetId);
        assertEquals("타이머 1", snapshot.timers.get(0).spec.name);
        assertEquals(0, snapshot.timers.get(0).spec.durationSeconds);
        assertEquals(50, snapshot.timers.get(0).spec.volume);
        PresetLibrary.ensureDefault(snapshot.presets);
        assertEquals(1, snapshot.presets.size());
    }

    @Test public void creationDoesNotCapturePlaylistAndSavingCopiesDraft() {
        List<PlaylistPreset> presets = new ArrayList<>();
        PlaylistPreset created = PresetLibrary.create(presets, "  기다리는 동안  ");
        assertEquals("기다리는 동안", created.name);
        assertTrue(created.items.isEmpty());
        List<Track> draft = new ArrayList<>();
        draft.add(new Track("content://music/1", "노래", false));
        PresetLibrary.savePlaylist(presets, created.id, draft, true);
        draft.get(0).title = "저장 후 편집";
        draft.clear();
        assertEquals(1, created.items.size());
        assertEquals("노래", created.items.get(0).title);
        assertTrue(created.shuffle);
        PlaylistPreset copied = created.copy();
        copied.items.get(0).uri = "other";
        assertEquals("content://music/1", created.items.get(0).uri);
    }

    @Test public void namesRejectBlanksLongNamesAndCaseInsensitiveDuplicates() {
        List<PlaylistPreset> presets = new ArrayList<>();
        expectInvalid(() -> PresetLibrary.create(presets, "  "));
        expectInvalid(() -> PresetLibrary.create(presets, "a".repeat(81)));
        PresetLibrary.create(presets, "Evening");
        expectInvalid(() -> PresetLibrary.create(presets, " evening "));
        expectInvalid(() -> PresetLibrary.create(presets, PresetLibrary.DEFAULT_NAME));
        expectInvalid(() -> PresetLibrary.savePlaylist(presets, "missing", new ArrayList<>(), false));
    }

    @Test public void replacingOnePresetDoesNotChangeOtherPresets() {
        List<PlaylistPreset> presets = new ArrayList<>();
        PlaylistPreset one = PresetLibrary.create(presets, "하나");
        PlaylistPreset two = PresetLibrary.create(presets, "둘");
        List<Track> draft = new ArrayList<>();
        draft.add(new Track("content://music", "음악", false));
        PresetLibrary.savePlaylist(presets, one.id, draft, false);
        assertTrue(two.items.isEmpty());
        assertTrue(PresetLibrary.find(presets, PresetLibrary.DEFAULT_ID).items.isEmpty());
    }

    @Test public void snapshotCopyKeepsDraftChangesIndependentOfStoredState() {
        Snapshot original = new Snapshot();
        original.timers.get(0).spec.playlist.add(new Track("content://one", "첫 곡", false));
        original.timers.get(0).playbackOrder.add(0);
        original.presets.get(0).items.add(new Track("content://preset", "프리셋 곡", false));
        Snapshot copy = original.copy();
        copy.timers.get(0).spec.playlist.get(0).uri = "changed";
        copy.timers.get(0).playbackOrder.clear();
        copy.presets.get(0).items.get(0).title = "편집";
        assertEquals("content://one", original.timers.get(0).spec.playlist.get(0).uri);
        assertEquals(1, original.timers.get(0).playbackOrder.size());
        assertEquals("프리셋 곡", original.presets.get(0).items.get(0).title);
    }

    private static void expectInvalid(Runnable action) {
        try { action.run(); fail("Validation should reject the input"); }
        catch (IllegalArgumentException expected) { }
    }
}
