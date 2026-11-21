using System.Windows;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    internal const int CallsSettingsPage = 24;

    private void CallsSettings()
    {
        SectionTitle(Loc.T("通话", "Calls"));
        Group(
            Toggle(Loc.T("通话计时", "Call timer"), S.CallTimer, value => Change(s => s.CallTimer = value),
                help: Loc.T("麦克风开启时，灵动岛显示已持续的时间。", "While the microphone is open, the island shows how long you have been talking and a mute switch. Click the mic, or use the hotkey.")),
            Toggle(Loc.T("麦克风提示", "Microphone HUD"), S.MicHud, value => Change(s => s.MicHud = value),
                help: Loc.T("麦克风被静音或取消静音时显示提示，包括由其他应用操作时。", "Show a readout whenever the microphone is muted or unmuted, including by other apps.")),
            Toggle(Loc.T("通话标签页", "Call tab"), S.CallsPage, value => Change(s => s.CallsPage = value),
                help: Loc.T("通话标签页：麦克风静音开关，以及通话前测试扬声器和麦克风。", "A tab for calls: the call timer, mute, your speaker and microphone, and a camera mirror that opens only when you press Check camera. The camera is released the moment the tab closes.")));
    }

    private void CallSizeControls() => PageSizeControls(DeckPage.Calls, Loc.T("通话", "Calls"));
}
