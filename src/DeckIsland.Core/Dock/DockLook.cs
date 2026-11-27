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
        DockStyle.White => new("#FAF5F5F7", "#12000000", "#FFFFFFFF", "#FFF0F0F2", "#FFE5E5E8", "#0D000000", "#FF1D1D1F", "#FF6E6E73", "#1F000000", false),
        DockStyle.Liquid => new("#8C1A1C23", "#2EFFFFFF", "#1AFFFFFF", "#26FFFFFF", "#33FFFFFF", "#12FFFFFF", "#FFF5F5F7", "#B3F5F5F7", "#26FFFFFF", true),
        DockStyle.IOS => new("#59505A6E", "#40FFFFFF", "#24FFFFFF", "#30FFFFFF", "#3DFFFFFF", "#1FFFFFFF", "#FFF5F5F7", "#BFF5F5F7", "#2EFFFFFF", true),
        DockStyle.Midnight => new("#E00F1013", "#17FFFFFF", "#14FFFFFF", "#1FFFFFFF", "#2BFFFFFF", "#0DFFFFFF", "#FFF5F5F7", "#A6F5F5F7", "#1FFFFFFF", true),
        DockStyle.Clear => clear switch
        {
            DockClearCards.White => new("#00000000", "#00000000", "#FFF5F5F7", "#FFEBEBEE", "#FFE0E0E4", "#0F000000", "#FF1D1D1F", "#FF6E6E73", "#1F000000", false),
            DockClearCards.Glass => new("#00000000", "#00000000", "#734A4C55", "#80565862", "#8C62646F", "#24FFFFFF", "#FFF5F5F7", "#CCF5F5F7", "#33FFFFFF", false),
            _ => new("#00000000", "#00000000", "#F51C1C1F", "#F5252528", "#F52F2F33", "#0DFFFFFF", "#FFF5F5F7", "#A6F5F5F7", "#26FFFFFF", false)
        },
        _ => new("#F2161618", "#17FFFFFF", "#FF232326", "#FF2A2A2E", "#FF333338", "#0AFFFFFF", "#FFF5F5F7", "#A6F5F5F7", "#21FFFFFF", false)
    };

    // A colour the dock can use as its accent: one of the nine, or any #RRGGBB; anything else falls back to blue.
    public static string NormalizeAccent(string? accent) =>
        ThemeColor.TryParse(accent, out var colour) && colour.IsOpaque ? colour.ToString() : DefaultAccent;
}
