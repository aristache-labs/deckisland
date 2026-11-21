using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeckIsland.Core;
using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace DeckIsland;

// The island's readouts: each source below watches one thing and shows a HudMessage, if its switch in Settings is on. A source
// that the machine cannot give (no backlight, no battery, no Bluetooth) stays quiet.
public partial class App
{
    private static readonly TimeSpan HudHold = TimeSpan.FromMilliseconds(1800);
    private HudWindow? _hud;
    private readonly List<IDisposable> _hudSources = [];
    private readonly List<object> _hudKeep = [];

    private void ShowHud(HudMessage message)
    {
        _hud ??= new HudWindow();
        _hud.Present(message with { Hold = message.Hold ?? HudHold }, PanelTheme.Main);
    }

    private void InitializeHud()
    {
        StartLockKeys();
        StartVolume();
        StartBrightness();
        StartBattery();
        StartBluetooth();
        StartDownloads();
        StartSensors();
        StartNotifications();
    }

    // ----- Caps, Num and Scroll Lock -----
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);

    private void StartLockKeys()
    {
        var state = new Dictionary<string, bool>();
        (string Key, int Code)[] keys = [("caps", 0x14), ("num", 0x90), ("scroll", 0x91)];
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            foreach (var (key, code) in keys)
            {
                bool on = (GetKeyState(code) & 1) != 0;
                if (state.TryGetValue(key, out bool previous) && previous != on && Settings.HudLocks)
                    ShowHud(new($"lock:{key}", HudIcon.Lock, $"{HudFormat.LockName(key)} {(on ? "on" : "off")}", On: on));
                state[key] = on;
            }
        };
        timer.Start();
        _hudKeep.Add(timer);
    }

    // ----- Volume -----
    private void StartVolume()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var started = DateTime.UtcNow;
            float last = device.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool muted = device.AudioEndpointVolume.Mute;
            device.AudioEndpointVolume.OnVolumeNotification += data => Dispatcher.BeginInvoke(() =>
            {
                if (DateTime.UtcNow - started < TimeSpan.FromSeconds(2) || !Settings.HudVolume) return;
                if (Math.Abs(data.MasterVolume - last) < .0005f && data.Muted == muted) return;
                last = data.MasterVolume; muted = data.Muted;
                ShowHud(new("volume", muted ? HudIcon.Muted : HudIcon.Volume, muted ? "Muted" : "Volume", muted ? 0 : last));
            });
            _hudKeep.Add(device); _hudKeep.Add(enumerator);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException) { }
    }

    // ----- Brightness (laptop panels report it through WMI) -----
    private void StartBrightness()
    {
        try
        {
            var watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.EventArrived += (_, args) =>
            {
                if (args.NewEvent["Brightness"] is not byte level) return;
                Dispatcher.BeginInvoke(() => { if (Settings.HudBrightness) ShowHud(new("brightness", HudIcon.Brightness, "Brightness", level / 100.0)); });
            };
            watcher.Start();
            _hudSources.Add(new Disposer(watcher.Stop));
            _hudKeep.Add(watcher);
        }
        catch (Exception error) when (error is ManagementException or COMException or UnauthorizedAccessException) { }
    }

    // ----- Battery -----
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLine, Flag, Percent, Saver; public int LifeTime, FullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);

    private void StartBattery()
    {
        bool? plugged = null;
        int lowestAlert = 101;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            if (!GetSystemPowerStatus(out var status) || (status.Flag & 128) != 0 || status.Percent > 100) return;
            bool nowPlugged = status.AcLine == 1;
            int percent = status.Percent;
            if (plugged is { } before && before != nowPlugged && Settings.HudBattery)
                ShowHud(new("battery", nowPlugged ? HudIcon.Charging : HudIcon.Battery, nowPlugged ? "Charging" : "Unplugged", percent / 100.0, Figure: $"{percent}%"));
            plugged = nowPlugged;
            if (nowPlugged) { lowestAlert = 101; return; }
            foreach (int threshold in new[] { 20, 10, 5 })
                if (percent <= threshold && lowestAlert > threshold)
                {
                    lowestAlert = threshold;
                    if (Settings.HudBattery) ShowHud(new("battery", HudIcon.Battery, "Low battery", percent / 100.0, Figure: $"{percent}%", Hold: TimeSpan.FromSeconds(4)));
                    break;
                }
        };
        timer.Start();
        _hudKeep.Add(timer);
    }

    // ----- Bluetooth devices connecting and disconnecting -----
    private void StartBluetooth()
    {
        try
        {
            var watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            bool listed = false;
            watcher.EnumerationCompleted += (_, _) => listed = true;
            watcher.Added += async (_, info) =>
            {
                try
                {
                    var device = await BluetoothDevice.FromIdAsync(info.Id);
                    if (device is null) return;
                    _hudKeep.Add(device);
                    device.ConnectionStatusChanged += (sender, _) => Dispatcher.BeginInvoke(() =>
                    {
                        if (!listed || !Settings.HudBluetooth) return;
                        bool connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
                        int? percent = Devices?.All.FirstOrDefault(item => string.Equals(item.Name, sender.Name, StringComparison.OrdinalIgnoreCase))?.Percent;
                        ShowHud(new($"bluetooth:{sender.DeviceId}", HudIcon.Bluetooth, sender.Name,
                            Figure: connected ? (percent is { } value ? $"{value}%" : "Connected") : "Disconnected"));
                    });
                }
                catch (Exception error) when (error is COMException or InvalidOperationException) { }
            };
            watcher.Start();
            _hudSources.Add(new Disposer(() =>
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
            }));
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException) { }
    }

    // ----- Downloads: a browser's partial file grows, then takes its final name -----
    private static readonly string[] PartialExtensions = [".crdownload", ".part", ".download", ".opdownload", ".tmp"];

    private void StartDownloads()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(folder)) return;
        var partial = new Dictionary<string, (long Size, DateTime At, double Speed)>(StringComparer.OrdinalIgnoreCase);
        var done = new Queue<string>();
        var watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = true
        };
        static bool IsPartial(string path) => PartialExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        watcher.Renamed += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            if (partial.Remove(e.OldFullPath) && !IsPartial(e.FullPath)) done.Enqueue(Path.GetFileName(e.FullPath));
        });
        watcher.Created += (_, e) => Dispatcher.BeginInvoke(() => { if (IsPartial(e.FullPath)) partial[e.FullPath] = (0, DateTime.UtcNow, 0); });
        watcher.Deleted += (_, e) => Dispatcher.BeginInvoke(() => partial.Remove(e.FullPath));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool showing = false;
        timer.Tick += (_, _) =>
        {
            if (!Settings.HudDownloads)
            {
                partial.Clear(); done.Clear();
                if (showing) { _hud?.Clear("download"); showing = false; }
                return;
            }
            foreach (var path in partial.Keys.ToList())
            {
                long size;
                try { size = new FileInfo(path).Length; } catch (IOException) { continue; }
                var (before, at, _) = partial[path];
                double seconds = Math.Max(.2, (DateTime.UtcNow - at).TotalSeconds);
                partial[path] = (size, DateTime.UtcNow, Math.Max(0, (size - before) / seconds));
            }
            if (done.Count > 0)
            {
                ShowHud(new("download", HudIcon.Done, done.Dequeue(), Figure: "Downloaded", Hold: TimeSpan.FromSeconds(3)));
                showing = false;
                return;
            }
            var active = partial.Where(item => item.Value.Speed > 0).OrderByDescending(item => item.Value.Speed).ToList();
            if (active.Count == 0)
            {
                if (showing) { _hud?.Clear("download"); showing = false; }
                return;
            }
            string title = active.Count > 1 ? $"{active.Count} downloads" : Path.GetFileNameWithoutExtension(active[0].Key);
            ShowHud(new("download", HudIcon.Download, title, Figure: HudFormat.Speed(active.Sum(item => item.Value.Speed)), Hold: TimeSpan.Zero));
            showing = true;
        };
        timer.Start();
        _hudKeep.Add(timer);
        _hudSources.Add(watcher);
    }

    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private void StopHud()
    {
        foreach (var source in _hudSources)
        {
            try { source.Dispose(); }
            catch (Exception error) when (error is COMException or InvalidOperationException or ManagementException) { }
        }
        foreach (var item in _hudKeep) if (item is DispatcherTimer timer) timer.Stop();
        _hud?.Close();
    }
}
