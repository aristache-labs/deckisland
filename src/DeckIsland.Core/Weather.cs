using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace DeckIsland.Core;

public sealed record WeatherCity(string Name, string? Region, string? Country, double Latitude, double Longitude)
{
    public bool Valid => !string.IsNullOrWhiteSpace(Name) && WeatherPoint.Valid(Latitude, Longitude);
    public string Label => FormatLabel(Name, Region);
    internal static string FormatLabel(string name, string? region) =>
        string.IsNullOrWhiteSpace(region) || name.Equals(region, StringComparison.OrdinalIgnoreCase) ? name : $"{name}, {region}";
    public override string ToString() => string.Join(" · ", new[] { Name, Region, Country }
        .Where(part => !string.IsNullOrWhiteSpace(part)).Distinct());
}

public sealed record WeatherPoint(string Name, double Latitude, double Longitude)
{
    public static bool Valid(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
}

public sealed record WeatherDay(DateOnly Date, int? Code, double? High, double? Low,
    double? PrecipitationProbability = null, double? PrecipitationMillimetres = null, double? SnowfallCentimetres = null,
    DateTime? Sunrise = null, DateTime? Sunset = null);
public sealed record WeatherHour(DateTime Time, double? Temperature, int? Code, bool Daylight, double? Chance);
public sealed record WeatherReading(WeatherPoint Place, double Temperature, int? Code, bool Daylight, IReadOnlyList<WeatherDay> Days)
{
    public IReadOnlyList<WeatherHour> Hours { get; init; } = [];
    public double? FeelsLike { get; init; }
    public double? WindSpeed { get; init; }
    public double? WindDirection { get; init; }
    public double? Humidity { get; init; }
    public TimeSpan? UtcOffset { get; init; }
}
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
    private const int ForecastDays = 7;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    public const string Attribution = "https://open-meteo.com/";
    public const string LocationAttribution = "https://www.geonames.org/";
    public const string AutomaticLocationAttribution = "https://www.bigdatacloud.com/docs/free-api/free-reverse-geocode-to-city-api";

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
        string url = $"https://api.open-meteo.com/v1/forecast?{coordinates}&timezone=auto&forecast_days={ForecastDays}&forecast_hours=24" +
            "&current=temperature_2m,weather_code,is_day,apparent_temperature,wind_speed_10m,wind_direction_10m,relative_humidity_2m" +
            "&hourly=temperature_2m,weather_code,is_day,precipitation_probability" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,snowfall_sum,sunrise,sunset";
        return Parse(place, await Http.GetFromJsonAsync<JsonElement>(url, token).ConfigureAwait(false));
    }

    public static async Task<string?> ResolveDeviceLocation(WeatherPoint point, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        string coordinates = FormattableString.Invariant($"latitude={point.Latitude}&longitude={point.Longitude}");
        string url = $"https://api.bigdatacloud.net/data/reverse-geocode-client?{coordinates}&localityLanguage=default";
        return ParseDeviceLocation(await Http.GetFromJsonAsync<JsonElement>(url, deadline.Token).ConfigureAwait(false));
    }

    public static string? ParseDeviceLocation(JsonElement response)
    {
        if (Str(Field(response, "lookupSource"))?.StartsWith("ip", StringComparison.OrdinalIgnoreCase) == true) return null;
        var city = Str(Field(response, "city"));
        if (string.IsNullOrWhiteSpace(city)) city = Str(Field(response, "locality"));
        if (string.IsNullOrWhiteSpace(city)) return null;

        string? region = Str(Field(response, "principalSubdivision"));
        var divisions = Field(Field(response, "localityInfo"), "administrative");
        if (divisions.ValueKind == JsonValueKind.Array)
        {
            // Counties are level 4 in Romania (the principal subdivision) and level 6 in the US.
            var country = Str(Field(response, "countryCode"));
            int? countyLevel = country == "RO" ? 4 : country == "US" ? 6 : null;
            if (countyLevel is not null)
                region = divisions.EnumerateArray().Where(item => Code(Field(item, "adminLevel")) == countyLevel)
                    .Select(item => Str(Field(item, "name"))).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? region;
        }
        return WeatherCity.FormatLabel(city, region);
    }

    public static WeatherReading? Parse(WeatherPoint place, JsonElement response)
    {
        var current = Field(response, "current");
        if (Number(Field(current, "temperature_2m")) is not { } temperature) return null;
        var daily = Field(response, "daily");
        var days = new List<WeatherDay>(ForecastDays);
        for (int i = 0; i < ForecastDays; i++)
        {
            if (DateOnly.TryParseExact(Str(At(daily, "time", i)), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                days.Add(new(date, Code(At(daily, "weather_code", i)), Number(At(daily, "temperature_2m_max", i)),
                    Number(At(daily, "temperature_2m_min", i)), Number(At(daily, "precipitation_probability_max", i)),
                    Number(At(daily, "precipitation_sum", i)), Number(At(daily, "snowfall_sum", i)),
                    Clock(At(daily, "sunrise", i)), Clock(At(daily, "sunset", i))));
        }
        var hours = new List<WeatherHour>();
        var hourly = Field(response, "hourly");
        var observed = Clock(Field(current, "time"));
        var times = Field(hourly, "time");
        if (times.ValueKind == JsonValueKind.Array)
        {
            for (int i = 0; i < times.GetArrayLength() && hours.Count < 8; i++)
            {
                if (Clock(times[i]) is not { } time || observed is { } when && time < when.AddMinutes(-when.Minute)) continue;
                hours.Add(new(time, Number(At(hourly, "temperature_2m", i)), Code(At(hourly, "weather_code", i)),
                    Number(At(hourly, "is_day", i)) != 0, Number(At(hourly, "precipitation_probability", i))));
            }
        }
        return new(place, temperature, Code(Field(current, "weather_code")), Number(Field(current, "is_day")) != 0, days)
        {
            Hours = hours,
            FeelsLike = Number(Field(current, "apparent_temperature")),
            WindSpeed = Number(Field(current, "wind_speed_10m")),
            WindDirection = Number(Field(current, "wind_direction_10m")),
            Humidity = Number(Field(current, "relative_humidity_2m")),
            UtcOffset = Code(Field(response, "utc_offset_seconds")) is >= -50400 and <= 50400 and var offset
                ? TimeSpan.FromSeconds(offset) : null
        };
    }

    private static DateTime? Clock(JsonElement value) =>
        DateTime.TryParseExact(Str(value), "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

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
