namespace DeckIsland.Core;

// How much of today, this week, this month and this year has gone, as shares from 0 to 1 of the time in the local zone.
// Measured against the real length of each period, so a day the clocks change (23 or 25 hours) still ends at 100%.
public static class PeriodProgress
{
    public readonly record struct Shares(double Day, double Week, double Month, double Year)
    {
        // Whole percents, to the nearest, but never 100% until the period is over.
        public static int Percent(double share)
        {
            double value = Math.Clamp(double.IsFinite(share) ? share : 0, 0, 1);
            return value >= 1 ? 100 : Math.Min(99, (int)Math.Round(value * 100, MidpointRounding.AwayFromZero));
        }
    }

    // firstDay: the day a week starts on (the language's own: Sunday, Monday or Saturday).
    public static Shares At(DateTime now, DayOfWeek firstDay)
    {
        var today = now.Date;
        int back = ((int)today.DayOfWeek - (int)firstDay + 7) % 7;
        var week = today.AddDays(-back);
        var month = new DateTime(today.Year, today.Month, 1, 0, 0, 0, now.Kind);
        var year = new DateTime(today.Year, 1, 1, 0, 0, 0, now.Kind);
        return new(Share(now, today, today.AddDays(1)), Share(now, week, week.AddDays(7)), Share(now, month, month.AddMonths(1)), Share(now, year, year.AddYears(1)));
    }

    // Compared in the zone's own clock when it is known, so the clock change inside a period is counted: a spring day is 23 hours.
    private static double Share(DateTime now, DateTime start, DateTime end)
    {
        var zone = TimeZoneInfo.Local;
        if (now.Kind == DateTimeKind.Unspecified) return (now - start).TotalHours / (end - start).TotalHours;
        double elapsed = (Absolute(now, zone) - Absolute(start, zone)).TotalHours, length = (Absolute(end, zone) - Absolute(start, zone)).TotalHours;
        return length <= 0 ? 0 : elapsed / length;
    }

    // A midnight a clock change skips (some zones change at midnight) is taken as the hour after it.
    private static DateTime Absolute(DateTime local, TimeZoneInfo zone)
    {
        var clock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(zone.IsInvalidTime(clock) ? clock.AddHours(1) : clock, zone);
    }
}
