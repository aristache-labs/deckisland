using System.Text.Json;
using DeckIsland.Core;

internal static class WeatherTests
{
    internal static void Run(Action<string, bool> check)
    {
        var legacy = JsonSerializer.Deserialize<DeckSettings>("{}")!;
        legacy.Normalize();
        check("Weather is opt-in for existing settings", !legacy.WeatherEnabled && !legacy.WeatherFahrenheit &&
            !legacy.ManualWeatherLocation && !legacy.ShowsWeather(CompactStyle.Notch) &&
            !DeckPages.Shown(legacy, DeckPage.Weather) && legacy.PageOrder.Contains(DeckPage.Weather));
        legacy.WeatherEnabled = true;
        check("Weather cards default to Minimal and persist Colourful per profile", !legacy.ColourfulWeatherCard &&
            Profiles.IsProfileKey(nameof(DeckSettings.ColourfulWeatherCard)) &&
            JsonSerializer.Deserialize<DeckSettings>(JsonSerializer.Serialize(new DeckSettings { ColourfulWeatherCard = true })) is { ColourfulWeatherCard: true });
        check("Weather integrates with the page switch and collapsed styles", legacy.ShowsWeather(CompactStyle.Notch) &&
            legacy.ShowsWeather(CompactStyle.Capsule) && !legacy.ShowsWeather(CompactStyle.Line) &&
            DeckPages.Visible(legacy).Contains(DeckPage.Weather) && DeckPages.Opening(DefaultDeckPage.Weather) == DeckPage.Weather);
        check("Weather location and units are shared and visibility belongs to the profile",
            Profiles.GlobalKeys.Contains(nameof(DeckSettings.WeatherCity)) && Profiles.GlobalKeys.Contains(nameof(DeckSettings.WeatherFahrenheit)) &&
            Profiles.IsProfileKey(nameof(DeckSettings.NotchWeather)) && Profiles.IsProfileKey(nameof(DeckSettings.WeatherPage)));

        WeatherPoint place = new("Tokyo", 35.68, 139.69);
        using var forecast = JsonDocument.Parse("""
            {"timezone":"Asia/Tokyo","current":{"time":"2026-11-21T01:00","temperature_2m":13,"is_day":0,"weather_code":0},
             "daily":{"time":["2026-11-21","2026-11-22","2026-11-23","2026-11-24"],"temperature_2m_max":[17,null,18,16],"temperature_2m_min":[7],
               "precipitation_probability_max":[75,0,null,32],"precipitation_sum":[2.4,0,null,0.8],"snowfall_sum":[0,0,null,0.56]}}
            """);
        var reading = Weather.Parse(place, forecast.RootElement);
        check("Weather keeps daily precipitation amounts, chance, zero and missing values distinct",
            reading?.Days[0] is { PrecipitationProbability: 75, PrecipitationMillimetres: 2.4 } &&
            reading.Days[1] is { PrecipitationProbability: 0, PrecipitationMillimetres: 0 } &&
            reading.Days[2] is { PrecipitationProbability: null, PrecipitationMillimetres: null } &&
            reading.Days[3] is { PrecipitationMillimetres: 0.8, SnowfallCentimetres: 0.56 });
        check("Weather keeps location-local dates and tolerates missing forecast fields",
            reading is { Temperature: 13, Daylight: false, Days.Count: 4 } &&
            reading.Days[0] is { High: 17, Low: 7, Code: null } &&
            reading.Days[0].Date == new DateOnly(2026, 11, 21) &&
            reading.Days[1] is { High: null, Low: null } && reading.Days[2].High == 18 &&
            reading.Days[3] is { High: 16 } && reading.Days[3].Date == new DateOnly(2026, 11, 24));
        using var automatic = JsonDocument.Parse("""
            {"city":"Reșița","countryCode":"RO","principalSubdivision":"Caraș-Severin",
             "localityInfo":{"administrative":[{"name":"România","adminLevel":2},{"name":"Caraș-Severin","adminLevel":4},{"name":"Reșița","adminLevel":8}]}}
            """);
        using var viaIp = JsonDocument.Parse("""{"lookupSource":"ip-geolocation","city":"Wrong city"}""");
        check("Weather labels use the city and county and exclude IP fallbacks",
            Weather.ParseDeviceLocation(automatic.RootElement) == "Reșița, Caraș-Severin" &&
            Weather.ParseDeviceLocation(viaIp.RootElement) is null &&
            new WeatherCity("Willard", "Ohio", "United States", 41, -83).Label == "Willard, Ohio");
        using var invalid = JsonDocument.Parse("""{"current":{"temperature_2m":"bad"}}""");
        check("Weather rejects a response without a numeric current temperature", Weather.Parse(place, invalid.RootElement) is null);
        var last = new WeatherUpdate(reading, false);
        check("A failed weather refresh keeps only a reading for the same location", last.Complete(place, null, default) == new WeatherUpdate(reading, true) &&
            last.Complete(new("London", 51.51, -.13), null, default).Reading is null);
        var replacement = new WeatherUpdate(null, false);
        check("A cancelled request cannot publish old-city weather", replacement.Complete(place, reading, new CancellationToken(true)) == replacement);
    }
}
