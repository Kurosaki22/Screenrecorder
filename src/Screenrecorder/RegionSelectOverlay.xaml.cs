using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Screenrecorder;

/// <summary>
/// Dimmed full-desktop overlay. Hovering suggests the window (or inner window, e.g. a browser's page) under the
/// mouse and a click takes it; dragging draws a free rectangle. Esc cancels.
/// With windowsOnly, only whole top-level windows are offered (for window capture) and dragging is off.
/// </summary>
public partial class RegionSelectOverlay : Window
{
    const int ClickTolerancePx = 5;
    readonly PixelRect desktop = Native.VirtualScreen();
    readonly bool windowsOnly;
    (int X, int Y) startPx;
    PixelRect? suggestion;
    long suggestedWindow;

    public PixelRect? Result { get; private set; }

    /// <summary>The picked window's handle in windowsOnly mode, else 0.</summary>
    public long ResultWindow { get; private set; }

    public RegionSelectOverlay(bool windowsOnly = false)
    {
        this.windowsOnly = windowsOnly;
        InitializeComponent();
        SourceInitialized += (_, _) => Native.SetWindowRect(new WindowInteropHelper(this).Handle, desktop);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // Screen coordinates come from Win32 so mixed-DPI monitors don't skew the result.
        Root.MouseLeftButtonDown += (_, _) =>
        {
            startPx = Native.CursorPos();
            Root.CaptureMouse();
        };
        Root.MouseMove += (_, _) =>
        {
            var p = Native.CursorPos();
            if (Root.IsMouseCaptured && IsDrag(p) && !windowsOnly)
                Show(DragRect(p));
            else if (!Root.IsMouseCaptured && windowsOnly)
            {
                var hit = WindowPicker.WindowAt(p.X, p.Y);
                suggestedWindow = hit?.Hwnd ?? 0;
                Show(suggestion = hit?.Bounds);
            }
            else if (!Root.IsMouseCaptured)
                Show(suggestion = WindowPicker.RectAt(p.X, p.Y));
        };
        Root.MouseLeftButtonUp += (_, _) =>
        {
            if (!Root.IsMouseCaptured) return;
            Root.ReleaseMouseCapture();
            var end = Native.CursorPos();
            if (windowsOnly || !IsDrag(end))
            {
                Result = suggestion;
                ResultWindow = suggestion is null ? 0 : suggestedWindow;
            }
            else if (DragRect(end) is { Width: >= 16, Height: >= 16 } r)
                Result = r;
            Close();
        };
    }

    bool IsDrag((int X, int Y) p) =>
        Math.Abs(p.X - startPx.X) >= ClickTolerancePx || Math.Abs(p.Y - startPx.Y) >= ClickTolerancePx;

    PixelRect DragRect((int X, int Y) end) => Displays.Even(new PixelRect(Math.Min(startPx.X, end.X), Math.Min(startPx.Y, end.Y),
        Math.Abs(end.X - startPx.X), Math.Abs(end.Y - startPx.Y)));

    /// <summary>Draws a physical-pixel rectangle on the overlay.</summary>
    void Show(PixelRect? rect)
    {
        var visibility = rect is null ? Visibility.Collapsed : Visibility.Visible;
        Band.Visibility = SizeTag.Visibility = visibility;
        if (rect is not PixelRect r) return;

        // shortcut: one DPI scale for the whole overlay; on mixed-DPI setups the drawn box can be offset (the result is not).
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double left = (r.X - desktop.X) / scale, top = (r.Y - desktop.Y) / scale;
        Canvas.SetLeft(Band, left);
        Canvas.SetTop(Band, top);
        Band.Width = r.Width / scale;
        Band.Height = r.Height / scale;
        SizeText.Text = r.ToString();
        Canvas.SetLeft(SizeTag, left);
        Canvas.SetTop(SizeTag, Math.Max(0, top - 26));
    }
}
