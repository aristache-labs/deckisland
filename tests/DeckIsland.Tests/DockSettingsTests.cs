using System.Text.Json.Nodes;
using DeckIsland.Core;

internal static class DockSettingsTests
{
    internal static void Run(string root, Action<string, bool> check)
    {
        var folder = Path.Combine(root, "dock");
        var store = new DockSettingsStore(folder);
        check("Dock starts with defaults without a file", store.Load() is { Edge: DockEdge.Bottom, TaskbarMode: DockTaskbarMode.AutoHide, Size: 52 });
        check("Dock is off in the island's settings by default", !new DeckSettings().DockEnabled && Profiles.GlobalKeys.Contains(nameof(DeckSettings.DockEnabled)));

        store.Save(new DockSettings { Edge = DockEdge.Top, TaskbarMode = DockTaskbarMode.Hide, Size = 500 });
        var read = new DockSettingsStore(folder).Load();
        check("Dock settings round-trip and clamp the size", read is { Edge: DockEdge.Top, TaskbarMode: DockTaskbarMode.Hide, Size: DockSettings.MaxSize });

        var file = JsonNode.Parse(File.ReadAllText(store.FilePath))!.AsObject();
        file["FutureSetting"] = 7;
        File.WriteAllText(store.FilePath, file.ToJsonString());
        var keeping = new DockSettingsStore(folder);
        keeping.Save(keeping.Load());
        check("Dock keeps settings it does not know", JsonNode.Parse(File.ReadAllText(store.FilePath))!["FutureSetting"]?.GetValue<int>() == 7);

        File.WriteAllText(store.FilePath, "{ broken");
        var broken = new DockSettingsStore(folder);
        check("Dock falls back to defaults and keeps an unreadable file aside",
            broken.Load().Edge == DockEdge.Bottom && broken.KeptUnreadable is { } kept && File.Exists(Path.Combine(folder, kept)));
    }

    internal static void Geometry(Action<string, bool> check)
    {
        check("Dock strip heights match the S/M/L presets",
            DockGeometry.StripHeight(46) == 52 && DockGeometry.StripHeight(52) == 60 && DockGeometry.StripHeight(62) == 72);
        var screen = new PixelRect(0, 0, 1920, 1080);
        check("Dock strip sits on the chosen edge",
            DockGeometry.Strip(screen, DockEdge.Bottom, 60) == new PixelRect(0, 1020, 1920, 1080) && DockGeometry.Strip(screen, DockEdge.Top, 60) == new PixelRect(0, 0, 1920, 60));
        var bar = DockGeometry.Bar(new PixelRect(0, 1020, 1920, 1080), 400, 52);
        check("Dock bar is centred in its strip", bar == new PixelRect(760, 1024, 1160, 1076));
        check("Dock bar keeps its minimum and side margins",
            DockGeometry.Bar(new PixelRect(0, 0, 1920, 60), 10, 52).Width == DockGeometry.MinBarWidth && DockGeometry.Bar(new PixelRect(0, 0, 1920, 60), 5000, 52).Width == 1920 - 2 * DockGeometry.SideMargin);
        check("Dock keeps its height on a strip the shell moved",
            DockGeometry.KeepHeight(new PixelRect(0, 900, 1920, 1040), DockEdge.Bottom, 60) == new PixelRect(0, 980, 1920, 1040));
        var state = new DockTaskbarState(2, true, 42);
        check("Dock taskbar state round-trips and rejects damage", DockTaskbarState.Parse(state.ToJson()) == state && DockTaskbarState.Parse("{ nope") is null);
    }

    internal static void Apps(Action<string, bool> check)
    {
        DockWindowInfo Win(long handle, string key, int z, bool minimized = false) => new(handle, key, key, "t" + handle, minimized, z);
        var pins = new List<(DockPin, string)> { (new DockPin("a.lnk", "A"), "a.exe"), (new DockPin("b.lnk", "B"), "b.exe"), (new DockPin("dup.lnk", "A again"), "a.exe") };
        var windows = new List<DockWindowInfo> { Win(10, "c.exe", 0), Win(11, "a.exe", 1), Win(12, "d.exe", 2), Win(13, "a.exe", 3) };
        var (apps, order) = DockApps.Build(pins, windows, ["d.exe"], 10, w => w.Key.ToUpperInvariant());
        check("Dock lists pins first, once per app, then running apps keeping their order",
            apps.Select(a => a.Key).SequenceEqual(["a.exe", "b.exe", "d.exe", "c.exe"]) && order.SequenceEqual(["d.exe", "c.exe"]));
        check("Dock groups an app's windows and marks the app in front",
            apps[0].Windows.Count == 2 && !apps[1].Open && apps[3].Active && apps[3].Name == "C.EXE" && apps[2].Launch == "d.exe");

        check("Dock click starts an app without windows", DockApps.Click(apps[1], 10).Kind == DockClickKind.Launch);
        check("Dock click brings the topmost window of an app behind", DockApps.Click(apps[0], 10) == new DockClick(DockClickKind.Focus, 11));
        check("Dock click minimizes the only window in front", DockApps.Click(apps[3], 10) == new DockClick(DockClickKind.Minimize, 10));
        check("Dock click cycles an app's windows", DockApps.Click(apps[0], 11) == new DockClick(DockClickKind.Focus, 13) && DockApps.Click(apps[0], 13) == new DockClick(DockClickKind.Focus, 11));

        var unpinned = DockApps.Unpin(pins, "a.exe");
        check("Dock unpins every pin of an app and pins once", unpinned.Count == 1 && unpinned[0].Name == "B"
            && DockApps.Pin(unpinned, new DockPin("B.LNK", "B")).Count == 1 && DockApps.Pin(unpinned, new DockPin("c.exe", "C")).Count == 2);
        var copy = new DockSettings { Pins = [new DockPin("x.exe", "")] }.Copy();
        copy.Normalize();
        check("Dock pins get a name and are copied deeply", copy.Pins![0].Name == "x");

        var tiles = new List<DockApp> { apps[1], apps[3], apps[0], apps[2] };
        var arranged = DockApps.Arrange(pins, tiles);
        check("Dock drag keeps pins in their new order and pins a running app dropped among them",
            arranged.Select(p => p.Path).SequenceEqual(["b.lnk", "c.exe", "a.lnk"]) && arranged[1].Name == "C.EXE");
        check("Dock drag leaves running apps after the last pin unpinned", DockApps.Arrange(pins, [apps[0], apps[1], apps[2], apps[3]]).Count == 2);
        check("Dock shows its buttons, previews and shortcuts by default",
            new DockSettings() is { ShowStart: true, ShowTrash: true, ShowDesktopButton: true, WindowPreviews: true, AppShortcuts: true });
    }
}
