using System.Runtime.InteropServices;

namespace DeckIsland.Native;

// The shell actions behind the dock's Start, Trash and Show desktop buttons.
internal static class DockShell
{
    public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private const byte VkLeftWindows = 0x5B;
    private const uint KeyUp = 2, FoDelete = 3;
    private const ushort FofAllowUndo = 0x40, FofNoConfirmation = 0x10, FofSilent = 0x4;

    [StructLayout(LayoutKind.Sequential)] private struct RecycleInfo { public int Size; public long Bytes, Items; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public nint Window; public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags; public bool Aborted; public nint Mappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Title;
    }
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nint extra);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? root, ref RecycleInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref FileOperation operation);

    // Opens Start as the Windows key does.
    public static void OpenStart() { keybd_event(VkLeftWindows, 0, 0, 0); keybd_event(VkLeftWindows, 0, KeyUp, 0); }

    // Minimizes every window, or brings them back, as the taskbar's corner does.
    public static void ToggleDesktop()
    {
        if (Type.GetTypeFromProgID("Shell.Application") is not { } type) return;
        object? shell = null;
        try { shell = Activator.CreateInstance(type); type.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null); }
        catch (Exception ex) when (ex is COMException or System.Reflection.TargetInvocationException) { }
        finally { if (shell is not null) Marshal.ReleaseComObject(shell); }
    }

    // shortcut: asked once a second with the tiles; slow on a very full bin. Upgrade to a change notification if it shows.
    public static bool RecycleBinHasItems()
    {
        var info = new RecycleInfo { Size = Marshal.SizeOf<RecycleInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 && info.Items > 0;
    }

    public static void OpenRecycleBin() => DockAppWindows.Launch("shell:RecycleBinFolder");

    // Moves the files to the Recycle Bin, where they can be restored; nothing is deleted for good.
    public static void Recycle(IEnumerable<string> paths)
    {
        var list = paths.Where(path => path.Length > 0).ToList();
        if (list.Count == 0) return;
        var operation = new FileOperation { Function = FoDelete, From = string.Join('\0', list) + "\0\0", Flags = FofAllowUndo | FofNoConfirmation | FofSilent };
        SHFileOperation(ref operation);
    }
}
