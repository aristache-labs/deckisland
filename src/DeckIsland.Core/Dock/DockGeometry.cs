using System.Globalization;

namespace DeckIsland.Core;

// A rectangle in device pixels, edges as Win32 gives them.
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

// Where DeckIsland Dock goes on a screen. The strip is the band the shell reserves for it (the AppBar); the bar is the visible
// part, in the strip, so clicks beside it reach the desktop. The window may be taller than the bar, towards the screen's middle,
// to leave room for magnified icons.
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

    // The bar in its strip: distance away from the screen edge, the rest of the strip shared evenly above and below it; centred and
    // sized to its tiles, or across the strip with the same gap at its ends.
    public static PixelRect Bar(PixelRect strip, DockEdge edge, int barWidth, int barHeight, int distance = 0, bool fullWidth = false)
    {
        int height = Math.Min(barHeight, Math.Max(1, strip.Height - distance));
        int gap = Math.Max(0, (strip.Height - distance - height) / 2);
        int top = edge == DockEdge.Top ? strip.Top + distance + gap : strip.Bottom - distance - gap - height;
        int width = fullWidth ? Math.Max(MinBarWidth, strip.Width - 2 * Math.Max(gap, SideMargin))
            : Math.Clamp(barWidth, MinBarWidth, Math.Max(MinBarWidth, strip.Width - 2 * SideMargin));
        int left = strip.Left + (strip.Width - width) / 2;
        return new PixelRect(left, top, left + width, top + height);
    }

    // The window around the bar, extra pixels taller on the side away from the screen edge.
    public static PixelRect Window(PixelRect bar, DockEdge edge, int extra) => edge == DockEdge.Top ? bar with { Bottom = bar.Bottom + extra } : bar with { Top = bar.Top - extra };

    // The corner radius for a roundness in percent of half the bar's height.
    public static double CornerRadius(int percent, double barHeight) => Math.Clamp(percent, 0, 100) / 100.0 * barHeight / 2;

    // The share of a tile its icon fills.
    public static double IconShare(DockIconSize size) => size switch { DockIconSize.Small => 0.58, DockIconSize.Large => 0.82, _ => 0.7 };

    // A tile's scale under magnification: 1 + size at the pointer, falling off smoothly to 1 at reach from the tile's centre.
    public static double Magnify(double distance, double reach, int sizePercent)
    {
        if (reach <= 0 || Math.Abs(distance) >= reach) return 1;
        return 1 + sizePercent / 100.0 * (Math.Cos(Math.Abs(distance) / reach * Math.PI) + 1) / 2;
    }

    public static TimeSpan Duration(DockSpeed speed) => TimeSpan.FromMilliseconds(speed switch { DockSpeed.Slow => 260, DockSpeed.Fast => 70, _ => 140 });

    // The dock's clock: Windows' format when h24 is null, else 24 or 12 hours, with seconds when asked.
    public static string Clock(DateTime now, bool? h24, bool seconds, CultureInfo culture) => h24 switch
    {
        null => now.ToString(seconds ? culture.DateTimeFormat.LongTimePattern : culture.DateTimeFormat.ShortTimePattern, culture),
        true => now.ToString(seconds ? "HH:mm:ss" : "HH:mm", culture),
        false => now.ToString(seconds ? "h:mm:ss tt" : "h:mm tt", CultureInfo.InvariantCulture)
    };
}
