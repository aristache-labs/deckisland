using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    internal const int WidgetsSettingsPage = 22;

    private void WidgetsSettings()
    {
        SectionTitle(Loc.T("小组件页", "Widget page"), description: Loc.T("展开面板是否带有一页小组件。", "Whether the open island has a page of widgets."));
        Group(Describe(Toggle(Loc.T("显示小组件", "Show Widgets"), S.WidgetsPage, value => Change(s => s.WidgetsPage = value)),
            Loc.T("在展开面板中加入小组件页。", "Add the widget page to the open island.")));
        WidgetGallery();
        WidgetOptions();
        // The tool tabs share this page instead of each having one in the navigation.
        ScratchpadSettings();
        ConverterSettings();
        TerminalSettings();
    }

    private void WidgetSizeControls() => PageSizeControls(DeckPage.Widgets, WidgetLayout.Title);

    private ComboBox WidgetChoice(string label, IEnumerable<string> names, int selected)
    {
        var choice = new ComboBox { Style = (Style)FindResource("SettingsCombo"), ItemsSource = names.ToArray(),
            SelectedIndex = selected, MinWidth = 160 };
        AutomationProperties.SetName(choice, label);
        PopupMotion.Attach(choice);
        return choice;
    }

    private void MinimalSettings(CompactStyle style)
    {
        bool capsule = style == CompactStyle.Capsule;
        string title = Loc.T("收起模式", "Collapsed mode");
        var mode = WidgetChoice(title, [Loc.T("完整", "Full"), Loc.T("精简", "Minimal")], S.Minimal(style) ? 1 : 0);
        var rows = new StackPanel { Visibility = S.Minimal(style) ? Visibility.Visible : Visibility.Collapsed };
        mode.SelectionChanged += (_, _) =>
        {
            bool minimal = mode.SelectedIndex == 1;
            Change(s => { if (capsule) s.CapsuleMinimal = minimal; else s.NotchMinimal = minimal; });
            rows.Visibility = minimal ? Visibility.Visible : Visibility.Collapsed;
            _refreshCollapsedRanges?.Invoke();
        };
        var first = capsule ? S.CapsuleMinimalFirst : S.NotchMinimalFirst;
        var second = capsule ? S.CapsuleMinimalSecond : S.NotchMinimalSecond;
        var parts = Enum.GetValues<CompactPart>().Where(part => part != CompactPart.Focus).ToArray();
        string Name(CompactPart part) => part switch
        {
            CompactPart.Clock => Loc.T("时钟", "Clock"), CompactPart.Rings => Loc.T("配额环", "Quota rings"),
            CompactPart.Music => Loc.T("正在播放", "Music now playing"), CompactPart.Transfer => Loc.T("文件中转", "File transfer"),
            CompactPart.Equalizer => Loc.T("均衡器", "Equalizer"), CompactPart.Monitor => WidgetLayout.Name(WidgetKind.Monitor),
            CompactPart.Date => Loc.T("日期", "Date"), CompactPart.Devices => WidgetLayout.Name(WidgetKind.Devices), CompactPart.Focus => WidgetLayout.Name(WidgetKind.Focus),
            _ => WidgetLayout.Name(WidgetKind.Weather)
        };
        string firstLabel = Loc.T("第一项", "First item"), secondLabel = Loc.T("第二项", "Second item");
        var one = WidgetChoice(firstLabel, parts.Select(Name), first is { } a && Array.IndexOf(parts, a) >= 0 ? Array.IndexOf(parts, a) : Array.IndexOf(parts, CompactPart.Clock));
        var two = WidgetChoice(secondLabel, new[] { WidgetLayout.Name(WidgetKind.Empty) }.Concat(parts.Select(Name)),
            second is { } b ? Array.IndexOf(parts, b) + 1 : 0);
        void Save()
        {
            if (one.SelectedIndex < 0) return;
            var primary = parts[one.SelectedIndex];
            CompactPart? secondary = two.SelectedIndex > 0 ? parts[two.SelectedIndex - 1] : null;
            if (primary == secondary) { two.SelectedIndex = 0; return; }
            Change(s =>
            {
                if (capsule) { s.CapsuleMinimalFirst = primary; s.CapsuleMinimalSecond = secondary; }
                else { s.NotchMinimalFirst = primary; s.NotchMinimalSecond = secondary; }
            });
        }
        one.SelectionChanged += (_, _) => Save();
        two.SelectionChanged += (_, _) => Save();
        rows.Children.Add(Row(firstLabel, one)); rows.Children.Add(Row(secondLabel, two));
        Group(Describe(Row(title, mode), Loc.T("完整显示下面的所有项目；精简只显示选定的两项。", "Full shows every part below; Minimal only the two you pick.")), rows);
    }
}
