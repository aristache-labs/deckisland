using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Animation;
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
            // The call pill shows the camera too, so it is read while that is on even without the indicator.
            if (!Settings.SensorIndicator && !Settings.CallTimer) { UpdateSensorIndicator(); return; }

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

    // Whether a camera films for an app the user has not set aside: the call pill shows it beside the microphone.
    internal bool CameraLive => _unattributedCameraStream || _sensorCameraApps.Any(name => !HudFormat.Ignored(Settings.SensorIgnoredApps, name));

    internal void UpdateSensorIndicator()
    {
        if (Exiting) return;
        var decks = Decks.Where(deck => deck is not null).ToArray();
        var uses = new List<string>();
        var lit = new List<(HudIcon Icon, string Color)>();

        if (Settings.SensorIndicator)
        {
            bool preview = decks.Any(deck => deck.CameraPreviewActive);
            if (preview) _unattributedCameraStream = false;
            Add(Loc.T("摄像头", "Camera"), HudIcon.Camera, "#30D158", _sensorCameraApps, preview);
            if (!preview && _sensorCameraApps.Count == 0 && _unattributedCameraStream)
            {
                Light(HudIcon.Camera, "#30D158");
                uses.Add(Loc.T("摄像头正在使用", "Camera in use"));
            }
            Add(Loc.T("麦克风", "Microphone"), HudIcon.Mic, "#FF9F0A", _sensorMicrophoneApps, decks.Any(deck => deck.MicrophoneTestActive));
            Add(Loc.T("屏幕", "Screen"), HudIcon.Display, "#0A84FF", _sensorScreenApps);
        }
        string? description = uses.Count > 0 ? string.Join("\n", uses) : null;
        foreach (var deck in decks) deck.ShowSensorIndicator(lit, description);

        void Light(HudIcon icon, string tint) { if (!lit.Exists(item => item.Icon == icon)) lit.Add((icon, tint)); }
        void Add(string label, HudIcon icon, string tint, IReadOnlyList<string> apps, bool ownPreview = false)
        {
            // The preview lifetime is authoritative for this process, even if Windows still lists it after closing.
            var names = apps.Where(name => !CallAudio.IsDeckIsland(name));
            if (ownPreview) names = names.Append("DeckIsland");
            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (HudFormat.Ignored(Settings.SensorIgnoredApps, name)) continue;
                Light(icon, tint);
                uses.Add($"{label}: {name}");
            }
        }
    }
}

// The sensors in use as small icons in their colours (the camera green, the microphone orange, the screen blue), breathing slowly. Collapsed,
// only the first stands in the free room left of the content; open, all of them stand right of the settings gear. A readout on the island
// (a notification, the call pill) speaks for itself, so they step aside while it shows.
public partial class MainWindow
{
    private IReadOnlyList<(HudIcon Icon, string Color)> _sensors = [];
    private bool _sensorPulse;
    // Whether the collapsed surface is widened for the icon: the content keeps its room and the icon gets its own at the start.
    private bool _sensorRoom;

    // The icon's room: the surface grows by it at both ends, so the content stays centred and nothing is crowded (a column on a side
    // edge and the line keep their shape; the icon then stands where there is room).
    private CollapsedShape SensorRoom(CollapsedShape shape)
    {
        if (!_sensorRoom || ColumnFor(shape.Style) || shape.Style is not (CompactStyle.Notch or CompactStyle.Capsule)) return shape;
        double room = Math.Round(SensorRoomWidth * CompactMusicLayout.SafeScale(shape.Scale));
        return shape with { Surface = shape.Surface with { X = Math.Max(0, shape.Surface.X - room), Width = shape.Surface.Width + 2 * room }, Inset = shape.Inset + room };
    }
    private const double SensorRoomWidth = 16;

    internal void ShowSensorIndicator(IReadOnlyList<(HudIcon Icon, string Color)> lit, string? description)
    {
        if (!_sensors.SequenceEqual(lit)) { _sensors = lit; SensorIcons.Children.Clear(); }
        if (!Equals(SensorIcons.ToolTip, description))
        {
            SensorIcons.ToolTip = description;
            // SetName throws on null or empty; with nothing in use the name is cleared instead.
            if (string.IsNullOrEmpty(description)) SensorIcons.ClearValue(AutomationProperties.NameProperty);
            else AutomationProperties.SetName(SensorIcons, description.Replace('\n', ','));
        }
        PlaceSensorIndicator();
    }

    private void PlaceSensorIndicator()
    {
        bool room = _sensors.Count > 0 && _hudMessage is null;
        if (room != _sensorRoom)
        {
            _sensorRoom = room;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitCompactToContent);
        }
        double width = IslandContent.ActualWidth, height = IslandContent.ActualHeight;
        double size = Math.Floor(Math.Min(_expanded ? 14 : 12, Math.Min(width, height)));
        bool show = _sensors.Count > 0 && size >= 4 && (_expanded || _hudMessage is null);
        SensorIcons.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        int count = _expanded ? _sensors.Count : 1;
        const double Gap = 4;
        if (SensorIcons.Children.Count != count || SensorIcons.Children[0] is not FrameworkElement { Width: var built } || built != size)
        {
            SensorIcons.Children.Clear();
            for (int i = 0; i < count; i++)
            {
                var icon = HudGlyph.Line(_sensors[i].Icon, _sensors[i].Color, size, size / 13);
                if (i > 0) icon.Margin = new Thickness(Gap, 0, 0, 0);
                SensorIcons.Children.Add(icon);
            }
        }
        if (!_sensorPulse)
        {
            _sensorPulse = true;
            var pulse = new DoubleAnimation(1, .45, TimeSpan.FromMilliseconds(1100)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            // A slow breath needs few frames; the layered window is redrawn only that often for it.
            Timeline.SetDesiredFrameRate(pulse, 24);
            SensorIcons.BeginAnimation(OpacityProperty, pulse);
        }
        double span = count * size + (count - 1) * Gap;
        // Open: just right of the settings gear, on its middle, clear of the gear and the page switch.
        if (_expanded && SettingsButton.ActualWidth > 0)
        {
            var gear = SettingsButton.TranslatePoint(new Point(SettingsButton.ActualWidth, SettingsButton.ActualHeight / 2), IslandContent);
            SensorIcons.Margin = new Thickness(Math.Clamp(gear.X + 4, 0, Math.Max(0, width - span)), Math.Clamp(gear.Y - size / 2, 0, Math.Max(0, height - size)), 0, 0);
            return;
        }
        // Collapsed: centred in the room between the body's start and the content, along the edge; a notch's shoulders flare outside its
        // body (IslandBorder.BuildOutline), so the room starts after them. On a side edge the content stands upright and the edge runs down it.
        double shoulder = !_expanded && double.IsFinite(Collapsed.Shoulder) ? Math.Max(0, Collapsed.Shoulder) : 0;
        var content = Compact.IsVisible && Compact.ActualWidth > 0 ? Compact.TranslatePoint(new Point(), IslandContent) : new Point(double.NaN, double.NaN);
        double Along(double contentStart, double length)
        {
            double room = contentStart - shoulder;
            double at = double.IsFinite(room) && room >= size + 4 ? shoulder + (room - size) / 2 : shoulder + 6;
            return Math.Clamp(at, 0, Math.Max(0, length - size));
        }
        double Across(double length) => Math.Max(0, (length - size) / 2);
        SensorIcons.Margin = _frame.Across ? new Thickness(Across(width), Along(content.Y, height), 0, 0) : new Thickness(Along(content.X, width), Across(height), 0, 0);
    }
}
