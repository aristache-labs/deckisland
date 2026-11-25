namespace DeckIsland.Core;

public enum HudIcon { Volume, Muted, Brightness, Battery, Charging, Bluetooth, Lock, Download, Done, Notice, NumLock, ScrollLock, Cancel, Mic, MicOff }

// One readout the island shows under the collapsed row: an icon, a line of text, and on the right a level bar, an on/off dot or a
// short figure. Reserve is the longest text the same readout can say, whose room is kept so the surface does not change size between them. IconLevel is the charge a battery icon holds, FigureColor the color (a hex) of the figure. Body and Picture make it a card (a notification: its app's picture, the title in Text and the message under it). Label names a readout without text (volume) for the tooltip and screen readers. Lit colors the icon: something that has just been switched on, never its switching off. A message with the same Key as the one shown updates it in place (a volume key held down moves the bar).
public sealed record HudMessage(string Key, HudIcon Icon, string Text, double? Level = null, bool? On = null, string? Figure = null,
    TimeSpan? Hold = null, bool Lit = false, string? Reserve = null, string? Label = null,
    string? Body = null, byte[]? Picture = null, double? IconLevel = null, string? FigureColor = null);

// A notification's line too long for its card scrolls left once, at one speed, after a short wait, as Crest's do; the card stays until
// it has scrolled and been read (Tail), never past the limit chosen for long texts. A line that would take longer stops where it got.
public static class HudScroll
{
    public const double Lead = 1, Tail = 1.5, Speed = 45;
    // overflow: how much wider the line is than its room, in DIPs at scale 1. Returns how long it scrolls and how long the card stays.
    public static (double Scroll, double Hold) Plan(double overflow, double holdSeconds, double limitSeconds)
    {
        if (overflow <= .5) return (0, holdSeconds);
        double hold = Math.Max(holdSeconds, Math.Min(Lead + overflow / Speed + Tail, Math.Max(holdSeconds, limitSeconds)));
        return (Math.Min(overflow / Speed, Math.Max(0, hold - Lead - Tail)), hold);
    }
}

public static class HudFormat
{
    public static string Speed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:0.#} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{Math.Max(0, bytesPerSecond):0} B/s"
    };

    // How long a call has run: "0:06", "12:34", "1:02:03".
    public static string Duration(TimeSpan span)
    {
        int total = (int)Math.Max(0, span.TotalSeconds);
        return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60}:{total % 60:00}";
    }

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
public sealed record NoticeItem(string App, string Title, string Body, DateTimeOffset At, byte[]? Picture = null, string? AppId = null);

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

    public void Remove(NoticeItem item) => _items.Remove(item);

    public void Clear() => _items.Clear();
}
