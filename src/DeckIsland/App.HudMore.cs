using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DeckIsland.Core;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace DeckIsland;

// The status alerts beyond volume, brightness, the battery, Bluetooth and the lock keys, as Crest has them: the power mode and Energy
// saver, the internet dropping and coming back, airplane mode, the output device Windows moves the sound to, monitors connecting,
// Do not disturb and copying. Each is read cheaply (a poll of a value Windows keeps, or an event) and shown only while its switch is on.
public partial class App
{
    private void StartMoreHuds()
    {
        StartPowerMode();
        StartNetwork();
        StartAirplane();
        StartOutputDevice();
        StartMonitors();
        StartDoNotDisturb();
        StartCopy();
        StartWindowsIndicator();
        StartQuotaResets();
    }

    // A timer kept with the HUD's sources, ticking `every` seconds.
    private void Every(double seconds, Action tick)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) => tick();
        timer.Start();
        _hudKeep.Add(timer);
    }

    // ----- The power mode (Balanced, Best power efficiency, Best performance) and Energy saver -----
    private void StartPowerMode()
    {
        PowerMode? mode = null; bool? saver = null;
        Every(2, () =>
        {
            var now = Native.BatteryProvider.Mode;
            bool saving = GetSystemPowerStatus(out var status) && status.Saver != 0;
            if (saver is { } before && before != saving && Settings.HudPowerMode)
                ShowHud(new("power", HudIcon.PowerMode, saving ? "Energy saver on" : "Energy saver off", On: saving, Lit: saving, Reserve: "Energy saver off"));
            else if (mode is { } was && was != now && now != PowerMode.Unknown && Settings.HudPowerMode)
                ShowHud(new("power", HudIcon.PowerMode, BatteryModel.ModeName(now), Lit: true, FigureColor: now switch
                {
                    PowerMode.BestEfficiency => "#30D158", PowerMode.BestPerformance or PowerMode.BetterPerformance => "#FF9F0A", _ => null
                }, Reserve: "Best power efficiency"));
            mode = now; saver = saving;
        });
    }

    // ----- The internet dropping and coming back (Windows' own connectivity level) -----
    private void StartNetwork()
    {
        bool? online = null;
        Every(3, () =>
        {
            bool now;
            string name = "";
            try
            {
                var profile = global::Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                now = profile?.GetNetworkConnectivityLevel() == global::Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess;
                name = profile?.ProfileName ?? "";
            }
            catch (Exception error) when (error is COMException or InvalidOperationException) { return; }
            if (online is { } before && before != now && Settings.HudNetwork)
                ShowHud(new("network", now ? HudIcon.Network : HudIcon.NetworkOff, now ? "Back online" : "No internet", Figure: now && name.Length > 0 ? name : null, Lit: now));
            online = now;
        });
    }

    // ----- Airplane mode (the radio management state Windows keeps) -----
    private void StartAirplane()
    {
        bool? on = null;
        Every(2, () =>
        {
            bool now;
            try { now = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState", "", 0) is int state && state == 1; }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return; }
            if (on is { } before && before != now && Settings.HudAirplane)
                ShowHud(new("airplane", HudIcon.Airplane, now ? "Airplane mode on" : "Airplane mode off", On: now, Lit: now, Reserve: "Airplane mode off"));
            on = now;
        });
    }

    // ----- The output device: Windows moving the sound to other speakers or headphones -----
    private void StartOutputDevice()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            _hudKeep.Add(enumerator);
            string? id = null;
            Every(2, () =>
            {
                try
                {
                    var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (id is not null && id != device.ID && Settings.HudOutput)
                    {
                        // Headphones or speakers, as the endpoint says of itself (its form factor) or its name.
                        int? form = null;
                        try { if (device.Properties.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor)) form = Convert.ToInt32(device.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value); }
                        catch (Exception error) when (error is COMException or InvalidCastException or FormatException) { }
                        ShowHud(new("output", HudDevices.ForOutput(device.FriendlyName, form), device.FriendlyName, Lit: true));
                    }
                    id = device.ID;
                }
                catch (Exception error) when (error is COMException or InvalidOperationException) { }
            });
        }
        catch (Exception error) when (error is COMException or InvalidOperationException) { }
    }

    // ----- Monitors connecting and disconnecting, with the refresh rate -----
    private void StartMonitors()
    {
        var known = Native.Displays.Current().ToDictionary(display => display.Id, display => display);
        EventHandler changed = (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var now = Native.Displays.Current().ToDictionary(display => display.Id, display => display);
            if (Settings.HudMonitors)
            {
                foreach (var added in now.Values.Where(display => !known.ContainsKey(display.Id)))
                    ShowHud(new($"monitor:{added.Id}", HudIcon.Display, added.Name, Figure: added.Refresh is { } hz ? $"{hz:0} Hz" : "Connected", Lit: true));
                foreach (var gone in known.Values.Where(display => !now.ContainsKey(display.Id)))
                    ShowHud(new($"monitor:{gone.Id}", HudIcon.Display, gone.Name, Figure: "Disconnected"));
            }
            known = now;
        });
        SystemEvents.DisplaySettingsChanged += changed;
        _hudSources.Add(new Disposer(() => SystemEvents.DisplaySettingsChanged -= changed));
    }

    // ----- Do not disturb (Windows 11 turns the toasts off for it) -----
    private void StartDoNotDisturb()
    {
        bool? on = null;
        Every(2, () =>
        {
            bool now;
            try { now = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 1) is int enabled && enabled == 0; }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return; }
            if (on is { } before && before != now && Settings.HudDoNotDisturb)
                ShowHud(new("dnd", HudIcon.DoNotDisturb, now ? "Do not disturb on" : "Do not disturb off", On: now, Lit: now, Reserve: "Do not disturb off"));
            on = now;
        });
    }

    // ----- A quota that had been used reaching its reset -----
    // Each quota's last reset time and use, by app and quota; a reset that passes with some of it used is announced once.
    private readonly Dictionary<string, (DateTimeOffset Reset, double Used)> _quotaResets = [];
    private readonly HashSet<string> _resetsAnnounced = [];
    private void StartQuotaResets()
    {
        Every(30, () =>
        {
            var now = DateTimeOffset.Now;
            bool quiet = DoNotDisturbOn();
            foreach (var snapshot in Deck?.Snapshots ?? [])
                foreach (var metric in snapshot.Metrics.Where(metric => metric.Kind == MetricKind.Percent && metric.ResetAt is not null))
                {
                    string key = $"{snapshot.Id}:{(metric.Id.Length > 0 ? metric.Id : metric.Label)}";
                    if (_quotaResets.TryGetValue(key, out var seen) && seen.Reset <= now && seen.Used >= 1 && _resetsAnnounced.Add($"{key}@{seen.Reset:O}")
                        && Settings.QuotaResetNotify && !quiet)
                        ShowHud(new($"reset:{key}", HudIcon.Done, $"{snapshot.Name} · {Loc.Label(metric.Label)} reset", Figure: "100% left", Lit: true, FigureColor: "#30D158"));
                    // A reading taken after the reset carries the next one; until then the old one is kept, so its passing is seen.
                    if (!_quotaResets.TryGetValue(key, out var kept) || metric.ResetAt!.Value > kept.Reset || kept.Reset > now)
                        _quotaResets[key] = (metric.ResetAt!.Value, metric.UsedPercent ?? 0);
                }
        });
    }

    // Today's AI limit resets, passed and coming, for the dock's Day Timeline: each app's name and the local time.
    internal IReadOnlyList<(DateTime At, string Name)> TodayQuotaResets()
    {
        var today = DateTime.Today;
        var snapshots = Deck?.Snapshots ?? [];
        var names = snapshots.ToDictionary(snapshot => snapshot.Id.ToString(), snapshot => snapshot.Name);
        var passed = _quotaResets.Select(pair => (At: pair.Value.Reset.LocalDateTime, Name: names.GetValueOrDefault(pair.Key.Split(':')[0]) ?? ""));
        var coming = snapshots.SelectMany(snapshot => snapshot.Metrics.Where(metric => metric.ResetAt is not null)
            .Select(metric => (At: metric.ResetAt!.Value.LocalDateTime, Name: snapshot.Name)));
        return passed.Concat(coming).Where(reset => reset.Name.Length > 0 && reset.At.Date == today)
            .Select(reset => (new DateTime(reset.At.Year, reset.At.Month, reset.At.Day, reset.At.Hour, reset.At.Minute, 0), reset.Name))
            .Distinct().OrderBy(reset => reset.Item1).ToList();
    }

    private static bool DoNotDisturbOn()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 1) is int enabled && enabled == 0; }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }

    // ----- Copying: text, a link, an image or files -----
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    private void StartCopy()
    {
        uint last = GetClipboardSequenceNumber();
        Every(.4, () =>
        {
            uint now = GetClipboardSequenceNumber();
            if (now == last) return;
            last = now;
            if (!Settings.HudCopy) return;
            string what;
            try
            {
                what = Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Count is var count && count == 1 ? "1 file" : $"{Clipboard.GetFileDropList().Count} files"
                    : Clipboard.ContainsImage() ? "Image"
                    : Clipboard.ContainsText() && Clipboard.GetText().Trim() is var text && text.Length > 0
                        ? Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? "Link" : "Text" : "";
            }
            catch (Exception error) when (error is COMException or InvalidOperationException or ExternalException) { return; }
            if (what.Length > 0) ShowHud(new("copy", HudIcon.Copy, "Copied", Figure: what, Lit: true));
        });
    }

    // ----- Hiding Windows' own volume and brightness indicator while the island shows them -----
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    private void StartWindowsIndicator()
    {
        bool hidden = false;
        // shortcut: the classic indicator's window (NativeHWNDHost holding a DirectUIHWND) is minimised, as HideVolumeOSD does; the newer
        // Windows 11 flyout drawn by the shell is not reached this way, upgrade when Windows offers a setting for it.
        static IntPtr Indicator()
        {
            for (IntPtr window = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "NativeHWNDHost", ""); window != IntPtr.Zero; window = FindWindowEx(IntPtr.Zero, window, "NativeHWNDHost", ""))
                if (FindWindowEx(window, IntPtr.Zero, "DirectUIHWND", "") != IntPtr.Zero) return window;
            return IntPtr.Zero;
        }
        Every(2, () =>
        {
            bool wanted = Settings.HudHideWindows && Settings.HudVolume && Settings.HudBrightness;
            if (wanted == hidden && !wanted) return;
            var window = Indicator();
            if (window == IntPtr.Zero) return;
            ShowWindow(window, wanted ? 6 : 9); // SW_MINIMIZE, SW_RESTORE
            hidden = wanted;
        });
        _hudSources.Add(new Disposer(() => { if (hidden && Indicator() is var window && window != IntPtr.Zero) ShowWindow(window, 9); }));
    }

    // The readouts "Show everywhere" governs: the ones a key or a quick change raises, which are left out over full-screen apps and
    // while the island is hidden unless it is on.
    private static bool EverywhereKind(string key) => key is "volume" or "brightness" or "mic" or "copy" or "insert" || key.StartsWith("lock:", StringComparison.Ordinal);
}
