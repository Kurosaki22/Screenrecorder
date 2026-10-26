using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using NAudio.Wave;

namespace Screenrecorder;

/// <summary>Streams a WASAPI capture's raw PCM into a named pipe that ffmpeg reads as an input.</summary>
public sealed class AudioPipeWriter : IDisposable
{
    static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    readonly IWaveIn capture;
    readonly NamedPipeServerStream pipe;
    readonly Lock gate = new();
    readonly Stopwatch clock = new();
    readonly int blockAlign;
    long writtenFrames;
    TimeSpan lastData;
    bool broken;
    Timer? padTimer;

    public AudioInput Input { get; }

    public AudioPipeWriter(IWaveIn capture)
    {
        this.capture = capture;
        var name = $"screenrecorder_{Guid.NewGuid():N}";
        pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 1 << 20);
        var wf = capture.WaveFormat;
        blockAlign = wf.BlockAlign;
        Input = new AudioInput($@"\\.\pipe\{name}", SampleFormat(wf), wf.SampleRate, wf.Channels);
        capture.DataAvailable += (_, e) =>
        {
            lock (gate)
            {
                lastData = clock.Elapsed;
                Write(e.Buffer, e.BytesRecorded);
            }
        };
    }

    /// <summary>Waits until ffmpeg opens the pipe, then starts capturing.</summary>
    public async Task StartWhenConnectedAsync(CancellationToken ct)
    {
        await pipe.WaitForConnectionAsync(ct);
        clock.Start();
        capture.StartRecording();
        padTimer = new Timer(_ => Pad(), null, 10, 10);
    }

    void Pad()
    {
        lock (gate)
        {
            var frames = FramesToPad(writtenFrames, clock.Elapsed, clock.Elapsed - lastData, capture.WaveFormat.SampleRate);
            if (frames > 0) Write(new byte[frames * blockAlign], (int)frames * blockAlign);
        }
    }

    void Write(byte[] buffer, int count)
    {
        if (broken) return;
        try
        {
            pipe.Write(buffer, 0, count);
            writtenFrames += count / blockAlign;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            broken = true; // ffmpeg closed the pipe (stopping or crashed)
        }
    }

    /// <summary>
    /// WASAPI loopback delivers no buffers while nothing plays. Without filling that gap with silence
    /// the audio track gets shorter than the video and drifts out of sync.
    /// </summary>
    public static long FramesToPad(long writtenFrames, TimeSpan elapsed, TimeSpan sinceLastData, int sampleRate)
    {
        if (sinceLastData < TimeSpan.FromMilliseconds(150)) return 0;
        // Stay ~50 ms behind the clock: that is roughly the capture latency of the first buffer after the stall.
        var target = (long)((elapsed.TotalSeconds - 0.05) * sampleRate);
        return Math.Max(0, target - writtenFrames);
    }

    public static string SampleFormat(WaveFormat wf)
    {
        bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat || wf is WaveFormatExtensible { SubFormat: var s } && s == IeeeFloatSubtype;
        return (isFloat, wf.BitsPerSample) switch
        {
            (true, 32) => "f32le",
            (false, 16) => "s16le",
            (false, 24) => "s24le",
            (false, 32) => "s32le",
            _ => throw new NotSupportedException($"Audio format {wf} is not supported."),
        };
    }

    public void Dispose()
    {
        padTimer?.Dispose();
        try { capture.StopRecording(); } catch (InvalidOperationException) { }
        capture.Dispose();
        lock (gate) pipe.Dispose();
    }
}
