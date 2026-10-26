package io.github.lch2301.timerchine;

import android.content.Context;
import android.util.AtomicFile;
import io.github.lch2301.timerchine.core.PlaylistPreset;
import io.github.lch2301.timerchine.core.PresetLibrary;
import io.github.lch2301.timerchine.core.TimerEngine;
import io.github.lch2301.timerchine.core.TimerSpec;
import io.github.lch2301.timerchine.core.TimerState;
import io.github.lch2301.timerchine.core.Track;
import java.io.File;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;
import java.util.zip.ZipFile;
import java.util.zip.ZipEntry;
import java.io.InputStream;
import java.security.MessageDigest;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

/** App-private state. App updates retain settings, timers, presets and playback state. */
public final class AppStore {
    private static final String FILE_NAME = "timerchine-state.json";
    private static final String PREVIOUS_FILE_NAME = "timerchine-state.previous.json";
    private static String currentBuildId;
    private AppStore() { }

    public static synchronized Snapshot load(Context context) {
        AtomicFile primary = file(context, FILE_NAME);
        AtomicFile previous = file(context, PREVIOUS_FILE_NAME);
        boolean damaged = false;
        Snapshot stored = null;
        try {
            stored = readStored(primary);
        } catch (FileNotFoundException missing) {
            // An interrupted first write can leave only the previous complete copy.
        } catch (IOException | JSONException | RuntimeException error) {
            damaged = true;
        }
        // Reading an older build must not rewrite the valid primary or cancel
        // its alarm reservations. Build identity is diagnostic metadata only.
        if (stored != null) return stored;
        try {
            stored = readStored(previous);
        } catch (FileNotFoundException missing) {
            // A first launch has neither file.
        } catch (IOException | JSONException | RuntimeException error) {
            damaged = true;
        }
        if (stored != null) {
            Snapshot recovered = stored;
            recovered.recoveryNotice = "저장된 설정을 백업에서 복원했습니다.";
            return recovered;
        }
        Snapshot defaults = new Snapshot();
        if (damaged) defaults.recoveryNotice = "저장된 설정을 읽지 못해 기본 설정으로 열었습니다.";
        // Persist the first IDs immediately. Recreating defaults on every read
        // would make controls refer to timer IDs that no longer exist.
        save(context, defaults);
        return defaults;
    }

    /** Throws if persistence fails so callers can avoid reporting unsaved changes as saved. */
    public static synchronized void save(Context context, Snapshot snapshot) {
        if (snapshot == null) throw new IllegalArgumentException("저장할 설정이 없습니다.");
        AtomicFile primary = file(context, FILE_NAME);
        AtomicFile previous = file(context, PREVIOUS_FILE_NAME);
        try {
            byte[] data = encode(decode(encode(snapshot, buildIdentity(context))), buildIdentity(context));
            byte[] old = null;
            try {
                byte[] candidate = primary.readFully();
                decode(candidate);
                old = candidate;
            } catch (IOException | JSONException | RuntimeException ignored) {
                // Do not overwrite a good recovery copy with a damaged primary file.
            }
            if (old != null) write(previous, old);
            write(primary, data);
        } catch (IOException | JSONException error) {
            throw new IllegalStateException("설정을 저장하지 못했습니다. 저장 공간을 확인하세요.", error);
        }
    }

    private static AtomicFile file(Context context, String name) {
        return new AtomicFile(new File(context.getFilesDir(), name));
    }

    private static void write(AtomicFile file, byte[] data) throws IOException {
        FileOutputStream output = null;
        try {
            output = file.startWrite();
            output.write(data);
            file.finishWrite(output);
            output = null;
            // AtomicFile logs a failed final rename instead of throwing. Do not
            // tell the caller that a configuration was saved if it stayed old.
            if (!java.util.Arrays.equals(data, file.readFully()))
                throw new IOException("Atomic configuration replacement did not persist.");
        } catch (IOException | RuntimeException error) {
            if (output != null) file.failWrite(output);
            throw error;
        }
    }

