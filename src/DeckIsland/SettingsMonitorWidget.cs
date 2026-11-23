using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    // The unit of the network figures. It is not only the widget's: the System monitor tab and the collapsed strip use it too,
    // which the row says.
    private Border MonitorOptionsCard()
    {
        var units = new UniformGrid { Columns = 2, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (megabits, name) in new[] { (false, "MB/s"), (true, "Mbps") })
        {
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = name, IsChecked = S.NetworkMegabits == megabits, GroupName = "NetworkSpeed",
                FontSize = 13, Padding = new Thickness(8, 7, 8, 7) };
            AutomationProperties.SetName(choice, Loc.T("网速单位 ", "Network speed ") + name);
            choice.Checked += (_, _) => Change(s => s.NetworkMegabits = megabits);
            units.Children.Add(choice);
        }
        return OptionsCard(WidgetLayout.Name(WidgetKind.Monitor), Loc.T("CPU、GPU、内存和网络，一眼看清。", "CPU, GPU, memory and network at a glance."),
            ChoiceRow(Loc.T("网速", "Network speed"),
                Loc.T("下载和上传按每秒兆字节（MB/s，像文件下载那样）或每秒兆比特（Mbps，像测速和宽带套餐那样）显示。这项设置同样用于系统监控页和收起时的状态条。",
                    "Show download and upload in megabytes a second (MB/s, as file downloads do) or megabits a second (Mbps, as speed tests and internet plans do). This applies to the System monitoring widget, the System monitor tab and the collapsed strip alike."),
                SegmentBorder(units)));
    }

    // What the Screen time widget keeps, said plainly, and a way to start today's count again.
    private Border ScreenTimeOptionsCard()
    {
        var reset = new Button { Style = (Style)FindResource("FormButton"), Content = Loc.T("重置今天", "Reset today"), HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(16, 0, 16, 0) };
        AutomationProperties.SetName(reset, Loc.T("重置今天的屏幕使用时间", "Reset today's screen time"));
        void Show() => reset.IsEnabled = S.ScreenTimeApps.Count > 0;
        reset.Click += (_, _) =>
        {
            if (!Confirm(Loc.T("重置今天", "Reset today"), Loc.T("今天已统计的屏幕使用时间会被清空，并重新开始计时。", "Today's counted screen time is cleared and the count starts again."), Loc.T("重置今天", "Reset today"))) return;
            _app.ResetScreenTime();
            Show();
        };
        Show();
        return OptionsCard(WidgetLayout.Name(WidgetKind.ScreenTime), Loc.T("今天每个应用在前台多久。", "How long each app was in front today."),
            ChoiceRow(Loc.T("统计内容", "What is counted"),
                Loc.T("只有小组件放在岛上时才统计：你在键盘前时，哪个应用的窗口在最前面，以及多久。不记录你在应用里做了什么，数据只保存在这台电脑上，每天从头开始。",
                    "Counted only while the widget is on the island: which app's window is in front while you are at the keyboard, and for how long. Nothing about what you do in it is recorded. It stays on this PC and starts over each day."),
                reset));
    }
}
