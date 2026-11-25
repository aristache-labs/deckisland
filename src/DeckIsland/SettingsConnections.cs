using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// Settings > Connections: the outside services DeckIsland reads. For now the calendars: ICS links (Google, Outlook, iCloud and others
// publish one). A link is a secret, kept encrypted in App.Secrets; the settings file holds only the calendar's name, colour and host.
public sealed partial class SettingsWindow
{
    internal const int ConnectionsSettingsPage = 28;

    private void ConnectionsSettings()
    {
        var rows = new List<FrameworkElement>();
        foreach (var source in S.CalendarSources) rows.Add(CalendarSourceRow(source));
        if (S.CalendarSources.Count == 0)
            rows.Add(Describe(Row(Loc.T("还没有日历", "No calendar yet"), new Border()), Loc.T("只读：DeckIsland 从不修改日程。", "Read only: DeckIsland never changes events.")));
        if (S.CalendarSources.Count < CalendarFeed.MaxSources) rows.Add(CalendarAddRow());

        int[] horizons = [.. CalendarFeed.Horizons];
        var horizon = new ComboBox { Width = 110, Style = (Style)FindResource("SettingsCombo"),
            ItemsSource = horizons.Select(minutes => minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h").ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(horizons, S.CalendarHorizon)) };
        horizon.SelectionChanged += (_, _) => { if (horizon.SelectedIndex >= 0) Change(s => s.CalendarHorizon = horizons[horizon.SelectedIndex]); };
        AutomationProperties.SetName(horizon, Loc.T("提前显示下一个日程", "Next event in the collapsed island"));
        rows.Add(Describe(Row(Loc.T("收起时显示下一个日程", "Next event in the collapsed island"), horizon),
            Loc.T("从开始前这么久显示；在“收起外观”中打开这一项。", "From this long before; switch the part on under Collapsed view.")));

        SectionTitle(Loc.T("日历", "Calendar"), description: Loc.T("日历页、“今日”小组件和收起时的下一个日程读取这些日历。", "The Calendar page, the Today widget and the collapsed island's next event read these calendars."));
        Group([.. rows]);
        var note = TextLine(Loc.T("链接经过加密，只保存在这台电脑上。", "Links are stored encrypted, only on this PC."), 12, TextTertiary);
        note.Margin = new Thickness(4, 0, 0, 16);
        _content.Children.Add(note);
    }

    // A calendar: its colour, name, host and the error of its last fetch, and Remove.
    private FrameworkElement CalendarSourceRow(CalendarSource source)
    {
        var remove = Outline(ActionButton(Loc.T("移除", "Remove"), () =>
        {
            if (!Confirm(Loc.T("移除日历", "Remove calendar"), Loc.T("这个日历的链接会被删除，日程不再显示。", "This calendar's link is deleted and its events no longer show."), Loc.T("移除", "Remove"))) return;
            Change(s => s.CalendarSources = [.. s.CalendarSources.Where(other => other.Id != source.Id)]);
            _app.ForgetCalendar(source.Id);
            ShowPage(_page);
        }));
        string name = source.Name.Length > 0 ? source.Name : source.Host;
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = UI.Brush(source.Color), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(TextLine(name, 14, TextPrimary, FontWeights.SemiBold));
        string detail = _app.CalendarErrors.TryGetValue(source.Id, out var error) ? source.Host + " · " + error : source.Host;
        var sub = TextLine(detail, 12, _app.CalendarErrors.ContainsKey(source.Id) ? "#FF6B6B" : TextTertiary);
        sub.Margin = new Thickness(20, 2, 0, 0);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 12, 10), Children = { label, sub } };
        var row = new Grid { MinHeight = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        remove.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(remove, 1);
        row.Children.Add(labels); row.Children.Add(remove);
        AutomationProperties.SetName(row, name);
        return row;
    }

    // A name and a link, then Add: the link is checked, kept as a secret, and every calendar read again.
    private FrameworkElement CalendarAddRow()
    {
        var name = new TextBox { Style = (Style)FindResource("FormInput"), MaxLength = 60, Width = 150, Tag = Loc.T("名称（可选）", "Name (optional)"), Margin = new Thickness(0, 0, 8, 0) };
        var link = new TextBox { Style = (Style)FindResource("FormInput"), MaxLength = 2000, MinWidth = 240, Tag = Loc.T("粘贴 ICS 链接", "Paste an ICS link"), Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(name, Loc.T("日历名称", "Calendar name")); AutomationProperties.SetName(link, Loc.T("ICS 链接", "ICS link"));
        var status = TextLine("", 12, TextTertiary);
        status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 6, 0, 0); status.Visibility = Visibility.Collapsed;
        var add = ActionButton(Loc.T("添加日历", "Add calendar"), async () =>
        {
            if (CalendarFeed.LinkUri(link.Text) is not { } uri)
            {
                status.Text = Loc.T("这不是有效的链接。请粘贴以 https:// 或 webcal:// 开头的 ICS 地址。", "That is not a valid link. Paste an ICS address starting with https:// or webcal://.");
                status.Foreground = UI.Brush("#FF6B6B"); status.Visibility = Visibility.Visible;
                return;
            }
            var source = new CalendarSource { Name = name.Text.Trim(), Host = uri.Host, Color = CalendarFeed.NextColor(S.CalendarSources) };
            _app.Secrets.Write(source.Id, uri.AbsoluteUri);
            Change(s => s.CalendarSources = [.. s.CalendarSources, source]);
            status.Text = Loc.T("正在读取…", "Reading…"); status.Foreground = UI.Brush(TextTertiary); status.Visibility = Visibility.Visible;
            await _app.RefreshCalendar();
            ShowPage(_page);
        }, primary: true);
        var line = new DockPanel();
        DockPanel.SetDock(name, Dock.Left); DockPanel.SetDock(add, Dock.Right);
        line.Children.Add(name); line.Children.Add(add); line.Children.Add(link);
        return new StackPanel { Margin = new Thickness(0, 10, 0, 10), Children = { TextLine(Loc.T("添加日历", "Add calendar"), 14, TextPrimary, FontWeights.SemiBold), Spaced(line), status } };
        static FrameworkElement Spaced(FrameworkElement element) { element.Margin = new Thickness(0, 8, 0, 0); return element; }
    }
}
