using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeckIsland.Native;

// Win+1 to Win+0 for the dock's apps, and Win+T for its app switcher (DockSwitcher). Explorer keeps these keys even with its taskbar hidden, and RegisterHotKey cannot take them
// from it, so a low-level keyboard hook sees them first and keeps them from Explorer. The hook runs on the UI thread and only
// looks at key codes, so it stays fast as Windows requires.
internal sealed class DockKeys : IDisposable
{
    private const int WhKeyboardLl = 13, WmKeyDown = 0x100, WmSysKeyDown = 0x104;
    private const int WmKeyUp = 0x101, WmSysKeyUp = 0x105, LlkhfInjected = 0x10;
    private const int VkLeftWindows = 0x5B, VkRightWindows = 0x5C, Vk0 = 0x30, Vk9 = 0x39, VkT = 0x54, VkShift = 0x10, VkControl = 0x11, VkAlt = 0x12;
    // An unassigned key, pressed while Windows is held so that its release does not open Start (the digit never reached it).
    private const byte VkUnassigned = 0xE8;
    private const uint KeyUp = 2;

    [StructLayout(LayoutKind.Sequential)] private struct KeyInfo { public int Key, Scan, Flags, Time; public nint Extra; }
    private delegate nint HookProc(int code, nint message, ref KeyInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, ref KeyInfo info);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nint extra);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);

    private readonly HookProc _proc;
    private readonly Action<int>? _pressed;
    private readonly Action? _switcher;
    private nint _hook;
    // The digit taken last, until its key-up: held keys repeat, and the app is acted on once.
    private int _held;

    // pressed gets the app's index: 0 for Win+1 ... 9 for Win+0.
    // switcher, when given, is called for Win+T.
    public DockKeys(Action<int>? pressed, Action? switcher = null)
    {
        _pressed = pressed; _switcher = switcher;
        _proc = Hook;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(module?.ModuleName), 0);
    }

    private nint Hook(int code, nint message, ref KeyInfo info)
    {
        if (code >= 0 && (message == WmKeyUp || message == WmSysKeyUp) && info.Key == _held) _held = 0;
        // Win with another modifier is Explorer's (jump lists, new window), and injected keys belong to whoever sent them.
        if (code >= 0 && (message == WmKeyDown || message == WmSysKeyDown) && (info.Key is >= Vk0 and <= Vk9 && _pressed is not null || info.Key == VkT && _switcher is not null) && WindowsHeld() && !OtherModifierHeld()
            && (info.Flags & LlkhfInjected) == 0)
        {
            if (info.Key == _held) return 1;
            _held = info.Key;
            keybd_event(VkUnassigned, 0, 0, 0); keybd_event(VkUnassigned, 0, KeyUp, 0);
            // Acted on after the hook returns: the hook must answer quickly.
            if (info.Key == VkT) { System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _switcher!()); return 1; }
            int index = info.Key == Vk0 ? 9 : info.Key - Vk0 - 1;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _pressed!(index));
            return 1;
        }
        return CallNextHookEx(_hook, code, message, ref info);
    }

    private static bool OtherModifierHeld() => (GetAsyncKeyState(VkShift) & 0x8000) != 0 || (GetAsyncKeyState(VkControl) & 0x8000) != 0 || (GetAsyncKeyState(VkAlt) & 0x8000) != 0;

    private static bool WindowsHeld() => (GetAsyncKeyState(VkLeftWindows) & 0x8000) != 0 || (GetAsyncKeyState(VkRightWindows) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
    }
}
