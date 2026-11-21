namespace BrimDeck.Core;

public enum HudIcon { Volume, Muted, Brightness, Battery, Charging, Bluetooth, Lock, Download, Done, Notice }

// One readout the island shows under the collapsed row: an icon, a line of text, and on the right a level bar, an on/off dot or a
// short figure. A message with the same Key as the one shown updates it in place (a volume key held down moves the bar).
public sealed record HudMessage(string Key, HudIcon Icon, string Text, double? Level = null, bool? On = null, string? Figure = null,
    TimeSpan? Hold = null);

public static class HudFormat
{
    public static string Speed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:0.#} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{Math.Max(0, bytesPerSecond):0} B/s"
    };

    public static string LockName(string key) => key switch
    {
        "caps" => "Caps Lock", "num" => "Num Lock", _ => "Scroll Lock"
    };

    // The part of a Windows privacy-store key that names the program: "C:#Tools#zoom.exe" becomes "zoom".
    public static string AppName(string consentKey)
    {
        string last = consentKey[(consentKey.LastIndexOf('#') + 1)..];
        return last.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
    }

    public static bool Ignored(IEnumerable<string> ignored, string name) =>
        ignored.Any(item => string.Equals(item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? item[..^4] : item, name, StringComparison.OrdinalIgnoreCase));
}

// The notices the island has shown: the last 24, newest first.
public sealed record NoticeItem(string App, string Title, string Body, DateTimeOffset At);

public sealed class NoticeLog
{
    public const int Capacity = 24;
    private readonly List<NoticeItem> _items = [];

    public IReadOnlyList<NoticeItem> Items => _items;

    public void Add(NoticeItem item)
    {
        _items.Insert(0, item);
        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
    }

    public void Clear() => _items.Clear();
}
