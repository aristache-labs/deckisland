using System.Globalization;

namespace DeckIsland.Core;

// One past day of screen time: the seconds in each app, and the seconds in each of its 24 hours.
public sealed class ScreenTimeDay
{
    public Dictionary<string, int> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<int> Hours { get; set; } = [];
}

// Time in each app, counted on this PC and kept in its settings file: the seconds an app's window was in front while the
// person was at the keyboard. Today's count is kept on its own (the widget shows it); when a new day starts, the day before
// joins the history, which keeps the last KeepDays days.
public static class ScreenTime
{
    public const int MaxApps = 60, MaxNameLength = 60, MaxSeconds = 24 * 60 * 60, KeepDays = 30;
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

    // Today's hours after another stretch: a new day's hours start from nothing.
    public static List<int> AddHour(string date, IReadOnlyList<int> hours, DateTime now, int seconds)
    {
        var counted = date == DayKey(now) && hours.Count == 24 ? hours.ToList() : [.. new int[24]];
        if (seconds > 0) counted[now.Hour] = Math.Min(counted[now.Hour] + seconds, 3600);
        return counted;
    }

    // The history once today's count belongs to a past day: that day joins it (when anything was counted), and days older than
    // KeepDays before now, or not days at all, leave it.
    public static Dictionary<string, ScreenTimeDay> Archive(IReadOnlyDictionary<string, ScreenTimeDay>? history, string date,
        IReadOnlyDictionary<string, int> apps, IReadOnlyList<int> hours, DateTime now)
    {
        var kept = new Dictionary<string, ScreenTimeDay>(history ?? new Dictionary<string, ScreenTimeDay>());
        if (date.Length > 0 && date != DayKey(now) && apps.Count > 0)
            kept[date] = new ScreenTimeDay { Apps = Normalize(date, new Dictionary<string, int>(apps)), Hours = hours.Count == 24 ? [.. hours] : [.. new int[24]] };
        var oldest = DateOnly.FromDateTime(now).AddDays(-(KeepDays - 1));
        return kept.Where(pair => DateOnly.TryParseExact(pair.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day >= oldest && day < DateOnly.FromDateTime(now) && pair.Value?.Apps is not null)
            .ToDictionary(pair => pair.Key, pair => new ScreenTimeDay
            {
                Apps = Normalize(pair.Key, pair.Value.Apps),
                Hours = pair.Value.Hours is { Count: 24 } h ? [.. h.Select(v => Math.Clamp(v, 0, 3600))] : [.. new int[24]]
            });
    }

    // A day as the page shows it: its apps longest first and its hours; today's from the running count, others from the history.
    public static (IReadOnlyList<KeyValuePair<string, int>> Apps, IReadOnlyList<int> Hours) Day(DateOnly day, string date, IReadOnlyDictionary<string, int> apps,
        IReadOnlyList<int> hours, IReadOnlyDictionary<string, ScreenTimeDay> history, DateTime now)
    {
        string key = DayKey(day.ToDateTime(TimeOnly.MinValue));
        if (key == DayKey(now)) return (Today(date, apps, now), date == key && hours.Count == 24 ? hours : new int[24]);
        // A past day still in the running count: counting was off since it ended, so it has not joined the history yet.
        if (key == date) return ([.. apps.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)], hours.Count == 24 ? hours : new int[24]);
        return history.TryGetValue(key, out var past)
            ? ([.. past.Apps.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)], past.Hours.Count == 24 ? past.Hours : new int[24])
            : ([], new int[24]);
    }

    private static string Clip(string name) => name.Trim().Length <= MaxNameLength ? name.Trim() : name.Trim()[..MaxNameLength];
}
