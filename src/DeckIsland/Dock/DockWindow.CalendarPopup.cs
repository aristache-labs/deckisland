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
    private const string AllDayColor = "#FF9B30FF";
    private const double DayCell = 64;

    private (FrameworkElement, Action) CalendarDetails()
    {
        var culture = CultureInfo.CurrentCulture;
        const int first = (int)DayOfWeek.Monday;
        DateOnly selected = DateOnly.FromDateTime(DateTime.Today);
        var month = selected.AddDays(1 - selected.Day);
        var caption = Label("", 24, bold: true);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var legend = new WrapPanel { Margin = new Thickness(0, 6, 0, 12) };
        legend.Children.Add(CalendarLegend(Loc.T("日程", "Events"), Accent));
        legend.Children.Add(CalendarLegend(Loc.T("全天", "All day"), AllDayColor));
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) };
        var dates = new UniformGrid { Rows = 6, Columns = 7 };
        var monthPanel = new StackPanel();
        monthPanel.Children.Add(legend);
        monthPanel.Children.Add(weekdays);
        monthPanel.Children.Add(dates);
        var aside = new Border { Background = UI.Brush(_look.Card), BorderBrush = UI.Brush(_look.CardRim),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16) };
        var columns = new Grid { Margin = new Thickness(0, 4, 0, 0), Height = (DayCell + 8) * 6 + 80 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(aside, 2);
        columns.Children.Add(monthPanel); columns.Children.Add(aside);

        var agendaTitle = Label("", 17, bold: true);
        var agendaCount = Label("", 12, dim: true);
        agendaCount.VerticalAlignment = VerticalAlignment.Center;
        var agendaHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(agendaCount, System.Windows.Controls.Dock.Right);
        agendaHeader.Children.Add(agendaCount); agendaHeader.Children.Add(agendaTitle);
        var agenda = new StackPanel();
        var emptyIcon = MonitorIcons.Icon("calendar", 22, UI.Brush(_look.Text));
        emptyIcon.Opacity = 0.7;
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

        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        var todayButton = DetailButton(Loc.T("今天", "Today"), Loc.T("回到今天", "Go to today"), () => Select(DateOnly.FromDateTime(DateTime.Today)), radius: 14);
        todayButton.Width = 58;
        todayButton.FontWeight = FontWeights.SemiBold;
        todayButton.Foreground = UI.Brush(Accent);
        nav.Children.Add(todayButton);
        var previous = DetailButton("‹", Loc.T("上个月", "Previous month"), () => Move(-1), radius: 14);
        var next = DetailButton("›", Loc.T("下个月", "Next month"), () => Move(1), radius: 14);
        previous.Width = next.Width = 30;
        previous.FontSize = next.FontSize = 18;
        previous.Margin = new Thickness(6, 0, 4, 0);
        nav.Children.Add(previous); nav.Children.Add(next);
        var header = new DockPanel { Margin = new Thickness(0, 0, 38, 0) };
        DockPanel.SetDock(nav, System.Windows.Controls.Dock.Right);
        header.Children.Add(nav); header.Children.Add(caption);
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
            weekdays.Children.Clear();
            for (int i = 0; i < 7; i++)
            {
                var dayOfWeek = (DayOfWeek)((first + i) % 7);
                string text = culture.DateTimeFormat.GetAbbreviatedDayName(dayOfWeek).ToUpper(culture);
                var name = Label(text, 11, bold: true, dim: dayOfWeek != today.DayOfWeek);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.VerticalAlignment = VerticalAlignment.Center;
                weekdays.Children.Add(new Border { Height = 30, Margin = new Thickness(0, 0, 8, 0),
                    Background = UI.Brush(_look.Card), CornerRadius = new CornerRadius(15), Child = name });
            }
            dates.Children.Clear();
            int offset = ((int)month.DayOfWeek - first + 7) % 7;
            for (int i = 0; i < 42; i++)
            {
                var day = month.AddDays(i - offset);
                if (day.Month != month.Month) { dates.Children.Add(new Border { Height = DayCell }); continue; }
                var items = CalendarFeed.On(_app.CalendarEvents, day);
                var cell = CalendarDayCell(day, today, selected, items, culture, () =>
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
                agenda.Children.Add(CalendarEventLine(item, sources));
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

    private FrameworkElement CalendarLegend(string text, string color)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 2) };
        label.Children.Add(CalendarSourceDot(color, 7));
        label.Children.Add(Label(text, 12, dim: true));
        return label;
    }

    private string CalendarKindColor(CalendarEvent item) => item.AllDay ? AllDayColor : Accent;

    // A rounded day tile: the number on top, then a corner dot and badge for one event, or a dot and "+N" for more.
    private Button CalendarDayCell(DateOnly day, DateOnly today, DateOnly selected, List<CalendarEvent> items, CultureInfo culture, Action click)
    {
        var cell = DetailButton(day.Day.ToString(culture), day.ToString("D", culture), click, radius: 12);
        cell.Height = DayCell;
        cell.Margin = new Thickness(0, 0, 8, 8);
        cell.Tag = day;
        cell.Background = UI.Brush(day == selected ? _look.CardActive : _look.Card);
        if (day == selected) cell.BorderBrush = UI.Brush(Accent);

        var number = Label(day.Day.ToString(culture), 17, bold: true, dim: day < today && day != selected);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        if (day == today) number.Foreground = UI.Brush(ThemeColor.TryParse(Accent, out var accent) && accent.G > 195 ? "#FF171717" : "#FFFFFFFF");
        var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Background = day == today ? UI.Brush(Accent) : Brushes.Transparent, Child = number };
        var body = new Grid { Height = DayCell - 2, Children = { badge } };
        cell.SizeChanged += (_, e) => body.Width = Math.Max(0, e.NewSize.Width - 2);

        if (items.Count == 1)
        {
            var dot = CalendarSourceDot(CalendarKindColor(items[0]), 7);
            dot.HorizontalAlignment = HorizontalAlignment.Right;
            dot.VerticalAlignment = VerticalAlignment.Top;
            dot.Margin = new Thickness(0, 7, 7, 0);
            var icon = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 6),
                Background = UI.Brush(CalendarKindColor(items[0])),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Child = MonitorIcons.Icon("calendar", 14, Brushes.White) };
            body.Children.Add(dot);
            body.Children.Add(icon);
        }
        else if (items.Count > 1)
        {
            var more = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 1, 5, 1), Background = UI.Brush(_look.CardHover),
                Child = Label($"+{items.Count - 1}", 10, bold: true, dim: true) };
            var marks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Children = { CalendarSourceDot(CalendarKindColor(items[0]), 7), more } };
            body.Children.Add(marks);
        }
        cell.Content = body;
        return cell;
    }

    private (FrameworkElement, TextBox) CalendarQuickEntry(Func<DateOnly> day, Action refresh)
    {
        var culture = CultureInfo.CurrentCulture;
        var now = DateTime.Now;
        var rounded = now.AddMinutes((15 - now.Minute % 15) % 15);
        var time = CalendarEntryField(Loc.T("事件时间", "Event time"), 12);
        time.Width = 64;
        time.FontWeight = FontWeights.SemiBold;
        time.Text = TimeText(rounded.Date == now.Date ? rounded : now);
        var clock = MonitorIcons.Icon("clock", 15, UI.Brush(_look.Text));
        clock.Margin = new Thickness(2, 0, 0, 0);
        var timePill = new Border { CornerRadius = new CornerRadius(16), Background = UI.Brush(_look.Card), Padding = new Thickness(6, 0, 10, 0),
            Margin = new Thickness(0, 0, 6, 0), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { time, clock } } };
        var title = CalendarEntryField(Loc.T("事件标题", "Event title"), LocalCalendarEvent.MaxTitleLength);
        var hint = Label(Loc.T("新日程 (N)", "New event (N)"), 12, dim: true);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(4, 0, 3, 0);
        var field = new Grid { Children = { title, hint }, Margin = new Thickness(0, 0, 4, 0) };
        var status = Label("", 11, dim: true);
        status.TextWrapping = TextWrapping.Wrap;
        status.Visibility = Visibility.Collapsed;
        var add = DetailButton("+", Loc.T("添加事件", "Add event"), Add, radius: 16);
        add.Width = add.Height = 32;
        add.FontSize = 18;
        add.Background = UI.Brush(_look.CardActive);
        add.IsEnabled = false;
        var row = new DockPanel();
        DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        row.Children.Add(add);
        row.Children.Add(timePill);
        row.Children.Add(field);
        var pill = new Border { CornerRadius = new CornerRadius(20), Background = UI.Brush(_look.CardHover), Padding = new Thickness(4),
            Margin = new Thickness(0, 8, 0, 0), Child = row };
        title.TextChanged += (_, _) =>
        {
            hint.Visibility = title.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            add.IsEnabled = !string.IsNullOrWhiteSpace(title.Text);
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
        var field = new TextBox { MaxLength = limit, Height = 30, FontSize = 12, Padding = new Thickness(2, 6, 2, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = UI.Brush(_look.Text), CaretBrush = UI.Brush(_look.Text) };
        AutomationProperties.SetName(field, name);
        return field;
    }


    private ToolTip CalendarDayPreview(DateOnly day, List<CalendarEvent> events, IReadOnlyDictionary<Guid, CalendarSource> sources)
    {
        var preview = new StackPanel { MinWidth = 175 };
        var title = Label(day.ToString("ddd, MMM d", CultureInfo.CurrentCulture), 12, bold: true);
        title.Margin = new Thickness(0, 0, 0, 5);
        preview.Children.Add(title);
        foreach (var item in events.Take(5)) preview.Children.Add(CalendarEventLine(item, sources));
        if (events.Count > 5) preview.Children.Add(Label($"+{events.Count - 5}", 11, dim: true));
        return new ToolTip { Content = preview, Background = UI.Brush(_look.Card), BorderBrush = UI.Brush(_look.Divider),
            Padding = new Thickness(9), Placement = PlacementMode.Top };
    }

    private FrameworkElement CalendarEventLine(CalendarEvent item, IReadOnlyDictionary<Guid, CalendarSource> sources)
    {
        string when = item.AllDay ? Loc.T("全天", "All day") : TimeText(item.Start);
        var time = Label(when, 11, dim: true);
        time.Width = 61;
        time.VerticalAlignment = VerticalAlignment.Center;
        var text = new StackPanel { Children = { Label(item.Title, 12, bold: true) } };
        if (item.Location.Length > 0) text.Children.Add(Label(item.Location, 10, dim: true));
        var entry = new Border { BorderBrush = UI.Brush(EventColor(item, sources)), BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(8, 4, 0, 4), Child = text };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), Children = { time, entry } };
        return row;
    }

    private string EventColor(CalendarEvent item, IReadOnlyDictionary<Guid, CalendarSource> sources) =>
        sources.GetValueOrDefault(item.Source)?.Color ?? Accent;

    private static Border CalendarSourceDot(string color, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = UI.Brush(color),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0)
    };
}
