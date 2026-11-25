using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// The Calendar page, as Crest's: the month at the left (today a filled circle, the chosen day a soft square, a dot under each day in the
// colour of each calendar with events on it), and at the right the chosen day and the six after it that have events, each event a
// colour bar, its time and its title on one line; how long ago the calendars were read at the foot. Refresh and the calendar settings
// sit at the right of the agenda's first line, under the pin. Read only: nothing is ever written back. Calendars are added under Settings > Connections.
public partial class MainWindow
{
    internal static string CalendarTitle => Loc.T("日历", "Calendar");
    private const int CalendarAgendaDays = 7;
    private const double CalendarMonthWidth = 222, CalendarCell = 28, CalendarTodaySize = 19, CalendarTimeWidth = 104;
    // The day the agenda starts from; null is today, so it moves on at midnight.
    private DateOnly? _calendarPicked;
    private DateOnly _calendarMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private string _calendarBuilt = "";

    private void CalendarPage_Click(object sender, RoutedEventArgs e) => OpenCalendarDay(DateOnly.FromDateTime(DateTime.Now));

    // Opens the page on a day (the Today widget's days, the page switch).
    private void OpenCalendarDay(DateOnly day)
    {
        _calendarPicked = day == DateOnly.FromDateTime(DateTime.Now) ? null : day;
        _calendarMonth = new(day.Year, day.Month, 1);
        if (CurrentPage == DeckPage.Calendar) BuildCalendar(); else SelectPage(DeckPage.Calendar);
    }

    private void RenderCalendar()
    {
        foreach (var page in DeckPages.Order)
            if (page != DeckPage.Calendar) PageContent(page).Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        if (!double.IsFinite(ContentCanvas.Height)) return;
        var header = PanelHeader.For(ToolbarFor(ContentCanvas.Height), 18);
        ApplyHeader(header);
        ShowHeaderPin(header);
        CalendarContent.Margin = new Thickness(header.Side, 0, header.Side, MonitorBottom(ContentCanvas.Height));
        _app.RefreshCalendarIfOld(TimeSpan.FromMinutes(5));
        BuildCalendar();
    }

    // The app's events changed (a fetch, a source removed) or a minute passed: the page, the collapsed row and the widgets follow.
    internal void CalendarChanged(bool page)
    {
        if (_expanded && CurrentPage == DeckPage.Calendar) BuildCalendar();
        if (!_expanded && Settings.ShowsNextEvent(EffectiveStyle)) RenderCompact();
        if (page && _widgetUpdates.Count > 0) foreach (var update in _widgetUpdates.ToList()) update();
    }