    private static byte[] encode(Snapshot snapshot, String buildId) throws JSONException {
        JSONObject root = new JSONObject();
        root.put("version", 1);
        root.put("buildId", buildId);
        root.put("useHourglass", snapshot.useHourglass);
        root.put("externalLinksConfirmed", snapshot.externalLinksConfirmed);
        root.put("minimizeToBackground", snapshot.minimizeToBackground == null ? JSONObject.NULL : snapshot.minimizeToBackground);
        root.put("closeBehaviorConfirmed", snapshot.closeBehaviorConfirmed);
        root.put("recoveryNotice", nonNull(snapshot.recoveryNotice));
        JSONArray timers = new JSONArray();
        if (snapshot.timers != null) for (TimerState timer : snapshot.timers) {
            if (timer == null) continue;
            JSONObject value = new JSONObject();
            TimerSpec spec = timer.spec == null ? new TimerSpec() : timer.spec;
            JSONObject configuration = new JSONObject();
            configuration.put("id", spec.id);
            configuration.put("name", spec.name);
            configuration.put("durationSeconds", spec.durationSeconds);
            configuration.put("volume", spec.volume);
            configuration.put("reverse", spec.reverse);
            configuration.put("usePlaylist", spec.usePlaylist);
            configuration.put("shuffle", spec.shuffle);
            configuration.put("presetId", spec.presetId == null ? PresetLibrary.DEFAULT_ID : spec.presetId);
            configuration.put("soundUri", nonNull(spec.soundUri));
            configuration.put("soundTitle", nonNull(spec.soundTitle));
            configuration.put("playlist", tracks(spec.playlist));
            value.put("spec", configuration);
            value.put("phase", (timer.phase == null ? TimerState.Phase.IDLE : timer.phase).name());
            value.put("deadlineElapsedMs", timer.deadlineElapsedMs);
            value.put("deadlineWallMs", timer.deadlineWallMs);
            value.put("generation", timer.generation);
            value.put("remainingSeconds", timer.remainingSeconds);
            value.put("remainingExactMs", timer.remainingExactMs);
            value.put("playbackPositionMs", timer.playbackPositionMs);
            value.put("playlistIndex", timer.playlistIndex);
            JSONArray order = new JSONArray();
            if (timer.playbackOrder != null) for (Integer index : timer.playbackOrder) if (index != null) order.put(index);
            value.put("playbackOrder", order);
            value.put("playbackSourceKey", nonNull(timer.playbackSourceKey));
            value.put("playbackError", nonNull(timer.playbackError));
            value.put("playbackFailed", timer.playbackFailed);
            timers.put(value);
        }
        root.put("timers", timers);
        JSONArray presets = new JSONArray();
        if (snapshot.presets != null) for (PlaylistPreset preset : snapshot.presets) {
            if (preset == null) continue;
            JSONObject value = new JSONObject();
            value.put("id", preset.id);
            value.put("name", preset.name);
            value.put("shuffle", preset.shuffle);
            value.put("items", tracks(preset.items));
            presets.put(value);
        }
        root.put("presets", presets);
        return root.toString(2).getBytes(StandardCharsets.UTF_8);
    }

