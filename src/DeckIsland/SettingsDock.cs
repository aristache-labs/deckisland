using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeckIsland.Core;

namespace DeckIsland;

// DeckIsland Dock's page. The switch is an island setting (DeckSettings.DockEnabled); everything else on the page is the dock's
// own, kept in dock.json (App.DockSettings, App.UpdateDockSettings), never in DeckSettings.
public sealed partial class SettingsWindow
{
    internal const int DockSettingsPage = 29;

    private void DockSettings()
    {
        var dock = _app.DockSettings;
        SectionTitle("DeckIsland Dock", description: Loc.T("代替 Windows 任务栏的程序坞，带应用和小组件。", "A dock with apps and widgets that replaces the Windows taskbar."));
        Group(Describe(Toggle(Loc.T("启用 Dock", "Turn on the dock"), S.DockEnabled, value => Change(s => s.DockEnabled = value),
                help: Loc.T("随时按 Ctrl+Alt+Shift+Q 关闭 Dock 并恢复任务栏。", "Press Ctrl+Alt+Shift+Q at any time to turn the dock off and get the taskbar back.")),
            Loc.T("关闭后 Windows 任务栏恢复原样。", "Off, the Windows taskbar is back as it was.")));

        SectionTitle(Loc.T("布局", "Layout"), description: Loc.T("Dock 在屏幕上的位置和大小。", "Where the dock sits and how big it is."));
        Group(
            Describe(Row(Loc.T("位置", "Position"), SegmentBorder(DockChoices("DockEdge",
                [(Loc.T("底部", "Bottom"), DockEdge.Bottom), (Loc.T("顶部", "Top"), DockEdge.Top)], dock.Edge, value => ChangeDock(d => d.Edge = value)))),
                Loc.T("主屏幕的底部或顶部边缘。", "The bottom or top edge of the main screen.")),
            Describe(Row(Loc.T("大小", "Size"), SegmentBorder(DockChoices("DockSize",
                [(Loc.T("小", "Small"), Core.DockSettings.SizePresets[0]), (Loc.T("中", "Medium"), Core.DockSettings.SizePresets[1]), (Loc.T("大", "Large"), Core.DockSettings.SizePresets[2])],
                dock.Size, value => ChangeDock(d => d.Size = value)))),
                Loc.T("Dock 栏的高度。", "The height of the bar.")));

        SectionTitle(Loc.T("应用", "Apps"), description: Loc.T("Dock 上的按钮和应用的行为。拖动图标可重新排序，拖出 Dock 可取消固定，把文件拖到 Dock 上可固定。",
            "The dock's buttons and how its apps behave. Drag an icon to reorder it, drag it off the dock to unpin it, drop a file on the dock to pin it."));
        Group(
            Describe(Toggle(Loc.T("开始按钮", "Start button"), dock.ShowStart, value => ChangeDock(d => d.ShowStart = value)), Loc.T("在最左侧打开开始菜单。", "Opens Start, at the left end.")),
            Describe(Toggle(Loc.T("回收站", "Recycle Bin"), dock.ShowTrash, value => ChangeDock(d => d.ShowTrash = value)),
                Loc.T("拖到上面的文件会移到回收站。", "Files dropped on it go to the Recycle Bin.")),
            Describe(Toggle(Loc.T("显示桌面按钮", "Show desktop button"), dock.ShowDesktopButton, value => ChangeDock(d => d.ShowDesktopButton = value)),
                Loc.T("最小化所有窗口，再点一次恢复。", "Minimizes every window; click again to bring them back.")),
            Describe(Toggle(Loc.T("窗口预览", "Window previews"), dock.WindowPreviews, value => ChangeDock(d => d.WindowPreviews = value)),
                Loc.T("鼠标停在应用上时显示其窗口的实时画面。", "Live pictures of an app's windows while the pointer rests on it.")),
            Describe(Toggle(Loc.T("Win+1 到 Win+0", "Win+1 to Win+0"), dock.AppShortcuts, value => ChangeDock(d => d.AppShortcuts = value)),
                Loc.T("打开 Dock 上的前十个应用，与任务栏相同。", "Open the dock's first ten apps, as on the taskbar.")));

        SectionTitle(Loc.T("Windows 任务栏", "Windows taskbar"), description: Loc.T("Dock 运行时如何处理 Windows 任务栏。", "What happens to the Windows taskbar while the dock runs."));
        var taskbar = Row(Loc.T("Dock 运行时", "While the dock runs"), SegmentBorder(DockChoices("DockTaskbar",
            [(Loc.T("自动隐藏", "Auto-hide"), DockTaskbarMode.AutoHide), (Loc.T("完全隐藏", "Hide"), DockTaskbarMode.Hide), (Loc.T("不隐藏", "Leave"), DockTaskbarMode.Leave)],
            dock.TaskbarMode, value => { ChangeDock(d => d.TaskbarMode = value); ShowPage(DockSettingsPage); })),
            // Built again so the line under it says whether Windows kept the taskbar hidden.
            help: () => Loc.T("自动隐藏：鼠标移到屏幕边缘或打开开始菜单时任务栏仍会出现。\n完全隐藏：任务栏不再出现；如果 Windows 不允许，则改用自动隐藏。\n不隐藏：任务栏保持原样。",
                "Auto-hide: the taskbar still slides in at the screen edge and for Start.\nHide: the taskbar stays away; where Windows will not allow it, auto-hide is used instead.\nLeave: the taskbar stays as it is."));
        Group(Native.DockTaskbar.CompleteFailed
            ? Describe(taskbar, Loc.T("Windows 不允许完全隐藏任务栏，已改用自动隐藏。", "Windows would not keep the taskbar hidden, so auto-hide is used instead."))
            : Describe(taskbar, Loc.T("关闭 Dock 或退出 DeckIsland 时恢复。", "Restored when the dock is turned off or DeckIsland exits.")));
    }

    private void ChangeDock(Action<DockSettings> edit)
    {
        var copy = _app.DockSettings.Copy(); edit(copy); _app.UpdateDockSettings(copy);
    }

    private UniformGrid DockChoices<T>(string group, (string Name, T Value)[] choices, T current, Action<T> chosen)
    {
        var grid = new UniformGrid { Columns = choices.Length, Width = 80 * choices.Length };
        foreach (var (name, value) in choices)
        {
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = name, GroupName = group, IsChecked = EqualityComparer<T>.Default.Equals(value, current) };
            AutomationProperties.SetName(choice, name);
            choice.Checked += (_, _) => chosen(value);
            grid.Children.Add(choice);
        }
        return grid;
    }
}
