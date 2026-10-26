package io.github.lch2301.timerchine;

import android.util.AtomicFile;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import org.robolectric.annotation.Implementation;
import org.robolectric.annotation.Implements;
import org.robolectric.annotation.RealObject;

/** Android rename(2) replaces a destination; Windows File.renameTo does not. */
@Implements(AtomicFile.class)
public final class AtomicFileTestShadow {
    @RealObject private AtomicFile atomicFile;

    @Implementation protected void finishWrite(FileOutputStream stream) {
        if(stream==null)return;
        try {
            stream.getFD().sync();
            stream.close();
            File base=atomicFile.getBaseFile();
            File replacement=new File(base.getPath()+".new");
            if(replacement.isFile())Files.move(replacement.toPath(),base.toPath(),StandardCopyOption.REPLACE_EXISTING);
            // SDK 26 writes the base directly and retains a .bak until success.
            Files.deleteIfExists(new File(base.getPath()+".bak").toPath());
        } catch(IOException failure) {
            throw new IllegalStateException("Android AtomicFile host emulation failed",failure);
        }
    }
}
