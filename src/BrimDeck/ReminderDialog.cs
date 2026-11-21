using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

internal sealed class ReminderDialog : ControlDialog
{
    internal Reminder? Reminder { get; private set; }

    internal ReminderDialog(SettingsTheme theme) : base(theme, Loc.T("新提醒", "New reminder"))
    {
        var title = new TextBox { MaxLength = 160, Padding = new Thickness(8), Margin = new Thickness(0, 14, 0, 0) };
        AutomationProperties.SetName(title, Loc.T("提醒事项", "Reminder title"));
        Body.Children.Add(title);
        Add(Loc.T("时间", "When"), 12, bold: true);
        var when = Choice([Loc.T("10 分钟后", "In 10 minutes"), Loc.T("30 分钟后", "In 30 minutes"),
            Loc.T("1 小时后", "In 1 hour"), Loc.T("今晚 19:00", "This evening at 19:00"),
            Loc.T("明早 09:00", "Tomorrow at 09:00"), Loc.T("选择时间", "Pick a time")], 1);
        AutomationProperties.SetName(when, Loc.T("提醒时间", "Reminder time"));
        var custom = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        var date = new DatePicker { SelectedDate = DateTime.Today, DisplayDateStart = DateTime.Today };
        var time = new TextBox { Text = DateTime.Now.AddHours(1).ToString("HH:mm"), Width = 74, MaxLength = 5, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(date, Loc.T("日期", "Date"));
        AutomationProperties.SetName(time, Loc.T("时间（24 小时制）", "Time in 24-hour format"));
        custom.Children.Add(date); custom.Children.Add(time);
        Body.Children.Add(custom);
        when.SelectionChanged += (_, _) => custom.Visibility = when.SelectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        Add(Loc.T("重复", "Repeat"), 12, bold: true);
        var repeat = Choice([Loc.T("一次", "Once"), Loc.T("每天", "Every day"), Loc.T("工作日", "Weekdays"), Loc.T("每周", "Every week")], 0);
        AutomationProperties.SetName(repeat, Loc.T("重复频率", "Repeat schedule"));
        var error = Add("", 6);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = Loc.T("取消", "Cancel"), Style = (Style)FindResource("ControlDialogButton"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = Loc.T("设置提醒", "Set reminder"), Style = (Style)FindResource("ControlDialogPrimaryButton"), IsDefault = true };
        buttons.Children.Add(cancel); buttons.Children.Add(save); Body.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(24), Child = Body };
        Loaded += (_, _) => title.Focus();
        save.Click += (_, _) =>
        {
            var now = DateTimeOffset.Now;
            var zone = TimeZoneInfo.Local;
            var evening = ReminderAt(now.Date.AddHours(19));
            DateTimeOffset ReminderAt(DateTime local) => BrimDeck.Core.Reminder.AtLocal(local, zone);
            DateTimeOffset? due = when.SelectedIndex switch
            {
                0 => now.AddMinutes(10), 1 => now.AddMinutes(30), 2 => now.AddHours(1),
                3 => evening > now ? evening : ReminderAt(now.Date.AddDays(1).AddHours(19)),
                4 => ReminderAt(now.Date.AddDays(1).AddHours(9)),
                _ => date.SelectedDate is { } selected && TimeOnly.TryParseExact(time.Text.Trim(), ["H:mm", "HH:mm"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock) ? ReminderAt(selected.Date + clock.ToTimeSpan()) : null
            };
            if (due is null || due <= now)
            {
                error.Text = Loc.T("请选择将来的日期和时间。", "Choose a date and time in the future.");
                return;
            }
            var rule = (ReminderRepeat)repeat.SelectedIndex;
            var localDue = due.Value;
            if (rule == ReminderRepeat.Weekdays && localDue.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                localDue = ReminderAt(localDue.DateTime.AddDays(localDue.DayOfWeek == DayOfWeek.Saturday ? 2 : 1));
            Reminder = BrimDeck.Core.Reminder.Create(title.Text, localDue, rule);
            if (Reminder is null) { error.Text = Loc.T("请输入提醒内容。", "Enter a title for the reminder."); title.Focus(); return; }
            DialogResult = true;
        };
    }

    private ComboBox Choice(string[] entries, int selected)
    {
        var choice = new ComboBox { ItemsSource = entries, SelectedIndex = selected, Style = (Style)FindResource("SettingsCombo") };
        PopupMotion.Attach(choice);
        Body.Children.Add(choice);
        return choice;
    }
}
