using System.Runtime.InteropServices;

namespace Screenrecorder;

public static class Native
{
    public const int WM_HOTKEY = 0x0312;
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11, MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    /// <summary>All monitors on any GPU, in physical pixels.</summary>
    public static IReadOnlyList<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref RECT r, IntPtr _) =>
        {
            list.Add(new MonitorInfo(m, new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Hides the window from every screen capture (Desktop Duplication, GDI, Graphics Capture). Windows 10 2004+.</summary>
    public static bool ExcludeFromCapture(IntPtr hwnd) => SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

    /// <summary>Work area (monitor minus taskbar) of the monitor that holds most of the rectangle.</summary>
    public static PixelRect WorkAreaFor(PixelRect r)
    {
        var rect = new RECT { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST), ref info);
        return new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

    /// <summary>Bounding box of all monitors (any adapter) in physical pixels.</summary>
    public static PixelRect VirtualScreen() =>
        new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    public static PixelRect GetWindowRect(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out RECT r);
        return new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static void SetWindowRect(IntPtr hwnd, PixelRect r) =>
        SetWindowPos(hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);

    public static (int X, int Y) CursorPos()
    {
        GetCursorPos(out POINT p);
        return (p.X, p.Y);
    }
}
