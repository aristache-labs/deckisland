using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace DeckIsland.Native;

// The Sound panel's data (DockWindow.QuickToggles.cs): the default output's volume and mute, the apps playing through it with
// their own volumes, and the outputs to switch between. Switching the default output goes through the undocumented but long-stable
// IPolicyConfig, which Windows' own sound settings use.
internal static class AudioMixer
{
    public sealed record AppVolume(string Key, string Name, string? Path, float Level);
    public sealed record Output(string Id, string Name, bool Default);

    private static T? WithDefault<T>(Func<MMDevice, T> read)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return read(device);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException) { return default; }
    }

    public static string? DefaultName() => WithDefault(device => device.FriendlyName);
    public static bool Muted() => WithDefault(device => device.AudioEndpointVolume.Mute);
    public static void SetMuted(bool muted) => WithDefault(device => device.AudioEndpointVolume.Mute = muted);
    public static float Level() => WithDefault(device => device.AudioEndpointVolume.MasterVolumeLevelScalar);
    public static void SetLevel(float level) => WithDefault(device => device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f));

    // One entry per process with a live session on the default output; System sounds under its own name.
    public static List<AppVolume> Apps()
    {
        return WithDefault(device =>
        {
            var apps = new Dictionary<string, AppVolume>();
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                string key, name; string? path = null;
                if (session.IsSystemSoundsSession) { key = "system"; name = "System sounds"; }
                else
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)session.GetProcessID);
                        key = process.ProcessName;
                        path = process.MainModule?.FileName;
                        name = process.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } described ? described : process.ProcessName;
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { continue; }
                }
                if (!apps.ContainsKey(key)) apps[key] = new AppVolume(key, name, path, session.SimpleAudioVolume.Volume);
            }
            return apps.Values.OrderBy(app => app.Key == "system").ThenBy(app => app.Name).ToList();
        }) ?? [];
    }

    public static void SetAppLevel(string key, float level) => WithDefault(device =>
    {
        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            bool match = key == "system" ? session.IsSystemSoundsSession : !session.IsSystemSoundsSession && ProcessName((int)session.GetProcessID) == key;
            if (match) session.SimpleAudioVolume.Volume = Math.Clamp(level, 0f, 1f);
        }
        return true;
    });

    private static string? ProcessName(int id)
    {
        try { using var process = Process.GetProcessById(id); return process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return null; }
    }

    public static List<Output> Outputs()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? current = null;
            try { using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); current = device.ID; }
            catch (COMException) { }
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(device => { using (device) return new Output(device.ID, device.FriendlyName, device.ID == current); }).ToList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return []; }
    }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, bool defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string id, bool defaultPeriod, IntPtr defaultPeriodOut, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(string id, bool visible);
    }

    // Makes the output the default for every role (console, multimedia, communications).
    public static bool SetDefaultOutput(string id)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"));
            if (type is null || Activator.CreateInstance(type) is not IPolicyConfig policy) return false;
            try { return Enumerable.Range(0, 3).All(role => policy.SetDefaultEndpoint(id, role) >= 0); }
            finally { Marshal.ReleaseComObject(policy); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return false; }
    }
}
