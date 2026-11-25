using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// Settings > Connections: the outside services DeckIsland reads. For now the calendars: ICS links (Google, Outlook, iCloud and others
// publish one), laid out as Crest's: a row per calendar with Edit and ×, and a form to add or edit one (link, name, colour). A link
// is a secret, kept encrypted in App.Secrets; the settings file holds only the calendar's name, colour and host.
public sealed partial class SettingsWindow
{
    internal const int ConnectionsSettingsPage = 28;
    // The calendar whose form is open: Guid.Empty for a new one, null for none.
    private Guid? _calendarEditing;

    private void ConnectionsSettings()
    {
        var rows = new List<FrameworkElement>();
        foreach (var source in S.CalendarSources)
        {
            rows.Add(CalendarSourceRow(source));
            if (_calendarEditing == source.Id) rows.Add(CalendarForm(source));
        }
        if (_calendarEditing == Guid.Empty) rows.Add(CalendarForm(null));
        else if (S.CalendarSources.Count < CalendarFeed.MaxSources)
        {
            var add = Outline(ActionButton(Loc.T("＋ 添加日历", "+ Add calendar"), () => { _calendarEditing = Guid.Empty; ShowPage(_page); }));
            rows.Add(Describe(Row(S.CalendarSources.Count == 0 ? Loc.T("还没有日历", "No calendar yet") : Loc.T("添加日历", "Add calendar"), add),
                Loc.T("只读：DeckIsland 从不修改日程。", "Read only: DeckIsland never changes events.")));
        }

        // The notice before events: on or off, and 5 or 10 minutes before.
        int[] leads = [5, 10];
        var lead = new ComboBox { Width = 90, Style = (Style)FindResource("SettingsCombo"), ItemsSource = leads.Select(minutes => $"{minutes} min").ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(leads, S.CalendarNoticeMinutes)), Margin = new Thickness(0, 0, 10, 0), IsEnabled = S.CalendarNotice };
        lead.SelectionChanged += (_, _) => { if (lead.SelectedIndex >= 0) Change(s => s.CalendarNoticeMinutes = leads[lead.SelectedIndex]); };
        AutomationProperties.SetName(lead, Loc.T("提前提醒", "Notice before events"));
        rows.Add(Describe(Toggle(Loc.T("日程开始前提醒", "Notice before events"), S.CalendarNotice, value => { Change(s => s.CalendarNotice = value); lead.IsEnabled = value; }),
            Loc.T("在岛上显示，直到日程开始。", "In the island, until the event starts.")));
        rows.Add(Describe(Row(Loc.T("提前多久", "How long before"), lead), Loc.T("日程开始前这么久显示提醒。", "The notice shows this long before the event starts.")));

        int[] horizons = [.. CalendarFeed.Horizons];
        var horizon = new ComboBox { Width = 110, Style = (Style)FindResource("SettingsCombo"),
            ItemsSource = horizons.Select(minutes => minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h").ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(horizons, S.CalendarHorizon)) };
        horizon.SelectionChanged += (_, _) => { if (horizon.SelectedIndex >= 0) Change(s => s.CalendarHorizon = horizons[horizon.SelectedIndex]); };
        AutomationProperties.SetName(horizon, Loc.T("收起时显示下一个日程", "Next event in the collapsed island"));
        rows.Add(Describe(Row(Loc.T("收起时显示下一个日程", "Next event in the collapsed island"), horizon),
            Loc.T("从开始前这么久显示；在“收起外观”中打开这一项。", "From this long before; switch the part on under Collapsed view.")));

        SectionTitle(Loc.T("日历", "Calendar"), description: Loc.T("日历页、“今日”小组件和收起时的下一个日程读取这些日历。", "The Calendar page, the Today widget and the collapsed island's next event read these calendars."));
        Group([.. rows]);
        var note = TextLine(Loc.T("🔒 链接经过加密，只保存在这台电脑上。", "🔒 Links are stored encrypted. Everything stays on this PC."), 12, TextTertiary);
        note.Margin = new Thickness(4, 0, 0, 16);
        _content.Children.Add(note);
    }

    // A calendar: its colour and name, "ICS link · host" (or the error of its last fetch), Edit and ×.
    private FrameworkElement CalendarSourceRow(CalendarSource source)
    {
        string name = source.Name.Length > 0 ? source.Name : source.Host;
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = UI.Brush(source.Color), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(TextLine(name, 14, TextPrimary, FontWeights.SemiBold));
        bool failed = _app.CalendarErrors.TryGetValue(source.Id, out var error);
        var sub = TextLine(Loc.T("ICS 链接 · ", "ICS link · ") + source.Host + (failed ? " · " + error : ""), 12, failed ? "#FF6B6B" : TextTertiary);
        sub.TextWrapping = TextWrapping.Wrap; sub.Margin = new Thickness(0, 2, 0, 0);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 12, 8), Children = { label, sub } };

