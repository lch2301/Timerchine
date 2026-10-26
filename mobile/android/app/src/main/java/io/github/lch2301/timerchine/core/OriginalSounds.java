package io.github.lch2301.timerchine.core;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** Timerchine 1.2.0's original PCM bell sounds, using the desktop formulas. */
public final class OriginalSounds {
    private static final int RATE = 44_100;
    private static final byte[] MELODY = buildMelody();
    private static final byte[] START = buildChime(659.255, 987.767);
    private static final byte[] STOP = buildChime(783.991, 523.251);
    private OriginalSounds() { }

    public static byte[] defaultMelody() { return MELODY.clone(); }
    public static byte[] startChime() { return START.clone(); }
    public static byte[] stopChime() { return STOP.clone(); }
    public static int melodyDurationMs() { return (int) ((MELODY.length - 44) * 1000L / (RATE * 2)); }

    private static byte[] buildMelody() {
        double[] frequencies = {523.251, 659.255, 783.991, 1046.502, 783.991, 659.255, 587.330, 523.251};
        double[] durations = {.24, .24, .28, .42, .24, .24, .28, .50};
        int samples = (int) (.55 * RATE);
        for (double duration : durations) samples += (int) (duration * RATE) + (int) (.055 * RATE);
        ByteBuffer wave = header(samples);
        for (int note = 0; note < frequencies.length; note++) {
            double duration = durations[note];
            for (int i = 0; i < (int) (duration * RATE); i++) {
                double time = i / (double) RATE;
                double attack = Math.min(1, time / .012);
                double release = Math.min(1, (duration - time) / .045);
                double envelope = attack * release * Math.exp(-2.6 * time / duration);
                double phase = 2 * Math.PI * frequencies[note] * time;
                double tone = .76 * Math.sin(phase) + .18 * Math.sin(2 * phase) + .06 * Math.sin(3 * phase);
                wave.putShort((short) (Short.MAX_VALUE * .43 * envelope * tone));
            }
            for (int i = 0; i < (int) (.055 * RATE); i++) wave.putShort((short) 0);
        }
        for (int i = 0; i < (int) (.55 * RATE); i++) wave.putShort((short) 0);
        return wave.array();
    }

    private static byte[] buildChime(double first, double second) {
        int samples = (int) Math.ceil((.105 + .34 + .025) * RATE);
        ByteBuffer wave = header(samples);
        for (int i = 0; i < samples; i++) {
            double time = i / (double) RATE;
            double signal = .78 * bell(time, first, .26) + bell(time - .105, second, .34);
            wave.putShort((short) (Short.MAX_VALUE * .38 * Math.max(-1, Math.min(1, signal))));
        }
        return wave.array();
    }

    private static double bell(double time, double frequency, double duration) {
        if (time < 0 || time >= duration) return 0;
        double envelope = Math.min(1, time / .006) * Math.min(1, (duration - time) / .035) * Math.exp(-6.5 * time);
        double phase = 2 * Math.PI * frequency * time;
        return envelope * (.82 * Math.sin(phase) + .13 * Math.sin(phase * 2.76) + .05 * Math.sin(phase * 4.08));
    }

    private static ByteBuffer header(int samples) {
        int dataBytes = samples * 2;
        ByteBuffer wave = ByteBuffer.allocate(44 + dataBytes).order(ByteOrder.LITTLE_ENDIAN);
        wave.put(new byte[] {'R','I','F','F'}).putInt(36 + dataBytes)
                .put(new byte[] {'W','A','V','E','f','m','t',' '}).putInt(16)
                .putShort((short) 1).putShort((short) 1).putInt(RATE).putInt(RATE * 2)
                .putShort((short) 2).putShort((short) 16).put(new byte[] {'d','a','t','a'}).putInt(dataBytes);
        return wave;
    }
}
