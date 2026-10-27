using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Screenrecorder;

/// <summary>Big 3-2-1 centered over the area that is about to be recorded.</summary>
public sealed class CountdownWindow : Window
{
    readonly TextBlock number = new()
    {
        FontSize = 140, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
        Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.9 },
    };

    CountdownWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = Top = -32000;
        Content = number;
        SourceInitialized += (_, _) => Native.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Counts down over the area; false when cancelled.</summary>
    public static async Task<bool> RunAsync(PixelRect area, CancellationToken cancel)
    {
        var window = new CountdownWindow();
        try
        {
            for (var i = 3; i >= 1; i--)
            {
                window.number.Text = i.ToString();
                if (i == 3) window.Show();
                window.UpdateLayout();
                var hwnd = new WindowInteropHelper(window).Handle;
                var size = Native.GetWindowRect(hwnd);
                Native.SetWindowRect(hwnd, size with { X = area.X + (area.Width - size.Width) / 2, Y = area.Y + (area.Height - size.Height) / 2 });
                await Task.Delay(1000, cancel);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            window.Close();
        }
    }
}
