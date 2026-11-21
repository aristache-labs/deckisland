using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;
using Microsoft.Win32;

namespace DeckIsland;

// External privacy activity comes from Windows and the call monitor. Calls reports its own previews directly.
public partial class App
{
    private const string ConsentRoot = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private IReadOnlyList<string> _sensorCameraApps = [], _sensorMicrophoneApps = [], _sensorScreenApps = [];
    private bool _unattributedCameraStream;

    private static IEnumerable<string> LiveApps(string capability)
    {
        using var root = Registry.CurrentUser.OpenSubKey(ConsentRoot + capability);
        if (root is null) yield break;
        foreach (var packaged in LiveKeys(root)) yield return packaged;
        using var loose = root.OpenSubKey("NonPackaged");
        if (loose is null) yield break;
        foreach (var name in LiveKeys(loose)) yield return name;
    }

    private static IEnumerable<string> LiveKeys(RegistryKey parent)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (name == "NonPackaged") continue;
            using var key = parent.OpenSubKey(name);
            if (key?.GetValue("LastUsedTimeStart") is long start && start > 0 && key.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                yield return HudFormat.AppName(name);
        }
    }

    private void StartSensors()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var camera = new CameraFrameServer();
        bool reading = false;
        timer.Tick += async (_, _) =>
        {
            if (Exiting || reading) return;
            if (!Settings.SensorIndicator) { UpdateSensorIndicator(); return; }

            reading = true;
            try
            {
                bool checkingOwnCamera = Decks.Any(deck => deck?.CameraPreviewActive == true);
                (_sensorCameraApps, _unattributedCameraStream, _sensorScreenApps) = await Task.Run(() =>
                {
                    var apps = ReadSensorApps("webcam").Where(name => !CallAudio.IsDeckIsland(name)).ToArray();
                    return (apps, !checkingOwnCamera && apps.Length == 0 && camera.IsActive(), ReadSensorApps("graphicsCaptureProgrammatic"));
                });
                UpdateSensorIndicator();
            }
            finally { reading = false; }
        };
        timer.Start();
        _hudKeep.Add(timer);
    }

    private static string[] ReadSensorApps(string capability)
    {
        try { return LiveApps(capability).ToArray(); }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return []; }
    }

    internal void UpdateSensorIndicator()
    {
        if (Exiting) return;
        var decks = Decks.Where(deck => deck is not null).ToArray();
        var uses = new List<string>();
        string? color = null;

        if (Settings.SensorIndicator)
        {
            bool preview = decks.Any(deck => deck.CameraPreviewActive);
            if (preview) _unattributedCameraStream = false;
            Add(Loc.T("摄像头", "Camera"), "#30D158", _sensorCameraApps, preview);
            if (!preview && _sensorCameraApps.Count == 0 && _unattributedCameraStream)
            {
                color = "#30D158";
                uses.Add(Loc.T("摄像头正在使用", "Camera in use"));
            }
            Add(Loc.T("麦克风", "Microphone"), "#FF9F0A", _sensorMicrophoneApps, decks.Any(deck => deck.MicrophoneTestActive));
            Add(Loc.T("屏幕", "Screen"), "#0A84FF", _sensorScreenApps);
        }
        string? description = uses.Count > 0 ? string.Join("\n", uses) : null;
        foreach (var deck in decks) deck.ShowSensorIndicator(color, description);

        void Add(string label, string tint, IReadOnlyList<string> apps, bool ownPreview = false)
        {
            // The preview lifetime is authoritative for this process, even if Windows still lists it after closing.
            var names = apps.Where(name => !CallAudio.IsDeckIsland(name));
            if (ownPreview) names = names.Append("DeckIsland");
            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (HudFormat.Ignored(Settings.SensorIgnoredApps, name)) continue;
                color ??= tint;
                uses.Add($"{label}: {name}");
            }
        }
    }
}

public partial class MainWindow
{
    internal void ShowSensorIndicator(string? color, string? description)
    {
        if (color is null)
        {
            SensorDot.Visibility = Visibility.Collapsed;
            return;
        }
        SensorDot.Fill = UI.Brush(color);
        if (!Equals(SensorDot.ToolTip, description))
        {
            SensorDot.ToolTip = description;
            AutomationProperties.SetName(SensorDot, description?.Replace('\n', ','));
        }
        SensorDot.Visibility = Visibility.Visible;
        PlaceSensorIndicator();
    }

    private void PlaceSensorIndicator()
    {
        if (SensorDot.Visibility != Visibility.Visible) return;
        double width = IslandContent.ActualWidth, height = IslandContent.ActualHeight;
        double size = Math.Min(8, Math.Min(width, height));
        SensorDot.Width = SensorDot.Height = size;
        double left = Math.Clamp((width - size) / 2, 0, _expanded ? 12 : 8);
        double top = Math.Clamp((height - size) / 2, 0, _expanded ? 10 : _frame.Across ? 8 : height / 2);
        SensorDot.Margin = new Thickness(left, top, 0, 0);
    }
}
