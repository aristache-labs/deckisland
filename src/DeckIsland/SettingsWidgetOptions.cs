using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    // The widgets that have options get a card each: name and note on top, then a short form, each field with its label above
    // and a line of help under it. The gallery above already shows how the widget looks.
    private void WidgetOptions()
    {
        SectionTitle(Loc.T("小组件选项", "Widget options"));
        Target.Children.Add(ClockOptionsCard());
        Target.Children.Add(CountdownOptionsCard());
    }

    private FrameworkElement ClockOptionsCard()
    {
        var formats = new UniformGrid { Columns = 3, Width = 330, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (format, name) in new[]
        {
            (ClockTimeFormat.System, Loc.T("跟随系统", "System")),
            (ClockTimeFormat.Hour12, Loc.T("12 小时制", "12-hour")),
            (ClockTimeFormat.Hour24, Loc.T("24 小时制", "24-hour"))
        })
        {
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = name, IsChecked = S.TimeFormat == format, GroupName = "TimeFormat",
                FontSize = 13, Padding = new Thickness(8, 7, 8, 7) };
            AutomationProperties.SetName(choice, Loc.T("时间格式 ", "Time format ") + name);
            choice.Checked += (_, _) => Change(s => s.TimeFormat = format);
            formats.Children.Add(choice);
        }
        var timeFormat = SegmentBorder(formats);
        timeFormat.HorizontalAlignment = HorizontalAlignment.Left;

        string[] names = [Loc.T("无", "None"), .. WorldClocks.Cities.Select(item => item.City)];
        var cities = new UniformGrid { Columns = WorldClocks.Maximum, Margin = new Thickness(0, 0, -10, 0) };
        string[] captions = [Loc.T("城市 1", "City 1"), Loc.T("城市 2", "City 2"), Loc.T("城市 3", "City 3")];
        for (int i = 0; i < WorldClocks.Maximum; i++)
        {
            int index = i;
            string current = index < S.WorldClocks.Count ? S.WorldClocks[index] : "";
            var city = WidgetChoice(Loc.T($"第 {i + 1} 个城市", $"City {i + 1}"), names, Math.Max(0, Array.IndexOf(names, current)));
            city.Style = (Style)FindResource("FormSelect");
            city.MinWidth = 0;
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
            var caption = TextLine(captions[i], 12, TextSecondary);
            caption.Margin = new Thickness(0, 0, 0, 6);
            cities.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 10, 0), Children = { caption, city } });
        }

        return OptionsCard(WidgetKind.Clock, Loc.T("你的时间，外加最多三个城市。", "Your time, plus up to three cities."),
            Field(Loc.T("时间格式", "Time format"), timeFormat, Loc.T("这是整个 DeckIsland 的设置，不只是这个小组件：收起时的时钟、提醒、活动时间和所有其他页面都会使用它。“跟随系统”沿用 Windows 的设置。",
                "This applies to all of DeckIsland, not only this widget: the collapsed clock, reminders, event times and every other page use it. System follows Windows."), Loc.T("全局", "All of DeckIsland")),
            Field(Loc.T("世界时钟", "World clock"), cities, Loc.T("按顺序列在你的时间下方；选“无”可跳过。", "Listed under your own time in this order. Choose None to leave one out.")));
    }

    private FrameworkElement CountdownOptionsCard()
    {
        var title = new TextBox { Style = (Style)FindResource("FormInput"), MaxLength = 40, Text = S.CountdownTitle, Tag = Loc.T("例如：东京之旅", "e.g. Trip to Tokyo") };
        AutomationProperties.SetName(title, Loc.T("倒计时名称", "Countdown title"));
        title.TextChanged += (_, _) => Change(s => s.CountdownTitle = title.Text.Trim());

        var dateLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var pick = new Button { Style = (Style)FindResource("FormButton"),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { CalendarMark(), dateLabel } } };
        var clear = new Button { Style = (Style)FindResource("FormButton"), Width = 36, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = Loc.T("清除日期", "Clear date"),
            Content = new Path { Data = Geometry.Parse("M 0,0 L 8,8 M 8,0 L 0,8"), Stroke = UI.Brush(TextSecondary), StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round } };
        AutomationProperties.SetName(clear, Loc.T("清除倒计时日期", "Clear countdown date"));
        void ShowDate()
        {
            var date = Countdown.Parse(S.CountdownDate);
            dateLabel.Text = date?.ToString("ddd d MMM yyyy", Loc.Culture) ?? Loc.T("选择日期", "Pick a date");
            dateLabel.Foreground = UI.Brush(date is null ? TextTertiary : TextPrimary);
            AutomationProperties.SetName(pick, Loc.T("倒计时日期 ", "Countdown date ") + dateLabel.Text);
            clear.Visibility = date is null ? Visibility.Collapsed : Visibility.Visible;
        }
        // The island's own calendar, as the reminders use it: one day, today or later.
        var popup = new Popup { PlacementTarget = pick, Placement = PlacementMode.Bottom, VerticalOffset = 6, StaysOpen = false, AllowsTransparency = true };
        pick.Click += (_, _) =>
        {
            var start = Countdown.Parse(S.CountdownDate)?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
            if (start < DateTime.Today) start = DateTime.Today;
            var calendar = new ModelDatePicker(new UsageDateRange(start, start), PanelTheme.Current, singleFutureDate: true);
            calendar.Applied += range =>
            {
                popup.IsOpen = false;
                Change(s => s.CountdownDate = range.Start.ToString(Countdown.Format, CultureInfo.InvariantCulture));
                ShowDate(); pick.Focus();
            };
            calendar.Cancelled += () => { popup.IsOpen = false; pick.Focus(); };
            popup.Child = calendar;
            popup.IsOpen = true;
            calendar.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        clear.Click += (_, _) => { Change(s => s.CountdownDate = ""); ShowDate(); pick.Focus(); };
        ShowDate();
        var dateRow = new DockPanel { Children = { popup } };
        DockPanel.SetDock(clear, Dock.Right);
        dateRow.Children.Add(clear); dateRow.Children.Add(pick);

        var pair = new Grid();
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        var titleField = Field(Loc.T("名称", "Title"), title, Loc.T("显示在小组件右上角。", "Shown at the widget's top right."));
        var dateField = Field(Loc.T("日期", "Date"), dateRow, Loc.T("没有日期时，小组件会提示你来这里设置。", "Without a date the widget asks you to set one."));
        Grid.SetColumn(dateField, 2);
        pair.Children.Add(titleField); pair.Children.Add(dateField);
        return OptionsCard(WidgetKind.Countdown, Loc.T("距离你设定的日期还有几天。", "Days left until a date you set."), pair);
    }

    private Border OptionsCard(WidgetKind kind, string note, params FrameworkElement[] fields)
    {
        var name = TextLine(WidgetLayout.Name(kind), 15, TextPrimary, FontWeights.SemiBold);
        var about = TextLine(note, 12.5, TextSecondary);
        about.Margin = new Thickness(0, 3, 0, 0);
        var body = new StackPanel { Children = { name, about, new Border { Height = 1, Background = UI.Brush(_palette.RowDivider), Margin = new Thickness(0, 16, 0, 18) } } };
        foreach (var field in fields) body.Children.Add(field);
        return new Border
        {
            CornerRadius = new CornerRadius(14), BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 22, 4),
            Margin = new Thickness(0, 0, 0, 16), Child = body
        };
    }

    // A label over its control, with a line of help under it.
    // A badge after the label marks a setting that reaches beyond this widget.
    private StackPanel Field(string label, FrameworkElement control, string help, string? badge = null)
    {
        var name = TextLine(label, 13, TextPrimary, FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { name } };
        if (badge is not null)
            heading.Children.Add(new Border { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Center, Background = (Brush)FindResource("SettingsAccentTint"),
                Child = TextLine(badge, 11, _palette.Accent, FontWeights.SemiBold) });
        var hint = TextLine(help, 12, TextTertiary);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 8, 0, 0);
        return new StackPanel { Margin = new Thickness(0, 0, 0, 20), Children = { heading, control, hint } };
    }

    private FrameworkElement CalendarMark()
    {
        var path = new Path { Data = Geometry.Parse("M 5,5 H 19 A 1,1 0 0 1 20,6 V 19 A 1,1 0 0 1 19,20 H 5 A 1,1 0 0 1 4,19 V 6 A 1,1 0 0 1 5,5 Z M 4,10 H 20 M 8,3 V 7 M 16,3 V 7"),
            Stroke = UI.Brush(TextSecondary), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        return new Viewbox { Width = 15, Height = 15, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }
}
