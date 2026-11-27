using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DeckIsland.Core;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Win32;

namespace DeckIsland.Native;

internal static partial class DockAppWindows
{
    private const string AppsFolder = @"shell:AppsFolder\";

    private static List<DockPin> WindowsPins(List<DockPin> shortcuts)
    {
        try
        {
            using var taskband = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband");
            if (taskband?.GetValue("Favorites") is not byte[] favorites) return shortcuts;
            int Position(string value) => favorites.AsSpan().IndexOf(Encoding.Unicode.GetBytes(value + '\0'));
            // Taskband carries packaged app IDs even when Explorer hasn't created a .lnk for them.
            var pinned = shortcuts.Select(pin => (Pin: pin, Index: Position(Path.GetFileName(pin.Path))))
                .Where(entry => entry.Index >= 0).ToList();
            if (pinned.Count == 0 && shortcuts.Count > 0)
                pinned.AddRange(shortcuts.Select(pin => (pin, int.MaxValue)));
            pinned.AddRange(PackagedPins(favorites));
            return pinned.OrderBy(entry => entry.Index).Select(entry => entry.Pin).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return shortcuts; }
    }

    private static List<(DockPin Pin, int Index)> PackagedPins(byte[] favorites)
    {
        var pins = new List<(DockPin, int)>();
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            foreach (dynamic item in folder.Items())
            {
                if (item.Path is not string id || !id.Contains('!') || item.Name is not string name) continue;
                int index = favorites.AsSpan().IndexOf(Encoding.Unicode.GetBytes(id + '\0'));
                if (index >= 0) pins.Add((new DockPin(AppsFolder + id, name), index));
            }
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException) { }
        return pins;
    }
}
