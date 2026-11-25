using DeckIsland.Core;

static class CalendarFeedTests
{
    private const string Sample = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:test
        BEGIN:VEVENT
        UID:standup
        SUMMARY:Standup
        DTSTART:20261123T090000Z
        DTEND:20261123T091500Z
        RRULE:FREQ=DAILY;COUNT=5
        EXDATE:20261125T090000Z
        DESCRIPTION:Join<https://meet.google.com/abc-defg-hij>.
        END:VEVENT
        BEGIN:VEVENT
        UID:trip
        SUMMARY:Trip
        DTSTART;VALUE=DATE:20261126
        DTEND;VALUE=DATE:20261128
        END:VEVENT
        BEGIN:VEVENT
        UID:berlin
        SUMMARY:Review
        DTSTART;TZID=Europe/Berlin:20261124T150000
        DTEND;TZID=Europe/Berlin:20261124T160000
        LOCATION:https://zoom.us/signin
        END:VEVENT
        END:VCALENDAR
        """;

    public static void Run(Action<string, bool> check)
    {
        var source = Guid.NewGuid();
        var from = new DateTime(2026, 11, 23); var to = new DateTime(2026, 12, 1);
        var events = CalendarFeed.Parse(Sample.Replace("\r\n", "\n").Replace("\n", "\r\n"), source, from, to);
        var standups = events.Where(e => e.Title == "Standup").ToList();
        check("Calendar: a daily repeat is expanded, its excluded day left out",
            standups.Count == 4 && standups.All(e => e.Start.TimeOfDay == new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc).ToLocalTime().TimeOfDay)
            && standups.Select(e => e.Id).Distinct().Count() == 4);
        var trip = events.Single(e => e.Title == "Trip");
        check("Calendar: an all-day event keeps its days", trip.AllDay && trip.Start == new DateTime(2026, 11, 26) && trip.End == new DateTime(2026, 11, 28)
            && CalendarFeed.On(events, new DateOnly(2026, 11, 27)).Any(e => e.Title == "Trip") && !CalendarFeed.On(events, new DateOnly(2026, 11, 28)).Any(e => e.Title == "Trip"));
        var review = events.Single(e => e.Title == "Review");
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        check("Calendar: a time with a zone is moved to this PC's zone",
            review.Start == TimeZoneInfo.ConvertTime(new DateTime(2026, 11, 24, 15, 0, 0), berlin, TimeZoneInfo.Local) && review.End - review.Start == TimeSpan.FromHours(1));
        check("Calendar: a meeting link is found, a sign-in page is not",
            standups[0].MeetingLink == "https://meet.google.com/abc-defg-hij" && review.MeetingLink is null);
        check("Calendar: a day lists all-day events first", CalendarFeed.On(events, new DateOnly(2026, 11, 26)) is [{ AllDay: true }, ..]);
        var now = standups[1].Start.AddMinutes(-30);
        check("Calendar: the next event is within the horizon only",
            CalendarFeed.Next(events, now, 60) == standups[1] && CalendarFeed.Next(events, now, 15) is null);
        check("Calendar: webcal and bare hosts become https, other schemes are refused",
            CalendarFeed.LinkUri("webcal://p01.icloud.com/x")?.Scheme == "https" && CalendarFeed.LinkUri("calendar.google.com/ical/a.ics")?.Host == "calendar.google.com"
            && CalendarFeed.LinkUri("file:///c:/x.ics") is null && CalendarFeed.LinkUri("") is null);
        var id = Guid.NewGuid();
        var sources = CalendarFeed.Normalize([new() { Id = id, Name = " Work ", Color = "bad" }, new() { Id = id }, null, new() { Id = Guid.Empty }]);
        var live = CalendarFeed.Parse(Sample.Replace("\r\n", "\n").Replace("\n", "\r\n"), source, DateTime.Today.AddDays(-400), DateTime.Today.AddDays(400));
        check("Calendar: a range given in local time (DateTime.Today) is read too", live.Count(e => e.Title == "Standup") == 4);
        check("Calendar: a web page is told from an ICS calendar",
            CalendarFeed.IsCalendar("\uFEFFBEGIN:VCALENDAR\r\n") && !CalendarFeed.IsCalendar("<!doctype html><html>"));
        var soon = standups[1];
        check("Calendar: the notice is due within its lead, not before, not once started, not once dismissed",
            CalendarFeed.Due(events, soon.Start.AddMinutes(-9), 10, new HashSet<string>()) == soon
            && CalendarFeed.Due(events, soon.Start.AddMinutes(-11), 10, new HashSet<string>()) is null
            && CalendarFeed.Due(events, soon.Start, 10, new HashSet<string>()) is null
            && CalendarFeed.Due(events, soon.Start.AddMinutes(-9), 10, new HashSet<string> { soon.Id }) is null);
        check("Calendar: saved sources are repaired", sources.Count == 1 && sources[0].Name == "Work" && sources[0].Color == CalendarFeed.Palette[0]);
    }
}
