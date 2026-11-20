using System.Windows;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

public sealed partial class SettingsWindow
{
    internal const int FocusSettingsPage = 20;

    private void FocusSettings()
    {
        string minutes = Loc.T("分钟", "min");
        Group(
            Toggle(Loc.T("启用专注计时器", "Enable Focus timer"), S.FocusPage, value => Change(s => s.FocusPage = value)),
            Toggle(Loc.T("专注期间隐藏其他组件", "Hide other widgets during Focus"), S.FocusHideOtherWidgets,
                value => Change(s => s.FocusHideOtherWidgets = value),
                help: Loc.T("专注和休息期间，收起的面板仅显示计时器和已启用的时钟、天气。",
                    "During Focus and Break, the collapsed island shows the timer and any enabled Clock or Weather widgets.")),
            SliderRow(Loc.T("专注时间", "Focus duration"), S.FocusMinutes, FocusTimer.MinimumMinutes, FocusTimer.MaximumMinutes, 1, minutes,
                value => Change(s => s.FocusMinutes = (int)value)),
            SliderRow(Loc.T("休息时间", "Break duration"), S.BreakMinutes, FocusTimer.MinimumMinutes, FocusTimer.MaximumMinutes, 1, minutes,
                value => Change(s => s.BreakMinutes = (int)value)));
    }

    private void FocusSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dimensions.ColumnDefinitions.Add(new());
        dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("宽度", "Width"), S.FocusWidth, DeckPages.MinWidth, DeckPages.MaxWidth,
            value => Resize(s => s.FocusWidth = value, DeckPage.Focus));
        var height = Dimension(Loc.T("高度", "Height"), S.FocusHeight, DeckPages.MinHeight, DeckPages.MaxHeight,
            value => Resize(s => s.FocusHeight = value, DeckPage.Focus));
        width.Margin = new Thickness(0, 0, 20, 0);
        height.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(height, 1);
        dimensions.Children.Add(width);
        dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