    private static Snapshot decode(byte[] bytes) throws JSONException {
        JSONObject root = new JSONObject(new String(bytes, StandardCharsets.UTF_8));
        if (root.optInt("version", 0) != 1) throw new JSONException("지원하지 않는 설정 형식입니다.");
        JSONArray timers = root.optJSONArray("timers");
        JSONArray presets = root.optJSONArray("presets");
        if (timers == null || presets == null) throw new JSONException("설정 목록이 없습니다.");
        Snapshot snapshot = new Snapshot();
        snapshot.timers.clear();
        snapshot.presets.clear();
        snapshot.useHourglass = root.optBoolean("useHourglass", true);
        snapshot.externalLinksConfirmed = root.optBoolean("externalLinksConfirmed", false);
        snapshot.minimizeToBackground = root.isNull("minimizeToBackground")
                || !root.has("minimizeToBackground") ? null : root.optBoolean("minimizeToBackground", false);
        snapshot.closeBehaviorConfirmed = root.optBoolean("closeBehaviorConfirmed", false);
        snapshot.recoveryNotice = root.optString("recoveryNotice", "");
        Set<String> timerIds = new HashSet<>();
        for (int i = 0; i < timers.length(); i++) {
            JSONObject value = timers.getJSONObject(i);
            JSONObject configuration = value.getJSONObject("spec");
            TimerState timer = new TimerState();
            TimerSpec spec = timer.spec;
            spec.id = uniqueId(configuration.optString("id", ""), timerIds);
            spec.name = boundedText(configuration.optString("name", ""), 80).trim();
            if (spec.name.isEmpty()) spec.name = "타이머 " + (i + 1);
            spec.durationSeconds = boundedInt(configuration, "durationSeconds", 0, TimerEngine.MAXIMUM_SECONDS, 0);
            spec.volume = boundedInt(configuration, "volume", 0, 100, 50);
            spec.reverse = configuration.optBoolean("reverse", false);
            spec.usePlaylist = configuration.optBoolean("usePlaylist", false);
            spec.shuffle = configuration.optBoolean("shuffle", false);
            spec.presetId = configuration.optString("presetId", PresetLibrary.DEFAULT_ID);
            if (!spec.presetId.isEmpty() && !validId(spec.presetId)) spec.presetId = PresetLibrary.DEFAULT_ID;
            spec.soundUri = configuration.optString("soundUri", "");
            spec.soundTitle = boundedText(configuration.optString("soundTitle", "기본 알람음"), 500);
            spec.playlist = readTracks(configuration.optJSONArray("playlist"));
            try { timer.phase = TimerState.Phase.valueOf(value.optString("phase", "IDLE")); }
            catch (IllegalArgumentException invalid) { timer.phase = TimerState.Phase.IDLE; }
            timer.deadlineElapsedMs = Math.max(0, value.optLong("deadlineElapsedMs", 0));
            timer.deadlineWallMs = Math.max(0, value.optLong("deadlineWallMs", 0));
            timer.generation = Math.max(0, value.optLong("generation", 0));
            timer.remainingSeconds = boundedInt(value, "remainingSeconds", 0, TimerEngine.MAXIMUM_SECONDS, spec.durationSeconds);
            timer.remainingExactMs = Math.max(0, Math.min(TimerEngine.MAXIMUM_SECONDS * 1000L,
                    value.optLong("remainingExactMs", timer.remainingSeconds * 1000L)));
            timer.playbackPositionMs = boundedInt(value, "playbackPositionMs", 0, Integer.MAX_VALUE, 0);
            timer.playlistIndex = boundedInt(value, "playlistIndex", 0, Math.max(0, spec.playlist.size() - 1), 0);
            timer.playbackSourceKey = value.optString("playbackSourceKey", "");
            timer.playbackError = value.optString("playbackError", "");
            timer.playbackFailed = value.optBoolean("playbackFailed", false);
            JSONArray order = value.optJSONArray("playbackOrder");
            Set<Integer> indices = new HashSet<>();
            if (order != null) for (int j = 0; j < order.length(); j++) {
                int index = order.optInt(j, -1);
                if (index >= 0 && index < spec.playlist.size() && indices.add(index)) timer.playbackOrder.add(index);
            }
            if (timer.phase == TimerState.Phase.RUNNING && timer.deadlineElapsedMs == 0 && timer.deadlineWallMs == 0)
                timer.phase = TimerState.Phase.STOPPED;
            if (timer.phase == TimerState.Phase.ALARMING || timer.phase == TimerState.Phase.COMPLETED) {
                timer.remainingSeconds = 0;
                timer.remainingExactMs = 0;
            }
            snapshot.timers.add(timer);
        }
        Set<String> presetIds = new HashSet<>();
        Set<String> names = new HashSet<>();
        for (int i = 0; i < presets.length(); i++) {
            JSONObject value = presets.getJSONObject(i);
            PlaylistPreset preset = new PlaylistPreset();
            preset.id = value.optString("id", "");
            preset.name = boundedText(value.optString("name", ""), 80).trim();
            if (!validId(preset.id) || !presetIds.add(preset.id) || preset.name.isEmpty()
                    || !names.add(preset.name.toLowerCase(Locale.ROOT)))
                throw new JSONException("프리셋 정보가 손상되었습니다.");
            preset.shuffle = value.optBoolean("shuffle", false);
            preset.items = readTracks(value.optJSONArray("items"));
            snapshot.presets.add(preset);
        }
        PresetLibrary.ensureDefault(snapshot.presets);
        return snapshot;
    }

