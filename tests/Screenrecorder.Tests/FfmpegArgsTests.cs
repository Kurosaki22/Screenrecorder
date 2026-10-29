using Screenrecorder;

namespace Screenrecorder.Tests;

public class FfmpegArgsTests
{
    static readonly VideoEncoder Nvenc = Encoders.Known.Single(e => e.Name == "h264_nvenc");
    static readonly VideoEncoder Mf = Encoders.Known.Single(e => e.Name == "h264_mf");
    static readonly AudioInput Sys = new(@"\\.\pipe\sys", "f32le", 48000, 2);
    static readonly AudioInput Mic = new(@"\\.\pipe\mic", "f32le", 44100, 1);

    static RecordOptions Options(CaptureArea area, VideoEncoder encoder, int fps = 30, int quality = 80, bool cursor = true) =>
        new(area, encoder, fps, quality, cursor, SystemAudio: false, Microphone: false);

    static readonly CaptureArea Small = new(Grabber.DesktopDuplication, new PixelRect(0, 0, 640, 360));
    static readonly MonitorInfo Monitor4K = new(12345, new PixelRect(0, 0, 3840, 2160));
    static readonly CaptureArea Gfx = new(Grabber.GraphicsCapture, new PixelRect(100, 200, 1280, 720), Monitor: Monitor4K);
    static CaptureArea Gdi(PixelRect r) => new(Grabber.Gdi, r);

    static string Line(List<string> args) => string.Join(' ', args);

    [Fact]
    public void Graphics_capture_crops_the_monitor_samples_at_240_and_resamples_to_the_target_rate()
    {
        var line = Line(FfmpegArgs.Build(Options(Gfx, Nvenc, fps: 60), [], "o.mp4"));
        Assert.Contains("gfxcapture=hmonitor=12345:max_framerate=240:capture_cursor=1:crop_left=100:crop_top=200:crop_right=2460:crop_bottom=1240", line);
        Assert.Contains("-vf fps=60 ", line);
    }

    [Fact]
    public void Window_capture_follows_the_window_handle_with_even_size()
    {
        var area = new CaptureArea(Grabber.GraphicsCapture, new PixelRect(10, 10, 800, 600), Window: 0x1234);
        var line = Line(FfmpegArgs.Build(Options(area, Nvenc, fps: 30), [], "o.mp4"));
        Assert.Contains("gfxcapture=hwnd=4660:max_framerate=240:capture_cursor=1:width=-2:height=-2", line);
        Assert.DoesNotContain("hmonitor", line);
        Assert.Contains("-vf fps=30 ", line);
    }

    [Fact]
    public void Output_survives_a_crash_and_has_a_keyframe_every_two_seconds()
    {
        var args = FfmpegArgs.Build(Options(Small, Nvenc, fps: 60), [], "o.mp4");
        Assert.Contains("-flush_packets 1 -movflags +frag_keyframe+empty_moov+default_base_moof o.mp4", Line(args));
        Assert.Contains("-g 120", Line(args));
    }

    [Fact]
    public void Graphics_capture_downloads_frames_for_cpu_side_encoders()
    {
        Assert.Contains("-vf fps=24,hwdownload,format=bgra ", Line(FfmpegArgs.Build(Options(Gfx, Mf, fps: 24), [], "o.mp4")));
    }

