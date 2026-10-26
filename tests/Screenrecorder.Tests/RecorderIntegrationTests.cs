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

    static RecordOptions Options(IReadOnlyList<Display> displays, PixelRect rect) =>
        new(Displays.Locate(rect, displays), Encoder, 30, 80, ShowCursor: true, SystemAudio: true, Microphone: false);

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
            f.Refresh();
            return f;
        });
    }

    public T Invoke<T>(Func<Form, T> action) => Ui.Value.Invoke(() => action(form));
    public void Invoke(Action<Form> action) => Ui.Value.Invoke(() => action(form));

    public void Dispose() => Ui.Value.Invoke(form.Close);
}

[Trait("Category", "Integration")]
[Collection("Desktop")] // these tests put windows on the real screen; run them one after another
public class ExcludeFromCaptureTests
{
    [Fact]
    public void Excluded_window_does_not_show_up_in_desktop_duplication()
    {
        if (Recorder.FindFfmpeg() is not string ffmpeg || Displays.Enumerate().FirstOrDefault(d => d.IsPrimary) is not Display primary) return;
        using var window = new TestForm(() => new Form
        {
            FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 300, 200), BackColor = Color.Red,
        });
        Thread.Sleep(500);
        var before = AverageColor(ffmpeg, primary.OutputIndex);
        Assert.True(IsRed(before), $"control: the red window should be captured before it is excluded, got {before}");

        Assert.True(window.Invoke(f => Native.ExcludeFromCapture(f.Handle)));
        Thread.Sleep(500);
        var after = AverageColor(ffmpeg, primary.OutputIndex);
        Assert.False(IsRed(after), $"excluded window still appears in the capture: {after}");
    }

    static bool IsRed((int R, int G, int B) c) => c.R > 200 && c.G < 60 && c.B < 60;

    /// <summary>Grabs the 200x150 top-left corner of the monitor via ddagrab and averages it to one RGB pixel.</summary>
    static (int R, int G, int B) AverageColor(string ffmpeg, int outputIndex)
    {
        var psi = new ProcessStartInfo(ffmpeg,
            $"-hide_banner -loglevel error -f lavfi -i ddagrab=output_idx={outputIndex}:video_size=200x150 -frames:v 1 " +
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
