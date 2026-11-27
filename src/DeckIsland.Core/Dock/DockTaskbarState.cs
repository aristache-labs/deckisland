using System.Text.Json;

namespace DeckIsland.Core;

// What DeckIsland Dock changed on the Windows taskbar, written to disk before the change so it can always be undone: the taskbar's
// ABM_GETSTATE flags before, whether its windows were hidden too, and the process that changed it (the watchdog restores only for
// the process it watched, so an old watchdog never undoes a newer run's change).
public sealed record DockTaskbarState(uint Original, bool Complete, int Pid)
{
    public const string FileName = "dock-taskbar-state.json";

    public string ToJson() => JsonSerializer.Serialize(this);

    // Null when the text is not a state; the caller then shows the taskbar anyway, so it is never left hidden.
    public static DockTaskbarState? Parse(string text)
    {
        try { return JsonSerializer.Deserialize<DockTaskbarState>(text); }
        catch (JsonException) { return null; }
    }
}
