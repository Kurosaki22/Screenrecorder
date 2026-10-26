namespace Screenrecorder;

/// <summary>Raw PCM arriving through a named pipe, described in ffmpeg's terms (e.g. f32le, 48000, 2).</summary>
public sealed record AudioInput(string PipePath, string SampleFormat, int SampleRate, int Channels);

/// <summary>Everything one recording segment needs besides the output path.</summary>
public sealed record RecordOptions(CaptureArea Area, VideoEncoder Encoder, int Fps, int Quality, bool ShowCursor,
    bool SystemAudio, bool Microphone);

public static class FfmpegArgs
{
    public static List<string> Build(RecordOptions o, IReadOnlyList<AudioInput> audio, string outputPath)
    {
        var r = o.Area.Rect;
        var mouse = o.ShowCursor ? "1" : "0";
        // -progress: key=value lines on stdout every 0.5 s (total_size drives the size display).
        var a = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-stats_period", "0.5", "-y" };

        if (o.Area.OutputIndex is int idx)
            a.AddRange(["-f", "lavfi", "-i",
                $"ddagrab=output_idx={idx}:framerate={o.Fps}:video_size={r.Width}x{r.Height}:offset_x={r.X}:offset_y={r.Y}:draw_mouse={mouse}"]);
        else
            a.AddRange(["-f", "gdigrab", "-framerate", $"{o.Fps}", "-offset_x", $"{r.X}", "-offset_y", $"{r.Y}",
                "-video_size", $"{r.Width}x{r.Height}", "-draw_mouse", mouse, "-i", "desktop"]);

        foreach (var input in audio)
            a.AddRange(["-f", input.SampleFormat, "-ar", $"{input.SampleRate}",
                "-ac", $"{input.Channels}", "-i", input.PipePath]);

        a.AddRange(["-map", "0:v"]);
        if (audio.Count == 1)
            a.AddRange(["-map", "1:a"]);
        else if (audio.Count > 1)
        {
            var inputs = string.Concat(Enumerable.Range(1, audio.Count).Select(i => $"[{i}:a]"));
            a.AddRange(["-filter_complex", $"{inputs}amix=inputs={audio.Count}:normalize=0[a]", "-map", "[a]"]);
        }

        if (o.Area.OutputIndex is not null && !o.Encoder.TakesD3D11)
            a.AddRange(["-vf", "hwdownload,format=bgra"]);

        a.AddRange(["-c:v", o.Encoder.Name, .. o.Encoder.QualityArgs(o.Quality)]);
        // Opus: royalty-free; -ac 2 downmixes 5.1/7.1 loopback devices.
        if (audio.Count > 0)
            a.AddRange(["-c:a", "libopus", "-b:a", "160k", "-ar", "48000", "-ac", "2"]);

        a.Add(outputPath);
        return a;
    }
}
