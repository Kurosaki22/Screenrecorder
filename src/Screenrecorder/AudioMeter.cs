using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Screenrecorder;

/// <summary>Feeds the mixer's level meters while no recording runs: captures the enabled sources, writes nothing.</summary>
public sealed class AudioMeter : IDisposable
{
    readonly List<IWaveIn> captures = [];

    public AudioMeter(AudioChannel? system, AudioChannel? mic)
    {
        try
        {
#pragma warning disable CS0618 // same IWaveIn classes as Recorder, see the shortcut note there
            if (system is not null) Start(new WasapiLoopbackCapture(), system);
            if (mic is not null) Start(new WasapiCapture(), mic);
#pragma warning restore CS0618
        }
        catch
        {
            Dispose(); // e.g. no microphone: the meters just stay empty
        }
    }

    void Start(IWaveIn capture, AudioChannel channel)
    {
        captures.Add(capture);
        if (AudioPipeWriter.SampleFormat(capture.WaveFormat) != "f32le") return;
        capture.DataAvailable += (_, e) => channel.Report(AudioLevel.Peak(e.Buffer.AsSpan(0, e.BytesRecorded), channel.Gain));
        capture.StartRecording();
    }

    public void Dispose()
    {
        foreach (var c in captures)
        {
            try { c.StopRecording(); } catch (InvalidOperationException) { }
            c.Dispose();
        }
        captures.Clear();
    }
}
