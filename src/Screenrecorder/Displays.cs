using Vortice.DXGI;

namespace Screenrecorder;

/// <summary>Rectangle in physical desktop pixels (virtual-desktop coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(PixelRect r) => r.X >= X && r.Y >= Y && r.Right <= Right && r.Bottom <= Bottom;
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public PixelRect? Intersect(PixelRect o)
    {
        int l = Math.Max(X, o.X), t = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r > l && b > t ? new PixelRect(l, t, r - l, b - t) : null;
    }
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>A monitor as seen by DXGI on adapter 0; OutputIndex is what ddagrab's output_idx expects.</summary>
public sealed record Display(int OutputIndex, PixelRect Bounds)
{
    public bool IsPrimary => Bounds.X == 0 && Bounds.Y == 0;
}

/// <summary>A monitor as Windows sees it (any GPU); Handle is the HMONITOR that gfxcapture takes.</summary>
public sealed record MonitorInfo(long Handle, PixelRect Bounds);

public enum Grabber
{
    /// <summary>Windows Graphics Capture (gfxcapture): GPU, real frame timestamps, any GPU's monitor. Smoothest.</summary>
    GraphicsCapture,
    /// <summary>Desktop Duplication (ddagrab): GPU, adapter 0 only. Fallback when gfxcapture is unavailable.</summary>
    DesktopDuplication,
    /// <summary>GDI (gdigrab): CPU, the only one that can span several monitors.</summary>
    Gdi,
}

/// <summary>
/// What to grab. Rect is relative to the monitor for GraphicsCapture/DesktopDuplication and absolute
/// (virtual desktop) for Gdi. OutputIndex is ddagrab's output_idx, Monitor is set for GraphicsCapture.
/// Window (an HWND) makes GraphicsCapture follow that window instead of a monitor area.
/// </summary>
public readonly record struct CaptureArea(Grabber Grabber, PixelRect Rect, int OutputIndex = 0, MonitorInfo? Monitor = null, long Window = 0);

public static class Displays
{
    public static IReadOnlyList<Display> Enumerate()
    {
        // shortcut: only adapter 0 (the one ddagrab uses by default); monitors on other GPUs fall back to gdigrab.
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var list = new List<Display>();
        if (factory.EnumAdapters1(0, out var adapter).Failure) return list;
        using (adapter)
        {
            for (uint i = 0; adapter.EnumOutputs(i, out var output).Success; i++)
                using (output)
                {
                    var r = output.Description.DesktopCoordinates;
                    list.Add(new Display((int)i, new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
                }
        }
        return list;
    }

    /// <summary>
    /// Vendor of DXGI adapter 0, where ddagrab and gfxcapture create their frames ("NVIDIA", "AMD", "Intel"
    /// as in VideoEncoder.Vendor; "" if unknown).
    /// </summary>
    public static string CaptureGpuVendor()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        if (factory.EnumAdapters1(0, out var adapter).Failure) return "";
        using (adapter)
            return adapter.Description1.VendorId switch { 0x10DE => "NVIDIA", 0x1002 => "AMD", 0x8086 => "Intel", _ => "" };
    }

    public static PixelRect VirtualDesktop(IReadOnlyList<Display> displays)
    {
        int l = displays.Min(d => d.Bounds.X), t = displays.Min(d => d.Bounds.Y);
        int r = displays.Max(d => d.Bounds.Right), b = displays.Max(d => d.Bounds.Bottom);
        return new PixelRect(l, t, r - l, b - t);
    }

    /// <summary>yuv420 encoders need even dimensions.</summary>
    public static PixelRect Even(PixelRect r) => r with { Width = Math.Max(2, r.Width & ~1), Height = Math.Max(2, r.Height & ~1) };

    /// <summary>
    /// Region fully on one monitor → gfxcapture when available (pass its monitors), else ddagrab;
    /// spanning monitors → gdigrab clamped to the desktop.
    /// </summary>
    public static CaptureArea Locate(PixelRect region, IReadOnlyList<Display> displays, IReadOnlyList<MonitorInfo>? graphicsCaptureMonitors = null)
    {
        region = Even(region);
        foreach (var m in graphicsCaptureMonitors ?? [])
            if (m.Bounds.Contains(region))
                return new CaptureArea(Grabber.GraphicsCapture, Relative(region, m.Bounds), Monitor: m);
        foreach (var d in displays)
            if (d.Bounds.Contains(region))
                return new CaptureArea(Grabber.DesktopDuplication, Relative(region, d.Bounds), d.OutputIndex);

        var desk = VirtualDesktop(displays);
        int x = Math.Clamp(region.X, desk.X, desk.Right - 2), y = Math.Clamp(region.Y, desk.Y, desk.Bottom - 2);
        return new CaptureArea(Grabber.Gdi, Even(new PixelRect(x, y,
            Math.Min(region.Right, desk.Right) - x, Math.Min(region.Bottom, desk.Bottom) - y)));
    }

    static PixelRect Relative(PixelRect r, PixelRect monitor) => r with { X = r.X - monitor.X, Y = r.Y - monitor.Y };
}
