using System.Globalization;
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
    private const double DayCell = 62;
    private const string LocalEventPrefix = "local:";

    // Fills that read on the popup's own background, which is almost the colour of the dock's cards.
    // A radius that follows the dock's corner setting: its value at the default 50%, scaled with the setting and capped at max.
    private double CalendarRadius(double atDefault, double max) =>
        Math.Min(max, atDefault * Math.Clamp(_app.DockSettings.Corner, 0, 100) / 50.0);

    private bool CalendarLight => _look.Text == "#FF1D1D1F";
    private string CalendarTile => CalendarLight ? "#0F000000" : "#14FFFFFF";
    private string CalendarTileHover => CalendarLight ? "#1A000000" : "#24FFFFFF";
    private string CalendarTileStrong => CalendarLight ? "#14000000" : "#1CFFFFFF";

    private (FrameworkElement, Action) CalendarDetails()
    {
        var culture = CultureInfo.CurrentCulture;
        const int first = (int)DayOfWeek.Monday;
        DateOnly selected = DateOnly.FromDateTime(DateTime.Today);
        var month = selected.AddDays(1 - selected.Day);
        var caption = Label("", 24, bold: true);
        var legend = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var titleBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { caption, legend } };
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 6) };
        var dates = new UniformGrid { Rows = 6, Columns = 7 };
        var monthPanel = new StackPanel { Children = { weekdays, dates } };
        var aside = new Border { Background = UI.Brush(CalendarLight ? "#08000000" : "#0AFFFFFF"), BorderBrush = UI.Brush(_look.Divider),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(CalendarRadius(16, 28)), Padding = new Thickness(16) };
        var columns = new Grid { Margin = new Thickness(0, 16, 0, 0), Height = (DayCell + 6) * 6 + 36 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(aside, 2);
        columns.Children.Add(monthPanel); columns.Children.Add(aside);

        var agendaTitle = Label("", 18, bold: true);
        var agendaCount = Label("", 12.5, dim: true);
        agendaCount.VerticalAlignment = VerticalAlignment.Center;
        var agendaHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(agendaCount, System.Windows.Controls.Dock.Right);
        agendaHeader.Children.Add(agendaCount); agendaHeader.Children.Add(agendaTitle);
        var agenda = new StackPanel();
        var emptyIcon = MonitorIcons.Icon("calendar", 24, UI.Brush(_look.Dim));
        emptyIcon.Margin = new Thickness(0, 0, 0, 8);
        var emptyText = Label(Loc.T("没有安排", "Nothing planned"), 13, dim: true);
        emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { emptyIcon, emptyText } };
        var agendaBody = new Grid { Children = { new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = agenda }, empty } };
        var agendaPanel = new DockPanel();
        var (composer, eventTitle) = CalendarQuickEntry(() => selected, () => Render());
        DockPanel.SetDock(agendaHeader, System.Windows.Controls.Dock.Top);
        DockPanel.SetDock(composer, System.Windows.Controls.Dock.Bottom);
        agendaPanel.Children.Add(agendaHeader);
        agendaPanel.Children.Add(composer);
        agendaPanel.Children.Add(agendaBody);
        aside.Child = agendaPanel;

        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var todayButton = CalendarHeaderButton(Loc.T("今天", "Today"), Loc.T("回到今天", "Go to today"), () => Select(DateOnly.FromDateTime(DateTime.Today)));
        todayButton.Width = 72;
        todayButton.FontSize = 14;
        todayButton.Foreground = UI.Brush(Accent);
        var previous = CalendarHeaderButton("", Loc.T("上个月", "Previous month"), () => Move(-1));
        var next = CalendarHeaderButton("", Loc.T("下个月", "Next month"), () => Move(1));
        // Drawn chevrons: the font's ‹ › sit off-centre in a round button.
        previous.Content = MonitorIcons.Icon("chevron-left", 16, UI.Brush(_look.Text));
        next.Content = MonitorIcons.Icon("chevron-right", 16, UI.Brush(_look.Text));
        previous.Margin = new Thickness(8, 0, 6, 0);
        nav.Children.Add(todayButton); nav.Children.Add(previous); nav.Children.Add(next);
        var header = new DockPanel { Margin = new Thickness(0, 0, 50, 0) };
        DockPanel.SetDock(nav, System.Windows.Controls.Dock.Right);
        header.Children.Add(nav); header.Children.Add(titleBlock);
        var root = new DockPanel();
        root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.N || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox) return;
            eventTitle.Focus();
            e.Handled = true;
        };
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 620, Content = columns });

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
                legend.Children.Add(CalendarLegend(source.Name.Length > 0 ? source.Name : source.Host, source.Color));
            legend.Children.Add(CalendarLegend(Loc.T("我的日程", "My events"), LocalEventColor(sources)));
            weekdays.Children.Clear();
            for (int i = 0; i < 7; i++)
            {
                var dayOfWeek = (DayOfWeek)((first + i) % 7);
                string text = culture.DateTimeFormat.GetAbbreviatedDayName(dayOfWeek).ToUpper(culture);
                var name = Label(text, 12, bold: true, dim: dayOfWeek != today.DayOfWeek);
                if (dayOfWeek == today.DayOfWeek) name.Foreground = UI.Brush(Accent);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.Margin = new Thickness(0, 0, 6, 0);
                weekdays.Children.Add(name);
            }
            dates.Children.Clear();
            int offset = ((int)month.DayOfWeek - first + 7) % 7;
            for (int i = 0; i < 42; i++)
            {
                var day = month.AddDays(i - offset);
                if (day.Month != month.Month) { dates.Children.Add(new Border { Height = DayCell }); continue; }
                var items = CalendarFeed.On(_app.CalendarEvents, day);
                var cell = CalendarDayCell(day, today, selected, items.Select(item => EventColor(item, sources)).ToList(), culture, () =>
                {
                    Select(day);
                    dates.Children.OfType<Button>().First(button => Equals(button.Tag, day)).Focus();
                });
                if (items.Count > 0) cell.ToolTip = CalendarDayPreview(day, items, sources);
                dates.Children.Add(cell);
            }
            var events = CalendarFeed.On(_app.CalendarEvents, selected);
            agendaTitle.Text = selected == today ? Loc.T("今天", "Today")
                : selected == today.AddDays(1) ? Loc.T("明天", "Tomorrow") : selected.ToString("ddd, MMM d", culture);
            agendaCount.Text = events.Count == 0 ? Loc.T("空闲", "Free") : Loc.T($"{events.Count} 个日程", Loc.Count(events.Count, "event", "events"));
            agenda.Children.Clear();
            foreach (var item in events)
            {
                Action? delete = LocalEventId(item) is { } id ? () => { _app.RemoveLocalCalendarEvent(id); Render(); } : null;
                agenda.Children.Add(CalendarEventLine(item, sources, delete));
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
        });
    }

    private static Guid? LocalEventId(CalendarEvent item) =>
        item.Source == Guid.Empty && item.Id.StartsWith(LocalEventPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(item.Id[LocalEventPrefix.Length..], "N", out var id) ? id : null;

    // The accent, unless a connected calendar already wears it: then the first accent no calendar uses.
    private string LocalEventColor(IReadOnlyDictionary<Guid, CalendarSource> sources)
    {
        bool Taken(string color) => sources.Values.Any(source => ThemeColor.TryParse(source.Color, out var a) && ThemeColor.TryParse(color, out var b)
            && a.R == b.R && a.G == b.G && a.B == b.B);
        return Taken(Accent) ? DockLook.Accents.FirstOrDefault(color => !Taken(color)) ?? Accent : Accent;
    }

    private Button CalendarHeaderButton(string text, string label, Action click)
    {
        var button = DetailButton(text, label, click, radius: CalendarRadius(17, 17), hover: CalendarTileHover);
        button.Height = 34;
        button.Width = 34;
        button.FontSize = 20;
        button.FontWeight = FontWeights.SemiBold;
        button.Background = UI.Brush(CalendarTile);
        return button;
    }

    private FrameworkElement CalendarLegend(string text, string color)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 2) };
        label.Children.Add(CalendarSourceDot(color, 8));
        label.Children.Add(Label(text, 12, dim: true));
        return label;
    }

    // A rounded day tile: the number on top, a dot per event (in its calendar's colour) underneath, "+N" past three.
    private Button CalendarDayCell(DateOnly day, DateOnly today, DateOnly selected, List<string> colors, CultureInfo culture, Action click)
    {
        bool isSelected = day == selected, isToday = day == today;
        var cell = DetailButton(day.Day.ToString(culture), day.ToString("D", culture), click, radius: CalendarRadius(12, 24), hover: CalendarTileHover);
        cell.Height = DayCell;
        cell.Margin = new Thickness(0, 0, 6, 6);
        cell.Tag = day;
        cell.Background = UI.Brush(CalendarTile);
        if (isSelected)
        {
            cell.BorderBrush = UI.Brush(Accent);
            cell.BorderThickness = new Thickness(1.5);
        }

        var number = Label(day.Day.ToString(culture), 16, bold: true, dim: day < today && !isSelected);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        if (isToday) number.Foreground = UI.Brush(ThemeColor.TryParse(Accent, out var accent) && accent.G > 195 ? "#FF171717" : "#FFFFFFFF");
        var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(CalendarRadius(15, 15)), Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Background = isToday ? UI.Brush(Accent) : Brushes.Transparent, Child = number };
        // One dot per calendar with events that day (at most three), then "+N" for the events the dots leave out.
        // A fixed-height row so the dots sit at the same height in every tile.
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Height = 14, Margin = new Thickness(0, 0, 0, 7),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        var shown = colors.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        foreach (var color in shown)
        {
            var dot = CalendarSourceDot(color, 5);
            dot.Margin = new Thickness(1.5, 0, 1.5, 0);
            marks.Children.Add(dot);
        }
        if (colors.Count > shown.Count)
        {
            var more = Label($"+{colors.Count - shown.Count}", 9.5, bold: true, dim: true);
            more.VerticalAlignment = VerticalAlignment.Center;
            more.Margin = new Thickness(2, 0, 0, 0);
            marks.Children.Add(more);
        }
        var body = new Grid { Height = DayCell - 3, Children = { badge, marks } };
        cell.SizeChanged += (_, e) => body.Width = Math.Max(0, e.NewSize.Width - 3);
        cell.Content = body;
        return cell;
    }

    private (FrameworkElement, TextBox) CalendarQuickEntry(Func<DateOnly> day, Action refresh)
    {
        var culture = CultureInfo.CurrentCulture;
        var now = DateTime.Now;
        var rounded = now.AddMinutes((15 - now.Minute % 15) % 15);
        var time = CalendarEntryField(Loc.T("事件时间", "Event time"), 12);
        time.Width = 66;
        time.FontWeight = FontWeights.SemiBold;
        time.Text = TimeText(rounded.Date == now.Date ? rounded : now);
        var clock = MonitorIcons.Icon("clock", 15, UI.Brush(_look.Dim));
        var timePill = new Border { CornerRadius = new CornerRadius(CalendarRadius(17, 17)), Background = UI.Brush(CalendarTileStrong), Padding = new Thickness(8, 0, 10, 0),
            Margin = new Thickness(0, 0, 6, 0), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { time, clock } } };
        var title = CalendarEntryField(Loc.T("事件标题", "Event title"), LocalCalendarEvent.MaxTitleLength);
        var hint = Label(Loc.T("新日程 (N)", "New event (N)"), 13, dim: true);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(4, 0, 3, 0);
        var field = new Grid { Children = { title, hint }, Margin = new Thickness(0, 0, 4, 0) };
        var status = Label("", 11, dim: true);
        status.TextWrapping = TextWrapping.Wrap;
        status.Visibility = Visibility.Collapsed;
        var add = DetailButton("+", Loc.T("添加事件", "Add event"), Add, radius: CalendarRadius(17, 17), hover: CalendarTileHover);
        add.Width = add.Height = 34;
        add.FontSize = 20;
        add.Background = UI.Brush(CalendarTileStrong);
        add.IsEnabled = false;
        var row = new DockPanel();
        DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        row.Children.Add(add);
        row.Children.Add(timePill);
        row.Children.Add(field);
        var pill = new Border { CornerRadius = new CornerRadius(CalendarRadius(21, 21)), Background = UI.Brush(CalendarTile), Padding = new Thickness(4),
            Margin = new Thickness(0, 8, 0, 0), Child = row };
        title.TextChanged += (_, _) =>
        {
            hint.Visibility = title.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            add.IsEnabled = !string.IsNullOrWhiteSpace(title.Text);
            add.Background = UI.Brush(add.IsEnabled ? Accent : CalendarTileStrong);
            add.Foreground = UI.Brush(add.IsEnabled ? "#FFFFFFFF" : _look.Text);
            status.Visibility = Visibility.Collapsed;
        };
        void Enter(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !add.IsEnabled) return;
            Add();
            e.Handled = true;
        }
        time.KeyDown += Enter;
        title.KeyDown += Enter;
        return (new StackPanel { Children = { pill, status } }, title);

        void Add()
        {
            var item = LocalCalendarEvent.Create(day(), time.Text, title.Text, culture);
            if (item is not null && _app.AddLocalCalendarEvent(item))
            {
                title.Clear();
                refresh();
                title.Focus();
                return;
            }
            status.Text = item is null ? Loc.T("请输入有效时间和标题。", "Enter a valid time and title.")
                : Loc.T("已达到本地事件上限。", "Local event limit reached.");
            status.Visibility = Visibility.Visible;
        }
    }

    private TextBox CalendarEntryField(string name, int limit)
    {
        var field = new TextBox { MaxLength = limit, Height = 34, FontSize = 13, Padding = new Thickness(2, 7, 2, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = UI.Brush(_look.Text), CaretBrush = UI.Brush(_look.Text) };
        AutomationProperties.SetName(field, name);
        return field;
    }

    private ToolTip CalendarDayPreview(DateOnly day, List<CalendarEvent> events, IReadOnlyDictionary<Guid, CalendarSource> sources)
    {
        var preview = new StackPanel { MinWidth = 200 };
        var title = Label(day.ToString("ddd, MMM d", CultureInfo.CurrentCulture), 12, bold: true);
        title.Margin = new Thickness(0, 0, 0, 6);
        preview.Children.Add(title);
        foreach (var item in events.Take(5)) preview.Children.Add(CalendarEventLine(item, sources, null));
        if (events.Count > 5) preview.Children.Add(Label($"+{events.Count - 5}", 11, dim: true));
        return new ToolTip { Content = preview, Background = UI.Brush(CalendarLight ? "#FFF8F8FA" : "#FF202024"), BorderBrush = UI.Brush(_look.Divider),
            Padding = new Thickness(10), Placement = PlacementMode.Top };
    }

    // One event as a card: a bar in its calendar's colour, the time, the title and, for events added here, a delete button.
    private FrameworkElement CalendarEventLine(CalendarEvent item, IReadOnlyDictionary<Guid, CalendarSource> sources, Action? delete)
    {
        var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = UI.Brush(EventColor(item, sources)),
            Margin = new Thickness(0, 0, 10, 0) };
        var time = Label(item.AllDay ? Loc.T("全天", "All day") : TimeText(item.Start), 11.5, dim: true);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Label(item.Title, 13, bold: true), time } };
        if (item.Location.Length > 0) text.Children.Add(Label(item.Location, 11, dim: true));
        var row = new DockPanel { Children = { bar } };
        if (delete is not null)
        {
            var remove = DetailButton("", Loc.T("删除事件", "Delete event"), delete, radius: CalendarRadius(14, 14), hover: CalendarTileHover);
            remove.Width = remove.Height = 28;
            remove.VerticalAlignment = VerticalAlignment.Center;
            remove.Content = MonitorIcons.Icon("trash", 15, UI.Brush(_look.Dim));
            remove.ToolTip = Loc.T("删除", "Delete");
            DockPanel.SetDock(remove, System.Windows.Controls.Dock.Right);
            row.Children.Add(remove);
        }
        DockPanel.SetDock(bar, System.Windows.Controls.Dock.Left);
        row.Children.Add(text);
        return new Border { Background = UI.Brush(CalendarTile), CornerRadius = new CornerRadius(CalendarRadius(10, 18)), Padding = new Thickness(8, 8, 6, 8),
            Margin = new Thickness(0, 0, 0, 6), Child = row };
    }

    private string EventColor(CalendarEvent item, IReadOnlyDictionary<Guid, CalendarSource> sources) =>
        sources.GetValueOrDefault(item.Source)?.Color ?? LocalEventColor(sources);

    private static Border CalendarSourceDot(string color, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = UI.Brush(color),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0)
    };
}
