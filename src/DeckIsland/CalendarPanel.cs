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

// The Calendar page, after Crest's: the month at the left (a dot under each day for each calendar with events on it), the chosen day's
// events at the right with a Join button for meeting links. Read only: nothing is ever written back. The calendars are added under
// Settings > Connections (SettingsConnections.cs); App.Calendar fetches them.
public partial class MainWindow
{
    internal static string CalendarTitle => Loc.T("日历", "Calendar");
    private DateOnly _calendarDay = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly _calendarMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private string _calendarBuilt = "";

    private void CalendarPage_Click(object sender, RoutedEventArgs e) => OpenCalendarDay(DateOnly.FromDateTime(DateTime.Now));

    // Opens the page on a day (the Today widget's days, the page switch).
    private void OpenCalendarDay(DateOnly day)
    {
        _calendarDay = day; _calendarMonth = new(day.Year, day.Month, 1);
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

    // The app's events changed (a fetch, a source removed) or a minute passed: the page and the collapsed row follow.
    internal void CalendarChanged(bool page)
    {
        if (_expanded && CurrentPage == DeckPage.Calendar && page) BuildCalendar();
        if (!_expanded && Settings.ShowsNextEvent(EffectiveStyle)) RenderCompact();
        if (page && _widgetUpdates.Count > 0) foreach (var update in _widgetUpdates.ToList()) update();
    }

    private void BuildCalendar()
    {
        var s = _app.Settings;
        string key = string.Join("|", _calendarDay, _calendarMonth, _app.CalendarFetched?.Ticks, _app.CalendarLoading, s.CalendarSources.Count,
            string.Join(",", s.CalendarSources.Select(source => source.Id + source.Color)), DateTime.Now.ToString("yyyyMMddHHmm"), Look.Generation, Loc.Language, s.Uses24HourClock);
        if (key == _calendarBuilt && CalendarContent.Children.Count > 0) return;
        _calendarBuilt = key;
        CalendarContent.Children.Clear();
        if (s.CalendarSources.Count == 0) { CalendarContent.Children.Add(CalendarEmpty()); return; }

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(CalendarMonth(s));
        var day = CalendarDayList(s);
        Grid.SetColumn(day, 2); grid.Children.Add(day);
        CalendarContent.Children.Add(grid);
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

    // The month as six weeks from the culture's first weekday; days outside the month dim, today ringed, the chosen day filled.
    private FrameworkElement CalendarMonth(DeckSettings s)
    {
        var culture = Loc.Culture;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var colors = s.CalendarSources.ToDictionary(source => source.Id, source => source.Color);
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var next = CalendarArrow(false, () => { _calendarMonth = _calendarMonth.AddMonths(1); BuildCalendar(); });
        var back = CalendarArrow(true, () => { _calendarMonth = _calendarMonth.AddMonths(-1); BuildCalendar(); });
        DockPanel.SetDock(back, System.Windows.Controls.Dock.Left); DockPanel.SetDock(next, System.Windows.Controls.Dock.Right);
        head.Children.Add(back); head.Children.Add(next);
        head.Children.Add(new TextBlock { Text = _calendarMonth.ToDateTime(TimeOnly.MinValue).ToString(Loc.T("yyyy年M月", "MMMM yyyy"), culture), FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = UI.Brush(Look.Primary), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);

        var days = new UniformGrid { Columns = 7 };
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
            days.Children.Add(new TextBlock { Text = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + i) % 7)).ToUpper(culture)[..1], FontSize = 11,
                Foreground = UI.Brush(Look.Tertiary), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
        var start = _calendarMonth.AddDays(-(((int)_calendarMonth.DayOfWeek - first + 7) % 7));
        for (int i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            bool inMonth = date.Month == _calendarMonth.Month, chosen = date == _calendarDay, isToday = date == today;
            var number = new TextBlock { Text = date.Day.ToString(culture), FontSize = 13, FontWeight = isToday || chosen ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = UI.Brush(!inMonth ? MonitorDraw.Faint(Look.Tertiary, .55) : Look.Primary), HorizontalAlignment = HorizontalAlignment.Center };
            Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 4, Margin = new Thickness(0, 2, 0, 0) };
            foreach (var source in CalendarFeed.On(_app.CalendarEvents, date).Select(e => e.Source).Distinct().Take(3))
                dots.Children.Add(new Ellipse { Width = 4, Height = 4, Margin = new Thickness(1, 0, 1, 0), Fill = UI.Brush(colors.GetValueOrDefault(source, Look.Accent)) });
            var cell = new Border
            {
                Child = new StackPanel { Children = { number, dots } }, Padding = new Thickness(0, 4, 0, 3), Margin = new Thickness(1), Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(Corner(8)), Background = chosen ? UI.Brush(Look.FillSelected) : Brushes.Transparent,
                BorderBrush = isToday ? UI.Brush(Look.Accent) : null, BorderThickness = new Thickness(isToday ? 1.5 : 0)
            };
            cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; _calendarDay = date; if (!inMonth) _calendarMonth = new(date.Year, date.Month, 1); BuildCalendar(); };
            AutomationProperties.SetName(cell, date.ToString("D", culture));
            days.Children.Add(cell);
        }
        panel.Children.Add(days);
        return panel;
    }

