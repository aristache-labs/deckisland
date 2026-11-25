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

    // What the Screen time widget and page keep, said plainly, and a way to delete all of it.
    private Border ScreenTimeOptionsCard()
    {
        var delete = new Button { Style = (Style)FindResource("FormButton"), Content = Loc.T("删除", "Delete"), HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(16, 0, 16, 0) };
        AutomationProperties.SetName(delete, Loc.T("删除所有屏幕使用时间", "Delete all Screen Time"));
        void Show() => delete.IsEnabled = S.ScreenTimeApps.Count > 0 || S.ScreenTimeHistory.Count > 0;
        delete.Click += (_, _) =>
        {
            if (!Confirm(Loc.T("删除屏幕使用时间", "Delete Screen Time"), Loc.T("所有日期已统计的屏幕使用时间都会被删除，无法恢复。统计会从现在重新开始。",
                "Screen Time counted on every day is deleted and cannot be restored. Counting starts again from now."), Loc.T("删除", "Delete"))) return;
            _app.ResetScreenTime();
            Show();
        };
        Show();
        // What is counted, then the one action: deleting every day's data, not only today's.
        return OptionsCard(WidgetLayout.Name(WidgetKind.ScreenTime),
            Loc.T("小组件显示今天，屏幕使用时间页显示最近 30 天。小组件放在岛上或屏幕使用时间页打开显示时统计：你在键盘前时，哪个应用的窗口在最前面，按小时记录。不读取窗口标题或网页，两分钟没有键盘或鼠标操作不计入。数据只保存在这台电脑上，保留 30 天。",
                "The widget shows today, the Screen Time page the last 30 days. Counted while the widget is on the island or the Screen Time page is shown: which app's window is in front while you are at the keyboard, hour by hour. No window titles or web pages are read, and two minutes without keyboard or mouse do not count. It stays on this PC for 30 days."),
            ChoiceRow(Loc.T("删除屏幕使用时间", "Delete Screen Time"), Loc.T("删除所有日期的数据，不只是今天。", "Deletes every day's data, not only today's."), delete));
    }
}
