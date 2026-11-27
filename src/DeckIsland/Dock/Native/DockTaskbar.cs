using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DeckIsland.Core;

namespace DeckIsland.Native;

// Hides and restores the Windows taskbar for DeckIsland Dock, after lumabar's taskbar.rs.
// - AutoHide: the documented ABM_SETSTATE switch; Start, Search and the screen edge still bring the taskbar in.
// - Hide: auto-hide plus hiding every taskbar window. Undocumented, so it is checked and falls back to auto-hide.
// Safety: the original state is written to disk before any change (DockTaskbarState); it is restored on exit, on a crash, at
// session end, by a watchdog process if DeckIsland is killed, and at the next start if a state file was left behind. Explorer is
// never killed or patched.
internal static class DockTaskbar
{
    public const string WatchdogSwitch = "--dock-watchdog";
    // Starts the watchdog and exits at once, so the watchdog is an orphan that "end process tree" on DeckIsland cannot reach.
    public const string WatchdogLaunchSwitch = "--dock-watchdog-launch";
    private const uint AbmGetState = 4, AbmSetState = 10, AbsAutoHide = 1;
    private const int SwHide = 0, SwShowNa = 8;
    private const uint EventObjectShow = 0x8002, WinEventOutOfContext = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size; public IntPtr Window; public uint CallbackMessage; public uint Edge;
        public int Left, Top, Right, Bottom; public IntPtr Param;
    }
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, nint wParam, nint lParam);
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

    private static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckIsland", DockTaskbarState.FileName);
    private static readonly object Gate = new();
    private static bool _completeActive, _watchdogStarted;
    private static IntPtr _showHook;
    private static WinEventProc? _showCallback;

    // True when complete hiding was asked for but Windows would not keep the taskbar hidden; auto-hide is used instead.
    public static bool CompleteFailed { get; private set; }

    public static void Apply(DockTaskbarMode mode)
    {
        // A leftover file means a run never restored: undo it first, so the original recorded now is the user's own.
        Restore();
        CompleteFailed = false;
        if (mode == DockTaskbarMode.Leave) return;
        bool complete = mode == DockTaskbarMode.Hide;
        uint original = GetFlags();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            // Written whole and moved into place, so a watchdog never reads half a file.
            var temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, new DockTaskbarState(original, complete, Environment.ProcessId).ToJson());
            File.Move(temporary, StatePath, true);
        }
        // Never change the taskbar without a way back.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        StartWatchdog();
        SetFlags(original | AbsAutoHide);
        if (!complete) return;
        // Explorer may show the taskbar again just after the auto-hide switch, so the hook keeps hiding it, and the result is
        // judged a moment later rather than at once.
        HideTaskbars();
        lock (Gate) _completeActive = true;
        HookShows();
        var check = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        check.Tick += (_, _) =>
        {
            check.Stop();
            bool active; lock (Gate) active = _completeActive;
            if (!active || HideTaskbars()) return;
            lock (Gate) _completeActive = false;
            ShowTaskbars(); CompleteFailed = true;
        };
        check.Start();
    }

    // Puts the taskbar back as it was, if the dock changed it. Safe to call any number of times, from any thread.
    public static void Restore() => Restore(null);

    private static void Restore(int? onlyFor)
    {
        lock (Gate) _completeActive = false;
        string text;
        try { text = File.ReadAllText(StatePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        var saved = DockTaskbarState.Parse(text);
        // A watchdog acts only on its own process's change; anything else is left to that run, or to the next start.
        if (onlyFor is { } pid && saved?.Pid != pid) return;
        if (saved is null || saved.Complete) ShowTaskbars();
        if (saved is not null) SetFlags(saved.Original);
        try { File.Delete(StatePath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // Explorer restarted and rebuilt the taskbar: hide it again, or fall back to auto-hide.
    public static void TaskbarRecreated()
    {
        bool active; lock (Gate) active = _completeActive;
        if (active && !HideTaskbars()) { lock (Gate) _completeActive = false; CompleteFailed = true; ShowTaskbars(); }
    }

    public static void Stop()
    {
        if (_showHook != IntPtr.Zero) { UnhookWinEvent(_showHook); _showHook = IntPtr.Zero; }
        Restore();
    }

    // Program.Main's entry for "--dock-watchdog <pid>": waits for that DeckIsland to end, then restores what it left changed.
    public static int RunWatchdog(string pid)
    {
        if (!int.TryParse(pid, out var id)) return 1;
        try { using var process = Process.GetProcessById(id); process.WaitForExit(); }
        catch (ArgumentException) { }
        Restore(id);
        // The dock's tray host (ManagedShell) took the icons apps added while it ran; told that the taskbar was made again, the apps
        // add them to Explorer's tray once more. A normal exit does this itself; this covers DeckIsland being killed.
        PostMessage(HwndBroadcast, RegisterWindowMessage("TaskbarCreated"), 0, 0);
        return 0;
    }

    // Program.Main's entry for "--dock-watchdog-launch <pid>": starts the real watchdog and exits.
    public static int LaunchWatchdog(string pid) => Spawn(WatchdogSwitch, pid) ? 0 : 1;

    // A second copy of DeckIsland that waits for this one to end; it covers being killed or a hard crash.
    private static void StartWatchdog()
    {
        if (!_watchdogStarted) _watchdogStarted = Spawn(WatchdogLaunchSwitch, Environment.ProcessId.ToString());
    }

    private static bool Spawn(string mode, string pid)
    {
        if (Environment.ProcessPath is not { } exe) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { ArgumentList = { mode, pid }, UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { return false; }
    }

    // Explorer shows a taskbar window again for Start or after a restart: hide it again while complete hiding is on.
    private static void HookShows()
    {
        if (_showHook != IntPtr.Zero) return;
        _showCallback = (_, _, window, objectId, _, _, _) =>
        {
            bool active; lock (Gate) active = _completeActive;
            if (active && objectId == 0 && IsTaskbar(window)) ShowWindow(window, SwHide);
        };
        _showHook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _showCallback, 0, 0, WinEventOutOfContext);
    }

    private static AppBarData Data() => new() { Size = Marshal.SizeOf<AppBarData>(), Window = Taskbars().FirstOrDefault(window => ClassIs(window, "Shell_TrayWnd")) };

    private static bool ClassIs(IntPtr window, string name)
    {
        var text = new StringBuilder(64);
        GetClassName(window, text, text.Capacity);
        return text.ToString() == name;
    }
    private static uint GetFlags() { var data = Data(); return (uint)SHAppBarMessage(AbmGetState, ref data); }
    private static void SetFlags(uint flags) { var data = Data(); data.Param = (IntPtr)flags; SHAppBarMessage(AbmSetState, ref data); }

    // Explorer's taskbars only: the dock's tray host (ManagedShell) makes a Shell_TrayWnd of its own in this process.
    internal static bool IsTaskbar(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var owner);
        if (owner == Environment.ProcessId) return false;
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static List<IntPtr> Taskbars()
    {
        var found = new List<IntPtr>();
        EnumWindows((window, _) => { if (IsTaskbar(window)) found.Add(window); return true; }, IntPtr.Zero);
        return found;
    }

    private static void ShowTaskbars() { foreach (var window in Taskbars()) ShowWindow(window, SwShowNa); }

    private static bool HideTaskbars()
    {
        var bars = Taskbars();
        if (bars.Count == 0) return false;
        foreach (var window in bars) ShowWindow(window, SwHide);
        return bars.All(window => !IsWindowVisible(window));
    }
}
