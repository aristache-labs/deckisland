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
}
