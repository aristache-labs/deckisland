namespace DeckIsland.Core;

// The cities the Clock widget can show beside your own time, each with its Windows time zone.
public static class WorldClocks
{
    public const int Maximum = 3;

    public static readonly IReadOnlyList<(string City, string Zone)> Cities =
    [
        ("Abu Dhabi", "Arabian Standard Time"), ("Amsterdam", "W. Europe Standard Time"), ("Athens", "GTB Standard Time"),
        ("Auckland", "New Zealand Standard Time"), ("Bangkok", "SE Asia Standard Time"), ("Beijing", "China Standard Time"),
        ("Berlin", "W. Europe Standard Time"), ("Bucharest", "GTB Standard Time"), ("Buenos Aires", "Argentina Standard Time"),
        ("Cairo", "Egypt Standard Time"), ("Chicago", "Central Standard Time"), ("Denver", "Mountain Standard Time"),
        ("Dubai", "Arabian Standard Time"), ("Havana", "Cuba Standard Time"), ("Hong Kong", "China Standard Time"),
        ("Honolulu", "Hawaiian Standard Time"), ("Istanbul", "Turkey Standard Time"), ("Jakarta", "SE Asia Standard Time"),
        ("Johannesburg", "South Africa Standard Time"), ("Kyiv", "FLE Standard Time"), ("Lisbon", "GMT Standard Time"),
        ("London", "GMT Standard Time"), ("Los Angeles", "Pacific Standard Time"), ("Madrid", "Romance Standard Time"),
        ("Mexico City", "Central Standard Time (Mexico)"), ("Moscow", "Russian Standard Time"), ("Mumbai", "India Standard Time"),
        ("New York", "Eastern Standard Time"), ("Paris", "Romance Standard Time"), ("Rome", "W. Europe Standard Time"),
        ("São Paulo", "E. South America Standard Time"), ("Seoul", "Korea Standard Time"), ("Singapore", "Singapore Standard Time"),
        ("Sydney", "AUS Eastern Standard Time"), ("Tokyo", "Tokyo Standard Time"), ("Toronto", "Eastern Standard Time"),
        ("Vancouver", "Pacific Standard Time"), ("Warsaw", "Central European Standard Time")
    ];

    public static TimeZoneInfo? Zone(string city)
    {
        foreach (var (name, zone) in Cities)
            if (name == city)
                try { return TimeZoneInfo.FindSystemTimeZoneById(zone); }
                catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
        return null;
    }

    // Known cities only, at most three; an empty entry keeps its position so the second and third pickers stay put.
    public static List<string> Normalize(IEnumerable<string>? cities)
        => [.. (cities ?? []).Take(Maximum).Select(city => Cities.Any(known => known.City == city) ? city : "")];
}
