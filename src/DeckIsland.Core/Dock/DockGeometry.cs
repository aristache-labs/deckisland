namespace DeckIsland.Core;

// A rectangle in device pixels, edges as Win32 gives them.
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

// Where DeckIsland Dock goes on a screen. The strip is the band the shell reserves for it (the AppBar); the bar is the visible
// window, centred in the strip, so clicks beside it reach the desktop.
public static class DockGeometry
{
    public const int MinBarWidth = 120, SideMargin = 8;

    // The strip around a bar of this height: 46 -> 52, 52 -> 60, 62 -> 72, as lumabar's Small, Medium and Large.
    public static int StripHeight(int barHeight) => barHeight + (int)Math.Round((barHeight - 22) / 4.0);

    // The strip on the edge of a screen, the screen's own rectangle in device pixels.
    public static PixelRect Strip(PixelRect screen, DockEdge edge, int stripHeight) => edge == DockEdge.Top
        ? screen with { Bottom = screen.Top + stripHeight }
        : screen with { Top = screen.Bottom - stripHeight };

    // The strip the shell grants may differ from the one asked for; the dock keeps its own height on the granted edge.
    public static PixelRect KeepHeight(PixelRect granted, DockEdge edge, int stripHeight) => Strip(granted, edge, stripHeight);

    // The bar, centred in the strip, never narrower than MinBarWidth nor wider than the strip less its side margins.
    public static PixelRect Bar(PixelRect strip, int barWidth, int barHeight)
    {
        int width = Math.Clamp(barWidth, MinBarWidth, Math.Max(MinBarWidth, strip.Width - 2 * SideMargin));
        int height = Math.Min(barHeight, strip.Height);
        int left = strip.Left + (strip.Width - width) / 2, top = strip.Top + (strip.Height - height) / 2;
        return new PixelRect(left, top, left + width, top + height);
    }
}
