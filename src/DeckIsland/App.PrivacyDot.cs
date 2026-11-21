using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeckIsland.Core;
using Microsoft.Win32;

namespace DeckIsland;

// A dot at the island's edge while the microphone, camera or screen capture is live. Windows keeps who uses what in its privacy
// store (CapabilityAccessManager): an entry whose LastUsedTimeStop is 0 is in use now. Only the program names are read.
public partial class App
{
    private const string ConsentRoot = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private static readonly (string Capability, string Label, Color Color)[] Sensors =
    [
        ("webcam", "Camera", Color.FromRgb(0x30, 0xD1, 0x58)),
        ("microphone", "Microphone", Color.FromRgb(0xFF, 0x9F, 0x0A)),
        ("graphicsCaptureProgrammatic", "Screen", Color.FromRgb(0x0A, 0x84, 0xFF))
    ];
    private PrivacyDotWindow? _privacyDot;

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
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            if (!Settings.SensorIndicator) { _privacyDot?.Hide(); return; }
            List<(string Label, Color Color, string App)> live = [];
            try
            {
                foreach (var (capability, label, color) in Sensors)
                    foreach (var app in LiveApps(capability).Where(app => !HudFormat.Ignored(Settings.SensorIgnoredApps, app)).Distinct())
                        live.Add((label, color, app));
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            if (live.Count == 0) { _privacyDot?.Hide(); return; }
            _privacyDot ??= new PrivacyDotWindow();
            _privacyDot.Present(live[0].Color, string.Join("\n", live.Select(item => $"{item.Label}: {item.App}")), Deck);
        };
        timer.Start();
        _hudKeep.Add(timer);
        _hudSources.Add(new Disposer(() => _privacyDot?.Close()));
    }
}

internal sealed class PrivacyDotWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);

    private readonly Ellipse _dot = new() { Width = 10, Height = 10 };

    internal PrivacyDotWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Width = 22; Height = 22;
        Content = new Grid { Background = Brushes.Transparent, Children = { _dot } };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x80);
        };
    }

    internal void Present(Color color, string who, Window island)
    {
        _dot.Fill = new SolidColorBrush(color);
        ToolTip = who;
        System.Windows.Automation.AutomationProperties.SetName(this, who.Replace('\n', ',') + " in use");
        // Only beside the collapsed island; an open one is already showing the pages.
        if (island.ActualHeight > 90 || !island.IsVisible) { Hide(); return; }
        Left = island.Left + island.ActualWidth - 20;
        Top = island.Top + Math.Max(0, (island.ActualHeight - Height) / 2);
        if (!IsVisible) Show();
    }
}
