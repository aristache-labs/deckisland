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
}
