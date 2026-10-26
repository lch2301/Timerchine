package io.github.lch2301.timerchine;

import android.content.Context;
import android.net.Uri;
import io.github.lch2301.timerchine.core.OriginalSounds;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;

/** App-private files keep MediaPlayer on the original desktop sound data. */
final class BuiltinSounds {
    private BuiltinSounds() { }
    static Uri melody(Context context) throws IOException {
        return write(context, "default-melody-v1.wav", OriginalSounds.defaultMelody());
    }
    static Uri action(Context context, boolean start) throws IOException {
        return write(context, start ? "start-chime-v1.wav" : "stop-chime-v1.wav",
                start ? OriginalSounds.startChime() : OriginalSounds.stopChime());
    }
    private static synchronized Uri write(Context context, String name, byte[] data) throws IOException {
        File folder = new File(context.getCacheDir(), "builtin-sounds");
        if (!folder.isDirectory() && !folder.mkdirs()) throw new IOException("Cannot create sound cache");
        File file = new File(folder, name);
        if (!file.isFile() || file.length() != data.length) {
            try (FileOutputStream stream = new FileOutputStream(file)) { stream.write(data); }
        }
        return Uri.fromFile(file);
    }
}
