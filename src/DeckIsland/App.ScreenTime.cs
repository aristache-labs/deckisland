using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

public partial class App
{
    private const int ScreenTimeStepSeconds = 5, ScreenTimeSaveEvery = 12;
    private readonly DispatcherTimer _screenTimer = new() { Interval = TimeSpan.FromSeconds(ScreenTimeStepSeconds) };
    private DateTime _screenTimeLast = DateTime.Now;
    private int _screenTimeUnsaved;

    private void InitializeScreenTime()
    {
        _screenTimeLast = DateTime.Now;
        _screenTimer.Tick += (_, _) => CountScreenTime();
        _screenTimer.Start();
    }

    // Nothing is watched unless a Screen time widget is on an island or the Screen Time page is shown: the app in front is only
    // asked for while one is.
    private bool CountsScreenTime => DockShows(DockWidgetKind.ScreenTime) || DockShows(DockWidgetKind.DayTimeline) || ShownSettings.Any(settings => settings.WidgetSlots.Contains(WidgetKind.ScreenTime) || settings.ScreenTimePage
        || settings.ShowsScreenTime(CompactStyle.Notch) || settings.ShowsScreenTime(CompactStyle.Capsule));
    // Today's total in whole minutes when the collapsed rows were last told: they are built again only when it changes.
    private int _screenTimeShownMinutes = -1;

    // One stretch counted for the app in front, unless the person is away (no key or mouse for ScreenTime.IdleAfter). The stretch
    // is the time since the last look, at most three steps, so a PC that slept does not count its night. Saved about once a minute.
    private void CountScreenTime()
    {
        var now = DateTime.Now;
        double gap = (now - _screenTimeLast).TotalSeconds;
        _screenTimeLast = now;
        if (Exiting || !CountsScreenTime) return;
        int seconds = (int)Math.Round(Math.Clamp(gap, 0, ScreenTimeStepSeconds * 3));
        ShowScreenTimeSummary(now);
        if (seconds == 0 || IdleFor() >= ScreenTime.IdleAfter) { _screenRunApp = null; return; }
        var (name, path) = ForegroundApp();
        if (name.Length == 0) return;
        string shown = ScreenTime.Display(name);
        // A new day: the one before joins the history before today's count starts.
        if (Settings.ScreenTimeDate != ScreenTime.DayKey(now))
            Settings.ScreenTimeHistory = ScreenTime.Archive(Settings.ScreenTimeHistory, Settings.ScreenTimeDate, Settings.ScreenTimeApps, Settings.ScreenTimeHours, now);
        Settings.ScreenTimeHours = ScreenTime.AddHour(Settings.ScreenTimeDate, Settings.ScreenTimeHours, now, seconds);
        var (date, apps) = ScreenTime.Add(Settings.ScreenTimeDate, Settings.ScreenTimeApps, now, shown, seconds);
        Settings.ScreenTimeDate = date;
        Settings.ScreenTimeApps = apps;
        // The extras: a change of app (not the first look after a break), the unbroken stretch in this app, and the site in front.
        bool switched = _screenRunApp is not null && !string.Equals(_screenRunApp, shown, StringComparison.OrdinalIgnoreCase);
        if (_screenRunApp is null || switched) { _screenRunApp = shown; _screenRun = 0; }
        _screenRun += seconds;
        string? site = Settings.ScreenTimeSites && ScreenTimeExtras.Category(shown) == ScreenCategory.Browser ? ScreenTimeExtras.SiteFromTitle(ForegroundTitle()) : null;
        Settings.ScreenTimeDetails = ScreenTimeExtras.Record(Settings.ScreenTimeDetails, now, shown, seconds, switched, _screenRun, site);
        int minutes = ScreenTime.Total(Settings.ScreenTimeApps) / 60;
        if (minutes != _screenTimeShownMinutes)
        {
            _screenTimeShownMinutes = minutes;
            foreach (var deck in Decks) deck.ScreenTimeMinuteChanged();
        }
        if (path.Length > 0 && !Settings.ScreenTimePaths.ContainsKey(shown)) Settings.ScreenTimePaths = new(Settings.ScreenTimePaths, StringComparer.OrdinalIgnoreCase) { [shown] = path };
        if (++_screenTimeUnsaved < ScreenTimeSaveEvery) return;
        _screenTimeUnsaved = 0;
        QueueSave();
    }

