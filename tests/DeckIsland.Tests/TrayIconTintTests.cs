using DeckIsland.Core;

internal static class TrayIconTintTests
{
    internal static void Run(Action<string, bool> check)
    {
        byte[] Pixels(params (byte B, byte G, byte R, byte A)[] pixels) => pixels.SelectMany(p => new[] { p.B, p.G, p.R, p.A }).ToArray();
        var white = Pixels((255, 255, 255, 255), (250, 250, 250, 255), (0, 0, 0, 0), (128, 128, 128, 128));
        var blue = Pixels((255, 120, 30, 255), (255, 255, 255, 255), (240, 100, 20, 255));
        var dark = Pixels((40, 40, 40, 255), (30, 30, 30, 255));
        check("A white tray glyph (with a half-transparent white edge) is a light glyph", TrayIconTint.IsLightGlyph(white));
        check("A coloured tray icon is not a light glyph", !TrayIconTint.IsLightGlyph(blue));
        check("A dark tray icon is not a light glyph", !TrayIconTint.IsLightGlyph(dark));
        check("A fully transparent icon is not a light glyph", !TrayIconTint.IsLightGlyph(Pixels((0, 0, 0, 0))));
        var grey = TrayIconTint.Recolor(white, 60, 60, 64);
        check("Recolour keeps alpha and premultiplies the new colour",
            grey[3] == 255 && grey[0] == 64 && grey[2] == 60 && grey[11] == 0 && grey[15] == 128 && grey[12] == 32);
    }
}
