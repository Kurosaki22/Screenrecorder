namespace Screenrecorder;

/// <summary>Where the mini control bar goes relative to the framed area (all in physical pixels).</summary>
public static class MiniBarPlacement
{
    /// <summary>Below-left of the anchor if it fits, else above-left, else inside at the bottom-left.</summary>
    public static (int X, int Y) Place(PixelRect anchor, int barWidth, int barHeight, PixelRect workArea, int gap)
    {
        int y;
        if (anchor.Bottom + gap + barHeight <= workArea.Bottom)
            y = anchor.Bottom + gap;
        else if (anchor.Y - gap - barHeight >= workArea.Y)
            y = anchor.Y - gap - barHeight;
        else
            y = Math.Min(anchor.Bottom, workArea.Bottom) - gap - barHeight; // full screen: the bar is excluded from capture anyway

        // Left-aligned with the area, but never past the work area's edges.
        var x = Math.Max(workArea.X, Math.Min(anchor.X, workArea.Right - barWidth));
        return (x, Math.Max(workArea.Y, y));
    }
}
