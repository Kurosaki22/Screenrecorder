using Screenrecorder;

namespace Screenrecorder.Tests;

public class DisplaysTests
{
    // Secondary monitor left of the primary (negative X), DXGI lists it first.
    static readonly Display Left = new(0, new PixelRect(-1920, 0, 1920, 1080));
    static readonly Display Primary = new(1, new PixelRect(0, 0, 2560, 1440));
    static readonly IReadOnlyList<Display> Layout = [Left, Primary];

    // Same layout as Windows sees it, plus a third monitor on another GPU that DXGI adapter 0 doesn't list.
    static readonly MonitorInfo LeftMonitor = new(11, Left.Bounds);
    static readonly MonitorInfo PrimaryMonitor = new(22, Primary.Bounds);
    static readonly MonitorInfo OtherGpu = new(33, new PixelRect(2560, 0, 2560, 1440));
    static readonly IReadOnlyList<MonitorInfo> Monitors = [LeftMonitor, PrimaryMonitor, OtherGpu];

    [Fact]
    public void Region_on_one_monitor_uses_graphics_capture_with_monitor_relative_offset()
    {
        var area = Displays.Locate(new PixelRect(-1800, 100, 1280, 720), Layout, Monitors);
        Assert.Equal(new CaptureArea(Grabber.GraphicsCapture, new PixelRect(120, 100, 1280, 720), Monitor: LeftMonitor), area);
    }

    [Fact]
    public void Graphics_capture_reaches_monitors_on_other_gpus()
    {
        var area = Displays.Locate(new PixelRect(2660, 100, 640, 360), Layout, Monitors);
        Assert.Equal(new CaptureArea(Grabber.GraphicsCapture, new PixelRect(100, 100, 640, 360), Monitor: OtherGpu), area);
    }

    [Fact]
    public void Without_graphics_capture_a_region_maps_to_its_ddagrab_output_index()
    {
        var area = Displays.Locate(new PixelRect(-1800, 100, 1280, 720), Layout);
        Assert.Equal(new CaptureArea(Grabber.DesktopDuplication, new PixelRect(120, 100, 1280, 720), OutputIndex: 0), area);
        area = Displays.Locate(new PixelRect(0, 0, 2560, 1440), Layout);
        Assert.Equal(new CaptureArea(Grabber.DesktopDuplication, new PixelRect(0, 0, 2560, 1440), OutputIndex: 1), area);
    }

    [Fact]
    public void Region_spanning_monitors_falls_back_to_gdigrab_with_absolute_coordinates()
    {
        var area = Displays.Locate(new PixelRect(-100, 0, 400, 300), Layout, Monitors);
        Assert.Equal(new CaptureArea(Grabber.Gdi, new PixelRect(-100, 0, 400, 300)), area);
    }

    [Fact]
    public void Region_hanging_off_the_desktop_is_clamped()
    {
        var area = Displays.Locate(new PixelRect(2400, 1300, 400, 400), Layout);
        Assert.Equal(new CaptureArea(Grabber.Gdi, new PixelRect(2400, 1300, 160, 140)), area);
    }

    [Fact]
    public void Odd_sizes_are_rounded_down_to_even()
    {
        Assert.Equal(new PixelRect(5, 7, 640, 360), Displays.Even(new PixelRect(5, 7, 641, 361)));
        Assert.Equal(new PixelRect(0, 0, 2, 2), Displays.Even(new PixelRect(0, 0, 1, 0)));
    }

    [Fact]
    public void Virtual_desktop_spans_all_monitors()
    {
        Assert.Equal(new PixelRect(-1920, 0, 4480, 1440), Displays.VirtualDesktop(Layout));
    }

    [Fact]
    public void Primary_is_the_monitor_at_origin()
    {
        Assert.True(Primary.IsPrimary);
        Assert.False(Left.IsPrimary);
    }
}
