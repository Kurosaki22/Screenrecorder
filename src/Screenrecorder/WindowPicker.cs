using System.Runtime.InteropServices;

namespace Screenrecorder;

/// <summary>Suggests a capture area: the deepest visible window under a screen point (e.g. a browser's page area).</summary>
public static class WindowPicker
{
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    // No CWP_SKIPTRANSPARENT: Chromium marks its page area (Chrome_RenderWidgetHostHWND) WS_EX_TRANSPARENT.
    const uint CWP_SKIPINVISIBLE = 0x1;
    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr parent, POINT p, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    /// <summary>
    /// Screen rectangle (physical pixels) of the deepest visible window at (x, y), ignoring windows of
    /// <paramref name="ignoredProcessId"/> (default: this app, so its overlay and frame don't get picked).
    /// </summary>
    public static PixelRect? RectAt(int x, int y, int? ignoredProcessId = null)
    {
        var top = TopLevelAt(x, y, (uint)(ignoredProcessId ?? Environment.ProcessId));
        if (top == IntPtr.Zero) return null;

        var hwnd = top;
        while (true)
        {
            var p = new POINT { X = x, Y = y };
            ScreenToClient(hwnd, ref p);
            var child = ChildWindowFromPointEx(hwnd, p, CWP_SKIPINVISIBLE);
            if (child == IntPtr.Zero || child == hwnd) break;
            hwnd = child;
        }

        var rect = (hwnd == top ? TopLevelBounds(hwnd) : WindowBounds(hwnd)).Intersect(Native.VirtualScreen());
        return rect is PixelRect v ? Displays.Even(v) : null;
    }

    /// <summary>The whole top-level window at (x, y) (not this app's), for window capture.</summary>
    public static (long Hwnd, PixelRect Bounds)? WindowAt(int x, int y)
    {
        var top = TopLevelAt(x, y, (uint)Environment.ProcessId);
        return top == IntPtr.Zero ? null : (top, TopLevelBounds(top));
    }

    /// <summary>Visible bounds of a top-level window (without the invisible shadow border).</summary>
    public static PixelRect Bounds(long hwnd) => TopLevelBounds((IntPtr)hwnd);

    static IntPtr TopLevelAt(int x, int y, uint ignoredPid)
    {
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ignoredPid || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            // Click-through overlays (e.g. GPU driver overlays) cover the screen but are not what the user sees.
            if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0) return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (!TopLevelBounds(hwnd).Contains(x, y)) return true;
            found = hwnd;
            return false; // EnumWindows walks top to bottom, so the first hit is the visible one
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>DWM bounds exclude the invisible ~7 px resize/shadow border that GetWindowRect includes.</summary>
    static PixelRect TopLevelBounds(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0 ? ToRect(r) : WindowBounds(hwnd);

    static PixelRect WindowBounds(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return ToRect(r);
    }

    static PixelRect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
