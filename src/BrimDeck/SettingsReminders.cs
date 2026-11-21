using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using BrimDeck.Core;

namespace BrimDeck;

public sealed partial class SettingsWindow
{
    internal const int RemindersSettingsPage = 21;

    private void RemindersSettings()
    {
        Group(Toggle(Loc.T("启用提醒", "Enable Reminders"), S.RemindersPage, value => Change(s => s.RemindersPage = value)));
        var entry = new TextBox { MinWidth = 200, MaxLength = 200, Padding = new Thickness(6) };
        AutomationProperties.SetName(entry, Loc.T("提醒我", "Remind me"));
        var status = TextLine("", 12, TextSecondary);
        void Add()
        {
            var item = Reminder.Parse(entry.Text, DateTimeOffset.Now, TimeZoneInfo.Local);
            if (item is null)
            {
                status.Text = Loc.T("请输入内容和有效时间。", "Enter a title and a valid time.");
                return;
            }
            if (!S.RemindersPage) Change(s => s.RemindersPage = true);
            _app.AddReminder(item);
            entry.Clear();
            status.Text = Loc.T("已保存提醒。", "Reminder saved.");
        }
        var add = ActionButton(Loc.T("添加", "Add"), Add);
        entry.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Children = { entry, add } };
        Group(Row(Loc.T("提醒我", "Remind me"), quick), status);
        Target.Children.Add(TextLine(Loc.T("例如：20m 买牛奶、19:30 打电话、tomorrow 9:00 开会。未指定时间时，默认一小时后。",
            "Examples: 20m buy milk, 19:30 call Mum, tomorrow 9:00 stand-up. Without a time, the reminder is due in one hour."), 12, TextSecondary));
        var sound = new ComboBox
        {
            ItemsSource = new[] { Loc.T("关闭", "Off"), Loc.T("一次", "Once"), Loc.T("直到回应", "Until answered") },
            SelectedIndex = (int)S.ReminderSound, Style = (Style)FindResource("SettingsCombo"), MinWidth = 144
        };
        AutomationProperties.SetName(sound, Loc.T("提醒音效", "Reminder sound"));
        PopupMotion.Attach(sound);
        sound.SelectionChanged += (_, _) => { if (sound.SelectedIndex >= 0) Change(s => s.ReminderSound = (ReminderSound)sound.SelectedIndex); };
        Group(Row(Loc.T("提醒音效", "Reminder sound"), sound));
    }

    private void ReminderSizeControls()
    {
        Target.Children.Add(Dimension(Loc.T("宽度", "Width"), S.RemindersWidth, DeckPages.MinWidth, DeckPages.MaxWidth,
            value => Resize(s => s.RemindersWidth = value, DeckPage.Reminders)));
        Target.Children.Add(Dimension(Loc.T("高度", "Height"), S.RemindersHeight, DeckPages.MinHeight, DeckPages.MaxHeight,
            value => Resize(s => s.RemindersHeight = value, DeckPage.Reminders)));
    }
}
