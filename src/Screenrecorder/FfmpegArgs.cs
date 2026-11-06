namespace Screenrecorder;

/// <summary>Raw PCM arriving through a named pipe, described in ffmpeg's terms (e.g. f32le, 48000, 2).</summary>
public sealed record AudioInput(string PipePath, string SampleFormat, int SampleRate, int Channels);

/// <summary>
/// Everything one recording segment needs besides the output path. The channels carry the mixer's live gain
/// and receive level peaks; null means unity gain and no meter. CaptureGpu: vendor of the GPU the grabber's
/// frames live on (see Displays.CaptureGpuVendor); "" = unknown, frames are downloaded.
/// </summary>
public sealed record RecordOptions(CaptureArea Area, VideoEncoder Encoder, int Fps, int Quality, bool ShowCursor,
    bool SystemAudio, bool Microphone, AudioChannel? SystemChannel = null, AudioChannel? MicChannel = null,
    string CaptureGpu = "");

public static class FfmpegArgs
{
    public static List<string> Build(RecordOptions o, IReadOnlyList<AudioInput> audio, string outputPath)
    {
        var r = o.Area.Rect;
        var mouse = o.ShowCursor ? "1" : "0";
        // -progress: key=value lines on stdout every 0.5 s (total_size drives the size display).
        var a = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-stats_period", "0.5", "-y" };

        var filters = new List<string>();
        switch (o.Area.Grabber)
        {
            case Grabber.GraphicsCapture:
                // Sample at up to 240 Hz with real timestamps, then pick evenly spaced frames: measured on a 240 Hz
                // screen this halves motion judder compared to ddagrab at the target rate and never drops a frame.
                var common = $"max_framerate={Math.Max(240, o.Fps)}:capture_cursor={mouse}";
                if (o.Area.Window != 0)
                    // width/height -2: round the window size down to even, as yuv420 encoders need
                    a.AddRange(["-f", "lavfi", "-i", $"gfxcapture=hwnd={o.Area.Window}:{common}:width=-2:height=-2"]);
                else
                {
                    var m = o.Area.Monitor!.Bounds;
                    a.AddRange(["-f", "lavfi", "-i",
                        $"gfxcapture=hmonitor={o.Area.Monitor.Handle}:{common}" +
                        $":crop_left={r.X}:crop_top={r.Y}:crop_right={m.Width - r.Right}:crop_bottom={m.Height - r.Bottom}"]);
                }
                filters.Add($"fps={o.Fps}");
                break;
            case Grabber.DesktopDuplication:
                a.AddRange(["-f", "lavfi", "-i",
                    $"ddagrab=output_idx={o.Area.OutputIndex}:framerate={o.Fps}:video_size={r.Width}x{r.Height}:offset_x={r.X}:offset_y={r.Y}:draw_mouse={mouse}"]);
                break;
            default:
                a.AddRange(["-f", "gdigrab", "-framerate", $"{o.Fps}", "-offset_x", $"{r.X}", "-offset_y", $"{r.Y}",
                    "-video_size", $"{r.Width}x{r.Height}", "-draw_mouse", mouse, "-i", "desktop"]);
                break;
        }

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

        // GPU grabbers deliver D3D11 frames; encoders that can't take them get a download to system memory.
        // NVENC can't open a session on another vendor's device ("no encode device"), e.g. when the NVIDIA card
        // has no monitor and the iGPU is adapter 0.
        if (o.Area.Grabber != Grabber.Gdi && !(o.Encoder.TakesD3D11 && o.Encoder.Vendor == o.CaptureGpu))
            filters.Add("hwdownload,format=bgra");
        if (filters.Count > 0)
            a.AddRange(["-vf", string.Join(",", filters)]);

        // Keyframe every 2 s: bounds what a crash can cost (see movflags) and keeps seeking quick.
        a.AddRange(["-c:v", o.Encoder.Name, .. o.Encoder.QualityArgs(o.Quality), "-g", $"{2 * o.Fps}"]);
        // Opus: royalty-free; -ac 2 downmixes 5.1/7.1 loopback devices.
        if (audio.Count > 0)
            a.AddRange(["-c:a", "libopus", "-b:a", "160k", "-ar", "48000", "-ac", "2"]);

        // Fragmented MP4: playable up to the last fragment even if the app or PC crashes mid-recording
        // (a plain MP4 writes its index only at the end and is unreadable without it). flush_packets: a calm
        // screen makes fragments smaller than ffmpeg's 32 KB write buffer, which a crash would otherwise lose.
        a.AddRange(["-flush_packets", "1", "-movflags", "+frag_keyframe+empty_moov+default_base_moof"]);
        a.Add(outputPath);
        return a;
    }
}
