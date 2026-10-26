package io.github.lch2301.timerchine.core;

import org.junit.Test;
import java.security.MessageDigest;
import static org.junit.Assert.*;

public class OriginalSoundsTest {
    // Golden hashes independently generated from the original PC 1.2.0 C# code.
    @Test public void originalDesktopPcmIsPreservedByteForByte() throws Exception {
        assertEquals("7d84db6bcf45788eb979203d4a2bb067d538461923140010d9e2d89fd01594f3", sha(OriginalSounds.defaultMelody()));
        assertEquals("81a6f210e379ea6c2e6c310970b1d1ab0855c5624776d03fac2c0ca15ff0e184", sha(OriginalSounds.startChime()));
        assertEquals("821c7941d3c1a1255b9838b5af0697f53ffc354b047ee1bcf86da5fe1901372b", sha(OriginalSounds.stopChime()));
    }

    @Test public void callersCannotCorruptSharedSoundSamples() {
        byte[] altered = OriginalSounds.defaultMelody();
        altered[0] = 0;
        assertEquals('R', OriginalSounds.defaultMelody()[0]);
        assertEquals(3429, OriginalSounds.melodyDurationMs());
    }

    private static String sha(byte[] data) throws Exception {
        StringBuilder hex = new StringBuilder();
        for (byte value : MessageDigest.getInstance("SHA-256").digest(data))
            hex.append(String.format(java.util.Locale.ROOT, "%02x", value & 255));
        return hex.toString();
    }
}
