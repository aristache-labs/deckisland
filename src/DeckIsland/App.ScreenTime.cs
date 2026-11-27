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
    private bool CountsScreenTime => DockShows(DockWidgetKind.ScreenTime) || ShownSettings.Any(settings => settings.WidgetSlots.Contains(WidgetKind.ScreenTime) || settings.ScreenTimePage
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
        if (seconds == 0 || IdleFor() >= ScreenTime.IdleAfter) return;
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
        _screenTimeShownMinutes = -1;
        foreach (var deck in Decks) deck.ScreenTimeMinuteChanged();
        QueueSave();
    }

    private static TimeSpan IdleFor()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Time; }
    [DllImport("user32")] private static extern bool GetLastInputInfo(ref LastInput info);
}
