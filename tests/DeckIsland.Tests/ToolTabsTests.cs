using System.Net;
using DeckIsland.Core;

// The Scratchpad, Converter and Terminal tabs' logic: drafts, unit and currency conversion, the rate cache, shells and pages.
internal static class ToolTabsTests
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        Drafts(check);
        Units(check);
        Shells(check);
        Pages(check);
        await Rates(root, check);
    }

    private static void Drafts(Action<string, bool> check)
    {
        var settings = new DeckSettings();
        Scratchpad.SetDraft(settings, ScratchLanguage.Rust, "fn main() {}");
        Scratchpad.SetDraft(settings, ScratchLanguage.Python, "print(1)");
        check("Scratchpad: each language keeps its own draft",
            Scratchpad.Draft(settings, ScratchLanguage.Rust) == "fn main() {}" && Scratchpad.Draft(settings, ScratchLanguage.Python) == "print(1)" && Scratchpad.Draft(settings, ScratchLanguage.JavaScript) == "");
        Scratchpad.SetDraft(settings, ScratchLanguage.Python, "");
        check("Scratchpad: an emptied draft is dropped", !settings.ScratchpadDrafts.ContainsKey("Python"));
        Scratchpad.SetDraft(settings, ScratchLanguage.PlainText, new string('x', Scratchpad.MaxDraftLength + 10));
        check("Scratchpad: a draft is kept within the limit", Scratchpad.Draft(settings, ScratchLanguage.PlainText).Length == Scratchpad.MaxDraftLength);
        var copy = settings.Copy();
        copy.ScratchpadDrafts["Rust"] = "changed";
        check("Scratchpad: a copy of the settings has its own drafts", Scratchpad.Draft(settings, ScratchLanguage.Rust) == "fn main() {}");
        settings.ScratchpadDrafts["Cobol"] = "x"; settings.ScratchpadDrafts["3"] = "x"; settings.ScratchpadDrafts["TypeScript"] = "";
        settings.Normalize();
        check("Scratchpad: unknown languages and empty drafts are dropped when read", settings.ScratchpadDrafts.Keys.Order().SequenceEqual(["PlainText", "Rust"]));
        var read = System.Text.Json.JsonSerializer.Deserialize<DeckSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
        check("Scratchpad: drafts and language survive saving", read is not null && Scratchpad.Draft(read, ScratchLanguage.Rust) == "fn main() {}");
        check("Scratchpad: every language but plain text has keywords",
            Enum.GetValues<ScratchLanguage>().All(language => Scratchpad.Keywords(language).Count > 0 == (language != ScratchLanguage.PlainText)));
    }

    private static void Units(Action<string, bool> check)
    {
        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9 * Math.Max(1, Math.Abs(b));
        double Convert(UnitCategory category, double value, string from, string to)
            => UnitConversion.Convert(value, UnitConversion.Find(category, from), UnitConversion.Find(category, to));
        check("Units: 1 mile is 1.609344 km", Near(Convert(UnitCategory.Length, 1, "mi", "km"), 1.609344));
        check("Units: 100 °C is 212 °F and 373.15 K", Near(Convert(UnitCategory.Temperature, 100, "c", "f"), 212) && Near(Convert(UnitCategory.Temperature, 100, "c", "k"), 373.15));
        check("Units: -40 °F is -40 °C", Near(Convert(UnitCategory.Temperature, -40, "f", "c"), -40));
        check("Units: 1 lb is 0.45359237 kg", Near(Convert(UnitCategory.Weight, 1, "lb", "kg"), 0.45359237));
        check("Units: 1 US gallon is 3.785411784 l", Near(Convert(UnitCategory.Volume, 1, "gal", "l"), 3.785411784));
        check("Units: 36 km/h is 10 m/s", Near(Convert(UnitCategory.Speed, 36, "kmh", "ms"), 10));
        check("Units: every unit converts there and back",
            Enum.GetValues<UnitCategory>().All(category => UnitConversion.In(category).All(unit =>
                Near(UnitConversion.Convert(UnitConversion.Convert(12.5, unit, UnitConversion.In(category)[0]), UnitConversion.In(category)[0], unit), 12.5))));
        check("Units: an unknown unit falls back to the category's pair",
            UnitConversion.Find(UnitCategory.Length, "parsec").Id == "km" && UnitConversion.Find(UnitCategory.Length, null, to: true).Id == "mi");
        check("Units: unit ids are unique within each category",
            Enum.GetValues<UnitCategory>().All(category => UnitConversion.In(category).Select(unit => unit.Id).Distinct().Count() == UnitConversion.In(category).Count));
    }

    private static void Shells(Action<string, bool> check)
    {
        check("Terminal: PowerShell 7 runs pwsh where it is installed", TerminalShells.Program(TerminalShell.PowerShell7, _ => true).StartsWith("pwsh.exe"));
        check("Terminal: PowerShell 7 falls back to Windows PowerShell", TerminalShells.Program(TerminalShell.PowerShell7, _ => false).StartsWith("powershell.exe"));
        check("Terminal: Command Prompt runs cmd", TerminalShells.Program(TerminalShell.CommandPrompt, _ => true) == "cmd.exe");
        check("Terminal: a missing start folder falls back to the profile folder",
            TerminalShells.StartFolder(@"C:\nope", _ => false, @"C:\Users\me") == @"C:\Users\me" && TerminalShells.StartFolder("", _ => true, @"C:\Users\me") == @"C:\Users\me"
            && TerminalShells.StartFolder(@"D:\work", _ => true, @"C:\Users\me") == @"D:\work");
        var settings = new DeckSettings { TerminalFontSize = 99, TerminalShell = (TerminalShell)42 };
        settings.Normalize();
        check("Terminal: the font size and shell are kept valid", settings.TerminalFontSize == DeckSettings.MaxTerminalFontSize && settings.TerminalShell == TerminalShell.PowerShell7);
    }

    private static void Pages(Action<string, bool> check)
    {
        var settings = new DeckSettings();
        check("Pages: the tool tabs come last in the switch", DeckPages.Order.TakeLast(3).SequenceEqual([DeckPage.Scratchpad, DeckPage.Converter, DeckPage.Terminal]));
        check("Pages: the tool tabs open on their own setting",
            DeckPages.Opening(DefaultDeckPage.Scratchpad) == DeckPage.Scratchpad && DeckPages.Opening(DefaultDeckPage.Converter) == DeckPage.Converter && DeckPages.Opening(DefaultDeckPage.Terminal) == DeckPage.Terminal);
        DeckPages.SetSize(settings, DeckPage.Terminal, 700, 320);
        DeckPages.SetSize(settings, DeckPage.Converter, 5000, 1);
        check("Pages: each tool tab keeps its own size within the limits",
            DeckPages.Size(settings, DeckPage.Terminal) == (700, 320) && DeckPages.Size(settings, DeckPage.Converter) == (DeckPages.MaxWidth, DeckPages.MinHeight)
            && DeckPages.Size(settings, DeckPage.Scratchpad) == (560, 260));
        settings.TerminalPage = false;
        check("Pages: a tool tab switched off is not shown", !DeckPages.Visible(settings).Contains(DeckPage.Terminal) && DeckPages.Visible(settings).Contains(DeckPage.Converter));
        var normalized = new DeckSettings { ConverterBaseCurrency = "usd", ConverterCurrencyFrom = "bitcoin", ConverterMode = (ConverterMode)9 };
        normalized.Normalize();
        check("Converter: codes are upper-cased and anything else dropped",
            normalized.ConverterBaseCurrency == "USD" && normalized.ConverterCurrencyFrom == "" && normalized.ConverterMode == ConverterMode.Currency);
        check("Profiles: the Scratchpad's drafts and the Converter's choices are shared by every profile",
            Profiles.GlobalKeys.Contains(nameof(DeckSettings.ScratchpadDrafts)) && Profiles.GlobalKeys.Contains(nameof(DeckSettings.ConverterBaseCurrency)));
    }

    private static async Task Rates(string root, Action<string, bool> check)
    {
        var folder = Path.Combine(root, "rates");
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var handler = new RatesHandler();
        using (var rates = new ExchangeRates(folder, new HttpClient(handler), () => now))
        {
            check("Rates: none before the first fetch", rates.Codes.Count == 0 && rates.Convert(1, "EUR", "USD") is null && rates.IsStale("EUR"));
            bool changed = await rates.RefreshAsync("EUR");
            check("Rates: a fetch lands the currencies, crypto left out", changed && rates.Codes.SequenceEqual(["EUR", "GBP", "JPY", "USD"]) && rates.NameOf("GBP") == "British Pound");
            check("Rates: the base converts at 1", rates.Convert(10, "EUR", "USD") == 11m && rates.Convert(11, "USD", "EUR") == 10m);
            check("Rates: two other currencies convert through the base", rates.Convert(100, "GBP", "USD") is { } usd && Math.Abs(usd - 100m / 0.8m * 1.1m) < 0.0001m);
            check("Rates: a pair is dated by the older of its rates",
                rates.DateFor("GBP", "JPY") == new DateOnly(2026, 11, 19) && rates.DateFor("EUR", "USD") == new DateOnly(2026, 11, 20));
            check("Rates: an unknown currency has no conversion", rates.Convert(1, "EUR", "XYZ") is null);
            check("Rates: fresh rates are not fetched again", !await rates.RefreshAsync("EUR") && handler.Requests == 2);
        }
        using (var cached = new ExchangeRates(folder, new HttpClient(new RatesHandler { Fail = true }), () => now.AddHours(1)))
        {
            check("Rates: the cache is read at start", cached.Base == "EUR" && cached.Convert(10, "EUR", "USD") == 11m && cached.NameOf("USD") == "US Dollar");
            bool changed = await cached.RefreshAsync("USD");
            check("Rates: a failed fetch keeps the rates there were and says so", !changed && cached.Base == "EUR" && cached.LastError is not null && cached.Convert(10, "EUR", "USD") == 11m);
            check("Rates: a failed fetch is not retried at once", !await cached.RefreshAsync("USD"));
        }
        using (var changedBase = new ExchangeRates(folder, new HttpClient(handler), () => now.AddHours(1)))
        {
            check("Rates: a new base is fetched although the rates are fresh", await changedBase.RefreshAsync("USD") && handler.LastBase == "USD");
        }
        check("Rates: codes are three letters", ExchangeRates.NormalizeCode(" gbp ") == "GBP" && ExchangeRates.NormalizeCode("USDT") is null && ExchangeRates.NormalizeCode(null) is null);
    }

    private sealed class RatesHandler : HttpMessageHandler
    {
        public bool Fail { get; init; }
        public int Requests { get; private set; }
        public string? LastBase { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var uri = request.RequestUri!;
            string body;
            if (uri.AbsolutePath.EndsWith("/currencies", StringComparison.Ordinal))
                body = """
                    [{"iso_code":"EUR","name":"Euro","type":"currency"},{"iso_code":"GBP","name":"British Pound","type":"currency"},
                     {"iso_code":"USD","name":"US Dollar","type":"currency"},{"iso_code":"JPY","name":"Japanese Yen","type":"currency"},
                     {"iso_code":"ADA","name":"Cardano","type":"crypto"}]
                    """;
            else
            {
                LastBase = System.Web.HttpUtility.ParseQueryString(uri.Query)["base"];
                body = """
                    [{"date":"2026-11-20","base":"EUR","quote":"USD","rate":1.1},{"date":"2026-11-21","base":"EUR","quote":"GBP","rate":0.8},
                     {"date":"2026-11-19","base":"EUR","quote":"JPY","rate":170.5},{"date":"2026-11-20","base":"EUR","quote":"ADA","rate":7.6},
                     {"date":"bad","base":"EUR","quote":"CHF","rate":0.9}]
                    """;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
