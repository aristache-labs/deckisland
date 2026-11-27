using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeckIsland.Core;

namespace DeckIsland.Native;

// The windows and apps DeckIsland Dock shows, after lumabar's apps.rs: the windows the Windows taskbar would show (the Alt+Tab
// rules), the taskbar's own pins, launching, focusing and closing, and the apps' icons.
internal static partial class DockAppWindows
{
    private const int GwlExStyle = -20, DwmCloaked = 14;
    private const long ExAppWindow = 0x40000, ExToolWindow = 0x80, ExNoActivate = 0x08000000;
    private const uint GwOwner = 4, WmClose = 0x10;
    private const int SwMinimize = 6, SwRestore = 9;
    private delegate bool EnumWindowsProc(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    public static long Foreground => GetForegroundWindow();

    // Every window the Windows taskbar would show a button for, top first.
    public static List<DockWindowInfo> Collect()
    {
        var all = new List<nint>();
        EnumWindows((window, _) => { all.Add(window); return true; }, 0);
        var found = new List<DockWindowInfo>();
        int own = Environment.ProcessId;
        for (int z = 0; z < all.Count; z++)
        {
            var window = all[z];
            if (!IsTaskbarWindow(window)) continue;
            GetWindowThreadProcessId(window, out var pid);
            if (pid == own) continue;
            var exe = AppIdentity.ImagePath((int)pid);
            if (exe.Length == 0) continue;
            // Store apps run inside ApplicationFrameHost; the app is the process of one of its child windows.
            string? appId;
            if (exe.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (StoreAppExe(window, pid) is { } real) exe = real;
                appId = AppIdentity.WindowId(window);
            }
            else appId = AppIdentity.PackagedId(pid);
            string key = (appId is null ? exe : AppsFolder + appId).ToLowerInvariant();
            found.Add(new DockWindowInfo(window, key, exe, Title(window), IsIconic(window), z, appId));
        }
        return found;
    }

    private static bool IsTaskbarWindow(nint window)
    {
        if (!IsWindowVisible(window)) return false;
        long ex = GetWindowLongPtr(window, GwlExStyle);
        if ((ex & ExAppWindow) == 0 && ((ex & (ExToolWindow | ExNoActivate)) != 0 || GetWindow(window, GwOwner) != 0)) return false;
        if (DwmGetWindowAttribute(window, DwmCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        if (GetWindowTextLength(window) == 0) return false;
        return ClassName(window) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private static string? StoreAppExe(nint frame, uint framePid)
    {
        string? found = null;
        EnumChildWindows(frame, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var pid);
            if (pid == framePid) return true;
            var exe = AppIdentity.ImagePath((int)pid);
            if (exe.Length > 0) { found = exe; return false; }
            return true;
        }, 0);
        return found;
    }

    private static string Title(nint window) { var text = new StringBuilder(256); GetWindowText(window, text, text.Capacity); return text.ToString(); }
    private static string ClassName(nint window) { var name = new StringBuilder(64); GetClassName(window, name, name.Capacity); return name.ToString(); }

    // The shortcuts pinned to the Windows taskbar, one per app.
    public static List<DockPin> TaskbarPins()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var shortcuts = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.lnk")
            .Order(StringComparer.OrdinalIgnoreCase).Select(path => new DockPin(path, Path.GetFileNameWithoutExtension(path))).ToList() : [];
        return WindowsPins(shortcuts).Where(pin => seen.Add(PinKey(pin))).ToList();
    }

    private static readonly Dictionary<string, string> PinKeys = new(StringComparer.OrdinalIgnoreCase);

    // The key a pin's windows will have: the lower-cased exe it starts.
    public static string PinKey(DockPin pin)
    {
        if (PinKeys.TryGetValue(pin.Path, out var known)) return known;
        string key;
        if (pin.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) key = pin.Path.ToLowerInvariant();
        else if (ShortcutTarget(pin.Path) is { Length: > 0 } target) key = target.ToLowerInvariant();
        // File Explorer's pin points at a shell folder, not an exe.
        else if (pin.Name.StartsWith("File Explorer", StringComparison.OrdinalIgnoreCase))
            key = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe").ToLowerInvariant();
        else key = pin.Path.ToLowerInvariant();
        return PinKeys[pin.Path] = key;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink;
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, nint data, uint flags);
    }
    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
    }

    private static string? ShortcutTarget(string path)
    {
        try
        {
            var link = new ShellLink();
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(target, target.Capacity, 0, 0);
            Marshal.ReleaseComObject(link);
            return target.ToString();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or FileNotFoundException) { return null; }
    }

    // The app's name for a window: its exe's description, else the exe's file name.
    public static string AppName(DockWindowInfo window)
    {
        if (window.AppId is { } id && AppIdentity.RegisteredName(id) is { } name) return name;
        try
        {
            var description = FileVersionInfo.GetVersionInfo(window.Exe).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException) { }
        return Path.GetFileNameWithoutExtension(window.Exe);
    }

    public static void Act(DockApp app, DockClick click)
    {
        switch (click.Kind)
        {
            case DockClickKind.Launch: Launch(app.Launch); break;
            case DockClickKind.Minimize: ShowWindow((nint)click.Handle, SwMinimize); break;
            case DockClickKind.Focus: Focus((nint)click.Handle); break;
        }
    }

    // Starts the app (a shortcut, an exe or a shell path); false when Windows could not.
    public static bool Launch(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); return true; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException) { return false; }
    }

    public static void Focus(nint window)
    {
        if (IsIconic(window)) ShowWindow(window, SwRestore);
        if (SetForegroundWindow(window)) return;
        // Windows refused: borrow the foreground thread's input state, as the taskbar may.
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _), me = GetCurrentThreadId();
        bool attached = AttachThreadInput(me, foreground, true);
        BringWindowToTop(window); SetForegroundWindow(window);
        if (attached) AttachThreadInput(me, foreground, false);
    }

    public static void Close(nint window) => PostMessage(window, WmClose, 0, 0);

    public static void EndTask(DockApp app)
    {
        foreach (var pid in app.Windows.Select(w => { GetWindowThreadProcessId((nint)w.Handle, out var id); return (int)id; }).Distinct())
        {
            try { using var process = Process.GetProcessById(pid); process.Kill(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    public static void OpenFileLocation(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + path }, UseShellExecute = false })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    [StructLayout(LayoutKind.Sequential)] private struct IconSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(IconSize size, int flags, out nint bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint context, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] private static extern int GetObject(nint handle, int size, out Bitmap bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Icon(DockApp app, int size) =>
        Icon(app.Launch, size) ?? (app.Windows.FirstOrDefault() is { } window ? Icon(window.Exe, size) : null);

    // The shell's picture for a shortcut, exe, folder or shell item at this pixel size, with its transparency; null when there is
    // none. Cached unless the picture changes (the Recycle Bin's).
    public static ImageSource? Icon(string path, int size, bool cache = true)
    {
        string cacheKey = size + "|" + path;
        if (cache && Icons.TryGetValue(cacheKey, out var cached)) return cached;
        nint handle = 0;
        ImageSource? icon = null;
        try
        {
            var riid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, 0, ref riid, out var item);
            // SIIGBF_ICONONLY (4): the icon, not a thumbnail of the file.
            if (item.GetImage(new IconSize { Width = size, Height = size }, 4, out handle) == 0 && handle != 0
                && GetObject(handle, Marshal.SizeOf<Bitmap>(), out var bitmap) != 0 && bitmap.Bits != 0 && bitmap.BitsPixel == 32)
            {
                // A bottom-up DIB in premultiplied BGRA; WPF wants it top-down.
                int stride = bitmap.WidthBytes, height = Math.Abs(bitmap.Height);
                var pixels = new byte[stride * height];
                for (int row = 0; row < height; row++)
                    Marshal.Copy(bitmap.Bits + (bitmap.Height > 0 ? height - 1 - row : row) * stride, pixels, row * stride, stride);
                var source = BitmapSource.Create(bitmap.Width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
                source.Freeze();
                icon = source;
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException or UnauthorizedAccessException) { }
        finally { if (handle != 0) DeleteObject(handle); }
        if (cache) Icons[cacheKey] = icon;
        return icon;
    }
}
