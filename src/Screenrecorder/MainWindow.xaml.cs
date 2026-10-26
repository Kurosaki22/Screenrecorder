using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Screenrecorder.Resources;

namespace Screenrecorder;

public partial class MainWindow : Window
{
    const int HotkeyId = 1;
    const uint MOD_NOREPEAT = 0x4000;
    static readonly string[] Presets16x9 = ["640x360", "800x450", "1024x576"];
    static readonly string[] Presets4x3 = ["640x480", "800x600", "1024x768"];
    static readonly string[] PresetsYoutube = ["1280x720", "1920x1080"];

    readonly Settings settings;
    readonly RegionFrameWindow frame = new();
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Stopwatch recordingClock = new();
    string? ffmpeg;
    List<VideoEncoder>? encoders;
    Recorder? recorder;
    bool busy;

    public MainWindow(Settings settings)
    {
        this.settings = settings;
        InitializeComponent();
        statusTimer.Tick += (_, _) => UpdateStatus();
        Loaded += OnLoaded;
    }

    async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        frame.Owner = this;
        frame.Show();
        frame.Region = OnScreen(settings.Region);
        ApplyAreaMode();

        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        Native.RegisterHotKey(hwnd, HotkeyId, MOD_NOREPEAT, Native.VK_F2);

        ffmpeg = Recorder.FindFfmpeg();
        if (ffmpeg is null)
        {
            RecordButton.IsEnabled = false;
            MessageBox.Show(this, Strings.FfmpegMissing, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        encoders = await Encoders.DetectAsync(ffmpeg);
        if (encoders.Count == 0)
        {
            RecordButton.IsEnabled = false;
            MessageBox.Show(this, Strings.NoCodecs, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (!encoders.Any(x => x.Name == settings.Encoder))
            settings.Encoder = encoders[0].Name;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam == HotkeyId && RecordButton.IsEnabled)
        {
            ToggleRecording();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>A saved region on a monitor that is gone gets moved to the primary screen.</summary>
    static PixelRect OnScreen(PixelRect r)
    {
        var displays = Displays.Enumerate();
        if (displays.Count == 0 || displays.Any(d => d.Bounds.Contains(r))) return r;
        var p = displays.FirstOrDefault(d => d.IsPrimary)?.Bounds ?? displays[0].Bounds;
        return new PixelRect(p.X + 100, p.Y + 100, Math.Min(r.Width, p.Width - 200), Math.Min(r.Height, p.Height - 200));
    }

    void ApplyAreaMode() => frame.Visibility = settings.Area == AreaMode.Region ? Visibility.Visible : Visibility.Hidden;

    // ---------- Recording ----------

    void Record_Click(object sender, RoutedEventArgs e) => ToggleRecording();

    async void ToggleRecording()
    {
        if (busy || ffmpeg is null || encoders is null) return;
        busy = true;
        try
        {
            if (recorder is not null)
            {
                var r = recorder;
                recorder = null;
                await r.StopAsync();
                SetRecordingUi(false);
            }
            else
                StartRecording();
        }
        finally
        {
            busy = false;
        }
    }

    void StartRecording()
    {
        var displays = Displays.Enumerate();
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
        PixelRect rect;
        switch (settings.Area)
        {
            case AreaMode.PrimaryScreen: rect = primary.Bounds; break;
            case AreaMode.SecondaryScreen:
                var second = displays.FirstOrDefault(d => !d.IsPrimary);
                if (second is null)
                {
                    MessageBox.Show(this, Strings.NoSecondScreen, Title);
                    return;
                }
                rect = second.Bounds;
                break;
            case AreaMode.AllScreens: rect = Displays.VirtualDesktop(displays); break;
            default: rect = frame.Region; break;
        }

        var encoder = encoders!.First(x => x.Name == settings.Encoder);
        var path = Path.Combine(Settings.OutputFolder, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");
        try
        {
            recorder = Recorder.Start(ffmpeg!, Displays.Locate(rect, displays), encoder, settings.Fps,
                settings.SystemAudio, settings.Microphone, path);
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message);
            return;
        }
        var started = recorder;
        started.Failed += message => Dispatcher.Invoke(() =>
        {
            if (recorder != started) return;
            recorder = null;
            SetRecordingUi(false);
            ShowFailure(message);
        });
        SetRecordingUi(true);
    }

    void ShowFailure(string details) =>
        MessageBox.Show(this, $"{Strings.RecordingFailed}{Environment.NewLine}{details}", Title, MessageBoxButton.OK, MessageBoxImage.Error);

    void SetRecordingUi(bool on)
    {
        RecordGlyph.Text = on ? "" : "";
        RecordText.Text = on ? Strings.Stop : Strings.Record;
        AreaButton.IsEnabled = CodecsButton.IsEnabled = SoundButton.IsEnabled = MenuButton.IsEnabled = !on;
        frame.Recording = on;
        if (on)
        {
            recordingClock.Restart();
            statusTimer.Start();
        }
        else
        {
            statusTimer.Stop();
            Title = "Screenrecorder";
        }
    }

    void UpdateStatus()
    {
        if (recorder is null) return;
        var file = new FileInfo(recorder.OutputPath);
        var mb = file.Exists ? file.Length / (1024.0 * 1024.0) : 0;
        Title = $"● {recordingClock.Elapsed:hh\\:mm\\:ss}  {mb:0.0} MB";
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (recorder is not null)
        {
            e.Cancel = true; // finish the file first, then close for real
            var r = recorder;
            recorder = null;
            await r.StopAsync();
            Close();
            return;
        }
        settings.Region = frame.Region;
        settings.Save();
        Native.UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
    }

    // ---------- Menus ----------

    void ShowMenu(Button anchor, IEnumerable<object> items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var item in items) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    static MenuItem Item(string header, Action onClick, bool isChecked = false, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked, IsEnabled = enabled };
        item.Click += (_, _) => onClick();
        return item;
    }

    void Area_Click(object sender, RoutedEventArgs e)
    {
        var current = frame.Region.ToString();
        MenuItem SizeItem(string size) => Item(size, () => SetRegionSize(size), settings.Area == AreaMode.Region && size == current);

        var youtube = new MenuItem { Header = "YouTube" };
        foreach (var s in PresetsYoutube) youtube.Items.Add(SizeItem(s));

        var items = new List<object> { youtube };
        items.AddRange(Presets16x9.Select(SizeItem));
        items.Add(new Separator());
        items.AddRange(Presets4x3.Select(SizeItem));
        if (settings.CustomSizes.Count > 0)
        {
            items.Add(new Separator());
            items.AddRange(settings.CustomSizes.Select(SizeItem));
        }
        items.Add(new Separator());
        items.Add(Item(Strings.AllScreens, () => SetArea(AreaMode.AllScreens), settings.Area == AreaMode.AllScreens));
        items.Add(Item(Strings.PrimaryScreen, () => SetArea(AreaMode.PrimaryScreen), settings.Area == AreaMode.PrimaryScreen));
        items.Add(Item(Strings.SecondaryScreen, () => SetArea(AreaMode.SecondaryScreen), settings.Area == AreaMode.SecondaryScreen));
        items.Add(new Separator());
        items.Add(Item(Strings.SelectArea, SelectArea));
        items.Add(Item(Strings.ResizeArea, () =>
        {
            var text = InputDialog.Ask(this, Strings.ResizeArea, Strings.SizePrompt, current);
            if (text is not null) SetRegionSize(text);
        }));
        items.Add(new Separator());
        items.Add(Item(Strings.AddArea, () =>
        {
            if (!settings.CustomSizes.Contains(current)) settings.CustomSizes.Add(current);
        }));
        items.Add(Item(Strings.EditAreas, EditCustomSizes));
        ShowMenu(AreaButton, items);
    }

    void SetArea(AreaMode mode)
    {
        settings.Area = mode;
        ApplyAreaMode();
    }

    void SetRegionSize(string text)
    {
        if (!Settings.TryParseSize(text, out var w, out var h))
        {
            MessageBox.Show(this, string.Format(Strings.InvalidSize, text), Title);
            return;
        }
        frame.Region = frame.Region with { Width = w, Height = h };
        SetArea(AreaMode.Region);
    }

    void SelectArea()
    {
        var overlay = new RegionSelectOverlay();
        overlay.ShowDialog();
        if (overlay.Result is PixelRect r)
        {
            frame.Region = r;
            SetArea(AreaMode.Region);
        }
    }

    void EditCustomSizes()
    {
        var text = InputDialog.Ask(this, Strings.EditAreas, Strings.EditAreasPrompt,
            string.Join(Environment.NewLine, settings.CustomSizes), multiline: true);
        if (text is null) return;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalid = lines.FirstOrDefault(l => !Settings.TryParseSize(l, out _, out _));
        if (invalid is not null)
        {
            MessageBox.Show(this, string.Format(Strings.InvalidSize, invalid), Title);
            return;
        }
        settings.CustomSizes = lines.Select(l =>
        {
            Settings.TryParseSize(l, out var w, out var h);
            return $"{w}x{h}";
        }).Distinct().ToList();
    }

    void Codecs_Click(object sender, RoutedEventArgs e)
    {
        var items = new List<object>();
        if (encoders is null)
            items.Add(Item(Strings.DetectingCodecs, () => { }, enabled: false));
        else
            foreach (var group in encoders.GroupBy(x => x.Vendor))
            {
                if (items.Count > 0) items.Add(new Separator());
                items.Add(Item(group.Key, () => { }, enabled: false));
                items.AddRange(group.Select(x => Item(x.Label, () => settings.Encoder = x.Name, x.Name == settings.Encoder)));
            }
        items.Add(new Separator());
        foreach (var fps in new[] { 30, 60 })
            items.Add(Item($"{fps} fps", () => settings.Fps = fps, settings.Fps == fps));
        ShowMenu(CodecsButton, items);
    }

    void Sound_Click(object sender, RoutedEventArgs e) => ShowMenu(SoundButton,
    [
        Item(Strings.SystemSound, () => settings.SystemAudio = !settings.SystemAudio, settings.SystemAudio),
        Item(Strings.Microphone, () => settings.Microphone = !settings.Microphone, settings.Microphone),
    ]);

    void Open_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Settings.OutputFolder);
        Process.Start("explorer.exe", Settings.OutputFolder);
    }

    void Menu_Click(object sender, RoutedEventArgs e)
    {
        var language = new MenuItem { Header = Strings.Language };
        foreach (var (code, name) in new[] { ("", Strings.LanguageSystem), ("de", "Deutsch"), ("en", "English") })
            language.Items.Add(Item(name, () =>
            {
                settings.Language = code;
                MessageBox.Show(this, Strings.RestartHint, Title);
            }, settings.Language == code));
        ShowMenu(MenuButton, [language, new Separator(), Item(Strings.Exit, Close)]);
    }
}
