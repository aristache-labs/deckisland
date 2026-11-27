using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

internal sealed partial class DockWindow
{
    private (FrameworkElement, Action) CalendarDetails()
    {
        var culture = CultureInfo.CurrentCulture;
        const int first = (int)DayOfWeek.Monday;
        DateOnly selected = DateOnly.FromDateTime(DateTime.Today);
        var month = selected.AddDays(1 - selected.Day);
        var caption = Label("", 22, bold: true);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 9), MaxHeight = 42, ClipToBounds = true };
        var weekdays = new UniformGrid { Columns = 7 };
        var dates = new UniformGrid { Rows = 6, Columns = 7, Margin = new Thickness(0, 3, 0, 0) };
        var monthPanel = new StackPanel();
        monthPanel.Children.Add(legend);
        monthPanel.Children.Add(weekdays);
        monthPanel.Children.Add(dates);
        var aside = new Border { Background = UI.Brush(_look.Card), BorderBrush = UI.Brush(_look.CardRim),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(13) };
        var columns = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 364 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(aside, 2);
        columns.Children.Add(monthPanel); columns.Children.Add(aside);

        var agendaTitle = Label("", 15, bold: true);
        var agendaCount = Label("", 11, dim: true);
        agendaCount.VerticalAlignment = VerticalAlignment.Center;
        var agendaHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(agendaCount, System.Windows.Controls.Dock.Right);
        agendaHeader.Children.Add(agendaCount); agendaHeader.Children.Add(agendaTitle);
        var agenda = new StackPanel();
        var empty = Label(Loc.T("没有安排", "Nothing planned"), 13, dim: true);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        var agendaBody = new Grid { Children = { new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = agenda }, empty } };
        var agendaPanel = new DockPanel();
        DockPanel.SetDock(agendaHeader, System.Windows.Controls.Dock.Top);
        agendaPanel.Children.Add(agendaHeader);
        agendaPanel.Children.Add(agendaBody);
        aside.Child = agendaPanel;

        Action refreshSources = () => { };
        Button? calendars = null;
        void ShowAgenda()
        {
            aside.Child = agendaPanel;
            calendars!.Background = Brushes.Transparent;
        }
        calendars = DetailButton(Loc.T("日历来源", "Calendars"), Loc.T("管理日历来源", "Manage calendars"), () =>
        {
            if (aside.Child != agendaPanel) { ShowAgenda(); return; }
            var (manager, update) = CalendarSubscriptions(ShowAgenda);
            aside.Child = manager;
            refreshSources = update;
            calendars!.Background = UI.Brush(_look.CardActive);
            update();
        });
        calendars.Width = 86;
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        nav.Children.Add(calendars);
        var todayButton = DetailButton(Loc.T("今天", "Today"), Loc.T("回到今天", "Go to today"), () => Select(DateOnly.FromDateTime(DateTime.Today)));
        todayButton.Width = 54;
        todayButton.Foreground = UI.Brush(Accent);
        nav.Children.Add(todayButton);
        var previous = DetailButton("‹", Loc.T("上个月", "Previous month"), () => Move(-1));
        var next = DetailButton("›", Loc.T("下个月", "Next month"), () => Move(1));
        previous.Width = next.Width = 29;
        nav.Children.Add(previous); nav.Children.Add(next);
        var header = new DockPanel { Margin = new Thickness(0, 0, 36, 0) };
        DockPanel.SetDock(nav, System.Windows.Controls.Dock.Right);
        header.Children.Add(nav); header.Children.Add(caption);
        var root = new DockPanel();
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 475, Content = columns });

        for (int i = 0; i < 7; i++)
        {
            string text = culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((first + i) % 7)).ToUpper(culture);
            var name = Label(text, 10.5, bold: true, dim: true);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Center;
            weekdays.Children.Add(new Border { Height = 23, Margin = new Thickness(0, 0, 4, 0),
                Background = UI.Brush(_look.Card), CornerRadius = new CornerRadius(4), Child = name });
        }

        void Select(DateOnly day)
        {
            selected = day;
            month = day.AddDays(1 - day.Day);
            Render();
        }
        void Move(int direction) => Select(month.AddMonths(direction));

        void Render()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var sources = _app.Settings.CalendarSources.ToDictionary(source => source.Id);
            caption.Text = month.ToString("Y", culture);
            legend.Children.Clear();
            foreach (var source in sources.Values)
            {
                var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 2) };
                label.Children.Add(CalendarSourceDot(source.Color, 6));
                label.Children.Add(Label(CalendarSourceName(source), 11, dim: true));
                legend.Children.Add(label);
            }
            dates.Children.Clear();
            int offset = ((int)month.DayOfWeek - first + 7) % 7;
            for (int i = 0; i < 42; i++)
            {
                var day = month.AddDays(i - offset);
                if (day.Month != month.Month) { dates.Children.Add(new Border { Height = 44 }); continue; }
                var cell = DetailButton(day.Day.ToString(culture), day.ToString("D", culture), () =>
                {
                    Select(day);
                    dates.Children.OfType<Button>().First(button => Equals(button.Tag, day)).Focus();
                });
                cell.Height = 44;
                cell.Margin = new Thickness(0, 0, 4, 4);
                cell.Tag = day;
                cell.Background = UI.Brush(_look.Card);
                if (day == selected)
                {
                    cell.Background = UI.Brush(_look.CardActive);
                    cell.BorderBrush = UI.Brush(Accent);
                }
                var number = Label(day.Day.ToString(culture), 14, bold: true, dim: day < today && day != selected);
                number.HorizontalAlignment = HorizontalAlignment.Center;
                if (day == today) number.Foreground = UI.Brush(Accent);
                var marks = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                    Height = 8, Margin = new Thickness(2, 2, 0, 0) };
                foreach (var sourceId in CalendarFeed.On(_app.CalendarEvents, day).Select(item => item.Source).Distinct().Take(3))
                    marks.Children.Add(CalendarSourceDot(sources.GetValueOrDefault(sourceId)?.Color ?? Accent, 5));
                cell.Content = TimeStack(number, marks);
                dates.Children.Add(cell);
            }
            var events = CalendarFeed.On(_app.CalendarEvents, selected);
            agendaTitle.Text = selected == today ? Loc.T("今天", "Today") : selected.ToString("ddd, MMM d", culture);
            agendaCount.Text = events.Count == 0 ? Loc.T("空闲", "Free") : Loc.T($"{events.Count} 个日程", Loc.Count(events.Count, "event", "events"));
            agenda.Children.Clear();
            foreach (var item in events)
            {
                string when = item.AllDay ? Loc.T("全天", "All day") : $"{TimeText(item.Start)} – {TimeText(item.End)}";
                var row = new StackPanel { ToolTip = item.Title };
                row.Children.Add(Label(when, 11, dim: true));
                row.Children.Add(Label(item.Title, 13, bold: true));
                if (item.Location.Length > 0) row.Children.Add(Label(item.Location, 11, dim: true));
                agenda.Children.Add(new Border { BorderBrush = UI.Brush(sources.GetValueOrDefault(item.Source)?.Color ?? Accent),
                    BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(8, 3, 0, 3),
                    Margin = new Thickness(0, 0, 0, 8), Child = row });
            }
            empty.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        IReadOnlyList<CalendarEvent>? shownEvents = null;
        List<CalendarSource>? shownSources = null;
        DateTime shownDay = default;
        _app.RefreshCalendarIfOld(TimeSpan.FromMinutes(5));
        return (root, () =>
        {
            if (!ReferenceEquals(shownEvents, _app.CalendarEvents) || !ReferenceEquals(shownSources, _app.Settings.CalendarSources) || shownDay != DateTime.Today)
            {
                shownEvents = _app.CalendarEvents;
                shownSources = _app.Settings.CalendarSources;
                shownDay = DateTime.Today;
                Render();
            }
            if (aside.Child != agendaPanel) refreshSources();
        });
    }

    private (FrameworkElement, Action) CalendarSubscriptions(Action done)
    {
        var pane = new DockPanel();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var finish = DetailButton(Loc.T("完成", "Done"), Loc.T("返回日程", "Back to agenda"), done);
        finish.Width = 48;
        DockPanel.SetDock(finish, System.Windows.Controls.Dock.Right);
        heading.Children.Add(finish);
        heading.Children.Add(Label(Loc.T("日历来源", "Calendars"), 16, bold: true));
        DockPanel.SetDock(heading, System.Windows.Controls.Dock.Top);
        pane.Children.Add(heading);
        var form = new StackPanel();
        var guide = Label(Loc.T("粘贴 Google、Outlook 或 iCloud 提供的 iCal 订阅链接。", "Paste an iCal subscription link from Google Calendar, Outlook, or iCloud."), 12, dim: true);
        guide.TextWrapping = TextWrapping.Wrap;
        guide.Margin = new Thickness(0, 0, 0, 8);
        form.Children.Add(guide);
        var linkRow = new DockPanel();
        string hint = Loc.T("粘贴 iCal 链接", "Paste an iCal link");
        var link = new TextBox { MaxLength = 2000, FontSize = 12, Height = 30, Padding = new Thickness(6, 5, 4, 0),
            Foreground = UI.Brush(_look.Text), CaretBrush = UI.Brush(_look.Text),
            Background = UI.Brush(_look.CardHover), BorderBrush = UI.Brush(_look.Divider) };
        AutomationProperties.SetName(link, hint);
        var watermark = Label(hint, 12, dim: true);
        watermark.Margin = new Thickness(7, 0, 0, 0);
        watermark.VerticalAlignment = VerticalAlignment.Center;
        watermark.IsHitTestVisible = false;
        var field = new Grid { Children = { link, watermark } };
        var status = Label("", 11, dim: true);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 6, 0, 0);
        bool saving = false;
        var existing = new StackPanel();
        List<CalendarSource>? sourcesShown = null;
        IReadOnlyDictionary<Guid, string>? errorsShown = null;
        Button? add = null;
        add = DetailButton(Loc.T("添加", "Add"), Loc.T("添加日历", "Add calendar"), () => _ = Add());
        add.Width = 48;
        add.Margin = new Thickness(4, 0, 0, 0);
        add.IsEnabled = false;
        DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        linkRow.Children.Add(add); linkRow.Children.Add(field);
        form.Children.Add(linkRow); form.Children.Add(status);
        DockPanel.SetDock(form, System.Windows.Controls.Dock.Bottom);
        pane.Children.Add(form);
        pane.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = existing });
        void Update()
        {
            add!.IsEnabled = !saving && link.Text.Trim().Length > 0;
            if (ReferenceEquals(sourcesShown, _app.Settings.CalendarSources) && ReferenceEquals(errorsShown, _app.CalendarErrors)) return;
            sourcesShown = _app.Settings.CalendarSources;
            errorsShown = _app.CalendarErrors;
            existing.Children.Clear();
            if (sourcesShown.Count == 0) existing.Children.Add(Label(Loc.T("尚未连接日历", "No calendars connected"), 12, dim: true));
            foreach (var source in sourcesShown)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
                row.Children.Add(CalendarSourceDot(source.Color, 7));
                var name = Label(CalendarSourceName(source), 12, bold: true);
                var labels = new StackPanel { Children = { name } };
                if (errorsShown.TryGetValue(source.Id, out var error))
                {
                    var failure = Label(error, 11, dim: true);
                    failure.TextWrapping = TextWrapping.Wrap;
                    labels.Children.Add(failure);
                }
                row.Children.Add(labels);
                existing.Children.Add(row);
            }
        }
        link.TextChanged += (_, _) =>
        {
            watermark.Visibility = link.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            status.Text = "";
            Update();
        };
        link.KeyDown += (_, e) => { if (e.Key == Key.Enter && add.IsEnabled) { e.Handled = true; _ = Add(); } };
        return (pane, Update);

        async Task Add()
        {
            if (saving) return;
            if (CalendarFeed.LinkUri(link.Text) is not { } uri)
            {
                status.Text = Loc.T("请输入有效的 iCal 链接。", "Enter a valid iCal link.");
                return;
            }
            var settings = _app.Settings.Copy();
            if (settings.CalendarSources.Count >= CalendarFeed.MaxSources)
            {
                status.Text = Loc.T("已达到日历数量上限。", "Calendar limit reached.");
                return;
            }
            var source = new CalendarSource { Host = uri.Host, Color = CalendarFeed.NextColor(settings.CalendarSources) };
            try { _app.Secrets.Write(source.Id, uri.AbsoluteUri); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                status.Text = Loc.T("无法保存链接。", "Could not save the link.");
                return;
            }
            saving = true;
            settings.CalendarSources.Add(source);
            _app.UpdateSettings(settings);
            link.Clear();
            status.Text = Loc.T("正在读取日历…", "Reading calendar…");
            Update();
            try { await _app.RefreshCalendar(); }
            finally
            {
                saving = false;
                status.Text = "";
                Update();
            }
        }
    }

    private static string CalendarSourceName(CalendarSource source) => source.Name.Length > 0 ? source.Name : source.Host;

    private static Border CalendarSourceDot(string color, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = UI.Brush(color),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0)
    };
}
