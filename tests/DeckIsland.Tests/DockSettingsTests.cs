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
        var bar = DockGeometry.Bar(new PixelRect(0, 1020, 1920, 1080), DockEdge.Bottom, 400, 52);
        check("Dock bar is centred in its strip", bar == new PixelRect(760, 1024, 1160, 1076));
        check("Dock bar keeps its minimum and side margins",
            DockGeometry.Bar(new PixelRect(0, 0, 1920, 60), DockEdge.Top, 10, 52).Width == DockGeometry.MinBarWidth && DockGeometry.Bar(new PixelRect(0, 0, 1920, 60), DockEdge.Top, 5000, 52).Width == 1920 - 2 * DockGeometry.SideMargin);
        check("Dock keeps its height on a strip the shell moved",
            DockGeometry.KeepHeight(new PixelRect(0, 900, 1920, 1040), DockEdge.Bottom, 60) == new PixelRect(0, 980, 1920, 1040));
        check("Dock bar keeps its distance from the edge on either edge",
            DockGeometry.Bar(new PixelRect(0, 1000, 1920, 1080), DockEdge.Bottom, 400, 52, 20) is { Bottom: 1056, Height: 52 }
            && DockGeometry.Bar(new PixelRect(0, 0, 1920, 80), DockEdge.Top, 400, 52, 20) is { Top: 24, Height: 52 });
        check("Dock full width keeps the same gap at its ends",
            DockGeometry.Bar(new PixelRect(0, 1020, 1920, 1080), DockEdge.Bottom, 400, 52, 0, true) is { Left: 8, Right: 1912 });
        check("Dock window grows towards the screen's middle for magnification",
            DockGeometry.Window(new PixelRect(10, 1000, 410, 1052), DockEdge.Bottom, 30) == new PixelRect(10, 970, 410, 1052)
            && DockGeometry.Window(new PixelRect(10, 4, 410, 56), DockEdge.Top, 30) == new PixelRect(10, 4, 410, 86));
        check("Dock corners go from square to a pill", DockGeometry.CornerRadius(0, 52) == 0 && DockGeometry.CornerRadius(100, 52) == 26 && DockGeometry.CornerRadius(50, 52) == 13);
        check("Dock magnification peaks at the pointer and fades to nothing at its reach",
            DockGeometry.Magnify(0, 100, 50) == 1.5 && DockGeometry.Magnify(100, 100, 50) == 1 && DockGeometry.Magnify(50, 100, 50) is > 1.2 and < 1.3);
        var noon = new DateTime(2026, 11, 27, 13, 5, 9);
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        check("Dock clock follows 12 or 24 hours and seconds",
            DockGeometry.Clock(noon, true, false, culture) == "13:05" && DockGeometry.Clock(noon, true, true, culture) == "13:05:09"
            && DockGeometry.Clock(noon, false, false, culture) == "1:05 PM" && DockGeometry.Clock(noon, null, false, culture) == "13:05");
        var state = new DockTaskbarState(2, true, 42);
        check("Dock taskbar state round-trips and rejects damage", DockTaskbarState.Parse(state.ToJson()) == state && DockTaskbarState.Parse("{ nope") is null);
    }

    internal static void Apps(string root, Action<string, bool> check)
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
        var ungrouped = DockApps.Ungroup(apps, 13);
        check("Dock gives each window its own tile with grouping off, and never pins one",
            ungrouped.Count == 5 && ungrouped[0].Windows.Count == 1 && ungrouped[1].Key == "a.exe#13" && ungrouped[1].Active && !ungrouped[0].Active
            && DockApps.Arrange(pins, ungrouped).All(p => !p.Path.Contains('#')));
        var odd = new DockSettings { Spacing = 99, Corner = -5, MagnificationSize = 1, Distance = 400, Padding = 30 };
        odd.Normalize();
        check("Dock layout settings are kept in range", odd is { Spacing: 24, Corner: 0, MagnificationSize: 10, Distance: 40, Padding: 16 });
        var looks = Enum.GetValues<DockStyle>().SelectMany(style => Enum.GetValues<DockClearCards>().Select(cards => DockLook.For(style, cards))).ToList();
        check("Dock looks are valid colours, glass only on the glass styles",
            looks.All(l => new[] { l.Bar, l.Rim, l.Card, l.CardHover, l.CardActive, l.CardRim, l.Text, l.Dim, l.Divider }.All(c => ThemeColor.TryParse(c, out _)))
            && DockLook.For(DockStyle.Liquid, 0).Glass && DockLook.For(DockStyle.IOS, 0).Glass && DockLook.For(DockStyle.Midnight, 0).Glass
            && !DockLook.For(DockStyle.Black, 0).Glass && !DockLook.For(DockStyle.Clear, 0).Glass);
        check("Dock Clear has no bar, and white cards get dark text",
            DockLook.For(DockStyle.Clear, DockClearCards.White) is { Bar: "#00000000", Text: "#FF1D1D1F" } && DockLook.For(DockStyle.White, 0).Text == "#FF1D1D1F");
        check("Dock accent keeps a colour and falls back to blue",
            DockLook.Accents.Length == 9 && DockLook.NormalizeAccent("#ff7a1a") == "#FF7A1A" && DockLook.NormalizeAccent("nope") == DockLook.DefaultAccent && DockLook.NormalizeAccent("#80FF0000") == DockLook.DefaultAccent);
        var widgets = DockWidgets.Add(DockWidgets.Add(DockWidgets.Add([], DockWidgetKind.Cpu), DockWidgetKind.Memory), DockWidgetKind.Battery);
        check("Dock widgets are added with their own ids, moved and removed",
            widgets.Select(w => w.Id).Distinct().Count() == 3 && DockWidgets.Move(widgets, widgets[2].Id, 0)[0].Kind == DockWidgetKind.Battery
            && DockWidgets.Remove(widgets, widgets[1].Id).Select(w => w.Kind).SequenceEqual([DockWidgetKind.Cpu, DockWidgetKind.Battery]));
        check("Dock widget search finds by name or description within a category",
            DockWidgets.Find(DockWidgetCategory.System, "").Count == 5 && DockWidgets.Catalog.Select(i => i.Kind).Distinct().Count() == Enum.GetValues<DockWidgetKind>().Length && DockWidgets.Find(null, "ram").Single().Kind == DockWidgetKind.Memory && DockWidgets.Find(DockWidgetCategory.Ai, "ram").Count == 0);
        var (profiles, currentProfile) = DockWidgets.Normalize([new DockProfile("a", " ", [new DockWidget("x", (DockWidgetKind)99), new DockWidget("y", DockWidgetKind.Cpu), new DockWidget("y", DockWidgetKind.Memory)]),
            new DockProfile("a", "dup", [])], "gone");
        check("Dock profiles drop unknown and repeated widgets and repeated profiles, and keep a current one",
            profiles.Count == 1 && profiles[0].Widgets.Count == 1 && profiles[0].Name.Length > 0 && currentProfile == "a" && DockWidgets.Normalize([], null).Profiles.Count == 1);
        var withWidgets = new DockSettings().WithWidgets(widgets);
        var copied = withWidgets.Copy(); copied.Profiles[0].Widgets.Clear();
        check("Dock widgets belong to the current profile and are copied deeply", withWidgets.Widgets.Count == 3 && new DockSettings().Widgets.Count == 0);
        var widgetStore = new DockSettingsStore(Path.Combine(root, "dock-widgets"));
        widgetStore.Save(withWidgets);
        check("Dock profiles and widgets survive saving", new DockSettingsStore(Path.Combine(root, "dock-widgets")).Load().Widgets.Select(w => w.Kind).SequenceEqual(widgets.Select(w => w.Kind)));
        File.WriteAllText(widgetStore.FilePath, """{"Style":"White","Profiles":[{"Id":"p","Name":"P","Widgets":[{"Id":"w","Kind":"FutureThing"},{"Id":"c","Kind":"Cpu"}]}],"CurrentProfile":"p"}""");
        var tolerant = new DockSettingsStore(Path.Combine(root, "dock-widgets")).Load();
        check("Dock skips a widget it does not know instead of losing its settings", tolerant.Style == DockStyle.White && tolerant.Widgets.Single().Kind == DockWidgetKind.Cpu);
        check("Dock shows its buttons, previews and shortcuts by default",
            new DockSettings() is { ShowStart: true, ShowTrash: true, ShowDesktopButton: true, WindowPreviews: true, AppShortcuts: true });
    }

    internal static void Smart(Action<string, bool> check)
    {
        check("Dock search reads the volume and the dock's edge, size, style, auto-hide and lock",
            DockSmart.Parse("volume 30") is { Kind: DockCommandKind.Volume, Number: 30 } && DockSmart.Parse("Volum 150") is { Kind: DockCommandKind.Volume, Number: 100 }
            && DockSmart.Parse("dock to the top") is { Kind: DockCommandKind.Edge, Edge: DockEdge.Top } && DockSmart.Parse("muta bara jos") is { Kind: DockCommandKind.Edge, Edge: DockEdge.Bottom }
            && DockSmart.Parse("make the dock bigger").Kind == DockCommandKind.Bigger && DockSmart.Parse("dock mai mic").Kind == DockCommandKind.Smaller
            && DockSmart.Parse("style midnight") is { Kind: DockCommandKind.Style, Style: DockStyle.Midnight } && DockSmart.Parse("auto-hide off") is { Kind: DockCommandKind.AutoHide, On: false }
            && DockSmart.Parse("unlock dock") is { Kind: DockCommandKind.Lock, On: false } && DockSmart.Parse("lock the dock") is { Kind: DockCommandKind.Lock, On: true });
        check("Dock search works out sums and leaves other text alone",
            DockSmart.Parse("2 + 3 * (4 - 1)") is { Kind: DockCommandKind.Answer, Text: "11" } && DockSmart.Sum("1/0") is null && DockSmart.Sum("12") is null
            && DockSmart.Sum("2+") is null && DockSmart.Parse("chrome").Kind == DockCommandKind.None && DockSmart.Parse("  ").Kind == DockCommandKind.None);
        check("Dock quiet hours run across midnight",
            DockSmart.Quiet(new DateTime(2026, 1, 1, 23, 0, 0), 22 * 60, 7 * 60) && DockSmart.Quiet(new DateTime(2026, 1, 1, 6, 59, 0), 22 * 60, 7 * 60)
            && !DockSmart.Quiet(new DateTime(2026, 1, 1, 12, 0, 0), 22 * 60, 7 * 60) && DockSmart.Quiet(new DateTime(2026, 1, 1, 13, 0, 0), 12 * 60, 14 * 60) && !DockSmart.Quiet(DateTime.Now, 60, 60));
        var now = new DateTime(2026, 1, 1, 10, 0, 0);
        CalendarEvent Event(string id, int minutes, bool allDay = false) => new(id, Guid.Empty, id, now.AddMinutes(minutes), now.AddMinutes(minutes + 30), allDay, "", null);
        var events = new[] { Event("later", 30), Event("allday", 2, true), Event("soon", 4), Event("past", -10) };
        check("Dock meeting mode picks the next timed event within five minutes, once",
            DockSmart.DueMeeting(events, now, TimeSpan.FromMinutes(5), new HashSet<string>())?.Id == "soon"
            && DockSmart.DueMeeting(events, now, TimeSpan.FromMinutes(5), new HashSet<string> { "soon" }) is null);
        check("Dock sunset dark mode is light by day and the chosen style by night",
            DockSmart.Effective(DockStyle.Midnight, true, true) == DockStyle.White && DockSmart.Effective(DockStyle.Midnight, true, false) == DockStyle.Midnight
            && DockSmart.Effective(DockStyle.White, true, false) == DockStyle.Black && DockSmart.Effective(DockStyle.Liquid, false, true) == DockStyle.Liquid && DockSmart.Effective(DockStyle.IOS, true, null) == DockStyle.IOS);
    }
}
