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

    // Nothing is watched unless a Screen time widget is on an island: the app in front is only asked for while it is.
    private bool CountsScreenTime => ShownSettings.Any(settings => settings.WidgetSlots.Contains(WidgetKind.ScreenTime));

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
        var (name, _) = ForegroundApp();
        if (name.Length == 0) return;
        var (date, apps) = ScreenTime.Add(Settings.ScreenTimeDate, Settings.ScreenTimeApps, now, ScreenTime.Display(name), seconds);
        Settings.ScreenTimeDate = date;
        Settings.ScreenTimeApps = apps;
        if (++_screenTimeUnsaved < ScreenTimeSaveEvery) return;
        _screenTimeUnsaved = 0;
        QueueSave();
    }

    internal void ResetScreenTime()
    {
        Settings.ScreenTimeDate = "";
        Settings.ScreenTimeApps = [];
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