    [Fact]
    public void Ddagrab_gets_monitor_offset_and_size()
    {
        var area = new CaptureArea(Grabber.DesktopDuplication, new PixelRect(10, 20, 1280, 720), OutputIndex: 1);
        var args = FfmpegArgs.Build(Options(area, Nvenc, fps: 60), [], "out.mp4");
        Assert.Contains("ddagrab=output_idx=1:framerate=60:video_size=1280x720:offset_x=10:offset_y=20:draw_mouse=1", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void Cursor_can_be_hidden_for_every_grabber()
    {
        Assert.Contains("capture_cursor=0", Line(FfmpegArgs.Build(Options(Gfx, Nvenc, cursor: false), [], "o.mp4")));
        Assert.Contains("draw_mouse=0", Line(FfmpegArgs.Build(Options(Small, Nvenc, cursor: false), [], "o.mp4")));
        var gdi = Line(FfmpegArgs.Build(Options(Gdi(new PixelRect(0, 0, 640, 360)), Mf, cursor: false), [], "o.mp4"));
        Assert.Contains("-draw_mouse 0", gdi);
    }

    [Fact]
    public void Quality_setting_reaches_the_encoder()
    {
        Assert.Contains("-cq 23", Line(FfmpegArgs.Build(Options(Small, Nvenc, quality: 80), [], "o.mp4")));
        Assert.Contains("-cq 42", Line(FfmpegArgs.Build(Options(Small, Nvenc, quality: 1), [], "o.mp4")));
    }

    [Fact]
    public void Progress_is_reported_on_stdout()
    {
        Assert.Contains("-progress pipe:1 -stats_period 0.5", Line(FfmpegArgs.Build(Options(Small, Nvenc), [], "o.mp4")));
    }

    [Fact]
    public void Nvenc_takes_gpu_frames_directly()
    {
        var args = FfmpegArgs.Build(Options(Small, Nvenc), [], "o.mp4");
        Assert.DoesNotContain("hwdownload,format=bgra", args);
        Assert.Contains("-c:v h264_nvenc", Line(args));
    }

    [Fact]
    public void Other_encoders_download_frames_from_gpu()
    {
        Assert.Contains("-vf hwdownload,format=bgra", Line(FfmpegArgs.Build(Options(Small, Mf), [], "o.mp4")));
    }

    [Fact]
    public void Gdigrab_fallback_uses_absolute_coordinates_and_no_hwdownload()
    {
        var line = Line(FfmpegArgs.Build(Options(Gdi(new PixelRect(-1920, 0, 3840, 1080)), Mf), [], "o.mp4"));
        Assert.Contains("-f gdigrab -framerate 30 -offset_x -1920 -offset_y 0 -video_size 3840x1080", line);
        Assert.DoesNotContain("hwdownload", line);
    }

    [Fact]
    public void No_audio_means_no_audio_codec()
    {
        var line = Line(FfmpegArgs.Build(Options(Small, Nvenc), [], "o.mp4"));
        Assert.DoesNotContain("-c:a", line);
        Assert.DoesNotContain("amix", line);
    }

    [Fact]
    public void One_audio_source_is_mapped_directly_and_encoded_as_opus()
    {
        var line = Line(FfmpegArgs.Build(Options(Small, Nvenc), [Sys], "o.mp4"));
        Assert.Contains(@"-f f32le -ar 48000 -ac 2 -i \\.\pipe\sys", line);
        Assert.Contains("-map 0:v -map 1:a", line);
        Assert.Contains("-c:a libopus", line);
        Assert.DoesNotContain("amix", line);
    }

    [Fact]
    public void Quick_sync_quality_applies_to_video_only()
    {
        // Bare -global_quality also hits libopus, which rejects quality mode (issue #1).
        var qsv = Encoders.Known.Single(e => e.Name == "h264_qsv");
        var line = Line(FfmpegArgs.Build(Options(Small, qsv), [Sys], "o.mp4"));
        Assert.Contains("-global_quality:v 23", line);
        Assert.DoesNotContain("-global_quality ", line);
    }

    [Fact]
    public void Two_audio_sources_are_mixed()
    {
        var line = Line(FfmpegArgs.Build(Options(Small, Nvenc), [Sys, Mic], "o.mp4"));
        Assert.Contains(@"-ar 44100 -ac 1 -i \\.\pipe\mic", line);
        Assert.Contains("-filter_complex [1:a][2:a]amix=inputs=2:normalize=0[a] -map [a]", line);
    }

    [Fact]
    public void Only_royalty_free_or_lgpl_encoders_are_known()
    {
        Assert.DoesNotContain(Encoders.Known, e => e.Name is "libx264" or "libx265");
    }
}

public class QualityTests
{
    static string Value(VideoEncoder e, int q) => e.QualityArgs(q)[^1];

    [Fact]
    public void Extremes_map_to_worst_and_best()
    {
        var nvenc = Encoders.Known.Single(e => e.Name == "h264_nvenc");
        Assert.Equal("42", Value(nvenc, 1));
        Assert.Equal("18", Value(nvenc, 100));
        Assert.Equal("42", Value(nvenc, -5)); // clamped
        Assert.Equal(["-preset", "p5", "-rc", "vbr", "-b:v", "0", "-cq", "23"], nvenc.QualityArgs(80));
    }

    [Fact]
    public void Bitrate_encoders_use_megabits()
    {
        var mf = Encoders.Known.Single(e => e.Name == "h264_mf");
        Assert.Equal("2M", Value(mf, 1));
        Assert.Equal("24M", Value(mf, 100));
    }

    [Fact]
    public void Higher_quality_never_means_a_smaller_file_setting()
    {
        foreach (var e in Encoders.Known)
        {
            var values = Enumerable.Range(1, 100).Select(q => int.Parse(Value(e, q).TrimEnd('M'))).ToList();
            // CQ/CRF: lower is better; bitrate: higher is better.
            var ordered = e.Unit == "M" ? values.Order() : values.OrderDescending();
            Assert.Equal(ordered, values);
        }
    }
}
