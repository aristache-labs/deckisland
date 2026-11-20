using System.Windows;
using System.Windows.Automation;
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
                value => Change(s => s.BreakMinutes = (int)value)),
            FocusSoundRow(Loc.T("专注结束音效", "Focus completion sound"), S.FocusEndSound, sound => Change(s => s.FocusEndSound = sound)),
            FocusSoundRow(Loc.T("休息结束音效", "Break completion sound"), S.BreakEndSound, sound => Change(s => s.BreakEndSound = sound)));
    }

    private FrameworkElement FocusSoundRow(string title, FocusSound selected, Action<FocusSound> change)
    {
        var sounds = Enum.GetValues<FocusSound>();
        var choice = new ComboBox
        {
            Style = (Style)FindResource("SettingsCombo"), MinWidth = 140,
            ItemsSource = sounds.Select(sound => sound switch
            {
                FocusSound.None => Loc.T("无声", "None"),
                FocusSound.Asterisk => Loc.T("信息", "Information"),
                FocusSound.Exclamation => Loc.T("提示", "Exclamation"),
                FocusSound.Beep => Loc.T("默认提示音", "Default beep"),
                FocusSound.Hand => Loc.T("停止", "Critical stop"),
                _ => Loc.T("询问", "Question")
            }).ToArray(),
            SelectedIndex = Array.IndexOf(sounds, selected)
        };
        PopupMotion.Attach(choice);
        AutomationProperties.SetName(choice, title);
        var test = ActionButton(Loc.T("试听", "Test"), () => App.PlayFocusSound(sounds[choice.SelectedIndex]));
        test.Margin = new Thickness(8, 0, 0, 0);
        test.IsEnabled = selected != FocusSound.None;
        AutomationProperties.SetName(test, Loc.T($"试听{title}", $"Test {title}"));
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedIndex < 0) return;
            var sound = sounds[choice.SelectedIndex];
            test.IsEnabled = sound != FocusSound.None;
            change(sound);
        };
        return Row(title, new StackPanel { Orientation = Orientation.Horizontal, Children = { choice, test } });
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
