using System.IO;
using System.Media;
using DeckIsland.Core;

namespace DeckIsland;

// The dock's automations (DockSettings.MeetingMode, AiAlerts, HealthAlerts, SunsetDarkMode), checked every half minute while the dock
// runs. Alerts show in the island's readout (ShowHud), with a tone when chosen, and none during quiet hours.
public partial class App
{
    private static readonly TimeSpan AutomationTick = TimeSpan.FromSeconds(30), MeetingLead = TimeSpan.FromMinutes(5), HealthRepeat = TimeSpan.FromMinutes(30);
    private const double AiAlertPercent = 90, HotCpu = 90, FullDrive = 0.05;
    private System.Windows.Threading.DispatcherTimer? _automations;
    private readonly HashSet<string> _meetingsWarned = [], _limitsWarned = [];
    private bool? _dockDaylight;
    private DateTime _hotWarned = DateTime.MinValue, _driveWarned = DateTime.MinValue;

    // Daylight at the weather's place for Sunset dark mode; null while the place is not known.
    internal bool? DockDaylight
    {
        get
        {
            if (CurrentWeather is not { } weather) return null;
            var now = DateTime.Now;
            return SunMoon.Sun(DateOnly.FromDateTime(now), weather.Place.Latitude, weather.Place.Longitude) is { } sun
                ? now.ToUniversalTime() >= sun.Rise && now.ToUniversalTime() < sun.Set : weather.Daylight;
        }
    }

    private void ApplyAutomations()
    {
        if (_docks.Count == 0) { _automations?.Stop(); return; }
        if (_automations is null)
        {
            _automations = new System.Windows.Threading.DispatcherTimer { Interval = AutomationTick };
            _automations.Tick += (_, _) => RunAutomations();
        }
        _automations.Start();
    }

    private void RunAutomations()
    {
        var settings = DockSettings;
        var now = DateTime.Now;
        if (settings.MeetingMode && DockSmart.DueMeeting(CalendarEvents, now, MeetingLead, _meetingsWarned) is { } meeting)
        {
            _meetingsWarned.Add(meeting.Id);
            bool paused = false;
            if (Media is { Current: { State: MediaState.Playing } track } media) { _ = media.CommandAsync(track.Id, MediaCommand.PlayPause); paused = true; }
            DockAlert("dock-meeting", Loc.T($"{(int)Math.Ceiling((meeting.Start - now).TotalMinutes)} 分钟后：{meeting.Title}", $"In {(int)Math.Ceiling((meeting.Start - now).TotalMinutes)} min: {meeting.Title}"),
                paused ? Loc.T("音乐已暂停", "Music paused") : null);
        }
        if (settings.AiAlerts)
            foreach (var snapshot in Deck?.Snapshots ?? [])
                foreach (var quota in snapshot.Quotas.Where(q => q.UsedPercent >= AiAlertPercent))
                {
                    // Once per limit until it resets.
                    string key = $"{snapshot.Id}|{quota.Label}|{quota.ResetAt:O}";
                    if (!_limitsWarned.Add(key)) continue;
                    DockAlert("dock-ai", Loc.T($"{snapshot.Name} {Loc.Label(quota.Label)} 已用 {Math.Round(quota.UsedPercent)}%", $"{snapshot.Name} {Loc.Label(quota.Label)}: {Math.Round(quota.UsedPercent)}% used"),
                        quota.ResetAt is { } reset ? Loc.T($"{reset.LocalDateTime:t} 重置", $"Resets at {reset.LocalDateTime:t}") : null);
                }
        if (settings.HealthAlerts)
        {
            if (DockMonitor?.Cpu.OrDefault?.Temperature is >= HotCpu and var temperature && now - _hotWarned > HealthRepeat)
            {
                _hotWarned = now;
                DockAlert("dock-health", Loc.T($"处理器温度 {Math.Round(temperature)}°C", $"Processor at {Math.Round(temperature)} °C"), null);
            }
            if (now - _driveWarned > TimeSpan.FromDays(1) && SystemDriveFree() is < FullDrive and var free)
            {
                _driveWarned = now;
                DockAlert("dock-health", Loc.T("系统盘快满了", "The system drive is almost full"), Loc.T($"剩余 {free:P0}", $"{free:P0} free"));
            }
        }
        // Sunset dark mode changes the look as day turns to night.
        if (settings.SunsetDarkMode && DockDaylight != _dockDaylight) { _dockDaylight = DockDaylight; foreach (var dock in _docks) dock.Apply(); }
    }

    private static double? SystemDriveFree()
    {
        try { var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"); return (double)drive.AvailableFreeSpace / drive.TotalSize; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    // An alert in the readout, with a tone when chosen; nothing during quiet hours.
    internal void DockAlert(string key, string text, string? body)
    {
        var settings = DockSettings;
        if (settings.QuietHours && DockSmart.Quiet(DateTime.Now, settings.QuietFrom, settings.QuietTo)) return;
        ShowHud(new HudMessage(key, HudIcon.Notice, text, Body: body, Hold: TimeSpan.FromSeconds(6)));
        if (settings.AlertSound) SystemSounds.Asterisk.Play();
    }
}
