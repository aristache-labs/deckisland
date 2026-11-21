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
        Group(Toggle(Loc.T("显示小组件", "Show Widgets"), S.WidgetsPage, value => Change(s => s.WidgetsPage = value)));
        var slots = S.WidgetSlots;
        var choices = new ComboBox[slots.Length];
        bool updating = false;
        for (int index = 0; index < slots.Length; index++)
        {
            int slot = index;
            string label = Loc.T($"位置 {index + 1}", $"Position {index + 1}");
            var choice = choices[index] = WidgetChoice(label, Enum.GetValues<WidgetKind>().Select(WidgetLayout.Name), (int)slots[index]);
            choice.SelectionChanged += (_, _) =>
            {
                if (updating || choice.SelectedIndex < 0) return;
                updating = true;
                var selected = (WidgetKind)choice.SelectedIndex;
                int previous = Array.IndexOf(slots, selected);
                if (selected != WidgetKind.Empty && previous >= 0 && previous != slot)
                {
                    slots[previous] = slots[slot];
                    choices[previous].SelectedIndex = (int)slots[previous];
                }
                slots[slot] = selected;
                Change(s => s.WidgetSlots = slots);
                updating = false;
            };
            Group(Row(label, choice));
        }
        ClockWidgetSettings();
    }

    // Time format applies to every clock in DeckIsland; the cities are the Clock widget's.
    private void ClockWidgetSettings()
    {
        SectionTitle(WidgetLayout.Name(WidgetKind.Clock));
        var timeFormats = new UniformGrid { Columns = 3, Width = 292 };
        foreach (var (format, name) in new[]
        {
            (ClockTimeFormat.System, Loc.T("跟随系统", "System")),
            (ClockTimeFormat.Hour12, Loc.T("12 小时制", "12-hour")),
            (ClockTimeFormat.Hour24, Loc.T("24 小时制", "24-hour"))
        })
        {
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = name,
                IsChecked = S.TimeFormat == format, GroupName = "TimeFormat" };
            AutomationProperties.SetName(choice, name);
            choice.Checked += (_, _) => Change(s => s.TimeFormat = format);
            timeFormats.Children.Add(choice);
        }
        var rows = new List<FrameworkElement> { Row(Loc.T("时间格式", "Time format"), SegmentBorder(timeFormats)) };
        string none = Loc.T("无", "None");
        string[] names = [none, .. WorldClocks.Cities.Select(item => item.City)];
        string[] labels = [Loc.T("第一个城市", "First city"), Loc.T("第二个城市", "Second city"), Loc.T("第三个城市", "Third city")];
        for (int i = 0; i < WorldClocks.Maximum; i++)
        {
            int index = i;
            string current = index < S.WorldClocks.Count ? S.WorldClocks[index] : "";
            var city = WidgetChoice(labels[i], names, Math.Max(0, Array.IndexOf(names, current)));
            city.SelectionChanged += (_, _) =>
            {
                if (city.SelectedIndex < 0) return;
                string picked = city.SelectedIndex == 0 ? "" : names[city.SelectedIndex];
                Change(s =>
                {
                    var list = new List<string>(s.WorldClocks);
                    while (list.Count < WorldClocks.Maximum) list.Add("");
                    list[index] = picked;
                    s.WorldClocks = list;
                });
            };
            rows.Add(Row(labels[i], city));
        }
        Group([.. rows]);
    }

    private void WidgetSizeControls()
    {
        Target.Children.Add(Dimension(Loc.T("宽度", "Width"), S.WidgetsWidth, DeckPages.MinWidth, DeckPages.MaxWidth,
            value => Resize(s => s.WidgetsWidth = value, DeckPage.Widgets)));
        Target.Children.Add(Dimension(Loc.T("高度", "Height"), S.WidgetsHeight, 220, DeckPages.MaxHeight,
            value => Resize(s => s.WidgetsHeight = value, DeckPage.Widgets)));
    }

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
        Group(Row(title, mode), rows);
    }
}
