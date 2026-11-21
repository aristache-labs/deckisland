using System.Globalization;
using System.Text.RegularExpressions;

namespace BrimDeck.Core;

public enum ReminderRepeat { Once, Daily, Weekdays, Weekly }
public enum ReminderSound { Off, Once, UntilAnswered }

public sealed record Reminder(Guid Id, string Title, DateTimeOffset Due, DateTimeOffset Scheduled, ReminderRepeat Repeat)
{
    private static readonly Regex Relative = new(@"^(\d+)\s*([mh])\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Clock = new(@"^(?:(tomorrow)\s+)?(\d{1,2}:\d{2})\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static List<Reminder> Normalize(IEnumerable<Reminder>? reminders) => [.. (reminders ?? [])
        .Where(item => item is not null && item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Title)
            && item.Title.Length <= 160 && item.Scheduled.Year is >= 2000 and <= 9900 && item.Due.Year is >= 2000 and <= 9900
            && Enum.IsDefined(item.Repeat)).DistinctBy(item => item.Id)];

    public static Reminder? Parse(string input, DateTimeOffset now, TimeZoneInfo zone)
    {
        string title = input.Trim();
        var due = now.AddHours(1);
        var relative = Relative.Match(title);
        var clock = Clock.Match(title);
        if (relative.Success)
        {
            if (!int.TryParse(relative.Groups[1].Value, out int value) || value is < 1 or > 10000) return null;
            due = relative.Groups[2].Value.Equals("h", StringComparison.OrdinalIgnoreCase) ? now.AddHours(value) : now.AddMinutes(value);
            title = relative.Groups[3].Value;
        }
        else if (clock.Success)
        {
            if (!TimeOnly.TryParseExact(clock.Groups[2].Value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
            due = AtLocal(TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(clock.Groups[1].Success ? 1 : 0) + time.ToTimeSpan(), zone);
            if (due <= now) due = AtLocal(due.Date.AddDays(1) + time.ToTimeSpan(), zone);
            title = clock.Groups[3].Value;
        }
        return Create(title, due, ReminderRepeat.Once);
    }

    public static Reminder? Create(string title, DateTimeOffset due, ReminderRepeat repeat)
    {
        title = title.Trim();
        return title.Length is > 0 and <= 160 && due.Year is >= 2000 and <= 9900 && Enum.IsDefined(repeat)
            ? new(Guid.NewGuid(), title, due, due, repeat) : null;
    }

    public Reminder Snooze(DateTimeOffset now) => this with { Due = now.AddMinutes(10) };

    public Reminder? Complete(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (Repeat == ReminderRepeat.Once) return null;
        var after = Due > now ? Due : now;
        var original = TimeZoneInfo.ConvertTime(Scheduled, zone).DateTime;
        var today = TimeZoneInfo.ConvertTime(after, zone).Date;
        int days = Math.Max(0, (today - original.Date).Days);
        if (Repeat == ReminderRepeat.Weekly) days -= days % 7;
        var next = original.AddDays(days);
        int step = Repeat == ReminderRepeat.Weekly ? 7 : 1;
        while (next <= original || AtLocal(next, zone) <= after ||
            Repeat == ReminderRepeat.Weekdays && next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            next = next.AddDays(step);
        var due = AtLocal(next, zone);
        return this with { Due = due };
    }

    public static DateTimeOffset AtLocal(DateTime value, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new(local, zone.GetUtcOffset(local));
    }
}
