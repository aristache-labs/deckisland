using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace DeckIsland.Native;

// Teams mutes its own microphone and turns its own camera off without telling Windows: the device stays open and its mute switch stays
// off. The meeting window's own buttons say it, read through UI Automation: "microphone-button" is named "Unmute mic" while muted and
// "video-button" "Turn camera on" while the camera is off (Teams' English names, as seen in new Teams). UI Automation calls into Teams
// and walks its web page, so this runs off the UI thread (App.CheckCallsAsync), and a window without the buttons is looked in again
// only every few seconds.
// shortcut: only Teams' English button names are read; another language gives null and the pill falls back to Windows' own state.
internal static class TeamsCall
{
    private static readonly Condition Windows = new PropertyCondition(AutomationElement.ClassNameProperty, "TeamsWebView");
    private static readonly Condition Mic = new PropertyCondition(AutomationElement.AutomationIdProperty, "microphone-button");
    private static readonly Condition Camera = new PropertyCondition(AutomationElement.AutomationIdProperty, "video-button");
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(4);
    private static AutomationElement? _mic, _camera;
    private static DateTime _nextSearch;

    // Whether Teams has its microphone muted and its camera on, each null when no meeting window shows it.
    public static (bool? Muted, bool? Camera) Read()
    {
        try
        {
            if (_mic is null && DateTime.UtcNow >= _nextSearch) Find();
            return (Named(ref _mic, "Unmute", "Mute"), Named(ref _camera, "Turn camera off", "Turn camera on"));
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
        {
            _mic = _camera = null;
            return (null, null);
        }
    }

    public static void Forget() { _mic = _camera = null; _nextSearch = default; }

    private static void Find()
    {
        _nextSearch = DateTime.UtcNow + Retry;
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, Windows))
        {
            if (window.FindFirst(TreeScope.Descendants, Mic) is not { } mic) continue;
            _mic = mic;
            _camera = window.FindFirst(TreeScope.Descendants, Camera);
            return;
        }
    }

    // True when the button's name starts with yes, false with no; a button gone (the meeting ended) is dropped and looked for again.
    private static bool? Named(ref AutomationElement? button, string yes, string no)
    {
        if (button is null) return null;
        string name;
        try { name = button.Current.Name ?? ""; }
        catch (ElementNotAvailableException) { button = null; return null; }
        if (name.Length == 0) { button = null; return null; }
        return name.StartsWith(yes, StringComparison.OrdinalIgnoreCase) ? true : name.StartsWith(no, StringComparison.OrdinalIgnoreCase) ? false : null;
    }
}
