using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Screenrecorder;
using Xunit.Abstractions;

namespace Screenrecorder.Tests;

/// <summary>
/// Needs a real desktop, audio output and ffmpeg/ffprobe; excluded in CI via --filter Category!=Integration.
/// The sync test briefly flashes a window in the top-left corner and plays a quiet 15 kHz tone
/// (high enough that other audio playing on the PC does not trigger the detector).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Desktop")] // these tests put windows on the real screen; run them one after another
public class RecorderIntegrationTests(ITestOutputHelper log)
{
    static readonly VideoEncoder Encoder = Encoders.Known.Single(e => e.Name == "h264_mf");

    [Fact]
    public async Task Records_video_with_system_audio_track()
    {
        var ffmpeg = Recorder.FindFfmpeg();
        var displays = Displays.Enumerate();
        if (ffmpeg is null || displays.Count == 0) return; // no environment to record in

        using var heartbeat = new Heartbeat(); // gfxcapture needs screen changes, like the app's mini bar provides
        var output = TempFile();
        try
        {
            var rec = Recorder.Start(ffmpeg, Options(displays, new PixelRect(displays[0].Bounds.X, displays[0].Bounds.Y, 640, 360)), output);
            string? failure = null;
            rec.Failed += m => failure = m;
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.True(rec.BytesWritten > 0, "ffmpeg reported no output size while recording");
            await rec.StopAsync();
            Assert.Null(failure);

            var streams = Streams(output);
            log.WriteLine($"video {streams["h264"]:0.000}s, audio {streams["opus"]:0.000}s");
            Assert.InRange(streams["h264"], 2.0, 4.0);
            // Silent loopback delivers no buffers; padding must keep the audio track about as long as the video.
            // The video runs ~0.3 s longer: ffmpeg still flushes queued frames after 'q' (start sync is checked below).
            Assert.InRange(streams["opus"], streams["h264"] - 0.5, streams["h264"] + 0.1);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Audio_and_video_are_in_sync()
    {
        var ffmpeg = Recorder.FindFfmpeg();
        var displays = Displays.Enumerate();
        if (ffmpeg is null || !displays.Any(d => d.IsPrimary)) return;

        using var heartbeat = new Heartbeat(); // gfxcapture needs screen changes, like the app's mini bar provides
        var output = TempFile();
        try
        {
            using (var flasher = new Flasher())
            {
                var rec = Recorder.Start(ffmpeg, Options(displays, new PixelRect(0, 0, 640, 360)) with { Fps = 60 }, output);
                await Task.Delay(TimeSpan.FromSeconds(1.5));

#pragma warning disable CS0618 // WasapiOut is fine for a test tone
                using var tone = new WasapiOut(AudioClientShareMode.Shared, true, 20);
#pragma warning restore CS0618
                tone.Init(new SignalGenerator(48000, 2) { Frequency = 15000, Gain = 0.1 }.Take(TimeSpan.FromSeconds(1)));
                flasher.White();
                tone.Play();

                await Task.Delay(TimeSpan.FromSeconds(1.5));
                await rec.StopAsync();
            }

            var video = FirstBrightFrame(output);
            var audio = FirstSound(output);
            log.WriteLine($"video {video:0.000}s, audio {audio:0.000}s, offset {(audio - video) * 1000:0} ms");
            Assert.InRange(video, 1.0, 3.0);
            // Within ~3 frames at 60 fps; screen compositing and audio output latency add a little jitter.
            Assert.True(Math.Abs(audio - video) < 0.06, $"video changes at {video:0.000}s, tone starts at {audio:0.000}s");
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Pause_leaves_out_the_paused_time_and_yields_one_file()
    {
        var ffmpeg = Recorder.FindFfmpeg();
        var displays = Displays.Enumerate();
        if (ffmpeg is null || displays.Count == 0) return;

        using var heartbeat = new Heartbeat(); // gfxcapture needs screen changes, like the app's mini bar provides
        var output = TempFile();
        try
        {
            var session = RecordingSession.Start(ffmpeg, Options(displays, new PixelRect(displays[0].Bounds.X, displays[0].Bounds.Y, 640, 360)), output);
            await Task.Delay(TimeSpan.FromSeconds(2));
            await session.PauseAsync();
            var pausedAt = session.Elapsed;
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            Assert.Equal(pausedAt, session.Elapsed); // clock stands still while paused
            Assert.True(session.BytesWritten > 0);
            session.Resume();
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.Null(await session.StopAsync());

            var streams = Streams(output);
            log.WriteLine($"video {streams["h264"]:0.000}s, audio {streams["opus"]:0.000}s");
            Assert.InRange(streams["h264"], 3.5, 5.0); // ~4 s recorded, the 1.5 s pause is not in the file
            Assert.InRange(streams["opus"], streams["h264"] - 1.0, streams["h264"] + 0.1);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + ".part*"));
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Recording_stays_readable_when_ffmpeg_is_killed_mid_way()
    {
        var ffmpeg = Recorder.FindFfmpeg();
        var displays = Displays.Enumerate();
        if (ffmpeg is null || displays.Count == 0) return;

        var output = TempFile();
        try
        {
            // ddagrab repeats frames on an idle screen, so this checks the file format, independent of desktop activity.
            var area = new CaptureArea(Grabber.DesktopDuplication, new PixelRect(0, 0, 640, 360), displays[0].OutputIndex);
            var options = new RecordOptions(area, Encoder, 30, 80, ShowCursor: true, SystemAudio: false, Microphone: false);
            var psi = new ProcessStartInfo(ffmpeg) { RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (var arg in FfmpegArgs.Build(options, [], output)) psi.ArgumentList.Add(arg);
            using var p = Process.Start(psi)!;
            _ = p.StandardOutput.ReadToEndAsync(); // drain -progress
            await Task.Delay(TimeSpan.FromSeconds(4));
            p.Kill(); // like a crash: no chance to write a final index

            var duration = double.Parse(Run("ffprobe", $"-v error -show_entries format=duration -of csv=p=0 \"{output}\"").stdout.Trim(),
                CultureInfo.InvariantCulture);
            log.WriteLine($"readable after kill: {duration:0.00} s");
            Assert.InRange(duration, 1.0, 5.0);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Window_capture_records_the_window_and_stop_never_hangs_on_a_static_one()
    {
        var ffmpeg = Recorder.FindFfmpeg();
        if (ffmpeg is null) return;
        // Animated window: frames flow, video has the window's (even) client size.
        using (var window = new TestForm(() =>
        {
            var f = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(300, 300, 401, 301), BackColor = Color.SteelBlue };
            var t = new System.Windows.Forms.Timer { Interval = 30, Enabled = true };
            t.Tick += (_, _) => f.BackColor = f.BackColor.B == 180 ? Color.FromArgb(70, 130, 181) : Color.SteelBlue;
            f.FormClosed += (_, _) => t.Dispose();
            return f;
        }))
        {
            var (hwnd, client) = window.Invoke(f => (f.Handle, f.ClientSize));
            var output = TempFile();
            try
            {
                var rec = Recorder.Start(ffmpeg, WindowOptions(hwnd), output);
                await Task.Delay(TimeSpan.FromSeconds(2.5));
                Assert.True(rec.HasStarted);
                await rec.StopAsync();
                var size = Run("ffprobe", $"-v error -select_streams v -show_entries stream=width,height -of csv=p=0 \"{output}\"").stdout.Trim();
                log.WriteLine($"window client {client.Width}x{client.Height}, video {size}");
                Assert.Equal($"{client.Width & ~1},{client.Height & ~1}", size);
            }
            finally
            {
                File.Delete(output);
            }
        }

        // Static window: Windows Graphics Capture delivers no frame, ffmpeg waits; stopping must still be immediate.
        using (var still = new TestForm(() => new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(300, 300, 401, 301) }))
        {
            var output = TempFile();
            var rec = Recorder.Start(ffmpeg, WindowOptions(still.Invoke(f => f.Handle)), output);
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            // Usually no frame arrives; sometimes the window's open animation delivers one.
            var waiting = !rec.HasStarted;
            var watch = Stopwatch.StartNew();
            await rec.StopAsync();
            log.WriteLine($"static window: started={!waiting}, stop took {watch.Elapsed.TotalSeconds:0.0} s");
            if (waiting)
            {
                // Nothing recorded yet: Stop kills at once and leaves no broken file behind.
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"stop took {watch.Elapsed}");
                Assert.False(File.Exists(output));
            }
            else if (Run(ffmpeg, "-hide_banner -h filter=gfxcapture").stdout.Contains("dup_frames"))
            {
                // gfxcapture repeats the last frame (dup_frames, the shipped build), so 'q' finishes at once.
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"stop took {watch.Elapsed}");
                Assert.True(new FileInfo(output).Length > 0);
            }
            else
                log.WriteLine("ffmpeg without gfxcapture dup_frames waits 10-15 s for the next frame; not checked");
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Mixer_gain_boosts_the_recorded_system_sound()
    {
        var ffmpeg = Recorder.FindFfmpeg();
        var displays = Displays.Enumerate();
        if (ffmpeg is null || displays.Count == 0) return;

        using var heartbeat = new Heartbeat();
        async Task<double> MaxVolumeAt(double gainDb)
        {
            var output = TempFile();
            try
            {
                var options = Options(displays, new PixelRect(displays[0].Bounds.X, displays[0].Bounds.Y, 640, 360))
                    with { SystemChannel = new AudioChannel(gainDb) };
                var rec = Recorder.Start(ffmpeg, options, output);
                await Task.Delay(TimeSpan.FromSeconds(1));
#pragma warning disable CS0618 // WasapiOut is fine for a test tone
                using (var tone = new WasapiOut(AudioClientShareMode.Shared, true, 20))
#pragma warning restore CS0618
                {
                    tone.Init(new SignalGenerator(48000, 2) { Frequency = 15000, Gain = 0.1 }.Take(TimeSpan.FromSeconds(1.5)));
                    tone.Play();
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
                await rec.StopAsync();
                // Same 15 kHz band as the sync test, so other audio playing on the PC doesn't count.
                var (_, stderr) = Run("ffmpeg", $"-hide_banner -i \"{output}\" -vn -af bandpass=f=15000:w=300,volumedetect -f null -");
                return double.Parse(Regex.Match(stderr, @"max_volume: (-?[\d.]+) dB").Groups[1].Value, CultureInfo.InvariantCulture);
            }
            finally
            {
                File.Delete(output);
            }
        }

        var unity = await MaxVolumeAt(0);
        var boosted = await MaxVolumeAt(12);
        log.WriteLine($"tone peak: {unity:0.0} dB at 0 dB gain, {boosted:0.0} dB at +12 dB gain");
        Assert.InRange(boosted - unity, 10, 14);
        Assert.True(boosted <= 0);
    }

    static RecordOptions WindowOptions(IntPtr hwnd) =>
        new(new CaptureArea(Grabber.GraphicsCapture, default, Window: hwnd), Encoder, 30, 80, ShowCursor: false, SystemAudio: false, Microphone: false);

    static RecordOptions Options(IReadOnlyList<Display> displays, PixelRect rect) =>
        new(Displays.Locate(rect, displays, Native.Monitors()), Encoder, 30, 80, ShowCursor: true, SystemAudio: true, Microphone: false);

    /// <summary>Duration per stream codec, e.g. { h264: 3.0, opus: 2.99 }.</summary>
    static Dictionary<string, double> Streams(string file) =>
        Run("ffprobe", $"-v error -show_entries stream=codec_name,duration -of csv=p=0 \"{file}\"").stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(','))
            .ToDictionary(p => p[0], p => double.Parse(p[1], CultureInfo.InvariantCulture));

    static string TempFile() => Path.Combine(Path.GetTempPath(), $"screenrecorder-test-{Guid.NewGuid():N}.mp4");

    static double FirstBrightFrame(string file)
    {
        var (stdout, _) = Run("ffmpeg", $"-hide_banner -i \"{file}\" -an -vf signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=- -f null -");
        double time = -1;
        foreach (var line in stdout.Split('\n'))
        {
            var t = Regex.Match(line, @"pts_time:([\d.]+)");
            if (t.Success) time = double.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
            var y = Regex.Match(line, @"YAVG=([\d.]+)");
            if (y.Success && double.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture) > 128) return time;
        }
        throw new InvalidOperationException("No bright frame found.");
    }

    static double FirstSound(string file)
    {
        var (_, stderr) = Run("ffmpeg", $"-hide_banner -i \"{file}\" -vn -af bandpass=f=15000:w=300,silencedetect=n=-40dB:d=0.2 -f null -");
        var m = Regex.Match(stderr, @"silence_end: ([\d.]+)");
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : throw new InvalidOperationException("No tone found.");
    }

    static (string stdout, string stderr) Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (stdout.Replace("\r", ""), stderr.Result.Replace("\r", ""));
    }

    /// <summary>Black topmost window over the top-left of the primary screen that can turn white on demand.</summary>
    sealed class Flasher() : TestForm(() => new Form
    {
        FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 800, 500), BackColor = Color.Black,
    })
    {
        public void White() => Invoke(f =>
        {
            f.BackColor = Color.White;
            f.Refresh();
        });
    }
}

/// <summary>
/// Tiny window excluded from capture that flips its color every 30 ms, like the app's mini bar: Windows Graphics
/// Capture only delivers frames when the screen changes, so recordings of an idle screen need it.
/// </summary>
sealed class Heartbeat() : TestForm(() =>
{
    var form = new Form
    {
        FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(1000, 600, 20, 20), BackColor = Color.Black,
    };
    form.HandleCreated += (_, _) => Native.ExcludeFromCapture(form.Handle);
    var timer = new System.Windows.Forms.Timer { Interval = 30, Enabled = true };
    timer.Tick += (_, _) => form.BackColor = form.BackColor.R == 0 ? Color.FromArgb(1, 1, 1) : Color.Black;
    form.FormClosed += (_, _) => timer.Dispose();
    return form;
});

/// <summary>
/// A WinForms window shown until disposed (per-monitor DPI aware = physical pixels). All test windows share one
/// long-lived STA UI thread: windows created after an earlier Application.Run had ended never appeared on screen.
/// </summary>
class TestForm : IDisposable
{
    static readonly Lazy<Control> Ui = new(() =>
    {
        using var ready = new ManualResetEventSlim();
        Control? marshal = null;
        var thread = new Thread(() =>
        {
            // Per thread, because the process-wide SetHighDpiMode is ignored once anything (e.g. an earlier unit
            // test) has touched DPI; physical pixels are what the app's own code works with.
            SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            marshal = new Control();
            marshal.CreateControl(); // gives Invoke a window handle on this thread
            ready.Set();
            Application.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return marshal!;
    });

    readonly Form form;

    public TestForm(Func<Form> create)
    {
        form = Ui.Value.Invoke(() =>
        {
            var f = create();
            f.Show();
            // Windows drops WS_EX_TOPMOST set at creation when a background process opens the window (e.g. while the
            // user works in another app); setting it again afterwards sticks.
            if (f.TopMost) SetWindowPos(f.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            f.Refresh();
            return f;
        });
    }

    static readonly IntPtr HWND_TOPMOST = -1, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public T Invoke<T>(Func<Form, T> action) => Ui.Value.Invoke(() => action(form));
    public void Invoke(Action<Form> action) => Ui.Value.Invoke(() => action(form));

    public void Dispose() => Ui.Value.Invoke(form.Close);
}

[Trait("Category", "Integration")]
[Collection("Desktop")] // these tests put windows on the real screen; run them one after another
public class ScreenCaptureTests
{
    [Fact]
    public void Screenshot_contains_what_is_on_screen_and_saves_as_png()
    {
        using var window = new TestForm(() => new Form
        {
            FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 300, 200), BackColor = Color.Red,
        });
        Thread.Sleep(500);
        var image = ScreenCapture.Grab(new PixelRect(20, 20, 200, 100));
        Assert.Equal((200, 100), (image.PixelWidth, image.PixelHeight));

        var pixels = new byte[200 * 100 * 4]; // BGRA
        image.CopyPixels(pixels, 200 * 4, 0);
        Assert.True(pixels[2] > 200 && pixels[1] < 60 && pixels[0] < 60, $"top-left pixel is not red: {pixels[2]},{pixels[1]},{pixels[0]}");

        var path = Path.Combine(Path.GetTempPath(), $"screenrecorder-test-{Guid.NewGuid():N}.png");
        try
        {
            ScreenCapture.SavePng(image, path);
            using var file = File.OpenRead(path);
            Assert.Equal(200, System.Windows.Media.Imaging.BitmapDecoder.Create(file, default, default).Frames[0].PixelWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

[Trait("Category", "Integration")]
[Collection("Desktop")] // these tests put windows on the real screen; run them one after another
public class ExcludeFromCaptureTests
{
    [Theory]
    [InlineData(Grabber.GraphicsCapture)]
    [InlineData(Grabber.DesktopDuplication)]
    public void Excluded_window_does_not_show_up_in_the_capture(Grabber grabber)
    {
        if (Recorder.FindFfmpeg() is not string ffmpeg || Displays.Enumerate().FirstOrDefault(d => d.IsPrimary) is not Display primary) return;
        var monitor = Native.Monitors().First(m => m.Bounds.X == 0 && m.Bounds.Y == 0);
        // Top-left 200x150 of the primary monitor, the corner the red test window covers.
        var source = grabber == Grabber.GraphicsCapture
            ? $"gfxcapture=hmonitor={monitor.Handle}:crop_right={monitor.Bounds.Width - 200}:crop_bottom={monitor.Bounds.Height - 150}"
            : $"ddagrab=output_idx={primary.OutputIndex}:video_size=200x150";
        using var window = new TestForm(() => new Form
        {
            FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 300, 200), BackColor = Color.Red,
        });
        Thread.Sleep(500);
        var before = AverageColor(ffmpeg, source);
        Assert.True(IsRed(before), $"control: the red window should be captured before it is excluded, got {before}");

        Assert.True(window.Invoke(f => Native.ExcludeFromCapture(f.Handle)));
        Thread.Sleep(500);
        var after = AverageColor(ffmpeg, source);
        Assert.False(IsRed(after), $"excluded window still appears in the capture: {after}");
    }

    static bool IsRed((int R, int G, int B) c) => c.R > 200 && c.G < 60 && c.B < 60;

    /// <summary>Grabs one frame from the lavfi source and averages it to one RGB pixel.</summary>
    static (int R, int G, int B) AverageColor(string ffmpeg, string source)
    {
        var psi = new ProcessStartInfo(ffmpeg,
            $"-hide_banner -loglevel error -f lavfi -i {source} -frames:v 1 " +
            "-vf hwdownload,format=bgra,scale=1:1:flags=area,format=rgb24 -f rawvideo -")
        { RedirectStandardOutput = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        var rgb = ms.ToArray();
        return rgb.Length == 3 ? (rgb[0], rgb[1], rgb[2]) : (-1, -1, -1);
    }
}

[Trait("Category", "Integration")]
[Collection("Desktop")] // these tests put windows on the real screen; run them one after another
public class WindowPickerTests
{
    [Fact]
    public void Suggests_inner_window_under_cursor_and_whole_window_on_title_bar()
    {
        using var window = new TestForm(() =>
        {
            var f = new Form { TopMost = true, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(200, 200, 700, 500) };
            f.Controls.Add(new Panel { Location = new Point(40, 30), Size = new Size(300, 200), BackColor = Color.SteelBlue });
            return f;
        });
        var (panel, outer, titleBar) = window.Invoke(f =>
        {
            var p = f.Controls[0];
            var title = f.PointToScreen(Point.Empty); // client origin; the title bar sits just above it
            return (p.RectangleToScreen(p.ClientRectangle), f.Bounds, new Point(title.X + 100, title.Y - 10));
        });
        Thread.Sleep(200); // let the window settle on screen

        // The test window belongs to this process, so nothing is ignored here.
        var inner = WindowPicker.RectAt(panel.X + panel.Width / 2, panel.Y + panel.Height / 2, ignoredProcessId: 0);
        Assert.Equal(new PixelRect(panel.X, panel.Y, panel.Width, panel.Height), inner);

        var whole = WindowPicker.RectAt(titleBar.X, titleBar.Y, ignoredProcessId: 0);
        Assert.NotNull(whole);
        var w = whole.Value;
        // Visible frame only: inside GetWindowRect's bounds, which include the invisible shadow border.
        Assert.True(w.X > outer.X && w.Right < outer.Right && w.Bottom < outer.Bottom, $"{w.X},{w.Y} {w} vs {outer}");
    }
}
