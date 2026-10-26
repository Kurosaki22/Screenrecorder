using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Screenrecorder;

/// <summary>Small recording status/control bar docked to the capture area; never shows up in recordings.</summary>
public partial class MiniBar : Window
{
    const int GapPx = 6;
    static readonly Brush RecordingBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    static readonly Brush PausedBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0x8C, 0x00));

    static readonly Brush[] HeartbeatBrushes =
        [new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x1E, 0x1E)), new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x1E, 0x1F))];

    readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromMilliseconds(30) };
    int beat;

    public event Action? PauseClicked, StopClicked;

    public MiniBar()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        // Windows Graphics Capture only delivers frames when something on the monitor changes, so on an idle screen a
        // recording would neither start nor end on time. An invisible 1-step color flip of this bar (which is itself
        // excluded from the capture) keeps frames coming; it is on the recorded monitor while recording.
        heartbeat.Tick += (_, _) => Panel.Background = HeartbeatBrushes[++beat % 2];
        IsVisibleChanged += (_, _) => heartbeat.IsEnabled = IsVisible;
    }

    public void Update(string status, bool paused, string pauseTooltip, string stopTooltip)
    {
        StatusText.Text = status;
        StateGlyph.Text = paused ? "" : ""; // pause bars / filled dot
        StateGlyph.Foreground = paused ? PausedBrush : RecordingBrush;
        PauseButton.Content = paused ? "" : "";
        PauseButton.ToolTip = pauseTooltip;
        StopButton.ToolTip = stopTooltip;
    }

    /// <summary>Shows the bar below-left of the anchor (screen pixels), or above/inside when there is no room.</summary>
    public void ShowAt(PixelRect anchor)
    {
        Show(); // lays out first so the physical size is known
        var hwnd = new WindowInteropHelper(this).Handle;
        var size = Native.GetWindowRect(hwnd);
        var (x, y) = MiniBarPlacement.Place(anchor, size.Width, size.Height, Native.WorkAreaFor(anchor), GapPx);
        Native.SetWindowRect(hwnd, size with { X = x, Y = y });
    }

    void Pause_Click(object sender, RoutedEventArgs e) => PauseClicked?.Invoke();

    void Stop_Click(object sender, RoutedEventArgs e) => StopClicked?.Invoke();
}
