using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using DeckIsland.Core;

namespace DeckIsland.Native;

// An app's jump list combines pinned destinations, custom categories and tasks, and recent items.
// The app is known by its AppUserModelID: its window's own,
// its pinned shortcut's, or the implicit one of its exe (DockJumpList).
internal static partial class DockJumpLists
{
    public sealed record Item(string Title, Action Open, string Target);
    public sealed record Section(string Title, IReadOnlyList<Item> Items);

    private const int MaxRecent = 10;
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");
    private static readonly PropertyKey AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly PropertyKey ActivationContext = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 20);
    private static readonly PropertyKey Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);

    // Every section the app's jump list holds; empty when it has none or it cannot be read.
    public static List<Section> For(DockApp app)
    {
        Section? fallback = null;
        foreach (var appId in AppIds(app))
        {
            var sections = new List<Section>();
            var pinned = Read(() => Pinned(appId));
            if (pinned is not null) sections.Add(pinned);
            var custom = Read(() => Custom(appId, app, pinned));
            if (custom is not null) sections.AddRange(custom);
            var recent = Read(() => Recent(appId, app, pinned, frequent: false));
            if (sections.Count > 0)
            {
                if (recent is { Items.Count: > 0 } && !sections.Any(section => section.Title == recent.Title))
                    sections.Add(recent);
                return sections;
            }
            if (recent is { Items.Count: > 0 }) fallback ??= recent;
        }
        return fallback is null ? [] : [fallback];
    }

    private static T? Read<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException or ArgumentException) { return null; }
    }

    private static IEnumerable<string> AppIds(DockApp app)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string appsFolder = @"shell:AppsFolder\";
        if (app.Launch.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase) && seen.Add(app.Launch[appsFolder.Length..]))
            yield return app.Launch[appsFolder.Length..];
        foreach (var window in app.Windows)
        {
            if (WindowAppId((nint)window.Handle) is { Length: > 0 } id && seen.Add(id)) yield return id;
            if (AppIdentity.WindowId((nint)window.Handle) is { Length: > 0 } resolved && seen.Add(resolved)) yield return resolved;
        }
        if (app.Launch.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && LinkAppId(app.Launch) is { Length: > 0 } linked && seen.Add(linked)) yield return linked;
        string exe = app.Windows.Count > 0 ? app.Windows[0].Exe : app.Key;
        if (exe.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase) && seen.Add("Microsoft.Windows.Explorer")) yield return "Microsoft.Windows.Explorer";
        if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && seen.Add(Implicit(exe))) yield return Implicit(exe);
    }

    private static string Implicit(string exe) => DockJumpList.ImplicitAppId(exe, [
        (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "{6D809377-6AF0-444B-8957-A3773F02200E}"),
        (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"),
        (Environment.GetFolderPath(Environment.SpecialFolder.System), "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"),
        (Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}"),
        (Environment.GetFolderPath(Environment.SpecialFolder.Windows), "{F38BF404-1D43-42F2-9305-67DE0B28FC23}"),
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), "{5CD7AEE2-2219-4A67-B85D-6C9CE15660CB}"),
        (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}")]);

    // The custom destinations file: categories one after another, each a type (0 custom with a name, 1 a known category, 2 tasks), its
    // shortcuts as a class id followed by the persisted object, and a closing mark.
    private static List<Section> Custom(string appId, DockApp app, Section? pinned)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent\CustomDestinations", DockJumpList.FileId(appId) + ".customDestinations-ms");
        var sections = new List<Section>();
        if (!File.Exists(path)) return sections;
        using var reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(path)), Encoding.Unicode);
        reader.ReadInt32();
        int categories = reader.ReadInt32();
        reader.ReadInt32();
        for (int c = 0; c < categories && c < 32; c++)
        {
            int type = reader.ReadInt32();
            string title;
            if (type == 0) title = new string(reader.ReadChars(reader.ReadUInt16()));
            else if (type == 1)
            {
                int category = reader.ReadInt32();
                reader.ReadUInt32();
                if (category is 0 or 1 && Read(() => Recent(appId, app, pinned, frequent: category == 0)) is { Items.Count: > 0 } known)
                    sections.Add(known);
                continue;
            }
            else if (type == 2) title = Loc.T("任务", "Tasks");
            else break;
            int count = reader.ReadInt32();
            var items = new List<Item>();
            for (int i = 0; i < count && i < 64; i++)
            {
                var clsid = new Guid(reader.ReadBytes(16));
                // Only shortcuts can be read past; anything else ends the file's reading.
                if (clsid != ShellLinkClsid) return sections;
                var stream = new ReadStream(reader.BaseStream);
                var link = (IShellLinkW)new ShellLink();
                try
                {
                    ((IPersistStream)link).Load(stream);
                    if (LinkItem(link, destination: type == 0) is { } item) items.Add(item);
                }
                finally { Marshal.ReleaseComObject(link); }
            }
            reader.ReadUInt32();
            if (items.Count > 0) sections.Add(new Section(title, items));
        }
        return sections;
    }

    private static Item? LinkItem(IShellLinkW link, bool destination = false)
    {
        var target = new StringBuilder(1024); link.GetPath(target, target.Capacity, 0, 0);
        var arguments = new StringBuilder(2048); link.GetArguments(arguments, arguments.Capacity);
        var folder = new StringBuilder(1024); link.GetWorkingDirectory(folder, folder.Capacity);
        var properties = (IPropertyStore)link;
        string title = PropertyString(properties, Title) ?? "";
        if (title.Length == 0) { var description = new StringBuilder(1024); link.GetDescription(description, description.Capacity); title = description.ToString(); }
        // Titles may name a string resource ("@file.dll,-123").
        if (title.StartsWith('@')) title = Indirect(title) ?? "";
        if (title.Length > 0 && PropertyString(properties, AppUserModelId) is { } appId && appId.Contains('!')
            && PropertyString(properties, ActivationContext) is { Length: > 0 } activation)
            return new Item(title, () => Activate(appId, activation), @"shell:AppsFolder\" + appId);

        string file = target.ToString(), args = arguments.ToString(), directory = folder.ToString();
        if (destination)
        {
            link.GetIDList(out var id);
            if (id != 0)
            {
                try
                {
                    if (title.Length == 0) title = NameFromId(id, 0) ?? "";
                    if (file.Length == 0) file = NameFromId(id, unchecked((int)0x80028000)) ?? "";
                }
                finally { Marshal.FreeCoTaskMem(id); }
            }
            if (title.Length == 0) title = Path.GetFileName(Path.TrimEndingDirectorySeparator(file));
        }
        if (title.Length == 0 || file.Length == 0) return null;
        return new Item(title, () => Start(file, args, directory), file);
    }

    private static Section? Recent(string appId, DockApp app, Section? pinned, bool frequent)
    {
        var lists = (IApplicationDocumentLists)new ApplicationDocumentLists();
        try
        {
            lists.SetAppID(appId);
            var iid = typeof(IObjectArray).GUID;
            if (lists.GetList(frequent ? 1 : 0, MaxRecent, ref iid, out var found) != 0 || found is not IObjectArray array) return null;
            array.GetCount(out var count);
            var items = new List<Item>();
            var itemIid = typeof(IShellItem).GUID;
            for (uint i = 0; i < count; i++)
            {
                array.GetAt(i, ref itemIid, out var shellItem);
                if (shellItem is not IShellItem item) continue;
                string? name = DisplayName(item, 0), path = DisplayName(item, unchecked((int)0x80058000));
                if (name is null || path is null || pinned?.Items.Any(pin => string.Equals(pin.Target, path, StringComparison.OrdinalIgnoreCase)) == true) continue;
                string exe = app.Windows.Count > 0 ? app.Windows[0].Exe : app.Key;
                // Opened with the app, as the taskbar does; a folder goes to Explorer.
                items.Add(new Item(name, () => { if (Directory.Exists(path) || !File.Exists(exe)) Start(path, "", ""); else Start(exe, Quote(path), ""); }, path));
            }
            return new Section(frequent ? Loc.T("常用", "Frequent") : Loc.T("最近", "Recent"), items);
        }
        finally { Marshal.ReleaseComObject(lists); }
    }

    private static string Quote(string path) => "\"" + path + "\"";

    private static void Activate(string appId, string arguments)
    {
        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            try
            {
                CoAllowSetForegroundWindow(manager, 0);
                manager.ActivateApplication(appId, arguments, 0, out _);
            }
            finally { Marshal.ReleaseComObject(manager); }
        }
        catch (COMException) { }
    }

    private static void Start(string file, string arguments, string directory)
    {
        try { Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, WorkingDirectory = Directory.Exists(directory) ? directory : "" })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException) { }
    }

    private static string? DisplayName(IShellItem item, int kind)
    {
        if (item.GetDisplayName(kind, out var text) != 0 || text == 0) return null;
        try { return Marshal.PtrToStringUni(text); } finally { Marshal.FreeCoTaskMem(text); }
    }

    private static string? WindowAppId(nint window)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(window, ref iid, out var store) != 0 || store is not IPropertyStore properties) return null;
        try { return PropertyString(properties, AppUserModelId); } finally { Marshal.ReleaseComObject(properties); }
    }

    private static string? LinkAppId(string path)
    {
        try
        {
            var link = new ShellLink();
            ((IPersistFile)link).Load(path, 0);
            try { return PropertyString((IPropertyStore)link, AppUserModelId); } finally { Marshal.ReleaseComObject(link); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FileNotFoundException or UnauthorizedAccessException) { return null; }
    }

    private static string? PropertyString(IPropertyStore store, PropertyKey key)
    {
        var copy = key;
        if (store.GetValue(ref copy, out var value) != 0) return null;
        try
        {
            if (value.Pointer == 0) return null;
            return value.Type switch
            {
                8 => Marshal.PtrToStringBSTR(value.Pointer),
                31 => Marshal.PtrToStringUni(value.Pointer),
                _ => null
            };
        }
        finally { PropVariantClear(ref value); }
    }

    private static string? Indirect(string resource)
    {
        var text = new StringBuilder(512);
        return SHLoadIndirectString(resource, text, text.Capacity, 0) == 0 ? text.ToString() : null;
    }

    // A read-only COM stream over the file's bytes, so a shortcut can be read from where it starts; its position follows what was read.
    private sealed class ReadStream(Stream source) : IStream
    {
        public void Read(byte[] buffer, int count, nint read) { int n = source.Read(buffer, 0, count); if (read != 0) Marshal.WriteInt32(read, n); }
        public void Seek(long offset, int origin, nint position)
        {
            long at = source.Seek(offset, (SeekOrigin)origin);
            if (position != 0) Marshal.WriteInt64(position, at);
        }
        public void Stat(out STATSTG stat, int flag) => stat = new STATSTG { cbSize = source.Length, type = 2 };
        public void Write(byte[] buffer, int count, nint written) => throw new COMException("read-only", unchecked((int)0x80030005));
        public void SetSize(long size) => throw new COMException("read-only", unchecked((int)0x80030005));
        public void CopyTo(IStream target, long count, nint read, nint written) => throw new NotSupportedException();
        public void Commit(int flags) { }
        public void Revert() { }
        public void LockRegion(long offset, long count, int type) { }
        public void UnlockRegion(long offset, long count, int type) { }
        public void Clone(out IStream copy) => throw new NotSupportedException();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid format, int id) { public readonly Guid Format = format; public readonly int Id = id; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant { public ushort Type; public ushort Reserved1, Reserved2, Reserved3; public nint Pointer; public nint Extra; }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink;
    [ComImport, Guid("86bec222-30f2-47e0-9f25-60d11cd75c28")] private class ApplicationDocumentLists;
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] private class ApplicationActivationManager;

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, nint data, uint flags);
        void GetIDList(out nint list);
        void SetIDList(nint list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
    }

    [ComImport, Guid("00000109-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistStream
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load(IStream stream);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("3c594f9f-9f30-47a1-979a-c9e83d3d0a06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationDocumentLists
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int GetList(int type, uint wanted, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? list);
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
        void GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(int kind, out nint name);
    }

    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("ole32.dll")] private static extern int CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object manager, nint reserved);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, int capacity, nint reserved);
}
