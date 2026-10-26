using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using NAudio.Wave;
using Screenrecorder;
using Screenrecorder.Resources;

namespace Screenrecorder.Tests;

public class EncodersTests
{
    const string Sample = """
        Encoders:
         V..... = Video
         ------
         V....D libsvtav1            SVT-AV1(Scalable Video Technology for AV1) encoder (codec av1)
         V....D av1_nvenc            NVIDIA NVENC av1 encoder (codec av1)
         V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)
         V..... h264_qsv             H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (Intel Quick Sync Video acceleration) (codec h264)
         V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
         A....D libopus              libopus Opus (codec opus)
         A....D h264_nvenc_fake      not a video encoder
        """;

    [Fact]
    public void Parses_known_video_encoders_in_preferred_order()
    {
        var names = Encoders.ParseAvailable(Sample).Select(e => e.Name);
        Assert.Equal(["h264_nvenc", "av1_nvenc", "h264_qsv", "libsvtav1"], names);
    }
}

public class AudioPipeWriterTests
{
    [Fact]
    public void No_padding_while_audio_flows()
    {
        Assert.Equal(0, AudioPipeWriter.FramesToPad(0, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(40), 48000));
    }

    [Fact]
    public void Silent_loopback_is_padded_up_to_the_clock_minus_latency()
    {
        // 1 s elapsed, nothing written, no data for 1 s → 0.95 s of silence.
        Assert.Equal(45600, AudioPipeWriter.FramesToPad(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 48000));
    }

    [Fact]
    public void Already_caught_up_needs_no_padding()
    {
        Assert.Equal(0, AudioPipeWriter.FramesToPad(48000, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 48000));
    }

    [Fact]
    public void Maps_wasapi_formats_to_ffmpeg_sample_formats()
    {
        Assert.Equal("f32le", AudioPipeWriter.SampleFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)));
        Assert.Equal("f32le", AudioPipeWriter.SampleFormat(new WaveFormatExtensible(48000, 32, 2)));
        Assert.Equal("s16le", AudioPipeWriter.SampleFormat(new WaveFormat(44100, 16, 2)));
        Assert.Throws<NotSupportedException>(() => AudioPipeWriter.SampleFormat(new WaveFormat(8000, 8, 1)));
    }
}

public class RecorderTests
{
    [Theory]
    [InlineData("total_size=123456", 123456L)]
    [InlineData("total_size=0", 0L)]
    [InlineData("total_size=N/A", null)]
    [InlineData("out_time_us=5000000", null)]
    [InlineData("progress=continue", null)]
    public void Parses_total_size_from_progress_lines(string line, long? expected) =>
        Assert.Equal(expected, Recorder.ParseTotalSize(line));
}

public class PixelRectTests
{
    [Fact]
    public void Intersect_returns_the_overlap()
    {
        Assert.Equal(new PixelRect(50, 50, 50, 50), new PixelRect(0, 0, 100, 100).Intersect(new PixelRect(50, 50, 100, 100)));
    }

    [Fact]
    public void Intersect_clips_a_window_hanging_off_the_desktop()
    {
        var desktop = new PixelRect(-1920, 0, 4480, 1440);
        Assert.Equal(new PixelRect(-1920, 0, 760, 600), desktop.Intersect(new PixelRect(-1960, -40, 800, 640)));
    }

    [Fact]
    public void Intersect_of_disjoint_or_touching_rects_is_null()
    {
        Assert.Null(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(20, 20, 10, 10)));
        Assert.Null(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(10, 0, 10, 10)));
    }
}

public class SettingsTests
{
    [Theory]
    [InlineData("1280x720", 1280, 720)]
    [InlineData(" 1600 X 900 ", 1600, 900)]
    [InlineData("800×600", 800, 600)]
    public void Parses_sizes(string text, int w, int h)
    {
        Assert.True(Settings.TryParseSize(text, out var pw, out var ph));
        Assert.Equal((w, h), (pw, ph));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1280")]
    [InlineData("axb")]
    [InlineData("0x720")]
    [InlineData("99999x720")]
    public void Rejects_invalid_sizes(string text) => Assert.False(Settings.TryParseSize(text, out _, out _));
}

public class TranslationTests
{
    static string ResxPath(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Screenrecorder.slnx"))) dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir, "src", "Screenrecorder", "Resources", name);
    }

    static Dictionary<string, string> Load(string name) => XDocument.Load(ResxPath(name)).Root!.Elements("data")
        .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")!.Value);

    [Fact]
    public void English_and_german_have_the_same_non_empty_keys()
    {
        var en = Load("Strings.resx");
        var de = Load("Strings.de.resx");
        Assert.Equal(en.Keys.Order(), de.Keys.Order());
        Assert.All(en.Values.Concat(de.Values), v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void Every_resource_key_has_a_typed_property_and_resolves_in_both_languages()
    {
        var props = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(p => p.Name);
        Assert.Equal(Load("Strings.resx").Keys.Order(), props.Order());

        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("de");
            Assert.Equal("Aufnahme", Strings.Record);
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Record", Strings.Record);
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }
}