    // Every day's screen time is deleted, today's and the history's.
    internal void ResetScreenTime()
    {
        Settings.ScreenTimeDate = "";
        Settings.ScreenTimeApps = [];
        Settings.ScreenTimeHours = [.. new int[24]];
        Settings.ScreenTimeHistory = [];
        Settings.ScreenTimePaths = [];
        Settings.ScreenTimeDetails = [];
        _screenTimeShownMinutes = -1;
        foreach (var deck in Decks) deck.ScreenTimeMinuteChanged();
        QueueSave();
    }

    // The dock's Screen Time panel changes these directly: counting websites, and the daily summary and its time.
    internal void SetScreenTimeOptions(bool? sites = null, bool? summary = null, int? summaryMinute = null)
    {
        if (sites is { } countSites) Settings.ScreenTimeSites = countSites;
        if (summary is { } on) Settings.ScreenTimeSummary = on;
        if (summaryMinute is { } minute) Settings.ScreenTimeSummaryMinute = ((minute % 1440) + 1440) % 1440;
        QueueSave();
    }

    private string? _screenRunApp;
    private int _screenRun;

    // The day's summary at its time, once a day; on Mondays the past week's too.
    private void ShowScreenTimeSummary(DateTime now)
    {
        string today = ScreenTime.DayKey(now);
        if (!Settings.ScreenTimeSummary || Settings.ScreenTimeSummaryShown == today || now.Hour * 60 + now.Minute < Settings.ScreenTimeSummaryMinute) return;
        Settings.ScreenTimeSummaryShown = today;
        var day = ScreenTime.Day(DateOnly.FromDateTime(now), Settings.ScreenTimeDate, Settings.ScreenTimeApps, Settings.ScreenTimeHours, Settings.ScreenTimeHistory, now);
        int total = ScreenTime.Total(day.Apps);
        if (total > 0)
            DockAlert("screen-time-day", Loc.T($"今天的屏幕时间：{ScreenTime.Duration(total)}", $"Screen time today: {ScreenTime.Duration(total)}"),
                day.Apps.Count > 0 ? Loc.T($"最常用：{day.Apps[0].Key} {ScreenTime.Duration(day.Apps[0].Value)}", $"Most used: {day.Apps[0].Key} {ScreenTime.Duration(day.Apps[0].Value)}") : null);
        if (now.DayOfWeek != DayOfWeek.Monday) return;
        int week = Enumerable.Range(1, 7).Sum(back => ScreenTime.Total(ScreenTime.Day(DateOnly.FromDateTime(now).AddDays(-back), Settings.ScreenTimeDate,
            Settings.ScreenTimeApps, Settings.ScreenTimeHours, Settings.ScreenTimeHistory, now).Apps));
        if (week > 0)
            DockAlert("screen-time-week", Loc.T($"上周屏幕时间：{ScreenTime.Duration(week)}", $"Screen time last week: {ScreenTime.Duration(week)}"),
                Loc.T($"每天平均 {ScreenTime.Duration(week / 7)}", $"{ScreenTime.Duration(week / 7)} a day on average"));
    }

    // The title of the window in front, for the site name only (ScreenTimeExtras.SiteFromTitle); nothing else of it is kept.
    private static string ForegroundTitle()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return "";
        var text = new System.Text.StringBuilder(512);
        return GetWindowText(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int length);

    private static TimeSpan IdleFor()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Time; }
    [DllImport("user32")] private static extern bool GetLastInputInfo(ref LastInput info);
}
