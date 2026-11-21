using System.Text;
using DeckIsland.Core;

static class ClipboardToolsTests
{
    public static void Run(Action<string, bool> check)
    {
        var defaults = new DeckSettings();
        check("Decode defaults on and Colour defaults off", defaults.ClipboardDecode && !defaults.ColourTool && !defaults.ClipboardPresentationMode);
        check("Colour is a separate page, disabled by default", !DeckPages.Shown(defaults, DeckPage.Colour)
            && DeckPages.Opening(DefaultDeckPage.Colour) == DeckPage.Colour);
        defaults.ColourTool = true;
        defaults.ClipboardPage = false;
        defaults.PageOrder = DeckPages.Normalize(defaults.PageOrder.Where(page => page != DeckPage.Colour));
        DeckPages.SetSize(defaults, DeckPage.Colour, 610, 315);
        check("Colour stays available with Clipboard disabled", DeckPages.Visible(defaults).Contains(DeckPage.Colour)
            && !DeckPages.Visible(defaults).Contains(DeckPage.Clipboard) && DeckPages.Size(defaults, DeckPage.Colour) == (610, 315));
        check("Tracking removes known keys, preserves raw query and fragment",
            ClipboardDecode.Detect("https://example.test/a%2Fb?utm_source=a&keep=%2B&FBCLID=b&keep=2&%67clid=3#part?x")
                is { Tracking: true, Result: "https://example.test/a%2Fb?keep=%2B&keep=2#part?x" });
        check("All tracking keys removed without dangling query", ClipboardDecode.Detect("https://example.test/?msclkid=id#part")?.Result == "https://example.test/#part");
        check("Unix start boundary", ClipboardDecode.Detect("946684800") is { Type: "Unix timestamp" } epoch
            && epoch.Result.Contains("2000-01-01 00:00:00.000 UTC") && epoch.Result.Contains("Local:"));
        check("Unix milliseconds through 2100", ClipboardDecode.Detect("4133980799999") is { Type: "Unix timestamp" });
        check("UUID version", ClipboardDecode.Detect("f81d4fae-7dec-11d0-a765-00a0c91e6bf6") is { Type: "UUID", Result: "Version 1" });
        check("Base64 text", ClipboardDecode.Detect("SGVsbG8h") is { Type: "Base64", Result: "Hello!" });
        check("JSON indentation", ClipboardDecode.Detect("{\"count\":2}") is { Type: "JSON" } json
            && json.Result.Replace("\r", "").Contains("\n  \"count\": 2"));
        check("Hex colour forms", ClipboardDecode.Detect("#abc") is { Type: "Hex colour" } hex
            && hex.Result.Contains("rgb(170 187 204)") && hex.Result.Contains("hsl("));
        check("Numeric shorthand grey", ClipboardDecode.Detect("#777") is { Type: "Hex colour" } && Shelf.Classify("#777") == ClipKind.Color);
        check("Issue reference stays text in Clipboard", Shelf.Classify("#123") == ClipKind.Text);

        static string Segment(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string header = Segment("{\"alg\":\"HS256\"}");
        string payload = Segment("{\"sub\":\"person\",\"exp\":1700000000}");
        check("JWT shows unverified header, payload and expiry", ClipboardDecode.Detect($"{header}.{payload}.YQ") is { Type: "JWT" } jwt
            && jwt.Result.Contains("Signature has not been verified.") && jwt.Result.Contains("2023-11-14")
            && jwt.Result.Contains("HS256") && jwt.Result.Contains("person"));
        foreach (string input in new[] { "#123", "ff00aa", "rgb(2 3 4)", "946684799", "4133980800000", "{bad}",
            "YWJj", "/wAAAIE=", "password", "https://example.test/?keep=1#?utm_source=2", $"{header}.{payload}.", new string('é', 52_000) })
            check($"No decode chip for {input[..Math.Min(input.Length, 55)]}", ClipboardDecode.Detect(input) is null);

        foreach (var (input, expected) in new[] { ("abc", "#AABBCC"), ("#abcd", "#AABBCCDD"), ("11223344", "#11223344"),
            ("#102030", "#102030"), ("rgb(100%, 0%, 0%)", "#FF0000"), ("rgb(255 0 0 / 50%)", "#FF000080"),
            ("hsl(.5turn 100% 50%)", "#00FFFF"), ("oklch(0.62796 0.25768 29.23)", "#FF0000"),
            ("RebeccaPurple", "#663399"), ("LightGrey", "#D3D3D3"), ("transparent", "#00000000") })
            check($"Parse {input}", CssColour.TryParse(input, out var colour) && colour.Hex == expected);
        foreach (string input in new[] { "#10", "WindowText", "rgb(255 0 0 .5)", "hsl(120 10 10)", "oklch(NaN 0.1 30)", "rgb(1,2,3 / .5)" })
            check($"Reject malformed colour {input}", !CssColour.TryParse(input, out _));
        var red = new CssColour(255, 0, 0, 128);
        check("Colour values round trip alpha", new[] { red.Hex, red.Rgb, red.Hsl, red.Oklch }
            .All(value => CssColour.TryParse(value, out var parsed) && parsed == red));
        var white = new CssColour(255, 255, 255);
        var black = new CssColour(0, 0, 0);
        var grey = new CssColour(128, 128, 128);
        check("WCAG black and white ratio", white.Contrast(black) == 21);
        check("Transparent text composites over background", new CssColour(255, 0, 0, 0).Contrast(white) == 1);
        check("AA suggestion meets threshold after rounding", grey.AaSuggestion(white) is { } darker
            && darker.R < grey.R && darker.Contrast(white) >= 4.5);
        check("Passing colours have no AA suggestion", black.AaSuggestion(white) is null);
        check("Harmonies rotate the hue", new CssColour(255, 0, 0).RotateHue(180).Hex == "#00FFFF"
            && new CssColour(255, 0, 0).RotateHue(120).Hex == "#00FF00"
            && new CssColour(255, 0, 0).RotateHue(30).Hex == "#FF8000");
    }
}
