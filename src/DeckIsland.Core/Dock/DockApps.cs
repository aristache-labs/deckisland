namespace DeckIsland.Core;

// A pinned app: a shortcut (.lnk) or an executable, with the name shown for it.
public sealed record DockPin(string Path, string Name);

// A window the Windows taskbar would show a button for. Key groups the windows of one app (the lower-cased exe path); Z is its
// place in the desktop's z-order, 0 on top.
public sealed record DockWindowInfo(long Handle, string Key, string Exe, string Title, bool Minimized, int Z);

// One tile on the dock: a pinned app, with or without windows, or a running app that is not pinned.
// WindowTile marks a window's own tile (Ungroup), which is never pinned.
public sealed record DockApp(string Key, string Name, bool Pinned, string Launch, IReadOnlyList<DockWindowInfo> Windows, bool Active, bool WindowTile = false)
{
    public bool Open => Windows.Count > 0;
}

public enum DockClickKind { Launch, Minimize, Focus }
public readonly record struct DockClick(DockClickKind Kind, long Handle);

// The dock's apps, after lumabar's apps.rs: pinned apps first in their own order, then running apps in the order they appeared.
public static class DockApps
{
    // pins pairs each pin with the key its windows will have; order is the running apps' order from the last call, and the order to
    // keep for the next one comes back with the tiles.
    public static (List<DockApp> Apps, List<string> Order) Build(IReadOnlyList<(DockPin Pin, string Key)> pins, IReadOnlyList<DockWindowInfo> windows,
        IReadOnlyList<string> order, long foreground, Func<DockWindowInfo, string> name)
    {
        var groups = windows.GroupBy(w => w.Key).ToDictionary(g => g.Key, g => (IReadOnlyList<DockWindowInfo>)g.OrderBy(w => w.Handle).ToList());
        IReadOnlyList<DockWindowInfo> Of(string key) => groups.TryGetValue(key, out var list) ? list : [];
        bool ActiveIn(IReadOnlyList<DockWindowInfo> list) => list.Any(w => w.Handle == foreground);

        var apps = new List<DockApp>();
        var pinned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pin, key) in pins)
        {
            if (!pinned.Add(key)) continue;
            var list = Of(key);
            apps.Add(new DockApp(key, pin.Name, true, pin.Path, list, ActiveIn(list)));
        }
        var kept = order.Where(key => groups.ContainsKey(key) && !pinned.Contains(key)).ToList();
        foreach (var window in windows)
            if (!pinned.Contains(window.Key) && !kept.Contains(window.Key)) kept.Add(window.Key);
        foreach (var key in kept)
        {
            var list = groups[key];
            apps.Add(new DockApp(key, name(list[0]), false, list[0].Exe, list, ActiveIn(list)));
        }
        return (apps, kept);
    }

    // A click on a tile: start the app when it has no window; minimize its only window when that is in front; with several, go to
    // the next one; otherwise bring its topmost window forward.
    public static DockClick Click(DockApp app, long foreground)
    {
        if (app.Windows.Count == 0) return new DockClick(DockClickKind.Launch, 0);
        int index = app.Windows.ToList().FindIndex(w => w.Handle == foreground);
        if (index >= 0 && app.Windows.Count == 1) return new DockClick(DockClickKind.Minimize, foreground);
        if (index >= 0) return new DockClick(DockClickKind.Focus, app.Windows[(index + 1) % app.Windows.Count].Handle);
        return new DockClick(DockClickKind.Focus, app.Windows.MinBy(w => w.Z)!.Handle);
    }

    // Group windows off: an app's second and later windows get tiles of their own, after it, keyed by app and window.
    public static List<DockApp> Ungroup(IReadOnlyList<DockApp> apps, long foreground) => apps.SelectMany(app => app.Windows.Count < 2 ? [app]
        : app.Windows.Select((window, i) => i == 0
            ? app with { Windows = [window], Active = window.Handle == foreground }
            : new DockApp(app.Key + WindowKeySeparator + window.Handle, app.Name, false, app.Launch, [window], window.Handle == foreground, WindowTile: true))).ToList();

    public const char WindowKeySeparator = '#';

    // The pins after the tiles were dragged into this order: every tile up to the last pinned one is pinned, in the new order,
    // so a running app dropped among the pins is pinned there and a pin dropped past running apps takes them with it.
    public static List<DockPin> Arrange(IReadOnlyList<(DockPin Pin, string Key)> pins, IReadOnlyList<DockApp> tiles)
    {
        int last = -1;
        for (int i = 0; i < tiles.Count; i++) if (tiles[i].Pinned) last = i;
        // A window's own tile (Ungroup) is not an app to pin.
        return tiles.Take(last + 1).Where(tile => !tile.WindowTile).Select(tile => pins.Where(p => p.Key == tile.Key).Select(p => p.Pin).FirstOrDefault() ?? new DockPin(tile.Launch, tile.Name)).ToList();
    }

    // The pins with the one for this key removed, or with this app added at the end.
    public static List<DockPin> Unpin(IReadOnlyList<(DockPin Pin, string Key)> pins, string key) => pins.Where(p => p.Key != key).Select(p => p.Pin).ToList();
    public static List<DockPin> Pin(IReadOnlyList<DockPin> pins, DockPin pin) =>
        pins.Any(p => string.Equals(p.Path, pin.Path, StringComparison.OrdinalIgnoreCase)) ? [.. pins] : [.. pins, pin];
}
