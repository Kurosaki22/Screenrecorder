using Vortice.DXGI;

namespace Screenrecorder;

/// <summary>Rectangle in physical desktop pixels (virtual-desktop coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(PixelRect r) => r.X >= X && r.Y >= Y && r.Right <= Right && r.Bottom <= Bottom;
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>A monitor as seen by DXGI on adapter 0; OutputIndex is what ddagrab's output_idx expects.</summary>
public sealed record Display(int OutputIndex, PixelRect Bounds)
{
    public bool IsPrimary => Bounds.X == 0 && Bounds.Y == 0;
}

/// <summary>What to grab: OutputIndex null means gdigrab over the virtual desktop with absolute coordinates.</summary>
public readonly record struct CaptureArea(int? OutputIndex, PixelRect Rect);

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

    public static PixelRect VirtualDesktop(IReadOnlyList<Display> displays)
    {
        int l = displays.Min(d => d.Bounds.X), t = displays.Min(d => d.Bounds.Y);
        int r = displays.Max(d => d.Bounds.Right), b = displays.Max(d => d.Bounds.Bottom);
        return new PixelRect(l, t, r - l, b - t);
    }

    /// <summary>yuv420 encoders need even dimensions.</summary>
    public static PixelRect Even(PixelRect r) => r with { Width = Math.Max(2, r.Width & ~1), Height = Math.Max(2, r.Height & ~1) };

    /// <summary>Region fully on one monitor → ddagrab (GPU), otherwise gdigrab clamped to the desktop.</summary>
    public static CaptureArea Locate(PixelRect region, IReadOnlyList<Display> displays)
    {
        region = Even(region);
        foreach (var d in displays)
            if (d.Bounds.Contains(region))
                return new CaptureArea(d.OutputIndex, region with { X = region.X - d.Bounds.X, Y = region.Y - d.Bounds.Y });

        var desk = VirtualDesktop(displays);
        int x = Math.Clamp(region.X, desk.X, desk.Right - 2), y = Math.Clamp(region.Y, desk.Y, desk.Bottom - 2);
        return new CaptureArea(null, Even(new PixelRect(x, y,
            Math.Min(region.Right, desk.Right) - x, Math.Min(region.Bottom, desk.Bottom) - y)));
    }
}
