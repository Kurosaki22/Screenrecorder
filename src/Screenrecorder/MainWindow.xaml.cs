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
    const int HotkeyRecordId = 1, HotkeyPauseId = 2, HotkeySelectAreaId = 3;
    static readonly string[] Presets16x9 = ["640x360", "800x450", "1024x576"];
    static readonly string[] Presets4x3 = ["640x480", "800x600", "1024x768"];
    static readonly string[] PresetsYoutube = ["1280x720", "1920x1080"];

    Settings settings;
    readonly RegionFrameWindow frame = new();
    readonly MiniBar miniBar = new();
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    string? ffmpeg;
    List<VideoEncoder>? encoders;
    RecordingSession? session;
    bool busy, minimizedForRecording;

    public MainWindow(Settings settings)
    {
        this.settings = settings;
        InitializeComponent();
        statusTimer.Tick += (_, _) => UpdateStatus();
        Loaded += OnLoaded;
    }

    IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // No Owner for frame and mini bar: WPF hides owned windows while the owner is minimized, and the main window
        // minimizes itself when it overlaps the recorded area. App.xaml's ShutdownMode closes them with the main window.
        frame.Show();
        frame.Region = OnScreen(settings.Region);
        ApplyAreaMode();
        miniBar.PauseClicked += TogglePause;
        miniBar.StopClicked += ToggleRecording;

        HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
        RegisterHotkeys();

        // Record stays disabled (XAML) until a working encoder is known.
        Title = Strings.DetectingCodecs;
        ffmpeg = await Task.Run(Recorder.FindFfmpeg); // may unpack the embedded ffmpeg on first start
        if (ffmpeg is null)
        {
            Title = "Screenrecorder";
            MessageBox.Show(this, Strings.FfmpegMissing, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        encoders = await Encoders.DetectAsync(ffmpeg);
        Title = "Screenrecorder";
        if (encoders.Count == 0)
        {
            MessageBox.Show(this, Strings.NoCodecs, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (!encoders.Any(x => x.Name == settings.Encoder))
            settings.Encoder = encoders[0].Name;
        RecordButton.IsEnabled = true;
    }

    // ---------- Hotkeys ----------

    /// <summary>(Re)registers all hotkeys from the settings and reports keys another program already owns.</summary>
    void RegisterHotkeys()
    {
        UnregisterHotkeys();
        var taken = new List<string>();
        foreach (var (id, text, hint) in new[]
        {
            (HotkeyRecordId, settings.HotkeyRecord, RecordHotkey),
            (HotkeyPauseId, settings.HotkeyPause, PauseHotkey),
            (HotkeySelectAreaId, settings.HotkeySelectArea, AreaHotkey),
        })
        {
            hint.Text = "";
            if (!Hotkey.TryParse(text, out var modifiers, out var key)) continue;
            var (mod, vk) = Hotkey.ToWin32(modifiers, key);
            if (Native.RegisterHotKey(Hwnd, id, mod, vk))
                hint.Text = Hotkey.Format(modifiers, key);
            else
                taken.Add(Hotkey.Format(modifiers, key));
        }
        if (taken.Count > 0)
            MessageBox.Show(this, string.Format(Strings.HotkeyTaken, string.Join(", ", taken)), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_HOTKEY) return IntPtr.Zero;
        switch ((int)wParam)
        {
            case HotkeyRecordId when RecordButton.IsEnabled: ToggleRecording(); break;
            case HotkeyPauseId when session is not null: TogglePause(); break;
            case HotkeySelectAreaId when session is null && IsEnabled: SelectArea(); break;
            default: return IntPtr.Zero;
        }
        handled = true;
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

    void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();

    async void ToggleRecording()
    {
        if (busy || ffmpeg is null || encoders is null) return;
        busy = true;
        try
        {
            if (session is not null)
                await StopRecording();
            else
                StartRecording();
        }
        finally
        {
            busy = false;
        }
    }

    async void TogglePause()
    {
        if (busy || session is null) return;
        busy = true;
        try
        {
            if (session.IsPaused)
                session.Resume();
            else
                await session.PauseAsync();
            UpdatePauseUi();
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message);
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

        var options = new RecordOptions(Displays.Locate(rect, displays), encoders!.First(x => x.Name == settings.Encoder),
            settings.Fps, settings.Quality, settings.ShowCursor, settings.SystemAudio, settings.Microphone);
        try
        {
            Directory.CreateDirectory(settings.OutputFolder);
            var path = FileNamer.Unique(settings.OutputFolder,
                FileNamer.Format(settings.FileNamePattern, DateTime.Now, settings.NextNumber));
            session = RecordingSession.Start(ffmpeg!, options, path);
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message);
            return;
        }
        settings.NextNumber++;
        var started = session;
        started.Failed += message => Dispatcher.Invoke(async () =>
        {
            if (session != started) return;
            session = null;
            SetRecordingUi(false);
            ShowFailure(message);
            await started.StopAsync(); // keep whatever segments were finished
        });
        SetRecordingUi(true);
        // Dock the mini bar to what is visibly framed: the frame window incl. its size label, or the recorded screen.
        miniBar.ShowAt(settings.Area == AreaMode.Region ? Native.GetWindowRect(new WindowInteropHelper(frame).Handle) : rect);
        // Get out of the picture, but only if we are in it (the window rect includes the shadow, so this errs on the safe side).
        if (rect.Intersect(Native.GetWindowRect(Hwnd)) is not null)
        {
            minimizedForRecording = true;
            WindowState = WindowState.Minimized;
        }
    }

    async Task StopRecording()
    {
        var s = session!;
        session = null;
        SetRecordingUi(false);
        if (await s.StopAsync() is string error) ShowFailure(error);
    }

    void ShowFailure(string details) =>
        MessageBox.Show(this, $"{Strings.RecordingFailed}{Environment.NewLine}{details}", Title, MessageBoxButton.OK, MessageBoxImage.Error);

    void SetRecordingUi(bool on)
    {
        RecordGlyph.Text = on ? "" : "";
        RecordText.Text = on ? Strings.Stop : Strings.Record;
        // Pause stays visible (only disabled) so the window never changes size; status lives in the title.
        PauseButton.IsEnabled = on;
        AreaButton.IsEnabled = CodecsButton.IsEnabled = SoundButton.IsEnabled = SettingsButton.IsEnabled = !on;
        frame.Recording = on;
        if (on)
        {
            UpdatePauseUi();
            statusTimer.Start();
        }
        else
        {
            statusTimer.Stop();
            miniBar.Hide();
            Title = "Screenrecorder";
            PauseGlyph.Text = "";
            PauseText.Text = Strings.Pause;
            if (minimizedForRecording)
            {
                minimizedForRecording = false;
                WindowState = WindowState.Normal;
                Activate();
            }
        }
    }

    void UpdatePauseUi()
    {
        var paused = session?.IsPaused == true;
        PauseGlyph.Text = paused ? "" : "";
        PauseText.Text = paused ? Strings.Resume : Strings.Pause;
        frame.Paused = paused;
        UpdateStatus();
    }

    void UpdateStatus()
    {
        if (session is null) return;
        var mb = session.BytesWritten / (1024.0 * 1024.0);
        var (dot, label) = session.IsPaused ? ("⏸ ", Strings.Paused) : ("● ", Strings.Recording);
        var text = $"{label} {session.Elapsed:hh\\:mm\\:ss} · {mb:0.0} MB";
        Title = dot + text;
        miniBar.Update(text, session.IsPaused,
            WithHotkey(session.IsPaused ? Strings.Resume : Strings.Pause, PauseHotkey.Text), WithHotkey(Strings.Stop, RecordHotkey.Text));
    }

    static string WithHotkey(string label, string hotkey) => hotkey == "" ? label : $"{label} ({hotkey})";

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (session is not null)
        {
            e.Cancel = true; // finish the file first, then close for real
            await StopRecording();
            Close();
            return;
        }
        settings.Region = frame.Region;
        settings.Save();
        UnregisterHotkeys();
    }

    void UnregisterHotkeys()
    {
        foreach (var id in new[] { HotkeyRecordId, HotkeyPauseId, HotkeySelectAreaId })
            Native.UnregisterHotKey(Hwnd, id);
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
        // The Fluent theme only draws the check mark on checkable items; menus are rebuilt on every open anyway.
        var item = new MenuItem { Header = header, IsCheckable = isChecked, IsChecked = isChecked, IsEnabled = enabled };
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
        var select = Item(Strings.SelectArea, SelectArea);
        select.InputGestureText = AreaHotkey.Text;
        items.Add(select);
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
        Directory.CreateDirectory(settings.OutputFolder);
        Process.Start("explorer.exe", settings.OutputFolder);
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Global hotkeys would swallow the keys the user wants to assign, so release them while the dialog is open.
        UnregisterHotkeys();
        var dialog = new SettingsWindow(settings.Clone(), ffmpeg) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            settings = dialog.Result;
            settings.Region = frame.Region;
            settings.Save();
            App.ApplyTheme(settings.Theme);
        }
        RegisterHotkeys();
    }
}
