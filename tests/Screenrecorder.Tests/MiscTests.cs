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

public class FfmpegExtractTests
{
    [Fact]
    public void Embedded_ffmpeg_is_written_once_and_old_versions_are_removed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sr-ffmpeg-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "0.1.0"));
            var exe = Recorder.ExtractIfNeeded(new MemoryStream([1, 2, 3]), root, "0.2.0");
            Assert.Equal(Path.Combine(root, "0.2.0", "ffmpeg.exe"), exe);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(exe));
            Assert.False(Directory.Exists(Path.Combine(root, "0.1.0")));

            var written = File.GetLastWriteTimeUtc(exe);
            Thread.Sleep(50);
            Recorder.ExtractIfNeeded(new MemoryStream([1, 2, 3]), root, "0.2.0");
            Assert.Equal(written, File.GetLastWriteTimeUtc(exe)); // same size: not rewritten

            Recorder.ExtractIfNeeded(new MemoryStream([1, 2, 3, 4]), root, "0.2.0");
            Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(exe)); // truncated/stale copy gets replaced
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class FileNamerTests
{
    static readonly DateTime Time = new(2026, 10, 26, 21, 30, 22, 663);

    [Fact]
    public void Replaces_all_placeholders()
    {
        Assert.Equal("Aufnahme_2026_10_26_21_30_22_663", FileNamer.Format(FileNamer.DefaultPattern, Time, 1));
        Assert.Equal("Clip 7 26.10", FileNamer.Format("Clip <Nr> <DD>.<MM>.", Time, 7)); // trailing dot is not allowed on Windows
    }

    [Fact]
    public void Invalid_characters_become_underscores_and_empty_falls_back()
    {
        Assert.Equal("a_b_c", FileNamer.Format("a:b/c", Time, 1));
        Assert.Equal("Aufnahme", FileNamer.Format("  ", Time, 1));
    }

    [Fact]
    public void Unique_appends_a_counter_when_the_file_exists()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Equal(Path.Combine(dir, "x.mp4"), FileNamer.Unique(dir, "x"));
            File.WriteAllText(Path.Combine(dir, "x.mp4"), "");
            File.WriteAllText(Path.Combine(dir, "x_2.mp4"), "");
            Assert.Equal(Path.Combine(dir, "x_3.mp4"), FileNamer.Unique(dir, "x"));
            Assert.Equal(Path.Combine(dir, "x.png"), FileNamer.Unique(dir, "x", ".png")); // screenshots don't collide with videos
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("F2", System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.F2)]
    [InlineData("Ctrl+Shift+F5", System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.F5)]
    [InlineData("alt + f9", System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.F9)]
    public void Parses_and_formats(string text, System.Windows.Input.ModifierKeys modifiers, System.Windows.Input.Key key)
    {
        Assert.True(Hotkey.TryParse(text, out var m, out var k));
        Assert.Equal((modifiers, key), (m, k));
        Assert.True(Hotkey.TryParse(Hotkey.Format(m, k), out var m2, out var k2));
        Assert.Equal((m, k), (m2, k2));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl")]
    [InlineData("F2+F3")]
    [InlineData("Banana")]
    [InlineData("Ctrl+LeftShift")]
    [InlineData("NumLock")]
    public void Rejects_invalid(string? text) => Assert.False(Hotkey.TryParse(text, out _, out _));

    [Fact]
    public void Maps_to_win32_with_norepeat()
    {
        Hotkey.TryParse("Ctrl+F2", out var m, out var k);
        Assert.Equal((Hotkey.MOD_CONTROL | Hotkey.MOD_NOREPEAT, 0x71u), Hotkey.ToWin32(m, k));
    }
}

public class RecordingSessionTests
{
    [Fact]
    public void Concat_list_escapes_single_quotes()
    {
        Assert.Equal("file 'C:\\a.mp4'\nfile 'C:\\Bob'\\''s.mp4'\n", RecordingSession.ConcatList([@"C:\a.mp4", @"C:\Bob's.mp4"]));
    }
}

public class MiniBarPlacementTests
{
    // 2560x1440 monitor with a 48 px taskbar at the bottom.
    static readonly PixelRect Work = new(0, 0, 2560, 1392);
    const int W = 300, H = 40, Gap = 6;

    [Fact]
    public void Goes_below_left_when_there_is_room()
    {
        Assert.Equal((200, 836), MiniBarPlacement.Place(new PixelRect(200, 100, 1286, 730), W, H, Work, Gap));
    }

    [Fact]
    public void Goes_above_left_when_the_area_touches_the_taskbar()
    {
        Assert.Equal((200, 554), MiniBarPlacement.Place(new PixelRect(200, 600, 1286, 780), W, H, Work, Gap));
    }

    [Fact]
    public void Goes_inside_bottom_left_when_the_area_fills_the_screen()
    {
        Assert.Equal((0, 1346), MiniBarPlacement.Place(new PixelRect(0, 0, 2560, 1440), W, H, Work, Gap));
    }

    [Fact]
    public void Is_clamped_to_the_work_area_horizontally()
    {
        Assert.Equal((2560 - W, 836), MiniBarPlacement.Place(new PixelRect(2400, 100, 150, 730), W, H, Work, Gap));
    }

    [Fact]
    public void Works_on_a_monitor_left_of_the_primary()
    {
        var left = new PixelRect(-1920, 0, 1920, 1032);
        Assert.Equal((-1800, 506), MiniBarPlacement.Place(new PixelRect(-1800, 100, 800, 400), W, H, left, Gap));
    }
}

public class DiskSpaceTests
{
    [Theory]
    [InlineData(10L << 30, DiskState.Ok)]
    [InlineData(2L << 30, DiskState.Ok)]
    [InlineData((2L << 30) - 1, DiskState.Low)]
    [InlineData(500L << 20, DiskState.Low)]
    [InlineData((500L << 20) - 1, DiskState.Critical)]
    [InlineData(0, DiskState.Critical)]
    public void Classifies_free_space(long free, DiskState expected) => Assert.Equal(expected, DiskSpace.Status(free));

    [Fact]
    public void Unknown_drives_count_as_plenty() => Assert.Equal(long.MaxValue, DiskSpace.FreeBytes(@"\\no-such-server\share"));
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
    [InlineData("24", 24)]
    [InlineData(" 60 ", 60)]
    [InlineData("1", 1)]
    [InlineData("240", 240)]
    public void Accepts_frame_rates_from_1_to_240(string text, int expected)
    {
        Assert.True(Settings.TryParseFps(text, out var fps));
        Assert.Equal(expected, fps);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("241")]
    [InlineData("abc")]
    [InlineData("29.97")]
    [InlineData("")]
    public void Rejects_invalid_frame_rates(string text) => Assert.False(Settings.TryParseFps(text, out _));

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
