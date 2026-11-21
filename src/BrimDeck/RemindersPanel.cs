using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BrimDeck.Core;
using BrimDeck.Native;

namespace BrimDeck;

public partial class MainWindow
{
    private Button? _reminderAlertSnooze;
    internal Guid? ReminderAlertId { get; private set; }
    private StackPanel? _reminderRows;
    private StackPanel? _reminderEmpty;
    private (int Theme, string Language, string TimePattern) _reminderAppearance;
    internal bool CanShowReminder => Settings.RemindersPage && IsVisible && !IsSuppressed && !DisplayPower.Off && !_preview;

    private void InitializeReminders()
    {
        _app.RemindersChanged += ReminderChanged;
        Closed += (_, _) => _app.RemindersChanged -= ReminderChanged;
    }

    private void RemindersPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Reminders);

    internal bool ShowReminderAlert(Reminder item)
    {
        if (!CanShowReminder || _poweringOff) return false;

        var accent = UI.Brush(Look.Hex("accent"));
        var bell = new Path
        {
            Data = Geometry.Parse("M 4,16 C 7,13 6,12 6,9 A 6,6 0 0 1 18,9 C 18,12 17,13 20,16 Z M 10,20 A 2,2 0 0 0 14,20"),
            Stroke = accent, StrokeThickness = 1.6, Stretch = Stretch.Uniform, Width = 21, Height = 21,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var bellBackground = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(12),
            Background = UI.Brush(Look.Hex("fillSelected")), Child = bell,
            Margin = new Thickness(0, 0, 12, 0)
        };
        var title = WeatherText(item.Title, 18);
        title.FontWeight = FontWeights.SemiBold;
        title.ToolTip = item.Title;
        var time = WeatherText($"{Loc.T("提醒", "Reminder")} · {item.Due.ToLocalTime().ToString(Settings.TimePattern, Loc.Culture)}", 13);
        time.Foreground = UI.Brush(Look.Secondary);
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, time } };

        Button ActionButton(string label, bool snooze)
        {
            var button = ReminderButton(label, () => _app.AnswerReminder(item.Id, snooze));
            button.Height = 34;
            button.Padding = new Thickness(12, 0, 12, 0);
            button.FontSize = 13;
            button.Background = snooze ? UI.Brush(Look.Hex("fillSelected")) : accent;
            button.Foreground = UI.Brush(Look.Hex(snooze ? "textPrimary" : "onAccent"));
            return button;
        }
        _reminderAlertSnooze = ActionButton(Loc.T("推迟 10 分钟", "Snooze 10 min"), snooze: true);
        var done = ActionButton(Loc.T("完成", "Done"), snooze: false);
        done.Margin = new Thickness(0);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0), Children = { _reminderAlertSnooze, done }
        };
        var content = new DockPanel { Margin = new Thickness(16, 0, 16, 0), Children = { bellBackground, buttons, details } };
        DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
        AutomationProperties.SetName(content, $"{time.Text} · {item.Title}");
        AutomationProperties.SetLiveSetting(content, AutomationLiveSetting.Assertive);
        ReminderAlertContent.Child = content;
        ReminderAlertId = item.Id;
        _handover.Drop();
        _hover.Stop();
        _leave.Stop();
        WatchOutsidePress(false);
        SetExpanded(true);
        if (IsActive) _reminderAlertSnooze.Focus();
        return _expanded;
    }

    private void ClearReminderAlert()
    {
        if (ReminderAlertId is null) return;
        ReminderAlertId = null;
        _reminderAlertSnooze = null;
        ReminderAlertContent.Child = null;
        _app.StopReminderAlarm();
    }

    private void ReminderChanged()
    {
        if (ReminderAlertId is { } id && !_app.OrderedReminders.Any(item => item.Id == id && item.Due <= DateTimeOffset.Now))
            SetExpanded(false);
        if (Settings.RemindersPage && CurrentPage == DeckPage.Reminders && _reminderRows is not null)
            FillReminderRows();
    }

    private void RenderReminders()
    {
        RefreshStatus();
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility = ShelfContent.Visibility =
            DevicesContent.Visibility = WeatherContent.Visibility = FocusContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        double height = ContentCanvas.Height;
        if (!double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        RemindersContent.Margin = new Thickness(_header.Side, 0, _header.Side, MonitorBottom(height));
        var appearance = (Look.Generation, Loc.Language, Settings.TimePattern);
        if (RemindersContent.Children.Count > 0 && _reminderAppearance == appearance) return;
        _reminderAppearance = appearance;
        RemindersContent.Children.Clear();
        var body = new DockPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(actions, System.Windows.Controls.Dock.Bottom);
        actions.Children.Add(ReminderButton(Loc.T("新提醒", "New reminder"), () =>
        {
            var dialog = new ReminderDialog(Settings.Theme, Look.Current, Settings.TimePattern);
            if (dialog.ShowDialog() == true && dialog.Reminder is { } item) _app.AddReminder(item);
        }));
        actions.Children.Add(ReminderButton("+15 min", () => QuickReminder(DateTimeOffset.Now.AddMinutes(15))));
        actions.Children.Add(ReminderButton(Loc.T("+1 小时", "+1 hour"), () => QuickReminder(DateTimeOffset.Now.AddHours(1))));
        actions.Children.Add(ReminderButton(Loc.T("明天", "Tomorrow"), () =>
            QuickReminder(Reminder.AtLocal(DateTime.Today.AddDays(1).AddHours(9), TimeZoneInfo.Local))));
        body.Children.Add(actions);
        _reminderRows = new StackPanel();
        _reminderEmpty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), IsHitTestVisible = false };
        var emptyTitle = WeatherText(Loc.T("还没有待办提醒。", "Nothing to remember yet."), 15);
        emptyTitle.FontWeight = FontWeights.SemiBold;
        var emptyHint = WeatherText(Loc.T("选择“新提醒”，填写事项和时间。", "Choose New reminder to set what and when."), 13);
        emptyHint.Foreground = UI.Brush(Look.Secondary);
        emptyHint.Margin = new Thickness(0, 4, 0, 0);
        foreach (var line in new[] { emptyTitle, emptyHint })
        {
            line.TextWrapping = TextWrapping.Wrap;
            line.TextAlignment = TextAlignment.Center;
            _reminderEmpty.Children.Add(line);
        }
        body.Children.Add(new Grid
        {
            Children = { new ScrollViewer { Content = _reminderRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, _reminderEmpty }
        });
        RemindersContent.Children.Add(body);
        FillReminderRows();
    }

    private void QuickReminder(DateTimeOffset due)
    {
        if (Reminder.Create(Loc.T("提醒", "Reminder"), due, ReminderRepeat.Once) is { } item) _app.AddReminder(item);
    }

    private Button ReminderButton(string label, Action action)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("PanelOutlineButton"), Margin = new Thickness(0, 0, 5, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void FillReminderRows()
    {
        if (_reminderRows is not { } rows || _reminderEmpty is not { } empty) return;
        rows.Children.Clear();
        var now = DateTimeOffset.Now;
        foreach (var item in _app.OrderedReminders)
        {
            bool due = item.Due <= now;
            var row = new DockPanel { LastChildFill = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
            if (due) buttons.Children.Add(ReminderButton(Loc.T("推迟 10 分钟", "Snooze 10 min"), () => _app.AnswerReminder(item.Id, snooze: true)));
            var done = ReminderButton(Loc.T("完成", "Done"), () => _app.AnswerReminder(item.Id, snooze: false));
            AutomationProperties.SetName(done, Loc.T($"完成 {item.Title}", $"Mark {item.Title} done"));
            buttons.Children.Add(done);
            row.Children.Add(buttons);
            var description = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var name = WeatherText(item.Title, 14);
            name.FontWeight = due ? FontWeights.SemiBold : FontWeights.Normal;
            description.Children.Add(name);
            var time = item.Due.ToLocalTime().ToString($"ddd d MMM · {Settings.TimePattern}", Loc.Culture);
            description.Children.Add(WeatherText(due ? $"{Loc.T("到期", "Due")} · {time}" : time, 11));
            row.Children.Add(description);
            rows.Children.Add(new Border { Child = row, Padding = new Thickness(8, 4, 4, 4), Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(8), Background = UI.Brush(Look.Hex(due ? "fillSelected" : "fill")) });
        }
        empty.Visibility = rows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
