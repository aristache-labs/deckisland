using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.NetworkOperators;

namespace DeckIsland.Native;

// What the dock's Quick Toggles read and switch (DockWindow.QuickToggles.cs). Each switch returns whether it took; one that cannot
// be done directly opens its page in Windows' Settings instead. Airplane mode, Do Not Disturb and Energy Saver have no public API:
// they go through undocumented paths (the radio manager COM object, the notifications registry value, powercfg) that may not
// hold on every Windows build, so each falls back to Settings when it does not take.
internal static class QuickSystem
{
    public sealed record WifiNetwork(string Ssid, bool Secured, int Bars, bool Connected);
    public sealed record BluetoothItem(string Id, string Name, bool Connected, bool Audio, bool Phone);

    public static void OpenSettings(string page)
    {
        try { Process.Start(new ProcessStartInfo($"ms-settings:{page}") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // --- Radios (Wi-Fi, Bluetooth) ---

    private static async Task<Radio?> RadioOf(RadioKind kind)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return null;
            return (await Radio.GetRadiosAsync()).FirstOrDefault(radio => radio.Kind == kind);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    // Null when there is no such radio.
    public static async Task<bool?> RadioOn(RadioKind kind) => await RadioOf(kind) is { } radio ? radio.State == RadioState.On : null;

    public static async Task<bool> SetRadio(RadioKind kind, bool on)
    {
        if (await RadioOf(kind) is not { } radio) return false;
        try { return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed; }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { return false; }
    }

    // --- Wi-Fi networks ---

    private static async Task<WiFiAdapter?> WifiAdapter()
    {
        try
        {
            if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed) return null;
            return (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { return null; }
    }

    // The networks in range, the connected one first, then by signal; one entry per name.
    public static async Task<List<WifiNetwork>> WifiNetworks(bool scan)
    {
        if (await WifiAdapter() is not { } adapter) return [];
        try
        {
            if (scan) await adapter.ScanAsync();
            var profile = await adapter.NetworkAdapter.GetConnectedProfileAsync();
            string? connected = profile?.WlanConnectionProfileDetails?.GetConnectedSsid();
            return adapter.NetworkReport.AvailableNetworks
                .Where(net => !string.IsNullOrWhiteSpace(net.Ssid))
                .GroupBy(net => net.Ssid).Select(group => group.OrderByDescending(net => net.SignalBars).First())
                .Select(net => new WifiNetwork(net.Ssid, net.SecuritySettings.NetworkAuthenticationType is not
                    (Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211 or Windows.Networking.Connectivity.NetworkAuthenticationType.None),
                    net.SignalBars, net.Ssid == connected))
                .OrderByDescending(net => net.Connected).ThenByDescending(net => net.Bars).ThenBy(net => net.Ssid).Take(20).ToList();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException) { return []; }
    }

    // Joins a network Windows already knows (or an open one); a new secured network needs its password, given in Settings.
    public static async Task<bool> ConnectWifi(string ssid)
    {
        if (await WifiAdapter() is not { } adapter) return false;
        try
        {
            var network = adapter.NetworkReport.AvailableNetworks.Where(net => net.Ssid == ssid).OrderByDescending(net => net.SignalBars).FirstOrDefault();
            if (network is null) return false;
            var result = await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic);
            return result.ConnectionStatus == WiFiConnectionStatus.Success;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    // --- Ethernet ---

    public static bool EthernetUp()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(nic => nic.OperationalStatus == OperationalStatus.Up
                && nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                && !nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && !nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase));
        }
        catch (NetworkInformationException) { return false; }
    }

    // --- Bluetooth devices ---

    public static async Task<List<BluetoothItem>> BluetoothDevices()
    {
        var items = new List<BluetoothItem>();
        try
        {
            foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
            {
                using var device = await BluetoothDevice.FromIdAsync(info.Id);
                if (device is null) continue;
                var major = device.ClassOfDevice.MajorClass;
                items.Add(new BluetoothItem(info.Id, string.IsNullOrWhiteSpace(device.Name) ? info.Name : device.Name,
                    device.ConnectionStatus == BluetoothConnectionStatus.Connected, major == BluetoothMajorClass.AudioVideo, major == BluetoothMajorClass.Phone));
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException) { }
        return items.OrderByDescending(item => item.Connected).ThenBy(item => item.Name).ToList();
    }

    // --- Mobile hotspot ---

    private static NetworkOperatorTetheringManager? Tethering()
    {
        try
        {
            return Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile() is { } profile
                ? NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile) : null;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public static bool HotspotOn() => Tethering()?.TetheringOperationalState == TetheringOperationalState.On;

    public static async Task<bool> SetHotspot(bool on)
    {
        if (Tethering() is not { } manager) return false;
        try
        {
            var result = on ? await manager.StartTetheringAsync() : await manager.StopTetheringAsync();
            return result.Status == TetheringOperationStatus.Success;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { return false; }
    }

    // --- Airplane mode (undocumented radio manager) ---

    public static bool AirplaneOn()
    {
        try { return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState", "", null) is 1; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }

    [ComImport, Guid("db3afbfb-08e6-46c6-aa70-bf9a34c30ab7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRadioManager
    {
        [PreserveSig] int IsRMSupported(out int supported);
        [PreserveSig] int GetUIRadioInstances(out IntPtr instances);
        [PreserveSig] int GetSystemRadioState(out int enabled, out int policy, out int reason);
        [PreserveSig] int SetSystemRadioState(int enabled);
    }

    // Airplane mode on means the system radio state off.
    public static bool SetAirplane(bool on)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("581333f6-28db-41be-bc7a-ff201f12f3f6"));
            if (type is null || Activator.CreateInstance(type) is not IRadioManager manager) return false;
            try { return manager.SetSystemRadioState(on ? 0 : 1) >= 0; }
            finally { Marshal.ReleaseComObject(manager); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException) { return false; }
    }

    // --- Camera (the user's camera access switch in Privacy settings) ---

    private const string WebcamKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    public static bool CameraAllowed()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(WebcamKey); return key?.GetValue("Value") as string != "Deny"; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return true; }
    }

    public static bool SetCameraAllowed(bool on)
    {
        try { using var key = Registry.CurrentUser.CreateSubKey(WebcamKey); key.SetValue("Value", on ? "Allow" : "Deny"); return true; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }

    // --- Keep awake ---

    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;
    [DllImport("kernel32")] private static extern uint SetThreadExecutionState(uint flags);
    public static bool KeepingAwake { get; private set; }

    // Held by the UI thread, which lives as long as the app.
    public static void SetKeepAwake(bool on)
    {
        SetThreadExecutionState(on ? EsContinuous | EsSystemRequired | EsDisplayRequired : EsContinuous);
        KeepingAwake = on;
    }

    // --- Desktop icons ---

    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr window);
    private const uint WmCommand = 0x0111;
    private const int ToggleDesktopIcons = 0x7402;

    // The shell's desktop view: under Progman, or under one of the WorkerW windows once a wallpaper has been animated.
    private static IntPtr DesktopView()
    {
        var view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        for (var worker = IntPtr.Zero; view == IntPtr.Zero && (worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero;)
            view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
        return view;
    }

    public static bool DesktopIconsShown()
    {
        var list = FindWindowEx(DesktopView(), IntPtr.Zero, "SysListView32", null);
        return list != IntPtr.Zero && IsWindowVisible(list);
    }

    public static void ToggleDesktopIconsShown()
    {
        var view = DesktopView();
        if (view != IntPtr.Zero) SendMessage(view, WmCommand, ToggleDesktopIcons, IntPtr.Zero);
    }

    // --- Display mode (the projection flyout) ---

    [DllImport("user32")] private static extern int GetSystemMetrics(int index);
    public static bool SeveralDisplays() => GetSystemMetrics(80) > 1; // SM_CMONITORS
    public static void OpenProjection()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings-displays-topology:projection") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { OpenSettings("display"); }
    }

    // --- Energy saver (powercfg's saver threshold: 100% keeps it on, the default 20% leaves it to the battery) ---

    public static bool EnergySaverOn()
    {
        try { return Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On; }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return false; }
    }

    public static async Task<bool> SetEnergySaver(bool on)
    {
        string threshold = on ? "100" : "20";
        bool ok = await PowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_ENERGYSAVER ESBATTTHRESHOLD {threshold}")
            & await PowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_ENERGYSAVER ESBATTTHRESHOLD {threshold}")
            & await PowerCfg("/setactive SCHEME_CURRENT");
        await Task.Delay(400);
        return ok && EnergySaverOn() == on;
    }

    private static async Task<bool> PowerCfg(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powercfg.exe", arguments) { CreateNoWindow = true, UseShellExecute = false });
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    // --- Do Not Disturb (notifications' global switch) ---

    private const string NotificationsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    public static bool DoNotDisturbOn()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(NotificationsKey); return key?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") is 0; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }

    public static bool SetDoNotDisturb(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(NotificationsKey);
            key.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", on ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }
}
