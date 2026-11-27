namespace DeckIsland.Core;

// The Quick Toggles widget's buttons, after lumabar's: each is shown on the strip or not (chosen in its Quick Toggles list).
// Wi-Fi, Bluetooth and Sound are pills (the switch and an arrow that opens their panel); the rest are round buttons.
public enum QuickToggle { WiFi, Ethernet, Bluetooth, Sound, Hotspot, Airplane, Microphone, Camera, KeepAwake, DesktopIcons, DisplayMode, EnergySaver, DoNotDisturb }

public static class QuickToggles
{
    public static readonly IReadOnlyList<QuickToggle> Defaults = [QuickToggle.WiFi, QuickToggle.Ethernet, QuickToggle.Bluetooth, QuickToggle.Sound];

    // Pills carry an arrow beside their switch, so they take two button widths.
    public static bool IsPill(QuickToggle toggle) => toggle is QuickToggle.WiFi or QuickToggle.Bluetooth or QuickToggle.Sound;

    // The shown toggles in the order chosen (dragged in the Quick Toggles list), once each; unknown values dropped.
    public static List<QuickToggle> Normalize(IEnumerable<QuickToggle>? shown) =>
        shown is null ? [.. Defaults] : shown.Where(Enum.IsDefined).Distinct().ToList();

    // The strip's width in button widths: pills 2, round buttons 1, with a minimum of 1 so an empty strip still has a card.
    public static int Units(IReadOnlyCollection<QuickToggle> shown) => Math.Max(1, shown.Sum(t => IsPill(t) ? 2 : 1));

    public static string Name(QuickToggle toggle) => toggle switch
    {
        QuickToggle.WiFi => "Wi-Fi",
        QuickToggle.Ethernet => Loc.T("以太网", "Ethernet"),
        QuickToggle.Bluetooth => Loc.T("蓝牙", "Bluetooth"),
        QuickToggle.Sound => Loc.T("声音", "Sound"),
        QuickToggle.Hotspot => Loc.T("移动热点", "Hotspot"),
        QuickToggle.Airplane => Loc.T("飞行模式", "Airplane"),
        QuickToggle.Microphone => Loc.T("麦克风", "Microphone"),
        QuickToggle.Camera => Loc.T("摄像头", "Camera"),
        QuickToggle.KeepAwake => Loc.T("保持唤醒", "Keep Awake"),
        QuickToggle.DesktopIcons => Loc.T("桌面图标", "Desktop Icons"),
        QuickToggle.DisplayMode => Loc.T("显示模式", "Display Mode"),
        QuickToggle.EnergySaver => Loc.T("节能模式", "Energy Saver"),
        _ => Loc.T("勿扰", "Do Not Disturb")
    };
}
