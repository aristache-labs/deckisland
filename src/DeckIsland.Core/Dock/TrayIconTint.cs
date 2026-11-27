namespace DeckIsland.Core;

// Apps draw their tray icons for the Windows taskbar: on a dark taskbar many are plain white glyphs, lost on a light grid. An icon
// whose visible pixels are almost all light and colourless is such a glyph; it is redrawn in a dark grey, its shape (alpha) kept.
// Coloured icons are never touched.
public static class TrayIconTint
{
    private const byte Visible = 64;          // alpha above which a pixel counts
    private const int LightLevel = 200;       // the darkest channel of a "light" pixel
    private const int MaxChroma = 40;         // brightest minus darkest channel for a colourless pixel
    private const double LightShare = 0.85;   // the share of visible pixels that must be light and colourless

    // Pixels are BGRA, premultiplied or not (the test reads channels relative to alpha only through Visible).
    public static bool IsLightGlyph(ReadOnlySpan<byte> bgra)
    {
        int visible = 0, light = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (a < Visible) continue;
            visible++;
            // Unpremultiply so a half-transparent white edge still reads as white.
            int b = bgra[i] * 255 / a, g = bgra[i + 1] * 255 / a, r = bgra[i + 2] * 255 / a;
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (min >= LightLevel && max - min <= MaxChroma) light++;
        }
        return visible > 0 && light >= visible * LightShare;
    }

    // Redraws every pixel in (r, g, b), keeping its alpha; writes premultiplied BGRA.
    public static byte[] Recolor(ReadOnlySpan<byte> bgra, byte r, byte g, byte b)
    {
        var result = new byte[bgra.Length];
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            result[i] = (byte)(b * a / 255);
            result[i + 1] = (byte)(g * a / 255);
            result[i + 2] = (byte)(r * a / 255);
            result[i + 3] = a;
        }
        return result;
    }
}
