using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

internal sealed partial class DockWindow
{
    private static readonly string[] DefaultWorldCities = ["New York", "London", "Tokyo"];

    private static string[] TimeCities(DockWidget widget)
    {
        var defaults = widget.Kind == DockWidgetKind.WorldTime ? ["Paris"] : DefaultWorldCities;
        var cities = WorldClocks.Normalize(widget.Option.Split('|'));
        return defaults.Select((city, i) => i < cities.Count && cities[i].Length > 0 ? cities[i] : city).ToArray();
    }

    private ContextMenu TimeWidgetMenu(DockWidget widget)
    {
        var menu = NewMenu();
        var settings = _app.DockSettings;
        var cities = TimeCities(widget);
        for (int i = 0; i < cities.Length; i++)
        {
            int slot = i;
            var cityMenu = new MenuItem { Header = $"{Loc.T("城市", "City")} · {cities[i]}", IsEnabled = !settings.Locked };
            foreach (var group in WorldClocks.Cities.GroupBy(city => city.City[0]))
            {
                var letter = new MenuItem { Header = group.Key.ToString() };
                foreach (var entry in group)
                {
                    var city = new MenuItem { Header = entry.City, IsCheckable = true, IsChecked = cities[slot] == entry.City };
                    city.Click += (_, _) =>
                    {
                        var current = _app.DockSettings;
                        if (current.Locked) return;
                        var options = TimeCities(current.Widgets.FirstOrDefault(w => w.Id == widget.Id) ?? widget);
                        options[slot] = entry.City;
                        _app.UpdateDockSettings(current.WithWidgets(current.Widgets.Select(w => w.Id == widget.Id
                            ? w with { Option = string.Join('|', options) } : w).ToList()));
                    };
                    letter.Items.Add(city);
                }
                cityMenu.Items.Add(letter);
            }
            menu.Items.Add(cityMenu);
        }
        return menu;
    }

