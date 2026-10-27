using System.Runtime.InteropServices;

namespace Screenrecorder;

/// <summary>Volume and level math on 32-bit float PCM (the WASAPI shared-mode format).</summary>
public static class AudioLevel
{
    public const double FloorDb = -60;

    public static float DbToGain(double db) => (float)Math.Pow(10, db / 20);

    public static double PeakToDb(float peak) => peak <= 0 ? FloorDb : Math.Max(FloorDb, 20 * Math.Log10(peak));

    /// <summary>Multiplies the samples in place, clamps to ±1 (no wrap-around distortion) and returns the peak.</summary>
    public static float ApplyGain(Span<byte> f32le, float gain)
    {
        var samples = MemoryMarshal.Cast<byte, float>(f32le);
        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var s = Math.Clamp(samples[i] * gain, -1f, 1f);
            samples[i] = s;
            peak = Math.Max(peak, Math.Abs(s));
        }
        return peak;
    }

    /// <summary>The peak the samples would have after the gain, without changing them (meter while idle).</summary>
    public static float Peak(ReadOnlySpan<byte> f32le, float gain)
    {
        var peak = 0f;
        foreach (var s in MemoryMarshal.Cast<byte, float>(f32le))
            peak = Math.Max(peak, Math.Abs(s));
        return Math.Min(1f, peak * gain);
    }
}

/// <summary>One audio source's live gain and latest peak; shared by the mixer UI and every recording segment.</summary>
public sealed class AudioChannel
{
    float peak;

    public AudioChannel(double gainDb) => Gain = AudioLevel.DbToGain(gainDb);

    /// <summary>Linear gain; written by the UI thread, read by the capture thread.</summary>
    public volatile float Gain;

    public void Report(float value)
    {
        // shortcut: not a lock-free max; a lost update only drops one meter sample, which is invisible.
        if (value > Volatile.Read(ref peak)) Volatile.Write(ref peak, value);
    }

    /// <summary>Highest peak since the last call; resets to 0.</summary>
    public float TakePeak() => Interlocked.Exchange(ref peak, 0f);
}
