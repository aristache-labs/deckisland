using System.Globalization;

namespace DeckIsland.Core;

// Time in each app today, counted on this PC and kept in its settings file: the seconds an app's window was in front while the
// person was at the keyboard. The count is of one day; the first reading of a new day starts it again from nothing.
public static class ScreenTime
{
    public const int MaxApps = 60, MaxNameLength = 60, MaxSeconds = 24 * 60 * 60;
    // Without a key press or a mouse move for this long, the person is away and nothing is counted.
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(2);

    public static string DayKey(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A saved count read back: a date that is not a day, or names and seconds that cannot be, leave nothing.
    public static Dictionary<string, int> Normalize(string? date, IDictionary<string, int>? apps)
    {
        if (!DateOnly.TryParseExact(date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || apps is null) return [];
        return apps.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
            .Select(pair => KeyValuePair.Create(Clip(pair.Key), Math.Min(pair.Value, MaxSeconds)))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(group => KeyValuePair.Create(group.First().Key, Math.Min(group.Sum(pair => pair.Value), MaxSeconds)))
            .OrderByDescending(pair => pair.Value).Take(MaxApps).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    // The count after another stretch in front: today's, or a new day's when the date has moved on. The app that would be the
    // sixty-first only counts if it beats the smallest one, which it then replaces, so a long day cannot grow the file.
    public static (string Date, Dictionary<string, int> Apps) Add(string date, IReadOnlyDictionary<string, int> apps, DateTime now, string app, int seconds)
    {
        string today = DayKey(now);
        var counted = date == today ? new Dictionary<string, int>(apps, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(app) || seconds <= 0) return (today, counted);
        string name = Clip(app);
        if (!counted.ContainsKey(name) && counted.Count >= MaxApps)
        {
            var smallest = counted.MinBy(pair => pair.Value);
            if (seconds <= smallest.Value) return (today, counted);
            counted.Remove(smallest.Key);
        }
        counted[name] = Math.Min(counted.GetValueOrDefault(name) + seconds, MaxSeconds);
        return (today, counted);
    }

    // What the widget shows: nothing for another day's count.
    public static IReadOnlyList<KeyValuePair<string, int>> Today(string date, IReadOnlyDictionary<string, int> apps, DateTime now)
        => date == DayKey(now) ? [.. apps.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)] : [];

    public static int Total(IEnumerable<KeyValuePair<string, int>> apps) => apps.Sum(pair => pair.Value);

    // "<1m", "6m", "1h 12m", "3h": minutes are what a day's screen time is read in.
    public static string Duration(int seconds)
    {
        int minutes = Math.Max(0, seconds) / 60;
        if (seconds <= 0) return "0m";
        if (minutes == 0) return "<1m";
        return minutes < 60 ? $"{minutes}m" : minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";
    }

    // An app's name as the widget writes it: the process name with its first letter capitalized.
    public static string Display(string name) => name.Length == 0 ? name : char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];

    private static string Clip(string name) => name.Trim().Length <= MaxNameLength ? name.Trim() : name.Trim()[..MaxNameLength];
}
