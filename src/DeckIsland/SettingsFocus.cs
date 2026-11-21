using System.Windows;
using System.Windows.Controls;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    internal const int FocusSettingsPage = 20;

    // The timer's two lengths and whether it clears the island of other widgets. The sounds are fixed: see DeckSettings.Normalize.
    private Border FocusOptionsCard()
    {
        string minutes = Loc.T("分钟", "min");
        var enabled = HeaderSwitch(Loc.T("启用专注计时器", "Enable Focus timer"), S.FocusPage, value => Change(s => s.FocusPage = value));
        return OptionsCard(WidgetLayout.Name(WidgetKind.Focus), Loc.T("工作和休息各多长，由你定。", "Set how long you work and how long you rest."), enabled,
            Columns(
                Field(Loc.T("专注分钟数", "Focus minutes"), FormSlider(Loc.T("专注分钟数", "Focus minutes"), S.FocusMinutes, minutes, value => Change(s => s.FocusMinutes = (int)value)), ""),
                Field(Loc.T("休息分钟数", "Break minutes"), FormSlider(Loc.T("休息分钟数", "Break minutes"), S.BreakMinutes, minutes, value => Change(s => s.BreakMinutes = (int)value)), "")),
            Rule(),
            SwitchRow(Loc.T("专注时隐藏其他组件", "Hide other widgets while focusing"),
                Loc.T("计时期间，收起的面板只留下计时器，以及你开着的时钟和天气。", "While the timer runs, the collapsed island keeps only the timer and any Clock or Weather you have on."),
                S.FocusHideOtherWidgets, value => Change(s => s.FocusHideOtherWidgets = value)));
    }

    private void FocusSizeControls() => PageSizeControls(DeckPage.Focus, Loc.T("专注计时器", "Focus timer"));
}
