using System.Text;
using NAudio.Wave;

namespace IntervalTimer;

internal sealed class ActionChimePlayer : IDisposable
{
    private WaveOutEvent? output;
    private WaveFileReader? source;
    private bool disposed;

    public void PlayStart() => Play(Chimes.Start);
    public void PlayStop() => Play(Chimes.Stop);

    private void Play(byte[] wave)
    {
        if (disposed) return;
        StopPlayback();
        try
        {
            // A separate, finite audio stream leaves each timer's repeating alarm intact.
            source = new WaveFileReader(new MemoryStream(wave, writable: false));
            output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 2 };
            output.Init(source);
            output.Play();
        }
        catch
        {
            // A missing audio device must not interrupt the timer action.
            StopPlayback();
        }
    }

    private void StopPlayback()
    {
        if (output is not null)
        {
            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { }
            output = null;
        }
        if (source is not null)
        {
            try { source.Dispose(); } catch { }
            source = null;
        }
    }

    public void Dispose()
    {
        disposed = true;
        StopPlayback();
    }

    private static class Chimes
    {
        // Original short bell pairs: rising for start, falling for stop.
        public static readonly byte[] Start = Build(659.255, 987.767);
        public static readonly byte[] Stop = Build(783.991, 523.251);

        private static byte[] Build(double firstFrequency, double secondFrequency)
        {
            const int sampleRate = 44_100;
            const double secondStartsAt = .105;
            const double firstDuration = .26;
            const double secondDuration = .34;
            var sampleCount = (int)Math.Ceiling((secondStartsAt + secondDuration + .025) * sampleRate);
            using var stream = new MemoryStream(44 + sampleCount * 2);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + sampleCount * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleCount * 2);
            for (var i = 0; i < sampleCount; i++)
            {
                var time = i / (double)sampleRate;
                var signal = .78 * Bell(time, firstFrequency, firstDuration)
                    + Bell(time - secondStartsAt, secondFrequency, secondDuration);
                writer.Write((short)(short.MaxValue * .38 * Math.Clamp(signal, -1, 1)));
            }
            writer.Flush();
            return stream.ToArray();
        }

        private static double Bell(double time, double frequency, double duration)
        {
            if (time < 0 || time >= duration) return 0;
            var envelope = Math.Min(1, time / .006) * Math.Min(1, (duration - time) / .035)
                * Math.Exp(-6.5 * time);
            var phase = 2 * Math.PI * frequency * time;
            return envelope * (.82 * Math.Sin(phase) + .13 * Math.Sin(phase * 2.76)
                + .05 * Math.Sin(phase * 4.08));
        }
    }
}