    private void BuildCalendar()
    {
        var s = _app.Settings;
        var now = DateTime.Now;
        string key = string.Join("|", _calendarPicked, _calendarMonth, _app.CalendarFetched?.Ticks, _app.CalendarLoading, _app.CalendarErrors.Count,
            string.Join(",", s.CalendarSources.Select(source => source.Id + source.Color + source.Name)), now.ToString("yyyyMMddHHmm"), Look.Generation, Loc.Language, s.Uses24HourClock);
        if (key == _calendarBuilt && CalendarContent.Children.Count > 0) return;
        _calendarBuilt = key;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CalendarMonthWidth) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.Children.Add(CalendarMonth(s, now));
        // The month shows without a calendar too: people look there for the date.
        FrameworkElement right = s.CalendarSources.Count == 0 ? CalendarEmpty() : CalendarAgenda(s, now);
        Grid.SetColumn(right, 2); columns.Children.Add(right);
        root.Children.Add(columns);
        if (s.CalendarSources.Count > 0)
        {
            var foot = CalendarFoot(s, now);
            Grid.SetRow(foot, 1); root.Children.Add(foot);
        }
        CalendarContent.Children.Clear();
        CalendarContent.Children.Add(root);
    }

    private FrameworkElement CalendarEmpty()
    {
        var icon = CalendarIcon(22, Look.Tertiary);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var title = new TextBlock { Text = Loc.T("没有连接日历", "No calendar connected"), FontSize = 15, Foreground = UI.Brush(Look.Secondary), Margin = new Thickness(0, 10, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
        var note = new TextBlock { Text = Loc.T("添加 ICS 链接（Google、Outlook、iCloud）。DeckIsland 只读取，不会修改日程。", "Add an ICS link (Google, Outlook, iCloud). DeckIsland only reads it."),
            FontSize = 12.5, Foreground = UI.Brush(Look.Tertiary), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Center };
        var add = PillButton(Loc.T("添加日历", "Add calendar"), primary: false, () => _app.OpenSettingsPage(SettingsWindow.ConnectionsSettingsPage));
        add.Margin = new Thickness(0, 12, 0, 0); add.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { icon, title, note, add } };
    }

    // ‹ November 2026 ›, the narrow weekday names, then six weeks from the culture's first weekday.
    private FrameworkElement CalendarMonth(DeckSettings s, DateTime now)
    {
        var culture = Loc.Culture;
        var today = DateOnly.FromDateTime(now);
        var picked = _calendarPicked ?? today;
        var colors = s.CalendarSources.ToDictionary(source => source.Id, source => source.Color);
        var panel = new StackPanel();

        var head = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        var back = CalendarArrow(true, () => { _calendarMonth = _calendarMonth.AddMonths(-1); BuildCalendar(); });
        var next = CalendarArrow(false, () => { _calendarMonth = _calendarMonth.AddMonths(1); BuildCalendar(); });
        back.HorizontalAlignment = HorizontalAlignment.Left; next.HorizontalAlignment = HorizontalAlignment.Right;
        string name = _calendarMonth.ToDateTime(TimeOnly.MinValue).ToString(Loc.T("yyyy年M月", "MMMM yyyy"), culture);
        // The title goes back to this month and today, as Crest's does.
        var title = new TextBlock { Text = char.ToUpper(name[0], culture) + name[1..], FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(Look.Primary),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = Loc.T("今天", "Today") };
        title.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenCalendarDay(today); };
        head.Children.Add(back); head.Children.Add(title); head.Children.Add(next);
        panel.Children.Add(head);

        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 2) };
        for (int i = 0; i < 7; i++)
            names.Children.Add(new TextBlock { Text = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + i) % 7))[..1].ToUpper(culture), FontSize = 11,
                Foreground = UI.Brush(MonitorDraw.Faint(Look.Tertiary, .7)), HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(names);

        var days = new UniformGrid { Columns = 7 };
        var start = _calendarMonth.AddDays(-(((int)_calendarMonth.DayOfWeek - first + 7) % 7));
        for (int i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            bool inMonth = date.Month == _calendarMonth.Month, isPicked = date == picked, isToday = date == today;
            // The number centred in a fixed circle (filled for today), so a wide figure never pushes it off centre.
            var number = new TextBlock { Text = date.Day.ToString(culture), FontSize = 12, FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = UI.Brush(isToday ? Look.Fill : inMonth ? Look.Primary : MonitorDraw.Faint(Look.Tertiary, .6)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
            var circle = new Grid { Width = CalendarTodaySize, Height = CalendarTodaySize, HorizontalAlignment = HorizontalAlignment.Center };
            if (isToday) circle.Children.Add(new Ellipse { Fill = UI.Brush(Look.Primary) });
            circle.Children.Add(number);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 5, Margin = new Thickness(0, 3, 0, 0) };
            foreach (var source in CalendarFeed.On(_app.CalendarEvents, date).Select(e => e.Source).Distinct().Take(3))
                dots.Children.Add(new Ellipse { Width = 4, Height = 4, Margin = new Thickness(1, 0, 1, 0), Opacity = inMonth ? 1 : .4,
                    Fill = UI.Brush(colors.GetValueOrDefault(source, Look.Accent)), VerticalAlignment = VerticalAlignment.Center });
            var cell = new Border
            {
                Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { circle, dots } }, Width = CalendarCell, Height = CalendarCell + 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand, CornerRadius = new CornerRadius(Corner(6)), Background = isPicked ? UI.Brush(Look.FillSelected) : Brushes.Transparent
            };
            cell.MouseEnter += (_, _) => { if (!isPicked) cell.Background = UI.Brush(Look.Fill); };
            cell.MouseLeave += (_, _) => { if (!isPicked) cell.Background = Brushes.Transparent; };
            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _calendarPicked = date == today ? null : date;
                if (!inMonth) _calendarMonth = new(date.Year, date.Month, 1);
                BuildCalendar();
            };
            AutomationProperties.SetName(cell, date.ToString("D", culture));
            days.Children.Add(cell);
        }
        panel.Children.Add(days);
        return panel;
    }

    private Button CalendarAction(string data, string label, bool enabled, Action click)
    {
        var button = new Button { Style = (Style)FindResource("PanelGlyphButton"), Width = 26, Height = 24, IsEnabled = enabled, Opacity = enabled ? 1 : .4, ToolTip = label, Margin = new Thickness(2, 0, 0, 0),
            Content = new Path { Data = Geometry.Parse(data), Width = 13, Height = 13, Stretch = Stretch.Uniform, Stroke = UI.Brush(Look.Secondary), StrokeThickness = 2.2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round } };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }

    private Button CalendarArrow(bool back, Action go)
    {
        var glyph = new Path { Data = Geometry.Parse(back ? "M 15,6 L 9,12 L 15,18" : "M 9,6 L 15,12 L 9,18"), StrokeThickness = 2.4, Width = 11, Height = 11, Stretch = Stretch.Uniform,
            Stroke = UI.Brush(Look.Tertiary), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        var button = new Button { Style = (Style)FindResource("PanelGlyphButton"), Content = glyph, Width = 24, Height = 22 };
        AutomationProperties.SetName(button, back ? Loc.T("上个月", "Previous month") : Loc.T("下个月", "Next month"));
        button.Click += (_, _) => go();
        return button;
    }

    //   TODAY · WED, NOV 25
    //   ▍ 09:30 - 09:45   Standup
    //   MON, NOV 30
    //   ▍ All day         Ziua Sfântului Andrei
    // The chosen day always shows; the six after it only when something is on.
    private FrameworkElement CalendarAgenda(DeckSettings s, DateTime now)
    {
        var culture = Loc.Culture;
        var today = DateOnly.FromDateTime(now);
        var from = _calendarPicked ?? today;
        var list = new StackPanel();
        for (int i = 0; i < CalendarAgendaDays; i++)
        {
            var day = from.AddDays(i);
            var events = CalendarFeed.On(_app.CalendarEvents, day);
            if (i > 0 && events.Count == 0) continue;
            string date = day.ToDateTime(TimeOnly.MinValue).ToString(Loc.T("M月d日 ddd", "ddd, MMM d"), culture);
            string word = day == today ? Loc.T("今天", "Today") : day == today.AddDays(1) ? Loc.T("明天", "Tomorrow") : "";
            var heading = new TextBlock { Text = (word.Length > 0 ? word + " · " + date : date).ToUpper(culture), FontSize = 11.5, FontWeight = FontWeights.Medium,
                Foreground = UI.Brush(Look.Tertiary), Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 3), VerticalAlignment = VerticalAlignment.Center };
            if (i > 0) { list.Children.Add(heading); }
            else
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                actions.Children.Add(CalendarAction("M 3,11 A 9,9 0 0 1 18.4,5.6 L 21,8 M 21,3 L 21,8 L 16,8 M 21,13 A 9,9 0 0 1 5.6,18.4 L 3,16 M 3,21 L 3,16 L 8,16",
                    Loc.T("刷新", "Refresh"), !_app.CalendarLoading, () => _ = _app.RefreshCalendar()));
                actions.Children.Add(CalendarAction("M 4,7 H 13 M 17,7 H 20 M 4,17 H 7 M 11,17 H 20 M 15,4.5 A 2.5,2.5 0 1 1 15,9.5 A 2.5,2.5 0 1 1 15,4.5 Z M 9,14.5 A 2.5,2.5 0 1 1 9,19.5 A 2.5,2.5 0 1 1 9,14.5 Z",
                    Loc.T("日历设置", "Calendar settings"), true, () => _app.OpenSettingsPage(SettingsWindow.ConnectionsSettingsPage)));
                DockPanel.SetDock(actions, System.Windows.Controls.Dock.Right);
                line.Children.Add(actions); line.Children.Add(heading);
                list.Children.Add(line);
            }
            if (events.Count == 0)
                list.Children.Add(new TextBlock { Text = _app.CalendarLoading && _app.CalendarFetched is null ? Loc.T("正在读取…", "Reading…") : Loc.T("没有日程", "No events"),
                    FontSize = 12.5, Foreground = UI.Brush(MonitorDraw.Faint(Look.Tertiary, .7)), Margin = new Thickness(0, 2, 0, 2) });
            foreach (var item in events) list.Children.Add(CalendarEventRow(item, s, now));
        }
        return new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    }

    // ▍ the calendar's colour, the time (or All day), the title with its place after a dot, on one line; past events fade; a meeting
    // link gets a small Join button. The calendar's name is the tooltip.
    private FrameworkElement CalendarEventRow(CalendarEvent item, DeckSettings s, DateTime now)
    {
        var source = s.CalendarSources.FirstOrDefault(other => other.Id == item.Source);
        string color = source?.Color ?? Look.Accent;
        bool past = item.End <= now && !(item.End == item.Start && item.Start > now);
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = source is null ? null : source.Name.Length > 0 ? source.Name : source.Host, Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CalendarTimeWidth + 10) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new Border { Background = UI.Brush(color), CornerRadius = new CornerRadius(1.5), Height = 16, Opacity = past ? .45 : 1 });
        string time = item.AllDay ? Loc.T("全天", "All day") : item.End > item.Start ? CalendarClock(item.Start, s) + " - " + CalendarClock(item.End, s) : CalendarClock(item.Start, s);
        var when = new TextBlock { Text = time, FontSize = 12.5, Foreground = UI.Brush(Look.Tertiary), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Typography.SetNumeralAlignment(when, FontNumeralAlignment.Tabular);
        Grid.SetColumn(when, 1); row.Children.Add(when);
        var title = new TextBlock { FontSize = 12.5, Foreground = UI.Brush(past ? Look.Secondary : Look.Primary), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        title.Inlines.Add(new Run(item.Title.Length > 0 ? item.Title : Loc.T("（无标题）", "(No title)")));
        if (item.Location.Length > 0 && !item.Location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            title.Inlines.Add(new Run(" · " + item.Location) { Foreground = UI.Brush(Look.Tertiary) });
        Grid.SetColumn(title, 2); row.Children.Add(title);
        if (item.MeetingLink is { } link && !past)
        {
            var join = new Button { Style = (Style)FindResource("PanelGlyphButton"), Width = 24, Height = 24, Margin = new Thickness(8, 0, 0, 0), ToolTip = Loc.T("加入 · ", "Join · ") + new Uri(link).Host,
                Content = new Path { Data = Geometry.Parse("M 2,7 H 14 A 1,1 0 0 1 15,8 V 16 A 1,1 0 0 1 14,17 H 2 A 1,1 0 0 1 1,16 V 8 A 1,1 0 0 1 2,7 Z M 15,11 L 22,7 V 17 L 15,13 Z"),
                    Fill = UI.Brush(Look.Primary), Width = 13, Height = 13, Stretch = Stretch.Uniform } };
            AutomationProperties.SetName(join, Loc.T("加入会议", "Join meeting"));
            join.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            };
            Grid.SetColumn(join, 3); row.Children.Add(join);
        }
        AutomationProperties.SetName(row, $"{time} · {item.Title}");
        return row;
    }

    // "9 min ago", or which calendars failed and why.
    private FrameworkElement CalendarFoot(DeckSettings s, DateTime now)
    {
        string text;
        if (_app.CalendarErrors.Count > 0)
            text = string.Join(" · ", s.CalendarSources.Where(source => _app.CalendarErrors.ContainsKey(source.Id))
                .Select(source => (source.Name.Length > 0 ? source.Name : source.Host) + ": " + _app.CalendarErrors[source.Id]));
        else if (_app.CalendarFetched is { } at)
        {
            int minutes = (int)(now - at).TotalMinutes;
            text = minutes < 1 ? Loc.T("刚刚更新", "Just now") : minutes < 60 ? Loc.T($"{minutes} 分钟前", $"{minutes} min ago") : Loc.T($"{minutes / 60} 小时前", $"{minutes / 60} h ago");
        }
        else text = _app.CalendarLoading ? Loc.T("正在读取…", "Reading…") : "";
        return new TextBlock { Text = text, FontSize = 11.5, Margin = new Thickness(10, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text.Length > 0 ? text : null,
            Foreground = UI.Brush(_app.CalendarErrors.Count > 0 ? Look.CriticalDefault : MonitorDraw.Faint(Look.Tertiary, .7)) };
    }

    private static string CalendarClock(DateTime time, DeckSettings s) => time.ToString(s.Uses24HourClock ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);

    private FrameworkElement CalendarIcon(double size, string color) =>
        new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { new Path {
            Data = Geometry.Parse("M 5,4 H 19 A 2,2 0 0 1 21,6 V 19 A 2,2 0 0 1 19,21 H 5 A 2,2 0 0 1 3,19 V 6 A 2,2 0 0 1 5,4 Z M 3,10 H 21 M 8,2 V 6 M 16,2 V 6"),
            Stroke = UI.Brush(color), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round } } } };

    // ---- The collapsed row's next event: "14:30 Standup" behind a dot in its calendar's colour, cut short past MaxNextEvent ----
    private const double MaxNextEvent = 170;
    private FrameworkElement? _compactNextEvent;

    private FrameworkElement? CompactNextEvent(double scale, bool vertical = false)
    {
        _compactNextEvent = null;
        if (!Settings.ShowsNextEvent(EffectiveStyle) || CalendarFeed.Next(_app.CalendarEvents, DateTime.Now, _app.Settings.CalendarHorizon) is not { } next) return null;
        string color = _app.Settings.CalendarSources.FirstOrDefault(source => source.Id == next.Source)?.Color ?? Look.Accent;
        var text = WeatherText(CalendarClock(next.Start, _app.Settings) + " " + (next.Title.Length > 0 ? next.Title : Loc.T("（无标题）", "(No title)")),
            CompactMusicLayout.CollapsedTextSize(Settings.QuotaRingStyle, scale, 11));
        text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; text.MaxWidth = Math.Round(MaxNextEvent * scale);
        double dotSize = Math.Round(6 * scale);
        var dot = new Ellipse { Width = dotSize, Height = dotSize, Fill = UI.Brush(color), VerticalAlignment = VerticalAlignment.Center,
            Margin = vertical ? new Thickness(0, 0, 0, 2 * scale) : new Thickness(0, 0, 6 * scale, 0) };
        var content = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Children = { dot, text }, ToolTip = Loc.T("下一个日程", "Next event") };
        if (vertical) dot.HorizontalAlignment = text.HorizontalAlignment = HorizontalAlignment.Center;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Width = Math.Ceiling(content.DesiredSize.Width + 2 * scale);
        content.Height = Math.Ceiling(content.DesiredSize.Height);
        AutomationProperties.SetName(content, Loc.T("下一个日程：", "Next event: ") + text.Text);
        _compactNextEvent = content;
        return content;
    }

    private bool PointerOnNextEvent()
    {
        if (_compactNextEvent is not { IsVisible: true } part || PresentationSource.FromVisual(part) is null) return false;
        var rect = new Rect(part.PointToScreen(new Point()), part.PointToScreen(new Point(part.ActualWidth, part.ActualHeight)));
        rect.Inflate(4, 6);
        return rect.Contains(Native.WindowsHost.Cursor());
    }
}

