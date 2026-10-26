using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Screenrecorder;

/// <summary>Movable, resizable frame whose transparent inside is the recording area.</summary>
public partial class RegionFrameWindow : Window
{
    const double BorderDip = 4, HeaderDip = 24;
    static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x88, 0xD8));
    static readonly Brush RecordingBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

    bool recording;

    public RegionFrameWindow()
    {
        InitializeComponent();
        LocationChanged += (_, _) => UpdateText();
        SizeChanged += (_, _) => UpdateText();
    }

    IntPtr Hwnd => new WindowInteropHelper(this).EnsureHandle();

    (int border, int header) Insets()
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return ((int)Math.Round(BorderDip * scale), (int)Math.Round(HeaderDip * scale));
    }

    /// <summary>Inside of the frame in physical pixels, rounded to even size.</summary>
    public PixelRect Region
    {
        get
        {
            var w = Native.GetWindowRect(Hwnd);
            var (b, h) = Insets();
            return Displays.Even(new PixelRect(w.X + b, w.Y + h + b, w.Width - 2 * b, w.Height - h - 2 * b));
        }
        set
        {
            var (b, h) = Insets();
            Native.SetWindowRect(Hwnd, new PixelRect(value.X - b, value.Y - h - b, value.Width + 2 * b, value.Height + h + 2 * b));
            UpdateText();
        }
    }

    public bool Recording
    {
        get => recording;
        set
        {
            recording = value;
            Frame.BorderBrush = value ? RecordingBrush : IdleBrush;
            ResizeMode = value ? ResizeMode.NoResize : ResizeMode.CanResize;
        }
    }

    void UpdateText() => SizeText.Text = Region.ToString();

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!recording) DragMove();
    }
}
