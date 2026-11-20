using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrimDeck.Core;

public sealed record WeatherCity(string Name, string? Region, string? Country, double Latitude, double Longitude)
{
    public bool Valid => !string.IsNullOrWhiteSpace(Name) && WeatherPoint.Valid(Latitude, Longitude);
    public override string ToString() => string.Join(" · ", new[] { Name, Region, Country }
        .Where(part => !string.IsNullOrWhiteSpace(part)).Distinct());
}

public sealed record WeatherPoint(string Name, double Latitude, double Longitude)
{
    public static bool Valid(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
}

public sealed record WeatherDay(DateOnly Date, int? Code, double? High, double? Low);
public sealed record WeatherReading(WeatherPoint Place, double Temperature, int? Code, bool Daylight, IReadOnlyList<WeatherDay> Days);
public sealed record WeatherUpdate(WeatherReading? Reading, bool Stale)
{
    public WeatherUpdate Complete(WeatherPoint? place, WeatherReading? fresh, CancellationToken token)
    {
        if (token.IsCancellationRequested) return this;
        if (fresh is not null) return new(fresh, false);
        return Reading is not null && Reading.Place == place ? new(Reading, true) : new(null, false);
    }
}

public static class Weather
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    public const string Attribution = "https://open-meteo.com/";
    public const string LocationAttribution = "https://www.geonames.org/";

    public static async Task<IReadOnlyList<WeatherCity>> Search(string query, bool chinese, CancellationToken token)
    {
        string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=10&language={(chinese ? "zh" : "en")}";
        var response = await Http.GetFromJsonAsync<JsonElement>(url, token).ConfigureAwait(false);
        if (Field(response, "results") is not { ValueKind: JsonValueKind.Array } results) return [];
        return results.EnumerateArray().Select(item => new WeatherCity(
            Str(Field(item, "name")) ?? "", Str(Field(item, "admin1")), Str(Field(item, "country")),
            Number(Field(item, "latitude")) ?? double.NaN, Number(Field(item, "longitude")) ?? double.NaN))
            .Where(city => city.Valid).ToArray();
    }

    public static async Task<WeatherReading?> Fetch(WeatherPoint place, CancellationToken token)
    {
        string coordinates = FormattableString.Invariant($"latitude={place.Latitude}&longitude={place.Longitude}");
        string url = $"https://api.open-meteo.com/v1/forecast?{coordinates}&timezone=auto&forecast_days=3&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min";
        return Parse(place, await Http.GetFromJsonAsync<JsonElement>(url, token).ConfigureAwait(false));
    }

    public static WeatherReading? Parse(WeatherPoint place, JsonElement response)
    {
        var current = Field(response, "current");
        if (Number(Field(current, "temperature_2m")) is not { } temperature) return null;
        var daily = Field(response, "daily");
        var days = new List<WeatherDay>(3);
        for (int i = 0; i < 3; i++)
        {
            if (DateOnly.TryParseExact(Str(At(daily, "time", i)), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                days.Add(new(date, Code(At(daily, "weather_code", i)), Number(At(daily, "temperature_2m_max", i)), Number(At(daily, "temperature_2m_min", i))));
        }
        return new(place, temperature, Code(Field(current, "weather_code")), Number(Field(current, "is_day")) != 0, days);
    }

    private static JsonElement Field(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var value) ? value : default;
    private static JsonElement At(JsonElement obj, string key, int index) =>
        Field(obj, key) is { ValueKind: JsonValueKind.Array } array && index < array.GetArrayLength() ? array[index] : default;
    private static string? Str(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static double? Number(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number) ? number : null;
    private static int? Code(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int code) ? code : null;
}
