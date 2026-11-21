using System.Diagnostics;
using System.Runtime.InteropServices;
using DeckIsland.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DeckIsland.Native;

internal static class CallAudio
{
    internal static bool IsUnavailable(Exception error) =>
        error is COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;

    internal static bool IsDeckIsland(string name) => name.Equals("DeckIsland", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("DeckIsland.Notifications_", StringComparison.OrdinalIgnoreCase);

    // Query active input sessions. A process playing sound on an output device does not count as a call.
    internal static List<string> ActiveInputApps()
    {
        var names = new List<string>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        var manager = device.AudioSessionManager;
                        try
                        {
                            ReadSessions(manager, names);
                        }
                        finally { manager.Dispose(); }
                    }
                    catch (Exception error) when (IsUnavailable(error)) { }
                }
            }
        }
        catch (Exception error) when (IsUnavailable(error)) { }
        return names;
    }

    private static void ReadSessions(AudioSessionManager manager, List<string> names)
    {
        var sessions = manager.Sessions;
        for (int index = 0; index < sessions.Count; index++)
        {
            try
            {
                using var session = sessions[index];
                if (session.State != AudioSessionState.AudioSessionStateActive) continue;
                uint pid = session.GetProcessID;
                if (pid == 0 || pid == Environment.ProcessId || pid > int.MaxValue) continue;
                using var process = Process.GetProcessById((int)pid);
                names.Add(process.ProcessName);
            }
            catch (Exception error) when (IsUnavailable(error)) { }
        }
    }

    internal static string? MeetingOwner(IEnumerable<string> active, IEnumerable<string> ignored, string current)
    {
        var names = active.Where(name => !string.IsNullOrWhiteSpace(name)
                && !IsDeckIsland(name)
                && !HudFormat.Ignored(ignored, name))
            .Select(DisplayName)
            .Where(name => !HudFormat.Ignored(ignored, name))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return names.OrderByDescending(IsMeetingClient)
            .ThenByDescending(name => name.Equals(current, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    private static bool IsMeetingClient(string name) => name is "Microsoft Teams" or "Zoom" or "Webex" or "Slack" or "Discord";

    private static string DisplayName(string name)
    {
        string app = name.Split('_', 2)[0].ToLowerInvariant();
        return app switch
        {
            "teams" or "ms-teams" or "msteams" or "microsoftteams" => "Microsoft Teams",
            "zoom" or "zoom.us" => "Zoom",
            "webex" or "webexhost" or "ciscocollabhost" => "Webex",
            "slack" => "Slack",
            "discord" => "Discord",
            "microsoft.screensketch" or "snippingtool" => "SnippingTool",
            _ => name
        };
    }
}
