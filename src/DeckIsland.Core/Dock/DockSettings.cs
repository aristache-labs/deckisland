using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DeckIsland.Core;

// The screen edge DeckIsland Dock sits on.
public enum DockEdge { Bottom, Top }
// What the dock does to the Windows taskbar while it runs: auto-hide it, hide it completely, or leave it alone.
public enum DockTaskbarMode { AutoHide, Hide, Leave }

// DeckIsland Dock's own settings, kept apart from DeckSettings in dock.json. DeckSettings.DockEnabled is the only switch the
// island's settings hold for it.
public sealed class DockSettings
{
    public const int MinSize = 36, MaxSize = 96;
    // The bar heights of the Small, Medium and Large presets; Size moves freely between MinSize and MaxSize.
    public static readonly int[] SizePresets = [46, 52, 62];

    [JsonConverter(typeof(JsonStringEnumConverter))] public DockEdge Edge { get; set; } = DockEdge.Bottom;
    [JsonConverter(typeof(JsonStringEnumConverter))] public DockTaskbarMode TaskbarMode { get; set; } = DockTaskbarMode.AutoHide;
    // The bar's height in device-independent pixels.
    public int Size { get; set; } = 52;
    // The pinned apps in their order; null until they are first taken from the Windows taskbar's pins.
    public List<DockPin>? Pins { get; set; }
    // The Start button at the left end, and the Trash and Show desktop buttons at the right.
    public bool ShowStart { get; set; } = true;
    public bool ShowTrash { get; set; } = true;
    public bool ShowDesktopButton { get; set; } = true;
    // Live pictures of an app's windows while the pointer rests on it.
    public bool WindowPreviews { get; set; } = true;
    // Win+1 to Win+0 open the dock's first ten apps, as on the taskbar.
    public bool AppShortcuts { get; set; } = true;

    public DockSettings Copy()
    {
        var copy = (DockSettings)MemberwiseClone();
        copy.Pins = Pins is null ? null : [.. Pins];
        return copy;
    }

    public void Normalize()
    {
        if (!Enum.IsDefined(Edge)) Edge = DockEdge.Bottom;
        if (!Enum.IsDefined(TaskbarMode)) TaskbarMode = DockTaskbarMode.AutoHide;
        Size = Math.Clamp(Size, MinSize, MaxSize);
        Pins = Pins?.Where(pin => pin is not null && !string.IsNullOrWhiteSpace(pin.Path))
            .Select(pin => pin with { Name = string.IsNullOrWhiteSpace(pin.Name) ? Path.GetFileNameWithoutExtension(pin.Path) : pin.Name }).ToList();
    }
}

// dock.json next to settings.json. Keys this build does not know are written back as they were, and a file that cannot be read is
// copied aside before defaults replace it, as SettingsStore does.
public sealed class DockSettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private JsonObject _unknown = [];
    public string FilePath => Path.Combine(directory, "dock.json");
    // The copy an unreadable file was kept as by the last Load, or null.
    public string? KeptUnreadable { get; private set; }

    public DockSettings Load()
    {
        _unknown = []; KeptUnreadable = null;
        try
        {
            if (!File.Exists(FilePath)) return new DockSettings();
            var root = JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject() ?? throw new JsonException("dock.json is empty");
            var settings = root.Deserialize<DockSettings>() ?? new DockSettings();
            var known = JsonSerializer.SerializeToNode(new DockSettings())!.AsObject();
            _unknown = new JsonObject(root.Where(pair => !known.ContainsKey(pair.Key)).Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            KeptUnreadable = KeepUnreadable();
            return new DockSettings();
        }
    }

    public void Save(DockSettings settings)
    {
        settings.Normalize();
        var file = JsonSerializer.SerializeToNode(settings)!.AsObject();
        foreach (var (key, value) in _unknown) if (!file.ContainsKey(key)) file[key] = value?.DeepClone();
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, file.ToJsonString(Options));
        File.Move(temporary, FilePath, true);
    }

    private string? KeepUnreadable()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var name = $"dock.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            File.Copy(FilePath, Path.Combine(directory, name), true);
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