        var edit = Outline(ActionButton(Loc.T("编辑", "Edit"), () => { _calendarEditing = _calendarEditing == source.Id ? null : source.Id; ShowPage(_page); }));
        var remove = new Button { Style = (Style)FindResource("SettingsButton"), Content = "✕", Width = 30, Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ToolTip = Loc.T("移除", "Remove") };
        AutomationProperties.SetName(remove, Loc.T("移除 ", "Remove ") + name);
        remove.Click += (_, _) =>
        {
            if (!Confirm(Loc.T("移除日历", "Remove calendar"), Loc.T("这个日历的链接会被删除，日程不再显示。", "This calendar's link is deleted and its events no longer show."), Loc.T("移除", "Remove"))) return;
            if (_calendarEditing == source.Id) _calendarEditing = null;
            Change(s => s.CalendarSources = [.. s.CalendarSources.Where(other => other.Id != source.Id)]);
            _app.ForgetCalendar(source.Id);
            ShowPage(_page);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { edit, remove } };
        var row = new Grid { MinHeight = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(buttons, 1);
        row.Children.Add(labels); row.Children.Add(buttons);
        AutomationProperties.SetName(row, name);
        return row;
    }

    // Adding (source null) or editing a calendar: the link (left empty when editing keeps the saved one), the name and the colour.
    private FrameworkElement CalendarForm(CalendarSource? source)
    {
        var link = new TextBox { Style = (Style)FindResource("FormInput"), MaxLength = 2000, Width = 260, Tag = source is null ? "webcal://…" : Loc.T("留空则保留当前链接", "Leave empty to keep the link") };
        var name = new TextBox { Style = (Style)FindResource("FormInput"), MaxLength = 60, Width = 260, Text = source?.Name ?? "" };
        AutomationProperties.SetName(link, Loc.T("ICS 链接", "ICS link")); AutomationProperties.SetName(name, Loc.T("名称", "Name"));
        string color = source?.Color ?? CalendarFeed.NextColor(S.CalendarSources);
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        void Swatches()
        {
            swatches.Children.Clear();
            foreach (var choice in CalendarFeed.Palette)
            {
                bool on = choice.Equals(color, StringComparison.OrdinalIgnoreCase);
                var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(6, 0, 0, 0), Background = UI.Brush(choice), Cursor = Cursors.Hand,
                    BorderBrush = UI.Brush(on ? TextPrimary : "#00000000"), BorderThickness = new Thickness(on ? 2.5 : 0) };
                AutomationProperties.SetName(dot, choice);
                dot.MouseLeftButtonUp += (_, _) => { color = choice; Swatches(); };
                swatches.Children.Add(dot);
            }
        }
        Swatches();
        var status = TextLine("", 12, "#FF6B6B");
        status.TextWrapping = TextWrapping.Wrap; status.Visibility = Visibility.Collapsed; status.Margin = new Thickness(0, 0, 0, 6);

        var cancel = ActionButton(Loc.T("取消", "Cancel"), () => { _calendarEditing = null; ShowPage(_page); });
        var save = ActionButton(Loc.T("保存", "Save"), async () =>
        {
            Uri? uri = null;
            if (link.Text.Trim().Length > 0 || source is null)
            {
                uri = CalendarFeed.LinkUri(link.Text);
                if (uri is null)
                {
                    status.Text = Loc.T("这不是有效的链接。请粘贴以 https:// 或 webcal:// 开头的 ICS 地址。", "That is not a valid link. Paste an ICS address starting with https:// or webcal://.");
                    status.Visibility = Visibility.Visible;
                    return;
                }
            }
            var saved = new CalendarSource { Id = source?.Id ?? Guid.NewGuid(), Name = name.Text.Trim(), Color = color, Host = uri?.Host ?? source!.Host };
            if (uri is not null) _app.Secrets.Write(saved.Id, uri.AbsoluteUri);
            Change(s => s.CalendarSources = source is null ? [.. s.CalendarSources, saved] : [.. s.CalendarSources.Select(other => other.Id == saved.Id ? saved : other)]);
            _calendarEditing = null;
            ShowPage(_page);
            await _app.RefreshCalendar();
            if (_page == ConnectionsSettingsPage) ShowPage(_page);
        }, primary: true);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 10), Children = { cancel, save } };

        return new StackPanel
        {
            Children =
            {
                Describe(Row(Loc.T("链接", "Link"), link), Loc.T("Google、Outlook 或 iCloud 的 iCal 私密地址；加密保存在这台电脑上。",
                    "The secret iCal address from Google, Outlook or iCloud; kept encrypted on this PC.")),
                Describe(Row(Loc.T("名称", "Name"), name), Loc.T("可选", "Optional")),
                Row(Loc.T("颜色", "Colour"), swatches),
                status, buttons
            }
        };
    }
}
