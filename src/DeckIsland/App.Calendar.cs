using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

// The calendars (CalendarFeed): every source's ICS link fetched while the Calendar page, the collapsed next event or the Today widget
// is on, every PollEvery, and on demand. The links are secrets (App.Secrets under each source's Id); the events stay in memory.
public partial class App
{
    private static readonly TimeSpan CalendarPollEvery = TimeSpan.FromMinutes(12);
    private const int CalendarMaxBytes = 8 * 1024 * 1024;
    private static readonly HttpClient CalendarHttp = new(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
    { Timeout = TimeSpan.FromSeconds(20), DefaultRequestHeaders = { { "User-Agent", "DeckIsland calendar" } } };
    private readonly DispatcherTimer _calendarPoll = new() { Interval = CalendarPollEvery };
    private readonly DispatcherTimer _calendarMinute = new() { Interval = TimeSpan.FromMinutes(1) };
    private int _calendarCall;
    private string? _calendarNextShown;
    // Notices closed with their ✕ (or Join): not shown again while DeckIsland runs.
    private readonly HashSet<string> _calendarDismissed = [];

    internal IReadOnlyList<CalendarEvent> CalendarEvents { get; private set; } = [];
    // Per source, why its last fetch failed; a source that answered has none.
    internal IReadOnlyDictionary<Guid, string> CalendarErrors { get; private set; } = new Dictionary<Guid, string>();
    internal DateTime? CalendarFetched { get; private set; }
    internal bool CalendarLoading { get; private set; }

    private void InitializeCalendar()
    {
        SetCalendarEvents([]);
        _calendarPoll.Tick += (_, _) => { if (CalendarWanted) _ = RefreshCalendar(); };
        _calendarPoll.Start();
        // The next event comes into the horizon and goes when it starts: the collapsed rows look once a minute.
        _calendarMinute.Tick += (_, _) => CalendarMinute();
        _calendarMinute.Start();
        if (CalendarWanted) _ = RefreshCalendar();
    }

    private bool CalendarWanted => Settings.CalendarSources.Count > 0 && (DockWantsCalendar || ShownSettings.Any(settings => settings.CalendarPage
        || settings.ShowsNextEvent(CompactStyle.Notch) || settings.ShowsNextEvent(CompactStyle.Capsule) || settings.WidgetSlots.Contains(WidgetKind.Today)));

    // Fetched when it is older than `older`, or always when null.
    internal void RefreshCalendarIfOld(TimeSpan older)
    {
        if (!CalendarLoading && Settings.CalendarSources.Count > 0 && (CalendarFetched is not { } at || DateTime.Now - at >= older)) _ = RefreshCalendar();
    }

    // Every source read again, in parallel; the newest call wins should two overlap. A source that fails keeps no events and its error.
    internal async Task RefreshCalendar()
    {
        int call = ++_calendarCall;
        var sources = Settings.CalendarSources.ToList();
        CalendarLoading = true; CalendarChanged();
        var from = DateTime.Today.AddDays(-45); var to = DateTime.Today.AddDays(75);
        var results = await Task.WhenAll(sources.Select(async source =>
        {
            try
            {
                var uri = CalendarFeed.LinkUri(Secrets.Read(source.Id)) ?? throw new InvalidOperationException(Loc.T("链接无效", "The link is not valid"));
                using var response = await CalendarHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}");
                if (response.Content.Headers.ContentLength > CalendarMaxBytes) throw new InvalidOperationException(Loc.T("日历太大", "The calendar is too large"));
                string text = await response.Content.ReadAsStringAsync();
                if (text.Length > CalendarMaxBytes) throw new InvalidOperationException(Loc.T("日历太大", "The calendar is too large"));
                if (!CalendarFeed.IsCalendar(text))
                    throw new InvalidOperationException(Loc.T("这个链接打开的是网页，不是 ICS 日历。在 Google 日历中请使用“iCal 格式的私密地址”。",
                        "This link opens a web page, not an ICS calendar. In Google Calendar, use the \"Secret address in iCal format\"."));
                // The text is from outside: whatever the parser throws on a broken file is a calendar that cannot be read.
                var events = await Task.Run(() =>
                {
                    try { return CalendarFeed.Parse(text, source.Id, from, to); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { throw new FormatException(Loc.T("无法读取日历", "Could not read the calendar"), ex); }
                });
                return (source.Id, Events: events, Error: (string?)null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException or ArgumentException
                or System.Security.Cryptography.CryptographicException or IOException)
            {
                // The message never carries the link: it is a secret.
                string reason = ex is TaskCanceledException ? Loc.T("超时", "Timed out") : ex is HttpRequestException http && http.StatusCode is { } code ? $"HTTP {(int)code}"
                    : ex is HttpRequestException ? Loc.T("无法连接", "Could not connect") : ex is InvalidOperationException ? ex.Message : Loc.T("无法读取日历", "Could not read the calendar");
                return (source.Id, Events: new List<CalendarEvent>(), Error: (string?)reason);
            }
        }));
        if (call != _calendarCall) return;
        SetCalendarEvents(results.SelectMany(result => result.Events));
        CalendarErrors = results.Where(result => result.Error is not null).ToDictionary(result => result.Id, result => result.Error!);
        CalendarFetched = DateTime.Now; CalendarLoading = false;
        _calendarNextShown = null;
        CalendarChanged();
    }

    private void SetCalendarEvents(IEnumerable<CalendarEvent> subscribed)
    {
        CalendarEvents = [.. subscribed.Concat(Settings.LocalCalendarEvents.Select(item => item.ToCalendarEvent())).OrderBy(item => item.Start)];
    }

    internal bool AddLocalCalendarEvent(LocalCalendarEvent item)
    {
        if (Settings.LocalCalendarEvents.Count >= CalendarFeed.MaxEvents) return false;
        Settings.LocalCalendarEvents = [.. Settings.LocalCalendarEvents, item];
        SetCalendarEvents(CalendarEvents.Where(item => item.Source != Guid.Empty));
        QueueSave();
        _calendarNextShown = null;
        CalendarChanged();
        return true;
    }

    // A source removed: its link leaves the secret store, and its events the list.
    internal void ForgetCalendar(Guid id)
    {
        Secrets.Delete(id);
        CalendarEvents = [.. CalendarEvents.Where(e => e.Source != id)];
        CalendarErrors = CalendarErrors.Where(pair => pair.Key != id).ToDictionary(pair => pair.Key, pair => pair.Value);
        _calendarNextShown = null;
        CalendarChanged();
    }

    internal void DismissCalendarNotice(string id) { _calendarDismissed.Add(id); CalendarNoticeStep(); }

    private void CalendarMinute()
    {
        CalendarNoticeStep();
        string next = CalendarFeed.Next(CalendarEvents, DateTime.Now, Settings.CalendarHorizon)?.Id ?? "";
        if (next == _calendarNextShown) return;
        _calendarNextShown = next;
        foreach (var deck in Decks) deck.CalendarChanged(page: false);
    }

    // The notice before an event: every panel's collapsed row shows it from CalendarNoticeMinutes before the start until the start,
    // unless its ✕ was pressed (DismissCalendarNotice).
    private void CalendarNoticeStep()
    {
        var due = Settings.CalendarNotice ? CalendarFeed.Due(CalendarEvents, DateTime.Now, Settings.CalendarNoticeMinutes, _calendarDismissed) : null;
        foreach (var deck in Decks) deck.ShowCalendarNotice(due);
    }

    private void CalendarChanged()
    {
        CalendarNoticeStep();
        foreach (var deck in Decks) deck.CalendarChanged(page: true);
    }
}
