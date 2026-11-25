using System.Text.RegularExpressions;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace DeckIsland.Core;

// A calendar read from an ICS link (Google, Outlook, iCloud and others publish one). The link itself is a secret, kept by
// App.Secrets under the source's Id; the settings keep only its name, colour and host.
public sealed class CalendarSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#419CFF";
    public string Host { get; set; } = "";
}

// One occurrence of an event, in local time. Id is the event's UID with its start, so each repeat is its own.
public sealed record CalendarEvent(string Id, Guid Source, string Title, DateTime Start, DateTime End, bool AllDay, string Location, string? MeetingLink);

public static partial class CalendarFeed
{
    public const int MaxSources = 20, MaxEvents = 2000;
    // The minutes ahead the collapsed "Next event" shows an event from.
    public static readonly IReadOnlyList<int> Horizons = [30, 60, 120, 240, 480];
    public const int DefaultHorizon = 60;
    public static readonly IReadOnlyList<string> Palette = ["#419CFF", "#30D158", "#FF9F0A", "#FF453A", "#BF5AF2", "#FFD60A", "#64D2FF", "#FF375F"];

    // The first colour no source uses yet.
    public static string NextColor(IEnumerable<CalendarSource> sources) =>
        Palette.FirstOrDefault(color => !sources.Any(source => source.Color.Equals(color, StringComparison.OrdinalIgnoreCase))) ?? Palette[sources.Count() % Palette.Count];

    // An address as it is fetched: webcal:// is https://, a bare host is https; null for anything that is not an http(s) link.
    public static Uri? LinkUri(string? link)
    {
        string text = Regex.Replace((link ?? "").Trim(), "^webcals?://", "https://", RegexOptions.IgnoreCase);
        if (text.Length == 0 || text.Length > 2000) return null;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.Host.Length > 0 ? uri : null;
    }

    // Saved sources repaired: no duplicate ids, a valid colour, at most MaxSources.
    public static List<CalendarSource> Normalize(IEnumerable<CalendarSource?>? sources)
    {
        var kept = new List<CalendarSource>();
        foreach (var source in (sources ?? []).OfType<CalendarSource>())
        {
            if (source.Id == Guid.Empty || kept.Any(other => other.Id == source.Id)) continue;
            kept.Add(new CalendarSource
            {
                Id = source.Id, Name = (source.Name ?? "").Trim() is { Length: > 0 and <= 60 } name ? name : "",
                Color = Regex.IsMatch(source.Color ?? "", "^#[0-9A-Fa-f]{6}$") ? source.Color!.ToUpperInvariant() : NextColor(kept),
                Host = (source.Host ?? "").Length <= 200 ? source.Host ?? "" : ""
            });
            if (kept.Count == MaxSources) break;
        }
        return kept;
    }

    // Whether a downloaded text is an ICS calendar at all: a link to a calendar's web page (Google's public or embed address) gives HTML.
    public static bool IsCalendar(string text) => text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t').StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase);

    // The events of one ICS text that touch [from, to), repeats expanded, in local time; the soonest first.
    public static List<CalendarEvent> Parse(string ics, Guid source, DateTime from, DateTime to)
    {
        var calendar = Ical.Net.Calendar.Load(ics);
        if (calendar is null) return [];
        var events = new List<CalendarEvent>();
        // Occurrences come in start order from the start given; an all-day event or one that began before `from` still touches it.
        // CalDateTime takes no local DateTime (DateTime.Today is one): the start is passed as a plain date and time.
        foreach (var occurrence in calendar.GetOccurrences(new CalDateTime(DateTime.SpecifyKind(from.AddDays(-14), DateTimeKind.Unspecified)), null))
        {
            if (occurrence.Source is not Ical.Net.CalendarComponents.CalendarEvent item) continue;
            var period = occurrence.Period;
            bool allDay = !period.StartTime.HasTime;
            DateTime start = Local(period.StartTime);
            if (start >= to) break;
            DateTime end = period.EffectiveEndTime is { } last ? Local(last) : allDay ? start.AddDays(1) : start;
            if (end <= from && !(end == start && start >= from)) continue;
            string title = (item.Summary ?? "").Trim();
            events.Add(new CalendarEvent($"{item.Uid}@{start:O}", source, title, start, end, allDay, (item.Location ?? "").Trim(),
                MeetingLink(item.Url?.ToString(), item.Location, item.Description)));
            if (events.Count >= MaxEvents) break;
        }
        return events;
    }

    // A date without a time is that day; a time with a zone (or UTC) is moved to this PC's zone; a floating time is taken as local.
    private static DateTime Local(CalDateTime time) =>
        !time.HasTime ? time.Value.Date : time.IsFloating ? DateTime.SpecifyKind(time.Value, DateTimeKind.Local) : time.AsUtc.ToLocalTime();

    // The events that touch the local day (a zero-length event counts on its own day): all-day first, then by start.
    public static List<CalendarEvent> On(IEnumerable<CalendarEvent> events, DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        var next = start.AddDays(1);
        return [.. events.Where(e => e.Start < next && (e.End > start || e.End == e.Start && e.Start >= start))
            .OrderByDescending(e => e.AllDay).ThenBy(e => e.Start).ThenBy(e => e.End)];
    }

    // The next timed event that has not started yet, within `minutes` of now; null when none is.
    public static CalendarEvent? Next(IEnumerable<CalendarEvent> events, DateTime now, int minutes) =>
        events.Where(e => !e.AllDay && e.Start > now && e.Start - now <= TimeSpan.FromMinutes(minutes)).MinBy(e => e.Start);

    // The first https link to a Google Meet, Zoom, Teams or Webex meeting in the URL, location or description; null otherwise.
    public static string? MeetingLink(params string?[] texts)
    {
        foreach (var text in texts)
            foreach (Match match in Link().Matches(text ?? ""))
            {
                string raw = Regex.Replace(match.Value, @"[.,;:!?)\]}*]+$", "");
                if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0 && IsMeeting(uri)) return uri.AbsoluteUri;
            }
        return null;
    }
    private static bool IsMeeting(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        if (host is "meet.google.com" or "teams.microsoft.com" or "teams.live.com") return true;
        if (host.EndsWith(".zoom.us", StringComparison.Ordinal) || host.EndsWith(".webex.com", StringComparison.Ordinal)) return true;
        return host == "zoom.us" && Regex.IsMatch(uri.AbsolutePath, "^/(j|my)/");
    }
    [GeneratedRegex("https://[^\\s<>\"']+", RegexOptions.IgnoreCase)] private static partial Regex Link();
}
