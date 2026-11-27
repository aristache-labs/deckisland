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
        foreach (var placement in DockWidgets.Placements(widget.Kind))
        {
            string name = placement switch
            {
                DockWidgetPlacement.Date => Loc.T("替换日期", "Replace date"),
                DockWidgetPlacement.Clock => Loc.T("替换时钟", "Replace clock"),
                DockWidgetPlacement.DateAndClock => Loc.T("替换日期和时钟", "Replace date and clock"),
                DockWidgetPlacement.NextToClock => Loc.T("放在时钟旁边", "Next to clock"),
                DockWidgetPlacement.NextToDesktop => Loc.T("放在显示桌面旁边", "Next to Show desktop"),
                _ => widget.Kind == DockWidgetKind.Tray ? Loc.T("默认位置", "Automatic placement") : Loc.T("放在小组件栏", "Place among widgets")
            };
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = widget.Placement == placement, IsEnabled = !settings.Locked };
            item.Click += (_, _) =>
            {
                var current = _app.DockSettings;
                if (!current.Locked) _app.UpdateDockSettings(current.WithWidgets(DockWidgets.Place(current.Widgets, widget.Id, placement)));
            };
            menu.Items.Add(item);
        }
        if (widget.Kind is not (DockWidgetKind.WorldTime or DockWidgetKind.WorldClocks)) return menu;
        menu.Items.Add(new Separator());
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
        if (kind == DockWidgetKind.Date)
        {
            main.TextTrimming = TextTrimming.None;
            body = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                Margin = new Thickness(tile * 0.1, 0, tile * 0.1, 0), Child = content };
        }
        if (compact)
        {
            content.Children.Clear();
            var cells = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            cells.Children.Add(detail);
            cells.Children.Add(new Border { Background = UI.Brush(_look.CardHover), CornerRadius = new CornerRadius(3), Child = temp });
            content.Children.Add(new Border { Background = UI.Brush(_look.CardHover), CornerRadius = new CornerRadius(4), Child = main });
            content.Children.Add(cells);
            body = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(3), Child = content };
        }
        return (body, () =>
        {
            var now = DateTime.Now;
            bool shortDate = kind is DockWidgetKind.DayNumber or DockWidgetKind.ClockWeather;
            detail.Text = now.ToString(shortDate ? "ddd" : dateOnly ? "dddd" : "ddd, MMM d", CultureInfo.CurrentCulture);
            if (compact) detail.Text = now.ToString("ddd d", CultureInfo.CurrentCulture);
            main.Text = dateOnly ? now.ToString(shortDate ? "%d" : "MMMM d", CultureInfo.CurrentCulture) : TimeText(now, seconds: !compact);
            if (compact) temp.Text = DockTemperature();
        });
    }

    private (FrameworkElement, Action) TodayTile(double tile)
    {
        var weekday = Label("", tile * 0.16, bold: true);
        weekday.Foreground = UI.Brush(_look.Text == "#FF1D1D1F" ? "#B42318" : DockLook.Weekday);
        var date = Label("", tile * 0.29, bold: true);
        var clock = Label("", tile * 0.29, bold: true);
        clock.TextAlignment = TextAlignment.Right;
        var time = new Run();
        var period = new Run { FontSize = tile * 0.17 };
        clock.Inlines.Add(time);
        clock.Inlines.Add(period);
        var icon = new Border { Width = tile * 0.28, Height = tile * 0.28, Margin = new Thickness(0, 0, 3, 0) };
        var temperature = Label("", tile * 0.17, bold: true, dim: true);
        temperature.VerticalAlignment = VerticalAlignment.Center;
        var weather = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        weather.Children.Add(icon);
        weather.Children.Add(temperature);
        var left = TimeStack(weekday, date);
        left.HorizontalAlignment = HorizontalAlignment.Left;
        var right = TimeStack(clock, weather);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        var row = new Grid { Margin = new Thickness(tile * 0.22, 0, tile * 0.22, 0) };
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

    private (FrameworkElement, Action) WorldTime(DockWidget widget, double tile)
    {
        string city = TimeCities(widget)[0];
        var zone = WorldClocks.Zone(city) ?? TimeZoneInfo.Utc;
        var caption = Label(city, tile * 0.2, bold: true);
        var value = Label("", tile * 0.31, bold: true);
        var offset = Label("", tile * 0.17, dim: true);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var location = TimeStack(caption, offset);
        location.Margin = new Thickness(0, 0, tile * 0.2, 0);
        row.Children.Add(location); row.Children.Add(value);
        value.VerticalAlignment = VerticalAlignment.Center;
        return (row, () =>
        {
            var now = DateTimeOffset.Now;
            var local = TimeZoneInfo.ConvertTime(now, zone);
            value.Text = TimeText(local.DateTime);
            double hours = (local.Offset - now.Offset).TotalHours;
            offset.Text = hours.ToString("+0.##;−0.##;0", CultureInfo.CurrentCulture) + " h";
        });
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

    private (FrameworkElement, Action) WeekStrip(double tile)
    {
        var header = Label("", tile * 0.19, bold: true);
        var days = new StackPanel { Orientation = Orientation.Horizontal };
        var cells = Enumerable.Range(0, 7).Select(i =>
        {
            var name = Label("", tile * 0.16, dim: true);
            var number = Label("", tile * 0.2, bold: true);
            name.TextAlignment = number.TextAlignment = TextAlignment.Center;
            days.Children.Add(new Border { Width = tile * 0.53, CornerRadius = new CornerRadius(4), Background = i == 0 ? UI.Brush(_look.CardHover) : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, i == 0 ? 1.5 : 0), BorderBrush = UI.Brush(Accent), Child = TimeStack(name, number) });
            return (name, number);
        }).ToArray();
        return (TimeStack(header, days), () =>
        {
            var now = DateTime.Today;
            header.Text = $"{now:MMMM}  ·  {DockTemperature()}";
            for (int i = 0; i < cells.Length; i++)
            {
                var day = now.AddDays(i);
                cells[i].name.Text = CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(day.DayOfWeek);
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
            if (i == 2)
            {
                var colon = Label(":", tile * 0.34, bold: true, dim: true);
                colon.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(colon);
            }
            var (digit, set) = segment ? SegmentDigit(tile) : FlipDigit(tile);
            row.Children.Add(digit);
            digits[i] = set;
        }
        row.Children.Add(suffix);
        return (segment ? TimeStack(caption, row) : TimeStack(row), () =>
        {
            var now = DateTime.Now;
            var format = CultureInfo.CurrentCulture.DateTimeFormat;
            bool clock24 = _app.DockSettings.Clock24Hour ?? format.ShortTimePattern.Contains('H');
            string text = now.ToString(clock24 ? "HHmm" : "hhmm", CultureInfo.InvariantCulture);
            for (int i = 0; i < digits.Length; i++) digits[i](text[i]);
            var designators = _app.DockSettings.Clock24Hour == false ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
            suffix.Text = clock24 ? "" : " " + now.ToString("tt", designators);
            if (segment) caption.Text = now.ToString("ddd · MMM d", CultureInfo.CurrentCulture);
        });
    }

    private (FrameworkElement, Action<char>) FlipDigit(double tile)
    {
        var label = Label("", tile * 0.56, bold: true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        var panel = new Grid { RenderTransformOrigin = new Point(0.5, 0.5) };
        var flip = new ScaleTransform();
        panel.RenderTransform = flip;
        panel.Children.Add(label);
        panel.Children.Add(new Rectangle { Height = 1, Fill = UI.Brush(_look.Divider), VerticalAlignment = VerticalAlignment.Center });
        var card = new Border { Width = tile * 0.47, Height = tile * 0.74, Margin = new Thickness(1, 0, 1, 0),
            Background = UI.Brush(_look.CardHover), CornerRadius = new CornerRadius(4), Child = panel };
        return (card, digit =>
        {
            string next = digit.ToString();
            if (label.Text == next) return;
            bool animate = label.Text.Length > 0 && SystemParameters.ClientAreaAnimation == true;
            label.Text = next;
            if (animate) flip.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(165)) { EasingFunction = new QuadraticEase() });
        });
    }

    private (FrameworkElement, Action<char>) SegmentDigit(double tile)
    {
        var canvas = new Canvas { Width = 20, Height = 34 };
        var segments = new[] { (4, 0, 12, 3), (17, 4, 3, 11), (17, 19, 3, 11), (4, 31, 12, 3),
            (0, 19, 3, 11), (0, 4, 3, 11), (4, 15, 12, 4) }.Select(segment =>
        {
            var (x, y, width, height) = segment;
            var shape = new Rectangle { Width = width, Height = height, RadiusX = 1, RadiusY = 1, Fill = UI.Brush(_look.Text) };
            Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); canvas.Children.Add(shape);
            return shape;
        }).ToArray();
        var view = new Viewbox { Width = tile * 0.41, Height = tile * 0.58, Margin = new Thickness(1, 0, 1, 0), Child = canvas };
        return (view, digit =>
        {
            int mask = DigitMasks[digit - '0'];
            for (int i = 0; i < segments.Length; i++) segments[i].Opacity = (mask & (1 << i)) == 0 ? 0.12 : 1;
        });
    }
}