    // World time's city picker: a search over the cities WorldClocks knows, each with its time zone, time and offset from here;
    // a click sets the widget's city.
    private void OpenCityPicker(DockWidget widget, FrameworkElement anchor)
    {
        if (_app.DockSettings.Locked || !BeginDockPanel(anchor)) return;
        string current = TimeCities(widget)[0];
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var close = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Child = MonitorIcons.Icon("close", 18, UI.Brush(_look.Dim)) };
        close.MouseLeftButtonUp += (_, _) => CloseWidgetDetails();
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
        header.Children.Add(close);
        header.Children.Add(Label(Loc.T("世界时间", "World time"), 15, bold: true));
        string hint = Loc.T("搜索城市", "Search a city");
        var search = new TextBox { FontSize = 13.5, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = UI.Brush(_look.Text),
            CaretBrush = UI.Brush(_look.Text), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(2, 0, 2, 0) };
        System.Windows.Automation.AutomationProperties.SetName(search, hint);
        var placeholder = Label(hint, 13.5, dim: true);
        placeholder.IsHitTestVisible = false; placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.Margin = new Thickness(4, 0, 0, 0);
        var field = new DockPanel { Children = { new Border { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = MonitorIcons.Icon("search", 16, UI.Brush(_look.Dim)) }, new Grid { Children = { search, placeholder } } } };
        var box = new Border { Background = UI.Brush(_look.CardHover), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Child = field,
            Margin = new Thickness(0, 0, 0, 10) };
        var list = new StackPanel();
        void Show()
        {
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            string query = search.Text.Trim();
            var matches = query.Length == 0 ? WorldClocks.Cities.Where(c => c.City == current)
                : WorldClocks.Cities.Where(c => c.City.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || IanaZone(c.Zone).Contains(query, StringComparison.CurrentCultureIgnoreCase));
            list.Children.Clear();
            var now = DateTimeOffset.Now;
            foreach (var (city, zoneId) in matches.Take(40))
            {
                var zone = WorldClocks.Zone(city) ?? TimeZoneInfo.Utc;
                var local = TimeZoneInfo.ConvertTime(now, zone);
                double hours = (local.Offset - now.Offset).TotalHours;
                var time = Label(TimeText(local.DateTime), 14, bold: true);
                var offset = Label(hours == 0 ? Loc.T("本地", "Local") : hours.ToString("+0.##;−0.##", CultureInfo.CurrentCulture) + "h", 11.5, dim: true);
                time.TextAlignment = offset.TextAlignment = TextAlignment.Right;
                var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { time, offset } };
                var line = new DockPanel();
                DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
                line.Children.Add(right);
                line.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Label(city, 14, bold: true), Label(IanaZone(zoneId), 11.5, dim: true) } });
                var row = new Border { Padding = new Thickness(10, 9, 10, 9), CornerRadius = new CornerRadius(10), Child = line, Cursor = System.Windows.Input.Cursors.Hand,
                    Background = city == current ? UI.Brush(_look.CardHover) : Brushes.Transparent };
                row.MouseEnter += (_, _) => row.Background = UI.Brush(_look.CardHover);
                row.MouseLeave += (_, _) => row.Background = city == current ? UI.Brush(_look.CardHover) : Brushes.Transparent;
                row.MouseLeftButtonUp += (_, _) =>
                {
                    var settings = _app.DockSettings;
                    if (settings.Locked) return;
                    CloseWidgetDetails();
                    _app.UpdateDockSettings(settings.WithWidgets(settings.Widgets.Select(w => w.Id == widget.Id ? w with { Option = city } : w).ToList()));
                };
                list.Children.Add(row);
            }
            if (list.Children.Count == 0) list.Children.Add(Label(Loc.T("没有找到城市", "No city found"), 13, dim: true));
        }
        search.TextChanged += (_, _) => Show();
        Show();
        var body = new DockPanel { Height = 460 };
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        DockPanel.SetDock(box, System.Windows.Controls.Dock.Top);
        body.Children.Add(header); body.Children.Add(box);
        body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        ShowDockPanel(anchor, body, () => { }, 440, 16, CalendarRadius(14, 30), search);
    }

    // A Windows time zone as its IANA name (America/New_York), the form people search by; the Windows name when there is none.
    private static string IanaZone(string windowsId) => TimeZoneInfo.TryConvertWindowsIdToIanaId(windowsId, out var iana) ? iana : windowsId;

    private (FrameworkElement, Action) TimeWidget(DockWidget widget, double tile) => widget.Kind switch
    {
        DockWidgetKind.FlipClock => DigitClock(tile, segment: false),
        DockWidgetKind.SegmentClock => DigitClock(tile, segment: true),
        DockWidgetKind.WorldClocks => WorldFaces(widget, tile),
        DockWidgetKind.WorldTime => WorldTime(widget, tile),
        DockWidgetKind.SevenDays => WeekStrip(tile),
        DockWidgetKind.Today => TodayTile(tile),
        _ => DateAndTime(widget.Kind, tile)
    };

    private static StackPanel TimeStack(params UIElement[] content)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in content) panel.Children.Add(child);
        return panel;
    }

    private string TimeText(DateTime time, bool seconds = false)
    {
        var settings = _app.DockSettings;
        return DockGeometry.Clock(time, settings.Clock24Hour, seconds && settings.ClockSeconds, CultureInfo.CurrentCulture);
    }

    private string DockTemperature()
    {
        if (!_app.Settings.WeatherEnabled || _app.CurrentWeather is not { } weather) return "–";
        double temperature = _app.Settings.WeatherFahrenheit ? weather.Temperature * 1.8 + 32 : weather.Temperature;
        return $"{Math.Round(temperature, MidpointRounding.AwayFromZero):0}°";
    }

    private (FrameworkElement, Action) DateAndTime(DockWidgetKind kind, double tile)
    {
        bool dateOnly = kind is DockWidgetKind.Date or DockWidgetKind.DayNumber;
        bool compact = kind == DockWidgetKind.ClockWeather;
        var main = Label("", tile * (kind == DockWidgetKind.DayNumber ? 0.43 : compact ? 0.28 : 0.34), bold: true);
        var detail = Label("", tile * 0.19, bold: true, dim: true);
        var temp = Label("", tile * 0.22, bold: true);
        main.TextAlignment = detail.TextAlignment = temp.TextAlignment = TextAlignment.Center;
        detail.Foreground = UI.Brush(dateOnly ? Accent : _look.Dim);
        var content = TimeStack(dateOnly ? detail : main, dateOnly ? main : detail);
        FrameworkElement body = content;
        // Clock: the hours and minutes large and bold with the seconds small and grey after them, the date under, left-aligned.
        var seconds = new Run { FontSize = tile * 0.33, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(_look.Dim) };
        var hoursMinutes = new Run();
        if (kind == DockWidgetKind.Clock)
        {
            main.FontSize = tile * 0.5;
            main.Inlines.Add(hoursMinutes);
            main.Inlines.Add(seconds);
            detail.FontSize = tile * 0.19;
            detail.FontWeight = FontWeights.SemiBold;
            main.TextAlignment = detail.TextAlignment = TextAlignment.Left;
            main.Margin = new Thickness(0, -tile * 0.04, 0, -tile * 0.07);
            content.HorizontalAlignment = HorizontalAlignment.Left;
            content.Margin = new Thickness(tile * 0.14, 0, tile * 0.08, 0);
        }
        if (kind == DockWidgetKind.Date)
        {
            // Date, after lumabar's: the weekday small over the month and day, left-aligned; the weekday keeps the accent.
            main.TextTrimming = TextTrimming.None;
            main.FontSize = tile * 0.36;
            detail.FontSize = tile * 0.17;
            main.TextAlignment = detail.TextAlignment = TextAlignment.Left;
            content.HorizontalAlignment = HorizontalAlignment.Left;
            detail.Margin = new Thickness(0, 0, 0, -tile * 0.03);
            body = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(tile * 0.18, 0, tile * 0.12, 0), Child = content };
        }
        var day = Label("", tile * 0.16, bold: true);
        var dayNumber = Label("", tile * 0.29, bold: true);
        if (compact)
        {
            // A calendar leaf: the time in black on red across the top, the weekday over the day in white on black below left, and
            // the temperature in black on white below right, the card's own colour showing as thin gaps between them.
            content.Children.Clear();
            double r = Math.Max(0, TileRadius() - 1.5), gap = Math.Max(1.5, Math.Round(tile * 0.035));
            main.FontSize = tile * 0.27;
            main.Foreground = UI.Brush("#FF111113");
            main.VerticalAlignment = VerticalAlignment.Center;
            day.Foreground = dayNumber.Foreground = Brushes.White;
            temp.Foreground = UI.Brush("#FF111113");
            day.Opacity = 0.8;
            temp.FontSize = tile * 0.27;
            day.TextAlignment = dayNumber.TextAlignment = TextAlignment.Center;
            day.Margin = new Thickness(0, 0, 0, -tile * 0.04);
            dayNumber.Margin = new Thickness(0, -tile * 0.03, 0, 0);
            temp.VerticalAlignment = VerticalAlignment.Center;
            var leaf = new Grid { Margin = new Thickness(gap) };
            leaf.RowDefinitions.Add(new RowDefinition());
            leaf.RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
            leaf.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) });
            var top = new Border { Background = UI.Brush("#FFE5121B"), CornerRadius = new CornerRadius(r, r, 0, 0), Child = main };
            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            var date = new Border { Background = UI.Brush("#FF111113"), CornerRadius = new CornerRadius(0, 0, 0, r),
                Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { day, dayNumber } } };
            var weather = new Border { Background = UI.Brush("#FFF7F7F9"), CornerRadius = new CornerRadius(0, 0, r, 0), Child = temp };
            Grid.SetColumn(weather, 2);
            bottom.Children.Add(date); bottom.Children.Add(weather);
            Grid.SetRow(bottom, 2);
            leaf.Children.Add(top); leaf.Children.Add(bottom);
            body = leaf;
        }
        return (body, () =>
        {
            var now = DateTime.Now;
            bool shortDate = kind is DockWidgetKind.DayNumber or DockWidgetKind.ClockWeather;
            detail.Text = now.ToString(shortDate ? "ddd" : dateOnly ? "dddd" : "ddd, MMM d", CultureInfo.CurrentCulture);
            if (kind == DockWidgetKind.Clock)
            {
                hoursMinutes.Text = TimeText(now);
                seconds.Text = now.ToString(":ss", CultureInfo.InvariantCulture);
                detail.Text = now.ToString("ddd d MMM", CultureInfo.CurrentCulture);
                return;
            }
            main.Text = dateOnly ? now.ToString(shortDate ? "%d" : "MMMM d", CultureInfo.CurrentCulture) : TimeText(now, seconds: !compact);
            if (!compact) return;
            day.Text = now.ToString("ddd", CultureInfo.CurrentCulture);
            dayNumber.Text = now.ToString("%d", CultureInfo.CurrentCulture);
            temp.Text = DockTemperature();
        });
    }

    private (FrameworkElement, Action) TodayTile(double tile)
    {
        var weekday = Label("", tile * 0.21, bold: true);
        weekday.Foreground = UI.Brush(_look.Text == "#FF1D1D1F" ? "#B42318" : DockLook.Weekday);
        var date = Label("", tile * 0.30, bold: true);
        var clock = Label("", tile * 0.30, bold: true);
        clock.TextAlignment = TextAlignment.Right;
        var time = new Run();
        var period = new Run { FontSize = tile * 0.17 };
        clock.Inlines.Add(time);
        clock.Inlines.Add(period);
        var icon = new Border { Width = tile * 0.37, Height = tile * 0.37, Margin = new Thickness(0, 0, tile * 0.06, 0) };
        var temperature = Label("", tile * 0.23, bold: true, dim: true);
        temperature.VerticalAlignment = VerticalAlignment.Center;
        var weather = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        weather.Children.Add(icon);
        weather.Children.Add(temperature);
        var left = TimeStack(weekday, date);
        left.HorizontalAlignment = HorizontalAlignment.Left;
        var right = TimeStack(clock, weather);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        var row = new Grid { Margin = new Thickness(tile * 0.25, 0, tile * 0.25, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var divider = new Rectangle { Height = tile * 0.59, Fill = UI.Brush(_look.Divider) };
        Grid.SetColumn(divider, 1);
        row.Children.Add(left);
        row.Children.Add(divider);
        var fit = new Viewbox { StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform, Child = right };
        fit.HorizontalAlignment = HorizontalAlignment.Right;
        fit.Margin = new Thickness(tile * 0.1, 0, 0, 0);
        Grid.SetColumn(fit, 2);
        row.Children.Add(fit);
        string? shownIcon = null;
        return (row, () =>
        {
            var now = DateTime.Now;
            var culture = CultureInfo.CurrentCulture;
            weekday.Text = now.ToString("dddd", culture).ToUpper(culture);
            date.Text = now.ToString("MMM d", culture);
            string clockText = TimeText(now, seconds: true);
            var format = _app.DockSettings.Clock24Hour == false ? CultureInfo.InvariantCulture : culture;
            string designator = now.ToString("tt", format);
            bool hasSuffix = designator.Length > 0 && clockText.EndsWith(designator, StringComparison.Ordinal);
            time.Text = hasSuffix ? clockText[..^designator.Length].TrimEnd() : clockText;
            period.Text = hasSuffix ? " " + designator : "";
            temperature.Text = DockTemperature();
            string symbol = _app.Settings.WeatherEnabled && _app.CurrentWeather is { } reading
                ? MainWindow.WeatherCondition(reading.Code, reading.Daylight).Item1 : "";
            if (symbol == shownIcon) return;
            shownIcon = symbol;
            var art = MainWindow.ColourfulWeatherIcon(symbol, animate: false);
            icon.Child = art is null ? null : new Viewbox { IsHitTestVisible = false, Child = art };
            icon.Visibility = art is null ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    // World time, after lumabar's: a sun or moon (day or night there) before the city, the day and offset from here under it
    // ("Today, +2h30"), and the time large on the right.
    private (FrameworkElement, Action) WorldTime(DockWidget widget, double tile)
    {
        string city = TimeCities(widget)[0];
        var zone = WorldClocks.Zone(city) ?? TimeZoneInfo.Utc;
        var caption = Label(city, tile * 0.19, bold: true);
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.VerticalAlignment = VerticalAlignment.Center;
        var value = Label("", tile * 0.44, bold: true);
        value.VerticalAlignment = VerticalAlignment.Center;
        var offset = Label("", tile * 0.16, bold: true, dim: true);
        offset.Margin = new Thickness(tile * 0.24, 0, 0, 0);
        offset.TextWrapping = TextWrapping.Wrap;
        offset.TextAlignment = TextAlignment.Center;
        offset.HorizontalAlignment = HorizontalAlignment.Left; // "Tomorrow, +5h" breaks onto two lines when the card is narrow, as on lumabar
        double mark = Math.Round(tile * 0.16);
        var sky = new Border { Width = mark, Height = mark, Margin = new Thickness(0, 0, tile * 0.08, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new DockPanel { Children = { sky, caption } };
        var location = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, offset } };
        var row = new DockPanel { Margin = new Thickness(tile * 0.16, 0, tile * 0.14, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
        value.Margin = new Thickness(tile * 0.12, 0, 0, 0);
        row.Children.Add(value);
        row.Children.Add(location);
        bool? day = null;
        return (row, () =>
        {
            var now = DateTimeOffset.Now;
            var local = TimeZoneInfo.ConvertTime(now, zone);
            value.Text = TimeText(local.DateTime);
            var difference = local.Offset - now.Offset;
            int dayShift = (local.Date - now.Date).Days;
            string when = dayShift switch { 0 => Loc.T("今天", "Today"), > 0 => Loc.T("明天", "Tomorrow"), _ => Loc.T("昨天", "Yesterday") };
            offset.Text = $"{when}, {OffsetText(difference)}";
            bool isDay = local.Hour is >= 6 and < 18;
            if (day == isDay) return;
            day = isDay;
            sky.Child = isDay
                ? new System.Windows.Shapes.Ellipse { Fill = UI.Brush("#FFFFD60A") }
                : new Viewbox { Child = new System.Windows.Shapes.Path { Fill = UI.Brush("#FFB4A0FF"),
                    Data = Geometry.Parse("M12,3 A9,9 0 1 0 21,12 A7,7 0 1 1 12,3 Z") } };
        });
    }

    // An offset from here as lumabar writes it: Local, +2h, -7h or +5h30.
    private static string OffsetText(TimeSpan difference)
    {
        if (difference == TimeSpan.Zero) return Loc.T("本地", "Local");
        var size = difference.Duration();
        string sign = difference < TimeSpan.Zero ? "\u2212" : "+";
        return size.Minutes == 0 ? $"{sign}{(int)size.TotalHours}h" : $"{sign}{(int)size.TotalHours}h{size.Minutes:00}";
    }

    private (FrameworkElement, Action) WorldFaces(DockWidget widget, double tile)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var updates = new List<Action>();
        foreach (string city in TimeCities(widget))
        {
            var zone = WorldClocks.Zone(city) ?? TimeZoneInfo.Utc;
            var (face, update) = AnalogClock(tile * 0.85, () => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
            var caption = Label(city, tile * 0.16, bold: true);
            caption.TextAlignment = TextAlignment.Center;
            var stack = TimeStack(face, caption);
            stack.Width = tile * 0.94;
            row.Children.Add(stack);
            updates.Add(update);
        }
        return (row, () => { foreach (var update in updates) update(); });
    }

    // Seven days from today: the month top left, the weather's icon and temperature top right, then each day's short name over
    // its number, today on an accent chip.
    private (FrameworkElement, Action) WeekStrip(double tile)
    {
        var month = Label("", tile * 0.18, bold: true);
        var temperature = Label("", tile * 0.18, bold: true);
        temperature.VerticalAlignment = VerticalAlignment.Center;
        var (icon, setIcon) = StaticWeatherPicture(tile * 0.24);
        icon.Margin = new Thickness(0, 0, tile * 0.05, 0);
        var weather = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, temperature } };
        var header = new DockPanel { Margin = new Thickness(tile * 0.04, 0, tile * 0.04, tile * 0.03) };
        DockPanel.SetDock(weather, System.Windows.Controls.Dock.Right);
        header.Children.Add(weather);
        header.Children.Add(month);
        var days = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
        var cells = Enumerable.Range(0, 7).Select(i =>
        {
            var name = Label("", tile * 0.14, bold: true, dim: i != 0);
            var number = Label("", tile * 0.2, bold: true);
            name.TextAlignment = number.TextAlignment = TextAlignment.Center;
            if (i == 0) name.Foreground = number.Foreground = Brushes.White;
            days.Children.Add(new Border { Margin = new Thickness(tile * 0.02, 0, tile * 0.02, 0), Padding = new Thickness(0, tile * 0.03, 0, tile * 0.03),
                CornerRadius = new CornerRadius(Math.Max(4, TileRadius() - 3)), Background = i == 0 ? UI.Brush(Accent) : Brushes.Transparent, Child = TimeStack(name, number) });
            return (name, number);
        }).ToArray();
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(tile * 0.12, 0, tile * 0.12, 0), Children = { header, days } };
        return (body, () =>
        {
            var today = DateTime.Today;
            month.Text = today.ToString("MMMM", CultureInfo.CurrentCulture);
            temperature.Text = DockTemperature();
            var reading = _app.Settings.WeatherEnabled ? _app.CurrentWeather : null;
            setIcon(reading?.Code, reading?.Daylight ?? true);
            weather.Visibility = reading is null ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 0; i < cells.Length; i++)
            {
                var day = today.AddDays(i);
                cells[i].name.Text = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek);
                cells[i].number.Text = day.Day.ToString(CultureInfo.CurrentCulture);
            }
        });
    }

    private static readonly int[] DigitMasks = [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F];

    private (FrameworkElement, Action) DigitClock(double tile, bool segment)
    {
        var caption = Label("", tile * 0.17, dim: true);
        var suffix = Label("", tile * 0.17, bold: true, dim: true);
        suffix.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var digits = new Action<char>[4];
        for (int i = 0; i < digits.Length; i++)
        {
            var (digit, set) = segment ? SegmentDigit(tile) : FlipDigit(tile);
            // Flip: the hours and the minutes as two pairs with a gap between.
            if (!segment && i == 2) digit.Margin = new Thickness(Math.Round(tile * 0.14), 0, 1, 0);
            if (segment && i == 2) digit.Margin = new Thickness(Math.Round(tile * 0.1), 0, Math.Round(tile * 0.03), 0);
            row.Children.Add(digit);
            digits[i] = set;
        }
        row.Children.Add(suffix);
        // Segment clock, after lumabar's: the weekday in a red outlined pill and the day and month beside it, over the digits.
        var weekday = Label("", tile * 0.12, bold: true);
        weekday.Foreground = UI.Brush("#FFFF3B30");
        var pill = new Border { BorderBrush = UI.Brush("#FFFF3B30"), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(tile * 0.08),
            Padding = new Thickness(tile * 0.05, 0, tile * 0.05, 0), VerticalAlignment = VerticalAlignment.Center, Child = weekday };
        caption.FontSize = tile * 0.16;
        caption.FontWeight = FontWeights.Bold;
        caption.Foreground = UI.Brush(_look.Text);
        caption.Margin = new Thickness(tile * 0.07, 0, 0, 0);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, tile * 0.04), Children = { pill, caption } };
        FrameworkElement face = TimeStack(row);
        if (segment)
        {
            row.HorizontalAlignment = HorizontalAlignment.Left;
            face = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(tile * 0.16, 0, tile * 0.1, 0), Children = { header, row } };
        }
        return (face, () =>
        {
            var now = DateTime.Now;
            var format = CultureInfo.CurrentCulture.DateTimeFormat;
            bool clock24 = _app.DockSettings.Clock24Hour ?? format.ShortTimePattern.Contains('H');
            string text = now.ToString(clock24 ? "HHmm" : "hhmm", CultureInfo.InvariantCulture);
            for (int i = 0; i < digits.Length; i++) digits[i](text[i]);
            var designators = _app.DockSettings.Clock24Hour == false ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
            suffix.Text = clock24 ? "" : " " + now.ToString("tt", designators);
            if (!segment) return;
            weekday.Text = now.ToString("ddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
            caption.Text = now.ToString("d MMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
        });
    }

    // A split-flap card, after lumabar's: a white digit on a dark card cut across the middle. On a change the top flap, still
    // showing the old digit, falls about the seam onto the bottom half while the new digit is revealed behind it; then the new
    // digit's bottom flap lands, covering the old one's bottom.
    private (FrameworkElement, Action<char>) FlipDigit(double tile)
    {
        double width = Math.Round(tile * 0.46), height = Math.Round(tile * 0.68), radius = Math.Max(3, tile * 0.08);
        var topShade = new LinearGradientBrush(Color.FromRgb(0x40, 0x3E, 0x44), Color.FromRgb(0x33, 0x31, 0x37), 90);
        var bottomShade = new LinearGradientBrush(Color.FromRgb(0x2E, 0x2C, 0x32), Color.FromRgb(0x23, 0x22, 0x26), 90);
        topShade.Freeze(); bottomShade.Freeze();
        // One half of the card: the whole digit laid out at full height, clipped to the top or bottom half.
        (Border Half, TextBlock Text) Half(bool top)
        {
            var text = Label("", tile * 0.42, bold: true);
            text.Foreground = Brushes.White;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            var whole = new Grid { Height = height, Width = width, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom, Children = { text } };
            var half = new Border { Height = Math.Floor(height / 2), Width = width, ClipToBounds = true, Child = whole,
                VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom, Background = top ? topShade : bottomShade,
                CornerRadius = top ? new CornerRadius(radius, radius, 0, 0) : new CornerRadius(0, 0, radius, radius) };
            return (half, text);
        }
        var (top, topText) = Half(true);
        var (bottom, bottomText) = Half(false);
        var (flapTop, flapTopText) = Half(true);
        var (flapBottom, flapBottomText) = Half(false);
        var fall = new ScaleTransform(1, 1);
        var land = new ScaleTransform(1, 0);
        flapTop.RenderTransformOrigin = new Point(0.5, 1); flapTop.RenderTransform = fall;
        flapBottom.RenderTransformOrigin = new Point(0.5, 0); flapBottom.RenderTransform = land;
        flapTop.Visibility = flapBottom.Visibility = Visibility.Hidden;
        var seam = new Rectangle { Height = Math.Max(1, Math.Round(tile * 0.025)), Fill = UI.Brush("#FF141416"), VerticalAlignment = VerticalAlignment.Center };
        var card = new Grid { Width = width, Height = height, Margin = new Thickness(1, 0, 1, 0), Children = { top, bottom, flapTop, flapBottom, seam } };
        string shown = "";
        return (card, digit =>
        {
            string next = digit.ToString();
            if (shown == next) return;
            string previous = shown;
            shown = next;
            if (previous.Length == 0 || SystemParameters.ClientAreaAnimation != true)
            {
                topText.Text = bottomText.Text = next;
                flapTop.Visibility = flapBottom.Visibility = Visibility.Hidden;
                return;
            }
            topText.Text = next;
            bottomText.Text = previous;
            flapTopText.Text = previous;
            flapBottomText.Text = next;
            flapTop.Visibility = flapBottom.Visibility = Visibility.Visible;
            land.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            land.ScaleY = 0;
            var down = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            down.Completed += (_, _) =>
            {
                if (shown != next) return;
                flapTop.Visibility = Visibility.Hidden;
                var settle = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } };
                settle.Completed += (_, _) =>
                {
                    if (shown != next) return;
                    bottomText.Text = next;
                    flapBottom.Visibility = Visibility.Hidden;
                };
                land.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
            };
            fall.BeginAnimation(ScaleTransform.ScaleYProperty, down);
        });
    }

    // An LCD digit: seven thin, wide segments, the unlit ones faint, as on lumabar's segment clock.
    private (FrameworkElement, Action<char>) SegmentDigit(double tile)
    {
        var canvas = new Canvas { Width = 26, Height = 30 };
        var segments = new[] { (3.0, 0.0, 20.0, 2.6), (23.4, 2.6, 2.6, 11.4), (23.4, 16.0, 2.6, 11.4), (3.0, 27.4, 20.0, 2.6),
            (0.0, 16.0, 2.6, 11.4), (0.0, 2.6, 2.6, 11.4), (3.0, 13.7, 20.0, 2.6) }.Select(segment =>
        {
            var (x, y, width, height) = segment;
            var shape = new Rectangle { Width = width, Height = height, RadiusX = 1.2, RadiusY = 1.2, Fill = UI.Brush(_look.Text) };
            Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); canvas.Children.Add(shape);
            return shape;
        }).ToArray();
        var view = new Viewbox { Width = tile * 0.5, Height = tile * 0.52, Margin = new Thickness(Math.Round(tile * 0.03), 0, Math.Round(tile * 0.03), 0), Child = canvas };
        return (view, digit =>
        {
            int mask = DigitMasks[digit - '0'];
            for (int i = 0; i < segments.Length; i++) segments[i].Opacity = (mask & (1 << i)) == 0 ? 0.08 : 1;
        });
    }
}
