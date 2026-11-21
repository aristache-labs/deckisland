using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BrimDeck.Core;

namespace BrimDeck;

internal sealed class ReminderDialog : ControlDialog
{
    internal Reminder? Reminder { get; private set; }

    internal ReminderDialog(SettingsTheme theme) : base(theme, Loc.T("新提醒", "New reminder"))
    {
        Width = 460;
        Body.Children.Clear();
        var heading = new StackPanel();
        heading.Children.Add(Text(Title, 19, Palette.Primary, true, 24));
        heading.Children.Add(Text(Loc.T("时间到了，岛屿会提醒你。", "The island tells you when it's time."), 12, Palette.Secondary));
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 4,16 C 7,13 6,12 6,9 A 6,6 0 0 1 18,9 C 18,12 17,13 20,16 Z M 10,20 A 2,2 0 0 0 14,20"),
            Stroke = UI.Brush(Palette.Surface), StrokeThickness = 1.3, Stretch = Stretch.Uniform,
            Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center
        };
        var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = UI.Brush(Palette.Primary),
            Child = icon, Margin = new Thickness(0, 0, 13, 0) };
        Body.Children.Add(new DockPanel { Children = { badge, heading } });

        var title = Field("", 42);
        title.MaxLength = 160;
        var hint = Text(Loc.T("提醒我……", "Remind me to…"), 14, Palette.Secondary);
        hint.Margin = new Thickness(13, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        AutomationProperties.SetName(title, Loc.T("提醒事项", "Reminder title"));
        var entry = new Grid { Margin = new Thickness(0, 16, 0, 0), Children = { title, hint } };
        Body.Children.Add(entry);
        title.TextChanged += (_, _) => hint.Visibility = title.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        Add(Loc.T("时间", "When"), 14, 12, bold: true);
        int when = 1;
        var rule = ReminderRepeat.Once;
        var summary = Text("", 13, Palette.Primary, true);
        var relative = Text("", 12, Palette.Secondary);
        var save = new Button { Content = Loc.T("设置提醒", "Set reminder"), Style = (Style)FindResource("ControlDialogPrimaryButton"), MinWidth = 114, IsDefault = true };
        var initial = DateTime.Now.AddMinutes(30);
        var selectedDate = initial.Date;
        var time = Field(initial.ToString("HH:mm", CultureInfo.InvariantCulture), 30);
        time.Width = 80;
        time.MaxLength = 5;
        AutomationProperties.SetName(time, Loc.T("时间（24 小时制）", "Time in 24-hour format"));
        var date = new Button { Style = (Style)FindResource("ControlDialogButton"), Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        void UpdateDate() => date.Content = selectedDate.ToString("ddd d MMM", Loc.Culture);
        UpdateDate();
        var custom = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 5, 0, 0) };
        var dateRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { date, time } };
        custom.Children.Add(dateRow);
        var calendarPalette = PanelThemes.Resolve(PanelThemes.BuiltIn(Palette.Dark ? PanelThemes.Dark : PanelThemes.Light)!);
        var calendar = new ModelDatePicker(new UsageDateRange(selectedDate, selectedDate), calendarPalette, singleFutureDate: true)
        {
            Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0)
        };
        custom.Children.Add(calendar);
        date.Click += (_, _) =>
        {
            bool opening = calendar.Visibility != Visibility.Visible;
            calendar.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
            if (opening) calendar.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        calendar.Applied += range =>
        {
            selectedDate = range.Start;
            UpdateDate();
            calendar.Visibility = Visibility.Collapsed;
            date.Focus();
            UpdatePreview();
        };
        Choice(Loc.T("提醒时间", "Reminder time"), [Loc.T("10 分钟后", "In 10 min"), Loc.T("30 分钟后", "In 30 min"),
            Loc.T("1 小时后", "In 1 hour"), Loc.T("今晚", "This evening"), Loc.T("明早", "Tomorrow morning"),
            Loc.T("选择时间", "Pick a time")], when, index =>
        {
            when = index;
            custom.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
            calendar.Visibility = Visibility.Collapsed;
            UpdatePreview();
        });
        Body.Children.Add(custom);
        Add(Loc.T("重复", "Repeat"), 14, 12, bold: true);
        Choice(Loc.T("重复频率", "Repeat schedule"), [Loc.T("一次", "Once"), Loc.T("每天", "Every day"),
            Loc.T("工作日", "Weekdays"), Loc.T("每周", "Every week")], 0, index =>
        {
            rule = (ReminderRepeat)index;
            UpdatePreview();
        });
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        Body.Children.Add(new Border { Background = UI.Brush(Palette.Inset), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 16, 0, 0),
            Child = new StackPanel { Children = { summary, relative } } });
        var error = Add("", 6, color: Palette.Negative);
        error.Visibility = Visibility.Collapsed;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var cancel = new Button { Content = Loc.T("取消", "Cancel"), Style = (Style)FindResource("ControlDialogButton"), MinWidth = 72, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        buttons.Children.Add(cancel); buttons.Children.Add(save); Body.Children.Add(buttons);
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 24);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false,
            Content = new Border { Padding = new Thickness(24), Child = Body } };
        Loaded += (_, _) => title.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || calendar.Visibility != Visibility.Visible) return;
            calendar.Visibility = Visibility.Collapsed;
            date.Focus();
            e.Handled = true;
        };
        time.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        DateTimeOffset? GetDue(DateTimeOffset now)
        {
            DateTimeOffset At(DateTime local) => BrimDeck.Core.Reminder.AtLocal(local, TimeZoneInfo.Local);
            var evening = At(now.Date.AddHours(19));
            DateTimeOffset? due = when switch
            {
                0 => now.AddMinutes(10), 1 => now.AddMinutes(30), 2 => now.AddHours(1),
                3 => evening > now ? evening : At(now.Date.AddDays(1).AddHours(19)),
                4 => At(now.Date.AddDays(1).AddHours(9)),
                _ => TimeOnly.TryParseExact(time.Text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var clock) ? At(selectedDate + clock.ToTimeSpan()) : null
            };
            if (due is not { } value || value <= now) return null;
            if (rule == ReminderRepeat.Weekdays && value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return At(value.DateTime.AddDays(value.DayOfWeek == DayOfWeek.Saturday ? 2 : 1));
            return value;
        }

        void UpdatePreview()
        {
            var now = DateTimeOffset.Now;
            var due = GetDue(now);
            save.IsEnabled = due.HasValue;
            if (due is { } value)
            {
                string day = value.Date == now.Date ? Loc.T("今天", "Today") :
                    value.Date == now.Date.AddDays(1) ? Loc.T("明天", "Tomorrow") : value.ToString("ddd d MMM", Loc.Culture);
                string clock = value.ToString("t", Loc.Culture);
                summary.Text = Loc.T($"{day} {clock}", $"{day} at {clock}");
                var remaining = value - now;
                int minutes = (int)Math.Ceiling(remaining.TotalMinutes);
                int hours = (int)Math.Ceiling(remaining.TotalHours);
                int days = (int)Math.Ceiling(remaining.TotalDays);
                relative.Text = remaining.TotalHours <= 1 ? Loc.T($"{minutes} 分钟后", $"In {minutes} min")
                    : remaining.TotalDays <= 1 ? Loc.T($"{hours} 小时后", $"In {hours} {(hours == 1 ? "hour" : "hours")}")
                    : Loc.T($"{days} 天后", $"In {days} days");
            }
            else
            {
                summary.Text = Loc.T("请选择将来的日期和时间。", "Choose a date and time in the future.");
                relative.Text = Loc.T("请输入 24 小时制时间，例如 14:30。", "Enter a 24-hour time, for example 14:30.");
            }
            FrameworkElementAutomationPeer.FromElement(summary)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        save.Click += (_, _) =>
        {
            if (GetDue(DateTimeOffset.Now) is not { } due) { UpdatePreview(); return; }
            Reminder = BrimDeck.Core.Reminder.Create(title.Text, due, rule);
            if (Reminder is null)
            {
                error.Text = Loc.T("请输入提醒内容。", "Enter a title for the reminder.");
                error.Visibility = Visibility.Visible;
                title.Focus();
                return;
            }
            DialogResult = true;
        };
    }

    private TextBox Field(string value, double height) => new()
    {
        Text = value, Height = height, FontSize = 14, Padding = new Thickness(11, 0, 11, 0),
        VerticalContentAlignment = VerticalAlignment.Center, Background = UI.Brush(Palette.Input),
        Foreground = UI.Brush(Palette.Primary), BorderBrush = UI.Brush(Palette.InputBorder)
    };

    private void Choice(string group, string[] labels, int selected, Action<int> changed)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(row, group);
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            var chip = new RadioButton { GroupName = group, Content = labels[i], IsChecked = i == selected,
                Style = (Style)FindResource("ReminderChoice") };
            chip.Checked += (_, _) => changed(index);
            row.Children.Add(chip);
        }
        Body.Children.Add(row);
    }
}
