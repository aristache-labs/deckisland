namespace DeckIsland.Core;

// The dock's styles, after lumabar's: two solid (Black, White), three glass (Liquid: clear glass with a light tint; iOS: a cooler,
// bluer glass; Midnight: glass under a deep dark tint) and Clear, which leaves only the tiles, on black, frosted or white cards.
public enum DockStyle { Black, White, Liquid, IOS, Midnight, Clear }
public enum DockClearCards { Black, Glass, White }

// The colours of one style, as #AARRGGBB: the bar and its rim, the cards tiles sit on (at rest, under the pointer, for the app in
// front), the text, the dimmer text and the divider. Glass styles show the screen behind the bar, blurred, under Bar as a tint.
public sealed record DockLook(string Bar, string Rim, string Card, string CardHover, string CardActive, string CardRim, string Text, string Dim,
    string Divider, bool Glass)
{
    // lumabar's nine accent colours: blue, indigo, purple, pink, red, orange, green, teal and graphite.
    public static readonly string[] Accents = ["#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF453A", "#FF7A1A", "#30D158", "#1AA6B7", "#8E8E93"];
    public const string DefaultAccent = "#0A84FF";
    // The red of the weekday over the date, as on lumabar's clock.
    public const string Weekday = "#FF453A";

    // Rims stay faint, as lumabar's: a hairline that separates the bar and its cards from what is behind, never a white frame.
    public static DockLook For(DockStyle style, DockClearCards clear) => style switch
    {
        // White: a white bar, light grey cards with a hairline.
        DockStyle.White => new("#FAFFFFFF", "#12000000", "#FFF2F2F4", "#FFE6E6E9", "#FFE6E6E9", "#14000000", "#FF1D1D1F", "#FF6E6E73", "#1F000000", false),
        // Liquid: clear glass, the screen behind softly blurred under a light smoke; the rim (drawn by the dock) catches light; cards a faint frost.
        DockStyle.Liquid => new("#6B050507", "#4DC8D7EB", "#4D000000", "#1AFFFFFF", "#24FFFFFF", "#1FFFFFFF", "#FFF5F5F7", "#B3F5F5F7", "#26FFFFFF", true),
        // iOS: almost clear blurred glass with a faint blue cast; cards barely there; a cool rim a little brighter than Liquid's.
        DockStyle.IOS => new("#59191E2D", "#59C8D7F0", "#4D000000", "#17FFFFFF", "#21FFFFFF", "#1CFFFFFF", "#FFF5F5F7", "#BFF5F5F7", "#2EFFFFFF", true),
        // Midnight: glass under a deep dark tint, cards a touch lighter than the bar, a faint rim.
        DockStyle.Midnight => new("#8C08080C", "#1AC8D7F0", "#08FFFFFF", "#14FFFFFF", "#1FFFFFFF", "#0DFFFFFF", "#FFF5F5F7", "#A6F5F5F7", "#1FFFFFFF", true),
        DockStyle.Clear => clear switch
        {
            DockClearCards.White => new("#00000000", "#00000000", "#FFF5F5F7", "#FFEBEBEE", "#FFE0E0E4", "#0F000000", "#FF1D1D1F", "#FF6E6E73", "#1F000000", false),
            DockClearCards.Glass => new("#00000000", "#00000000", "#734A4C55", "#80565862", "#8C62646F", "#24FFFFFF", "#FFF5F5F7", "#CCF5F5F7", "#33FFFFFF", false),
            _ => new("#00000000", "#00000000", "#F51C1C1F", "#F5252528", "#F52F2F33", "#0DFFFFFF", "#FFF5F5F7", "#A6F5F5F7", "#26FFFFFF", false)
        },
        // Black: no rim on the bar itself; the widget cards carry a visible hairline instead.
        _ => new("#F2111113", "#00000000", "#FF1E1E21", "#FF29292D", "#FF2E2E33", "#1AFFFFFF", "#FFF5F5F7", "#A6F5F5F7", "#21FFFFFF", false)
    };

    // A colour the dock can use as its accent: one of the nine, or any #RRGGBB; anything else falls back to blue.
    public static string NormalizeAccent(string? accent) =>
        ThemeColor.TryParse(accent, out var colour) && colour.IsOpaque ? colour.ToString() : DefaultAccent;
}
