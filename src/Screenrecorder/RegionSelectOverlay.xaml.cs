using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Screenrecorder;

/// <summary>Dimmed full-desktop overlay; drag a rectangle, Esc cancels.</summary>
public partial class RegionSelectOverlay : Window
{
    (int X, int Y) startPx;
    Point startLocal;

    public PixelRect? Result { get; private set; }

    public RegionSelectOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.SetWindowRect(new WindowInteropHelper(this).Handle, Native.VirtualScreen());
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Root.MouseLeftButtonDown += (_, e) =>
        {
            // Screen coordinates come from Win32 so mixed-DPI monitors don't skew the result.
            startPx = Native.CursorPos();
            startLocal = e.GetPosition(Root);
            Band.Visibility = Visibility.Visible;
            Root.CaptureMouse();
        };
        Root.MouseMove += (_, e) =>
        {
            if (!Root.IsMouseCaptured) return;
            var p = e.GetPosition(Root);
            Canvas.SetLeft(Band, Math.Min(p.X, startLocal.X));
            Canvas.SetTop(Band, Math.Min(p.Y, startLocal.Y));
            Band.Width = Math.Abs(p.X - startLocal.X);
            Band.Height = Math.Abs(p.Y - startLocal.Y);
        };
        Root.MouseLeftButtonUp += (_, _) =>
        {
            if (!Root.IsMouseCaptured) return;
            Root.ReleaseMouseCapture();
            var end = Native.CursorPos();
            var r = new PixelRect(Math.Min(startPx.X, end.X), Math.Min(startPx.Y, end.Y),
                Math.Abs(end.X - startPx.X), Math.Abs(end.Y - startPx.Y));
            if (r.Width >= 16 && r.Height >= 16) Result = Displays.Even(r);
            Close();
        };
    }
}
