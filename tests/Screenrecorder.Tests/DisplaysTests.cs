using Screenrecorder;

namespace Screenrecorder.Tests;

public class DisplaysTests
{
    // Secondary monitor left of the primary (negative X), DXGI lists it first.
    static readonly Display Left = new(0, new PixelRect(-1920, 0, 1920, 1080));
    static readonly Display Primary = new(1, new PixelRect(0, 0, 2560, 1440));
    static readonly IReadOnlyList<Display> Layout = [Left, Primary];

    [Fact]
    public void Region_on_one_monitor_maps_to_its_output_index_and_local_offset()
    {
        var area = Displays.Locate(new PixelRect(-1800, 100, 1280, 720), Layout);
        Assert.Equal(new CaptureArea(0, new PixelRect(120, 100, 1280, 720)), area);
    }

    [Fact]
    public void Region_on_primary_uses_primary_output_index()
    {
        var area = Displays.Locate(new PixelRect(0, 0, 2560, 1440), Layout);
        Assert.Equal(new CaptureArea(1, new PixelRect(0, 0, 2560, 1440)), area);
    }

    [Fact]
    public void Region_spanning_monitors_falls_back_to_gdigrab_with_absolute_coordinates()
    {
        var area = Displays.Locate(new PixelRect(-100, 0, 400, 300), Layout);
        Assert.Null(area.OutputIndex);
        Assert.Equal(new PixelRect(-100, 0, 400, 300), area.Rect);
    }

    [Fact]
    public void Region_hanging_off_the_desktop_is_clamped()
    {
        var area = Displays.Locate(new PixelRect(2400, 1300, 400, 400), Layout);
        Assert.Null(area.OutputIndex);
        Assert.Equal(new PixelRect(2400, 1300, 160, 140), area.Rect);
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
