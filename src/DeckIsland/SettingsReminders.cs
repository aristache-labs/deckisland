using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    internal const int RemindersSettingsPage = 21;

    private Border RemindersOptionsCard()
    {
        var entry = new TextBox { Style = (Style)FindResource("FormInput"), MaxLength = 200, Tag = Loc.T("例如：20m 买牛奶", "e.g. 20m buy milk") };
        AutomationProperties.SetName(entry, Loc.T("新提醒", "New reminder"));
        var status = TextLine("", 12, TextSecondary);
        status.Margin = new Thickness(0, 8, 0, 0);
        status.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        void Say(string text, bool problem)
        {
            status.Text = text;
            status.Foreground = UI.Brush(problem ? _palette.Negative : _palette.Positive);
            status.Visibility = Visibility.Visible;
        }
        void Add()
        {
            var item = Reminder.Parse(entry.Text, DateTimeOffset.Now, TimeZoneInfo.Local);
            if (item is null) { Say(Loc.T("请输入内容和有效时间。", "Enter a title and a valid time."), true); return; }
            _app.AddReminder(item);
            entry.Clear();
            Say(Loc.T("已保存提醒。", "Reminder saved."), false);
        }
        var add = new Button { Style = (Style)FindResource("FormButton"), Content = Loc.T("添加", "Add"), Width = 84, Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center };
        add.Click += (_, _) => Add();
        entry.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        var quick = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        quick.Children.Add(add); quick.Children.Add(entry);
        var quickField = Field(Loc.T("新提醒", "New reminder"), new StackPanel { Children = { quick, status } },
            Loc.T("先写时间，再写事情：20m、19:30 或 tomorrow 9:00。不写时间，则一小时后响。",
                "Start with a time (20m, 19:30 or tomorrow 9:00), then say what it is for. Leave the time out and it rings in an hour."));

        var sounds = new UniformGrid { Columns = 3, Width = 270, HorizontalAlignment = HorizontalAlignment.Left };
        string[] names = [Loc.T("静音", "Silent"), Loc.T("一次", "Once"), Loc.T("重复", "Repeat")];
        for (int i = 0; i < names.Length; i++)
        {
            var sound = (ReminderSound)i;
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = names[i], IsChecked = S.ReminderSound == sound, GroupName = "ReminderSound",
                FontSize = 13, Padding = new Thickness(8, 7, 8, 7) };
            AutomationProperties.SetName(choice, Loc.T("提醒音效 ", "Alert sound ") + names[i]);
            choice.Checked += (_, _) => Change(s => s.ReminderSound = sound);
            sounds.Children.Add(choice);
        }
        return OptionsCard(Loc.T("提醒", "Reminders"), Loc.T("提醒到点时，会在岛上响起。", "A reminder rings on the island when it comes due."),
            quickField,
            Rule(),
            ChoiceRow(Loc.T("提示音", "Alert sound"), Loc.T("“重复”会一直响，直到你回应或推迟。", "Repeat keeps chiming until you answer or snooze."), SegmentBorder(sounds)));
    }

    private void ReminderSizeControls() => PageSizeControls(DeckPage.Reminders, Loc.T("提醒", "Reminders"));
}