    private Button CalendarArrow(bool back, Action go)
    {
        var glyph = new Path { Data = Geometry.Parse(back ? "M 15,6 L 9,12 L 15,18" : "M 9,6 L 15,12 L 9,18"), StrokeThickness = 2, Width = 13, Height = 13, Stretch = Stretch.Uniform,
            Stroke = UI.Brush(Look.Secondary), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        var button = new Button { Style = (Style)FindResource("PanelGlyphButton"), Content = glyph, Width = 26, Height = 24 };
        AutomationProperties.SetName(button, back ? Loc.T("上个月", "Previous month") : Loc.T("下个月", "Next month"));
        button.Click += (_, _) => go();
        return button;
    }

    // The chosen day's heading, a refresh button, and its events; a source that failed says so under them.
    private FrameworkElement CalendarDayList(DeckSettings s)
    {
        var culture = Loc.Culture;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var refresh = new Button { Style = (Style)FindResource("PanelGlyphButton"), Width = 26, Height = 24, IsEnabled = !_app.CalendarLoading, Opacity = _app.CalendarLoading ? .4 : 1,
            Content = new Path { Data = Geometry.Parse("M 3,11 A 9,9 0 0 1 18.4,5.6 L 21,8 M 21,3 L 21,8 L 16,8 M 21,13 A 9,9 0 0 1 5.6,18.4 L 3,16 M 3,21 L 3,16 L 8,16"), Width = 13, Height = 13,
                Stretch = Stretch.Uniform, Stroke = UI.Brush(Look.Secondary), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round },
            ToolTip = _app.CalendarFetched is { } at ? Loc.T("更新于 ", "Updated ") + at.ToString(s.Uses24HourClock ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture) : Loc.T("刷新", "Refresh") };
        AutomationProperties.SetName(refresh, Loc.T("刷新日历", "Refresh calendars"));
        refresh.Click += (_, _) => _ = _app.RefreshCalendar();
        DockPanel.SetDock(refresh, System.Windows.Controls.Dock.Right); head.Children.Add(refresh);
        string label = _calendarDay == today ? Loc.T("今天", "Today") : _calendarDay == today.AddDays(1) ? Loc.T("明天", "Tomorrow")
            : _calendarDay.ToDateTime(TimeOnly.MinValue).ToString(Loc.T("M月d日 dddd", "dddd, MMMM d"), culture);
        head.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(Look.Primary), VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(head);

        var events = CalendarFeed.On(_app.CalendarEvents, _calendarDay);
        FrameworkElement body;
        if (events.Count == 0)
            body = new TextBlock { Text = _app.CalendarLoading && _app.CalendarFetched is null ? Loc.T("正在读取…", "Reading…") : Loc.T("这一天没有日程", "Nothing on this day"),
                FontSize = 13, Foreground = UI.Brush(Look.Tertiary), Margin = new Thickness(0, 6, 0, 0) };
        else
        {
            var list = new StackPanel();
            foreach (var item in events) list.Children.Add(CalendarEventRow(item, s));
            body = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        Grid.SetRow(body, 1); root.Children.Add(body);
        if (_app.CalendarErrors.Count > 0)
        {
            var failed = string.Join(" · ", s.CalendarSources.Where(source => _app.CalendarErrors.ContainsKey(source.Id))
                .Select(source => (source.Name.Length > 0 ? source.Name : source.Host) + ": " + _app.CalendarErrors[source.Id]));
            var error = new TextBlock { Text = failed, FontSize = 11.5, Foreground = UI.Brush(Look.CriticalDefault), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0), ToolTip = failed };
            Grid.SetRow(error, 2); root.Children.Add(error);
        }
        return root;
    }

    // A colour bar, the time (or "All day"), the title and place; past events dim; a meeting link gets a Join button.
    private FrameworkElement CalendarEventRow(CalendarEvent item, DeckSettings s)
    {
        string color = s.CalendarSources.FirstOrDefault(source => source.Id == item.Source)?.Color ?? Look.Accent;
        bool past = item.End < DateTime.Now && !(item.AllDay && item.End.Date >= DateTime.Today.AddDays(1));
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6), Opacity = past ? .5 : 1 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(s.Uses24HourClock ? 92 : 118) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new Border { Background = UI.Brush(color), CornerRadius = new CornerRadius(2), MinHeight = 18 });
        string time = item.AllDay ? Loc.T("全天", "All day") : CalendarClock(item.Start, s) + "–" + CalendarClock(item.End, s);
        var when = new TextBlock { Text = time, FontSize = 12.5, Foreground = UI.Brush(Look.Secondary), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 8, 0) };
        Typography.SetNumeralAlignment(when, FontNumeralAlignment.Tabular);
        Grid.SetColumn(when, 2); row.Children.Add(when);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = item.Title.Length > 0 ? item.Title : Loc.T("（无标题）", "(No title)"), FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = UI.Brush(Look.Primary), TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.Location.Length > 0 && !item.Location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            text.Children.Add(new TextBlock { Text = item.Location, FontSize = 11.5, Foreground = UI.Brush(Look.Tertiary), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 3); row.Children.Add(text);
        if (item.MeetingLink is { } link && !past)
        {
            var join = PillButton(Loc.T("加入", "Join"), primary: false, () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            });
            join.VerticalAlignment = VerticalAlignment.Center; join.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(join, 4); row.Children.Add(join);
        }
        AutomationProperties.SetName(row, $"{time} · {item.Title}");
        return row;
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
        text.TextTrimming = TextTrimming.CharacterEllipsis; text.MaxWidth = Math.Round(MaxNextEvent * scale);
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
