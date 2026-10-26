using Screenrecorder;

namespace Screenrecorder.Tests;

public class FfmpegArgsTests
{
    static readonly VideoEncoder Nvenc = Encoders.Known.Single(e => e.Name == "h264_nvenc");
    static readonly VideoEncoder Mf = Encoders.Known.Single(e => e.Name == "h264_mf");
    static readonly AudioInput Sys = new(@"\\.\pipe\sys", "f32le", 48000, 2);
    static readonly AudioInput Mic = new(@"\\.\pipe\mic", "f32le", 44100, 1);

    static string Line(List<string> args) => string.Join(' ', args);

    [Fact]
    public void Ddagrab_gets_monitor_offset_and_size()
    {
        var args = FfmpegArgs.Build(new CaptureArea(1, new PixelRect(10, 20, 1280, 720)), Nvenc, 60, [], "out.mp4");
        Assert.Contains("ddagrab=output_idx=1:framerate=60:video_size=1280x720:offset_x=10:offset_y=20:draw_mouse=1", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void Progress_is_reported_on_stdout()
    {
        var line = Line(FfmpegArgs.Build(new CaptureArea(0, new PixelRect(0, 0, 640, 360)), Nvenc, 30, [], "o.mp4"));
        Assert.Contains("-progress pipe:1 -stats_period 0.5", line);
    }

    [Fact]
    public void Nvenc_takes_gpu_frames_directly()
    {
        var args = FfmpegArgs.Build(new CaptureArea(0, new PixelRect(0, 0, 640, 360)), Nvenc, 30, [], "o.mp4");
        Assert.DoesNotContain("hwdownload,format=bgra", args);
        Assert.Contains("-c:v h264_nvenc", Line(args));
    }

    [Fact]
    public void Other_encoders_download_frames_from_gpu()
    {
        var args = FfmpegArgs.Build(new CaptureArea(0, new PixelRect(0, 0, 640, 360)), Mf, 30, [], "o.mp4");
        Assert.Contains("-vf hwdownload,format=bgra", Line(args));
    }

    [Fact]
    public void Gdigrab_fallback_uses_absolute_coordinates_and_no_hwdownload()
    {
        var args = FfmpegArgs.Build(new CaptureArea(null, new PixelRect(-1920, 0, 3840, 1080)), Mf, 30, [], "o.mp4");
        var line = Line(args);
        Assert.Contains("-f gdigrab -framerate 30 -offset_x -1920 -offset_y 0 -video_size 3840x1080", line);
        Assert.DoesNotContain("hwdownload", line);
    }

    [Fact]
    public void No_audio_means_no_audio_codec()
    {
        var line = Line(FfmpegArgs.Build(new CaptureArea(0, new PixelRect(0, 0, 640, 360)), Nvenc, 30, [], "o.mp4"));
        Assert.DoesNotContain("-c:a", line);
        Assert.DoesNotContain("amix", line);
    }

    [Fact]
    public void One_audio_source_is_mapped_directly_and_encoded_as_opus()
    {
        var line = Line(FfmpegArgs.Build(new CaptureArea(0, new PixelRect(0, 0, 640, 360)), Nvenc, 30, [Sys], "o.mp4"));
        Assert.Contains(@"-f f32le -ar 48000 -ac 2 -i \\.\pipe\sys", line);
        Assert.Contains("-map 0:v -map 1:a", line);
        Assert.Contains("-c:a libopus", line);
        Assert.DoesNotContain("amix", line);
    }

    [Fact]
    public void Two_audio_sources_are_mixed()
    {
        var line = Line(FfmpegArgs.Build(new CaptureArea(0, new PixelRect(0, 0, 640, 360)), Nvenc, 30, [Sys, Mic], "o.mp4"));
        Assert.Contains(@"-ar 44100 -ac 1 -i \\.\pipe\mic", line);
        Assert.Contains("-filter_complex [1:a][2:a]amix=inputs=2:normalize=0[a] -map [a]", line);
    }

    [Fact]
    public void Only_royalty_free_or_lgpl_encoders_are_known()
    {
        Assert.DoesNotContain(Encoders.Known, e => e.Name is "libx264" or "libx265");
    }
}
