namespace DeckIsland.Core;

// Where a quota is heading at its current pace, for the line under its bar, as Crest has it: the share it will have used at its reset,
// or, when that is past the limit, the moment the limit is reached. The pace is the share used so far over the time of its window gone
// by; the window's length comes from its name (a session of five hours, a day, a week, a month), as the sources give no length.
public static class QuotaPace
{
    // Too early in a window to tell (under this share of it gone, or under 1 % used) there is no forecast.
    private const double MinimumElapsed = .05, MinimumUsed = 1;

    public static TimeSpan? WindowOf(string label)
    {
        string text = label.ToLowerInvariant();
        if (text.Contains("session") || text.Contains("5h") || text.Contains("5 h") || text.Contains("hour") || text.Contains("会话") || text.Contains("小时")) return TimeSpan.FromHours(5);
        if (text.Contains("week") || text.Contains("周")) return TimeSpan.FromDays(7);
        if (text.Contains("month") || text.Contains("月")) return TimeSpan.FromDays(30);
        if (text.Contains("day") || text.Contains("daily") || text.Contains("日") || text.Contains("天")) return TimeSpan.FromDays(1);
        return null;
    }

    // Projected: the share used at the reset at this pace; LimitAt: when the limit is reached, when that is before the reset.
    public static (double Projected, DateTimeOffset? LimitAt)? Forecast(double usedPercent, DateTimeOffset resetAt, TimeSpan window, DateTimeOffset now)
    {
        var remaining = resetAt - now;
        var elapsed = window - remaining;
        if (remaining <= TimeSpan.Zero || elapsed <= TimeSpan.Zero || elapsed.TotalSeconds < window.TotalSeconds * MinimumElapsed || usedPercent < MinimumUsed) return null;
        if (usedPercent >= 100) return (100, null);
        double perSecond = usedPercent / elapsed.TotalSeconds;
        double projected = usedPercent + perSecond * remaining.TotalSeconds;
        DateTimeOffset? limit = projected >= 100 ? now + TimeSpan.FromSeconds((100 - usedPercent) / perSecond) : null;
        return (projected, limit);
    }
}
