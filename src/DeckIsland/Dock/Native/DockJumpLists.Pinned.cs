using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DeckIsland.Core;

namespace DeckIsland.Native;

internal static partial class DockJumpLists
{
    private static Section? Pinned(string appId)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Recent\AutomaticDestinations", DockJumpList.FileId(appId) + ".automaticDestinations-ms");
        const int shareDenyWrite = 0x20;
        if (StgOpenStorage(path, 0, shareDenyWrite, 0, 0, out var storage) != 0) return null;
        try
        {
            if (storage.OpenStream("DestList", 0, ShareExclusive, 0, out var list) != 0) return null;
            byte[] data;
            try
            {
                list.Stat(out var stat, 1);
                if (stat.cbSize is < 32 or > 8 * 1024 * 1024) return null;
                data = new byte[(int)stat.cbSize];
                list.Read(data, data.Length, 0);
            }
            finally { Marshal.ReleaseComObject(list); }

            var items = new List<Item>();
            foreach (uint id in DockJumpList.PinnedEntries(data))
                if (Read(() => PinnedItem(storage, id)) is { } item) items.Add(item);
            return items.Count > 0 ? new Section(Loc.T("已固定", "Pinned"), items) : null;
        }
        finally { Marshal.ReleaseComObject(storage); }
    }

    private static Item? PinnedItem(IStorage storage, uint id)
    {
        if (storage.OpenStream(id.ToString("x"), 0, ShareExclusive, 0, out var stream) != 0) return null;
        try
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistStream)link).Load(stream);
                return LinkItem(link, destination: true);
            }
            finally { Marshal.ReleaseComObject(link); }
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    private static string? NameFromId(nint id, int kind)
    {
        if (SHGetNameFromIDList(id, kind, out var name) != 0 || name == 0) return null;
        try { return Marshal.PtrToStringUni(name); }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    private const int ShareExclusive = 0x10;

    [ComImport, Guid("0000000b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        [PreserveSig] int CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, int mode, int reserved1, int reserved2, out IStream stream);
        [PreserveSig] int OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, nint reserved1, int mode, int reserved2, out IStream stream);
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StgOpenStorage(string path, nint priority, int mode, nint exclude, int reserved, out IStorage storage);

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint id, int kind, out nint name);
}