// The notice before an event, as Crest's: in the island's alert card (the reminders' ReminderAlertContent), the calendar icon on a
// tint of the event's calendar colour, the title and its start time, Join for a meeting link and ✕, and a bar in that colour that
// runs down to the start. App.Calendar takes it down when the event starts.
public partial class MainWindow
{
    internal CalendarEvent? CalendarNoticeShown { get; private set; }

    internal bool ShowCalendarNotice(CalendarEvent item)
    {
        if (!CanShowReminder || _poweringOff) return false;
        string color = _app.Settings.CalendarSources.FirstOrDefault(source => source.Id == item.Source)?.Color ?? Look.Accent;
        var icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(Corner(20)), Background = UI.Brush(MonitorDraw.Faint(color, .22)),
            Margin = new Thickness(0, 0, 12, 0), Child = CalendarIcon(19, color) };
        ((FrameworkElement)icon.Child).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)icon.Child).VerticalAlignment = VerticalAlignment.Center;
        var title = WeatherText(item.Title.Length > 0 ? item.Title : Loc.T("（无标题）", "(No title)"), 15);
        title.FontWeight = FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap; title.ToolTip = item.Title;
        var time = WeatherText(CalendarClock(item.Start, _app.Settings) + (item.Location.Length > 0 && !item.Location.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? " · " + item.Location : ""), 12.5);
        time.Foreground = UI.Brush(Look.Secondary); time.FontWeight = FontWeights.SemiBold; time.TextTrimming = TextTrimming.CharacterEllipsis;
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, time } };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (item.MeetingLink is { } link)
        {
            var join = ReminderButton(Loc.T("加入", "Join"), () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                CloseCalendarNotice();
            });
            join.Height = 30; join.Padding = new Thickness(12, 0, 12, 0); join.FontSize = 13; join.Margin = new Thickness(0, 0, 8, 0);
            join.Background = UI.Brush(Look.Accent); join.Foreground = UI.Brush(Look.Hex("onAccent"));
            buttons.Children.Add(join);
        }
        var close = ReminderButton("✕", CloseCalendarNotice);
        close.Width = close.Height = 30; close.Padding = new Thickness(0); close.FontSize = 13; close.Margin = new Thickness(0);
        close.Background = UI.Brush(Look.FillSelected); close.Foreground = UI.Brush(Look.Primary);
        AutomationProperties.SetName(close, Loc.T("关闭", "Close"));
        buttons.Children.Add(close);

        var row = new DockPanel { Children = { icon, buttons, details } };
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left); DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
        // The time left before the start, from the notice's lead, as a bar that empties.
        double lead = Math.Max(1, _app.Settings.CalendarNoticeMinutes * 60);
        double left = Math.Clamp((item.Start - DateTime.Now).TotalSeconds, 0, lead);
        var fill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = UI.Brush(color), RenderTransformOrigin = new Point(0, .5),
            RenderTransform = new ScaleTransform(left / lead, 1) };
        var track = new Grid { Height = 3, Margin = new Thickness(0, 10, 0, 0), Children = { new Border { CornerRadius = new CornerRadius(1.5), Background = UI.Brush(Look.Track) }, fill } };
        ((ScaleTransform)fill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(left / lead, 0, TimeSpan.FromSeconds(left)));
        var content = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Children = { row, track } };
        AutomationProperties.SetName(content, $"{Loc.T("即将开始", "Starting soon")} · {time.Text} · {item.Title}");
        AutomationProperties.SetLiveSetting(content, AutomationLiveSetting.Assertive);

        ReminderAlertContent.Child = content;
        ReminderAlertId = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(item.Id)));
        CalendarNoticeShown = item;
        _handover.Drop();
        _hover.Stop();
        _leave.Stop();
        WatchOutsidePress(false);
        SetExpanded(true);
        return _expanded;
    }

    // ✕, Join or the event's start: the notice goes and is not shown again.
    internal void CloseCalendarNotice()
    {
        if (CalendarNoticeShown is not { } shown) return;
        _app.DismissCalendarNotice(shown.Id);
        SetExpanded(false);
    }
}
