using System.Windows;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    internal const int CallsSettingsPage = 24;

    private void CallsSettings()
    {
        SectionTitle(Loc.T("通话", "Calls"), description: Loc.T("麦克风开启时，灵动岛能为你做的事。", "What the island does while the microphone is open."));
        Group(
            Describe(Toggle(Loc.T("通话计时", "Call timer"), S.CallTimer, value => Change(s => s.CallTimer = value),
                help: Loc.T("麦克风开启时，灵动岛显示已持续的时间。", "While the microphone is open, the island shows how long you have been talking and a mute switch. Click the mic, or use the hotkey.")),
                Loc.T("显示通话时长和静音开关。", "How long you have talked, and a mute switch.")),
            Describe(Toggle(Loc.T("麦克风提示", "Microphone HUD"), S.MicHud, value => Change(s => s.MicHud = value),
                help: Loc.T("麦克风被静音或取消静音时显示提示，包括由其他应用操作时。", "Show a readout whenever the microphone is muted or unmuted, including by other apps.")),
                Loc.T("静音或取消静音时提示。", "A readout when it is muted or unmuted.")));
    }

    private void CallSizeControls() => PageSizeControls(DeckPage.Calls, Loc.T("通话", "Calls"));
}
