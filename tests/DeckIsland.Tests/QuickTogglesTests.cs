using DeckIsland.Core;

internal static class QuickTogglesTests
{
    internal static void Run(Action<string, bool> check)
    {
        check("Quick toggles default to Wi-Fi, Ethernet, Bluetooth and Sound", new DockSettings().ShownToggles.SequenceEqual(QuickToggles.Defaults));
        var settings = new DockSettings { QuickToggles = ["Sound", "nope", "WiFi", "Sound", "Camera"] };
        check("Quick toggles keep known names once, in the chosen order", settings.ShownToggles.SequenceEqual([QuickToggle.Sound, QuickToggle.WiFi, QuickToggle.Camera]));
        check("An empty choice shows no toggles but keeps a one-unit card", new DockSettings { QuickToggles = [] }.ShownToggles.Count == 0
            && QuickToggles.Units([]) == 1);
        check("Pills count double", QuickToggles.Units([QuickToggle.WiFi, QuickToggle.Camera]) == 3);
        var copy = settings.Copy();
        copy.QuickToggles!.Add("Hotspot");
        check("Copy does not share the toggle list", settings.QuickToggles!.Count == 5);
    }
}
