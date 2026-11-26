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

    // The collapsed island shows it in place and takes its width; a panel that cannot (open, hidden, a line) gets the floating one.
    private void ShowHud(HudMessage message)
    {
        message = message with { Hold = message.Hold ?? HudHold };
        // The quick readouts stay off full-screen apps and a hidden island unless "Show everywhere" is on.
        if (!Settings.HudEverywhere && EverywhereKind(message.Key) && !Decks.Any(deck => deck.IsVisible && !deck.IsSuppressed && !deck.OverFullScreen)) return;
        if (message.Key != "call" && message.Hold is { } hold && hold > TimeSpan.Zero) _hudTransientUntil = DateTime.UtcNow + hold;
        bool inline = false;
        foreach (var deck in Decks.ToList()) inline |= deck.ShowHudInline(message);
        if (inline || message.Key == "call" && Decks.Any(deck => deck.IsVisible && deck.IsExpanded && deck.CurrentPage == DeckPage.Calls))
        { _hud?.Clear(message.Key); return; }
        _hud ??= new HudWindow();
        _hud.Present(message, PanelTheme.Main, Settings.NotificationScrollSeconds);
    }

    private void ClearHud(string key)
    {
        foreach (var deck in Decks.ToList()) deck.ClearHud(key);
        _hud?.Clear(key);
    }

    private void InitializeHud()
    {
        StartLockKeys();
        StartVolume();
        StartBrightness();
        StartBattery();
        StartBluetooth();
        StartDownloads();
        StartCalls();
        StartSensors();
        StartNotifications();
        StartMoreHuds();
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
                    ShowHud(new($"lock:{key}", key switch { "num" => HudIcon.NumLock, "scroll" => HudIcon.ScrollLock, _ => HudIcon.Lock }, $"{HudFormat.LockName(key)} {(on ? "on" : "off")}", On: on, Lit: on, Reserve: $"{HudFormat.LockName(key)} off"));
                state[key] = on;
            }
            // Insert pressed alone: its toggle changed with neither Shift nor Ctrl down (Shift+Insert and Ctrl+Insert paste and copy).
            bool insert = (GetKeyState(0x2D) & 1) != 0;
            if (state.TryGetValue("insert", out bool was) && was != insert && Settings.HudInsert && (GetKeyState(0x10) & 0x8000) == 0 && (GetKeyState(0x11) & 0x8000) == 0)
                ShowHud(new("insert", HudIcon.Insert, insert ? "Insert on" : "Insert off", On: insert, Lit: insert, Reserve: "Insert off"));
            state["insert"] = insert;
        };
        timer.Start();
        _hudKeep.Add(timer);
    }

    // ----- Volume (follows the default output device when it changes) -----
    private void StartVolume()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            MMDevice? device = null;
            float last = 0;
            bool muted = false;
            DateTime attached = default;
            void Attach()
            {
                var next = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (device is not null && device.ID == next.ID) return;
                if (device is not null) device.AudioEndpointVolume.OnVolumeNotification -= Changed;
                device = next;
                last = next.AudioEndpointVolume.MasterVolumeLevelScalar;
                muted = next.AudioEndpointVolume.Mute;
                attached = DateTime.UtcNow;
                next.AudioEndpointVolume.OnVolumeNotification += Changed;
            }
            void Changed(AudioVolumeNotificationData data) => Dispatcher.BeginInvoke(() =>
            {
                if (DateTime.UtcNow - attached < TimeSpan.FromSeconds(1) || !Settings.HudVolume) return;
                if (Math.Abs(data.MasterVolume - last) < .0005f && data.Muted == muted) return;
                last = data.MasterVolume; muted = data.Muted;
                ShowHud(new("volume", muted ? HudIcon.Muted : HudIcon.Volume, "", muted ? 0 : last, Lit: !muted,
                    Label: muted ? "Muted" : $"Volume {Math.Round(last * 100)}%"));
            });
            Attach();
            var watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            watch.Tick += (_, _) => { try { Attach(); } catch (Exception error) when (error is COMException or InvalidOperationException) { } };
            watch.Start();
            _hudKeep.Add(watch); _hudKeep.Add(enumerator);
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
                Dispatcher.BeginInvoke(() => { if (Settings.HudBrightness) ShowHud(new("brightness", HudIcon.Brightness, "", level / 100.0, Lit: true, Label: $"Brightness {level}%")); });
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
        int? lastPercent = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            if (!GetSystemPowerStatus(out var status) || (status.Flag & 128) != 0 || status.Percent > 100) return;
            bool nowPlugged = status.AcLine == 1;
            int percent = status.Percent;
            if (plugged is { } before && before != nowPlugged && Settings.HudBattery)
                ShowHud(new("battery", nowPlugged ? HudIcon.Charging : HudIcon.Battery, nowPlugged ? "Charging" : "On battery", Figure: $"{percent}%", Lit: nowPlugged, IconLevel: percent / 100.0, FigureColor: percent <= 20 && !nowPlugged ? null : "#30D158"));
            plugged = nowPlugged;
            // Finished charging on the charger.
            if (nowPlugged && percent >= 100 && lastPercent is < 100 && Settings.HudBattery)
                ShowHud(new("battery", HudIcon.Charging, "Fully charged", Figure: "100%", Lit: true, IconLevel: 1, FigureColor: "#30D158"));
            lastPercent = percent;
            if (nowPlugged) { lowestAlert = 101; return; }
            foreach (int threshold in new[] { 20, 10, 5 })
                if (percent <= threshold && lowestAlert > threshold)
                {
                    lowestAlert = threshold;
                    if (Settings.HudBattery) ShowHud(new("battery", HudIcon.Battery, "Low battery", Figure: $"{percent}%", Hold: TimeSpan.FromSeconds(4), IconLevel: percent / 100.0));
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
                            Figure: connected ? (percent is { } value ? $"{value}%" : "Connected") : "Disconnected", Lit: connected));
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
        // A browser writes "Unconfirmed 874876.crdownload" until it knows the file's name, may rename that partial file again, and at the
        // end renames it to the name it keeps: the same file all along. A partial file that is deleted instead was canceled.
        var partial = new Dictionary<string, (long Size, DateTime At, double Speed)>(StringComparer.OrdinalIgnoreCase);
        // A partial file just renamed to a name that is not partial: the name settles once nothing renames it for a moment.
        var finishing = new Dictionary<string, (string Name, DateTime Since)>(StringComparer.OrdinalIgnoreCase);
        // A partial file just deleted: a finished file of about its size appearing within a moment was its end; otherwise it was canceled.
        var vanished = new List<(string Name, long Size, DateTime Since)>();
        var done = new Queue<(string Name, bool Canceled)>();
        var watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = true
        };
        static bool IsPartial(string path) => PartialExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        var settle = TimeSpan.FromMilliseconds(1200);
        watcher.Renamed += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            if (finishing.Remove(e.OldFullPath))
            {
                if (!IsPartial(e.FullPath)) finishing[e.FullPath] = (Path.GetFileName(e.FullPath), DateTime.UtcNow);
                return;
            }
            if (!partial.Remove(e.OldFullPath, out var entry)) return;
            if (IsPartial(e.FullPath)) partial[e.FullPath] = entry;
            else finishing[e.FullPath] = (Path.GetFileName(e.FullPath), DateTime.UtcNow);
        });
        watcher.Created += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            if (IsPartial(e.FullPath)) { partial[e.FullPath] = (0, DateTime.UtcNow, 0); return; }
            long size;
            try { size = new FileInfo(e.FullPath).Length; } catch (IOException) { return; }
            int match = vanished.FindIndex(item => item.Size > 0 && size >= item.Size * .9);
            if (match < 0) return;
            finishing[e.FullPath] = (Path.GetFileName(e.FullPath), DateTime.UtcNow);
            vanished.RemoveAt(match);
        });
        watcher.Deleted += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            finishing.Remove(e.FullPath);
            if (partial.Remove(e.FullPath, out var entry)) vanished.Add((Path.GetFileNameWithoutExtension(e.FullPath), entry.Size, DateTime.UtcNow));
        });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool showing = false;
        timer.Tick += (_, _) =>
        {
            if (!Settings.HudDownloads)
            {
                partial.Clear(); finishing.Clear(); vanished.Clear(); done.Clear();
                if (showing) { ClearHud("download"); showing = false; }
                return;
            }
            var now = DateTime.UtcNow;
            foreach (var path in partial.Keys.ToList())
            {
                long size;
                try { size = new FileInfo(path).Length; } catch (IOException) { continue; }
                var (before, at, _) = partial[path];
                double seconds = Math.Max(.2, (now - at).TotalSeconds);
                partial[path] = (size, now, Math.Max(0, (size - before) / seconds));
            }
            foreach (var (path, item) in finishing.Where(pair => now - pair.Value.Since >= settle).ToList())
            {
                finishing.Remove(path);
                done.Enqueue((item.Name, false));
            }
            foreach (var item in vanished.Where(item => now - item.Since >= settle).ToList())
            {
                vanished.Remove(item);
                done.Enqueue((item.Name, true));
            }
            if (done.Count > 0)
            {
                var (name, canceled) = done.Dequeue();
                ShowHud(new("download", canceled ? HudIcon.Cancel : HudIcon.Done, name, Figure: canceled ? "Canceled" : "Completed", Hold: TimeSpan.FromSeconds(3), Lit: !canceled,
                    Label: $"{(canceled ? "Download canceled" : "Download completed")}: {name}"));
                showing = false;
                return;
            }
            var active = partial.Where(item => item.Value.Speed > 0).OrderByDescending(item => item.Value.Speed).ToList();
            if (active.Count == 0)
            {
                if (showing) { ClearHud("download"); showing = false; }
                return;
            }
            // The name the browser gives the partial file so far ("Unconfirmed 874876", or the file's own once it is known).
            if (_callShown) { showing = false; return; }
            string title = active.Count > 1 ? $"{active.Count} downloads" : Path.GetFileNameWithoutExtension(active[0].Key);
            ShowHud(new("download", HudIcon.Download, title, Figure: HudFormat.Speed(active.Sum(item => item.Value.Speed)), Hold: TimeSpan.Zero, Lit: true));
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
