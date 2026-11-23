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
        _cardBody = null;
        // A heading over the option cards, as the cards above head themselves (Card).
        var heading = TextLine(Loc.T("小组件选项", "Widget options"), 15, TextPrimary, FontWeights.SemiBold);
        var about = TextLine(Loc.T("有选项的小组件，各占一张卡片。", "The widgets that have options, a card each."), 12.5, TextSecondary);
        about.Margin = new Thickness(0, 3, 0, 0);
        Target.Children.Add(new StackPanel { Margin = new Thickness(2, 8, 0, 14), Children = { heading, about } });
        Target.Children.Add(ClockOptionsCard());
        Target.Children.Add(CountdownOptionsCard());
        // These three were pages of their own; a link to one of them opens its card (ShowPage).
        Target.Children.Add(_optionCards[WeatherSettingsPage] = WeatherOptionsCard());
        Target.Children.Add(_optionCards[FocusSettingsPage] = FocusOptionsCard());
        Target.Children.Add(_optionCards[RemindersSettingsPage] = RemindersOptionsCard());
        Target.Children.Add(MonitorOptionsCard());
        Target.Children.Add(ScreenTimeOptionsCard());
    }

    // The card a former page opens on: ShowPage brings it into view when that page is asked for.
    private readonly Dictionary<int, FrameworkElement> _optionCards = [];

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

        return OptionsCard(WidgetLayout.Name(WidgetKind.Clock), Loc.T("你的时间，外加最多三个城市。", "Your time, plus up to three cities."),
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

        return OptionsCard(WidgetLayout.Name(WidgetKind.Countdown), Loc.T("距离你设定的日期还有几天。", "Days left until a date you set."),
            Columns(
                Field(Loc.T("名称", "Title"), title, Loc.T("显示在小组件右上角。", "Shown at the widget's top right.")),
                Field(Loc.T("日期", "Date"), dateRow, Loc.T("没有日期时，小组件会提示你来这里设置。", "Without a date the widget asks you to set one."))));
    }

    private Border OptionsCard(string title, string note, params FrameworkElement[] fields)
    {
        var name = TextLine(title, 15, TextPrimary, FontWeights.SemiBold);
        var about = TextLine(note, 12.5, TextSecondary);
        about.Margin = new Thickness(0, 3, 0, 0);
        var head = new StackPanel { Margin = new Thickness(20, 16, 20, 14), Children = { name, about } };
        // The same frame as the page's other cards (Card): a header, a line across, then the form.
        var form = new StackPanel { Margin = new Thickness(20, 18, 20, 4) };
        foreach (var field in fields) form.Children.Add(field);
        return new Border
        {
            Background = UI.Brush(_palette.Card), CornerRadius = new CornerRadius(12), BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 20), Child = new StackPanel { Children = { head, new Border { Height = 1, Background = UI.Brush(Divider) }, form } }
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
        var field = new StackPanel { Margin = new Thickness(0, 0, 0, 20), Children = { heading, control } };
        if (help.Length == 0) return field;
        var hint = TextLine(help, 12, TextTertiary);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 8, 0, 0);
        field.Children.Add(hint);
        return field;
    }

    // A thin line between rows that are not fields, such as a switch and a choice.
    private FrameworkElement Rule() => new Border { Height = 1, Background = UI.Brush(_palette.RowDivider), Margin = new Thickness(0, 0, 0, 16) };

    // A choice with its name and explanation at the left and its control at the right, like a switch row.
    private FrameworkElement ChoiceRow(string title, string help, FrameworkElement control)
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        var name = TextLine(title, 13, TextPrimary, FontWeights.SemiBold);
        var hint = TextLine(help, 12, TextTertiary);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 4, 0, 0);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        row.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, hint } });
        return row;
    }

    // A yes-or-no option: its name and explanation at the left, the switch at the right.
    private FrameworkElement SwitchRow(string title, string help, bool value, Action<bool> changed)
    {
        var toggle = new CheckBox
        {
            Style = (Style)FindResource("SettingsToggle"), IsChecked = value, Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), ToolTip = title
        };
        AutomationProperties.SetName(toggle, title);
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        var name = TextLine(title, 13, TextPrimary, FontWeights.SemiBold);
        var hint = TextLine(help, 12, TextTertiary);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 4, 0, 0);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        row.Children.Add(new StackPanel { Children = { name, hint } });
        return row;
    }

    // Fields side by side in equal columns, 16 apart.
    private static Grid Columns(params FrameworkElement[] fields)
    {
        var grid = new Grid();
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(fields[i], i * 2);
            grid.Children.Add(fields[i]);
        }
        return grid;
    }

    // A slider with its value box, filling the field's width; minutes for the Focus timer.
    private FrameworkElement FormSlider(string title, double value, string unit, Action<double> changed)
    {
        var (slider, number) = NumericControls(title, value, FocusTimer.MinimumMinutes, FocusTimer.MaximumMinutes, 1, unit, changed);
        slider.Margin = new Thickness(2, 0, 14, 0);
        number.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel { Height = 36 };
        DockPanel.SetDock(number, Dock.Right);
        row.Children.Add(number); row.Children.Add(slider);
        return row;
    }

    private FrameworkElement CalendarMark()
    {
        var path = new Path { Data = Geometry.Parse("M 5,5 H 19 A 1,1 0 0 1 20,6 V 19 A 1,1 0 0 1 19,20 H 5 A 1,1 0 0 1 4,19 V 6 A 1,1 0 0 1 5,5 Z M 4,10 H 20 M 8,3 V 7 M 16,3 V 7"),
            Stroke = UI.Brush(TextSecondary), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        return new Viewbox { Width = 15, Height = 15, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }
}