    private static JSONArray tracks(List<Track> tracks) throws JSONException {
        JSONArray array = new JSONArray();
        if (tracks != null) for (Track track : tracks) {
            if (track == null) continue;
            JSONObject value = new JSONObject();
            value.put("uri", nonNull(track.uri));
            value.put("title", nonNull(track.title));
            value.put("remote", track.remote);
            value.put("thumbnailPath", nonNull(track.thumbnailPath));
            array.put(value);
        }
        return array;
    }

    private static List<Track> readTracks(JSONArray array) throws JSONException {
        List<Track> items = new ArrayList<>();
        if (array != null) for (int i = 0; i < array.length(); i++) {
            JSONObject value = array.getJSONObject(i);
            Track track = new Track(value.optString("uri", ""), boundedText(value.optString("title", ""), 500), value.optBoolean("remote", false));
            track.thumbnailPath = value.optString("thumbnailPath", "");
            items.add(track);
        }
        return items;
    }

    private static String uniqueId(String candidate, Set<String> ids) {
        String id = validId(candidate) ? candidate : UUID.randomUUID().toString();
        while (!ids.add(id)) id = UUID.randomUUID().toString();
        return id;
    }

    private static boolean validId(String value) {
        try { return value != null && UUID.fromString(value).toString().equalsIgnoreCase(value); }
        catch (IllegalArgumentException invalid) { return false; }
    }

    private static int boundedInt(JSONObject object, String key, int minimum, int maximum, int fallback) {
        return (int) Math.max(minimum, Math.min(maximum, object.optLong(key, fallback)));
    }

    private static String boundedText(String text, int maximum) {
        String value = nonNull(text);
        return value.length() <= maximum ? value : value.substring(0, maximum);
    }

    private static String nonNull(String text) { return text == null ? "" : text; }

    private static Snapshot readStored(AtomicFile file) throws IOException, JSONException {
        return decode(file.readFully());
    }

    private static String buildIdentity(Context context) {
        if (currentBuildId != null) return currentBuildId;
        try (ZipFile apk = new ZipFile(context.getApplicationInfo().sourceDir)) {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] buffer = new byte[64 * 1024];
            List<String> compiledEntries = new ArrayList<>();
            java.util.Enumeration<? extends ZipEntry> entries = apk.entries();
            while (entries.hasMoreElements()) {
                String name = entries.nextElement().getName();
                if (name.matches("classes[0-9]*\\.dex")) compiledEntries.add(name);
            }
            if (!compiledEntries.contains("classes.dex")) throw new IOException("Missing compiled APK entry: classes.dex");
            java.util.Collections.sort(compiledEntries);
            compiledEntries.add("resources.arsc");
            for (String entryName : compiledEntries) {
                ZipEntry entry = apk.getEntry(entryName);
                if (entry == null) throw new IOException("Missing compiled APK entry: " + entryName);
                digest.update(entryName.getBytes(StandardCharsets.UTF_8));
                try (InputStream stream = apk.getInputStream(entry)) {
                    int read;
                    while ((read = stream.read(buffer)) != -1) digest.update(buffer, 0, read);
                }
            }
            StringBuilder identity = new StringBuilder();
            for (byte value : digest.digest()) identity.append(String.format(Locale.ROOT, "%02x", value & 0xff));
            currentBuildId = identity.toString();
            return currentBuildId;
        } catch (Exception unavailableBuild) {
            throw new IllegalStateException("앱 빌드 정보를 읽지 못했습니다.", unavailableBuild);
        }
    }
}
