using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private const string RainIcon = "rain";
    private const string WaterIcon = "water";
    private const string PartlyCloudyNight = "partly-cloudy-night";
    // How much smaller the clear-night moon is drawn in the collapsed row (CompactWeather).
    private const double MoonShrink = .8;
    private const string RainCloud = "M 5.5,15 A 3.5,3.5 0 0 1 5,8 A 5,5 0 0 1 14.6,7 A 4.1,4.1 0 0 1 18,15";
    private static readonly string[] RainDrops = ["M 8,15 L 6.5,19", "M 12.5,16 L 11,20", "M 17,15 L 15.5,19"];
    private (WeatherReading? Reading, bool Loading, bool Setup, bool Stale, bool Fahrenheit, bool Colourful,
        bool Motion, int Theme, string Language)? _weatherView;

    private void InitializeWeather()
    {
        _app.WeatherChanged += RenderWeatherChange;
        WeatherContent.IsVisibleChanged += (_, e) => { if (!(bool)e.NewValue) _weatherView = null; };
        Closed += (_, _) =>
        {
            _app.WeatherChanged -= RenderWeatherChange;
            _app.PresentWeather(this, false);
        };
    }

    private void RenderWeatherChange()
    {
        if (_expanded && CurrentPage == DeckPage.Weather) RenderWeather();
        else if (!_expanded && Settings.ShowsWeather(EffectiveStyle)) RenderCompact();
    }

    private void WeatherPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Weather);

    private string WeatherTemperature(double? value, bool unit = false)
    {
        if (value is not { } temperature) return "—";
        bool fahrenheit = Settings.WeatherFahrenheit;
        return $"{(fahrenheit ? temperature * 1.8 + 32 : temperature):0}°{(unit ? fahrenheit ? "F" : "C" : "")}";
    }

    private static string WeatherPlace(WeatherReading reading) => reading.Place.Name.Length == 0
        ? Loc.T("当前位置", "Current location") : reading.Place.Name;

    private static (string Icon, string Condition) WeatherCondition(int? code, bool daylight) => code switch
    {
        0 => daylight ? ("☀", Loc.T("晴朗", "Sunny")) : ("☾", Loc.T("晴朗", "Clear")),
        1 or 2 => (daylight ? "⛅" : PartlyCloudyNight, Loc.T("局部多云", "Partly cloudy")),
        3 => ("☁", Loc.T("阴天", "Overcast")),
        45 or 48 => ("≋", Loc.T("雾", "Fog")),
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => (RainIcon, Loc.T("雨", "Rain")),
        71 or 73 or 75 or 77 or 85 or 86 => ("❄", Loc.T("雪", "Snow")),
        95 or 96 or 97 or 99 => ("ϟ", Loc.T("雷暴", "Thunderstorm")),
        _ => ("–", Loc.T("未知天气", "Unknown conditions"))
    };

    private TextBlock WeatherText(string text, double size) => new()
    {
        Text = text, FontSize = size, Foreground = UI.Brush(Look.Primary),
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    // vertical: on a side edge the temperature stands under the icon.
    private FrameworkElement? CompactWeather(double scale, bool vertical = false)
    {
        if (!Settings.ShowsWeather(EffectiveStyle) || _app.CurrentWeather is not { } reading) return null;
        var (icon, condition) = WeatherCondition(reading.Code, reading.Daylight);
        string temperature = WeatherTemperature(reading.Temperature);
        var text = WeatherText($"{temperature}{(_app.WeatherStale ? "*" : "")}",
            CompactMusicLayout.CollapsedTextSize(Settings.QuotaRingStyle, scale, 11));
        bool nested = Settings.QuotaRingStyle == QuotaRingStyle.Nested && icon != "–";
        double statusSize = CompactMusicLayout.StatusRingSize(Settings.QuotaRingStyle, scale);
        double iconHeight = nested ? CompactMusicLayout.Whole(statusSize * .9) : 14 * scale;
        // The crescent fills its whole box where the other marks leave air around them (the sun's rays, a cloud's curves), so at
        // the same height it read larger than the rings and text beside it.
        if (icon == "☾") iconHeight = CompactMusicLayout.Whole(iconHeight * MoonShrink);
        var mark = WeatherLineIcon(icon, iconHeight, text.Foreground, matchHeight: nested);
        if (nested && mark is Path path && path.Data.Bounds is { Height: > 0 } bounds)
            path.Width = Math.Min(statusSize, path.StrokeThickness + (iconHeight - path.StrokeThickness) * bounds.Width / bounds.Height);
        mark.Margin = vertical ? new Thickness(0, 0, 0, 2 * scale) : new Thickness(0, 0, (Settings.QuotaRingStyle == QuotaRingStyle.Nested ? 5 : 3) * scale, 0);
        var content = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Children = { mark, text } };
        if (vertical) { mark.HorizontalAlignment = text.HorizontalAlignment = HorizontalAlignment.Center; }
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Width = Math.Ceiling(content.DesiredSize.Width + 2 * scale);
        content.Height = Math.Ceiling(content.DesiredSize.Height);
        string label = $"{condition} · {WeatherTemperature(reading.Temperature, true)} · {WeatherPlace(reading)}";
        if (_app.WeatherStale) label += $" · {Loc.T("数据未更新", "Weather data is stale")}";
        AutomationProperties.SetName(text, label);
        return content;
    }

    private void RenderWeather()
    {
        RefreshStatus();
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility =
            ShelfContent.Visibility = DevicesContent.Visibility = Visibility.Collapsed;
        WeatherContent.Visibility = Visibility.Visible;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        double height = ContentCanvas.Height;
        if (!double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        WeatherContent.Margin = new Thickness(_header.Side, 0, _header.Side, MonitorBottom(height));

        bool colourful = Settings.ColourfulWeatherCard && !Look.HighContrast;
        var view = (_app.CurrentWeather, _app.WeatherLoading, _app.WeatherNeedsLocation, _app.WeatherStale,
            Settings.WeatherFahrenheit, colourful, Animates, Look.Generation, Loc.Language);
        if (_weatherView == view && WeatherContent.Children.Count > 0) return;
        _weatherView = view;
        var rows = new StackPanel();
        Grid.SetIsSharedSizeScope(rows, true);
        WeatherContent.Children.Clear();
        WeatherContent.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (_app.CurrentWeather is not { } reading)
        {
            rows.Children.Add(WeatherText(_app.WeatherLoading ? Loc.T("正在加载天气…", "Loading weather…")
                : _app.WeatherNeedsLocation ? Loc.T("请选择城市。", "Choose a city to see the weather.")
                : Loc.T("天气暂不可用。", "Weather is currently unavailable."), 13));
            var setup = new Button { Style = (Style)FindResource("PanelOutlineButton"), Content = Loc.T("天气设置", "Weather settings"),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            setup.Click += (_, _) => _app.OpenSettingsPage(SettingsWindow.WeatherSettingsPage);
            rows.Children.Add(setup);
            return;
        }

        var (icon, condition) = WeatherCondition(reading.Code, reading.Daylight);
        var (background, foreground) = WeatherCardColours(colourful ? icon : null);
        var overview = new Grid();
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35, GridUnitType.Star) });
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65, GridUnitType.Star) });
        rows.Children.Add(overview);

        var hero = new DockPanel();
        var facts = new StackPanel();
        DockPanel.SetDock(facts, System.Windows.Controls.Dock.Bottom);
        hero.Children.Add(facts);
        var now = new StackPanel();
        hero.Children.Add(now);
        overview.Children.Add(new Border { Child = hero, Background = colourful ? background : UI.Brush(Look.Hex("fill")), Padding = new Thickness(10),
            CornerRadius = new CornerRadius(Corner(12)), Margin = new Thickness(0, 0, 7, 0) });

        TextBlock Current(string text, double size, bool bold = false)
        {
            var label = WeatherText(text, size);
            label.Foreground = foreground;
            if (bold) label.FontWeight = FontWeights.SemiBold;
            return label;
        }

        var place = Current(WeatherPlace(reading), 12, true);
        var location = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = WeatherPlace(reading) };
        var pin = new Path { Data = Geometry.Parse("M8,14 C5,10 3,8 3,5 A5,5 0 0 1 13,5 C13,8 11,10 8,14 Z M10,5 A2,2 0 1 1 6,5 A2,2 0 1 1 10,5"),
            Stroke = foreground, StrokeThickness = 1.3, Width = 10, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 4, 0) };
        location.Children.Add(pin);
        location.Children.Add(place);
        now.Children.Add(location);

        var currentTemperature = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        currentTemperature.Children.Add(ForecastIcon(icon, 44, foreground, animate: Animates));
        var currentDegrees = Current(WeatherTemperature(reading.Temperature), 32, true);
        currentDegrees.Margin = new Thickness(7, 0, 0, 0);
        currentTemperature.Children.Add(currentDegrees);
        now.Children.Add(currentTemperature);
        now.Children.Add(Current(condition, 13, true));
        var today = reading.Days.FirstOrDefault();
        var feels = Current($"{Loc.T("体感", "Feels like")} {WeatherTemperature(reading.FeelsLike)} · {WeatherTemperature(today?.Low)}–{WeatherTemperature(today?.High)}", 11);
        feels.TextWrapping = TextWrapping.Wrap;
        now.Children.Add(feels);
        if (_app.WeatherStale) now.Children.Add(Current(Loc.T("数据未更新", "Weather data is stale"), 11));
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        facts.Children.Add(new Viewbox { Child = chips, StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 3) });
        void Chip(string symbol, string label, string value, double? degrees = null)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Current(symbol, 10));
            content.Children.Add(Current(" " + value, 10, true));
            if (degrees is { } direction)
            {
                var arrow = Current("↑", 10);
                arrow.RenderTransformOrigin = new Point(.5, .5);
                arrow.RenderTransform = new RotateTransform(direction + 180);
                arrow.Margin = new Thickness(2, 0, 0, 0);
                content.Children.Add(arrow);
            }
            AutomationProperties.SetName(content, $"{label} {value}");
            chips.Children.Add(new Border { Child = content, Background = UI.Brush(Look.Hex("fillSelected")), Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 3, 3), CornerRadius = new CornerRadius(Corner(5)), ToolTip = label });
        }
        Chip("≋", Loc.T("风速", "Wind speed"), reading.WindSpeed is { } wind ? $"{wind:0} km/h" : "—", reading.WindDirection);
        Chip("◊", Loc.T("湿度", "Humidity"), reading.Humidity is { } humidity ? $"{humidity:0}%" : "—");
        Chip("+", Loc.T("今日总降水量", "Today's total precipitation"), today?.PrecipitationMillimetres is { } rain ? $"{rain:0.##} mm" : "—");
        var sun = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (today?.Sunrise is { } rise && rise != today.Sunset) AddSun("↑", Loc.T("日出", "Sunrise"), rise);
        if (today?.Sunset is { } set && set != today.Sunrise) AddSun("↓", Loc.T("日落", "Sunset"), set);
        facts.Children.Add(sun);
        void AddSun(string marker, string label, DateTime time)
        {
            var icon = new Path
            {
                Data = Geometry.Parse("M1,10 H15 M3,13 H13 M4,10 A4,4 0 0 1 12,10 M2,7 L3,8 M14,7 L13,8 " +
                    (marker == "↑" ? "M8,0 V4 M6,2 L8,0 L10,2" : "M8,0 V4 M6,2 L8,4 L10,2")),
                Stroke = foreground, StrokeThickness = 1.2, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform, Width = 13, Height = 12, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 0),
                ToolTip = label, Children = { icon, Current($"{time:HH:mm}", 11) } };
            AutomationProperties.SetName(item, $"{label} {time:HH:mm}");
            sun.Children.Add(item);
        }

        var forecast = new StackPanel();
        Grid.SetColumn(forecast, 1);
        overview.Children.Add(forecast);
        var hourly = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        var secondary = UI.Brush(Look.Secondary);
        var primary = UI.Brush(Look.Primary);
        void Hour(string label, string mark, string summary, double? degrees, double? chance)
        {
            var heading = WeatherText(label, 11);
            heading.Foreground = secondary;
            var temperature = WeatherText(WeatherTemperature(degrees), 11);
            temperature.FontWeight = FontWeights.SemiBold;
            heading.HorizontalAlignment = temperature.HorizontalAlignment = HorizontalAlignment.Center;
            var tile = new StackPanel { Background = Brushes.Transparent, Children = { heading, ForecastIcon(mark, 22, primary), temperature } };
            string help = chance is { } percent
                ? $"{summary} · {Loc.T("降水概率", "Chance of precipitation")} {percent:0}%"
                : summary;
            tile.ToolTip = help;
            AutomationProperties.SetName(tile, $"{label}, {temperature.Text}, {help}");
            hourly.Children.Add(tile);
        }
        Hour(Loc.T("现在", "Now"), icon, condition, reading.Temperature, reading.Hours.FirstOrDefault()?.Chance);
        foreach (var hour in reading.Hours.Skip(1))
        {
            var (mark, summary) = WeatherCondition(hour.Code, hour.Daylight);
            Hour(hour.Time.ToString("HH"), mark, summary, hour.Temperature, hour.Chance);
        }
        forecast.Children.Add(ForecastCard(hourly, new Thickness(4, 5, 4, 5)));

        var daily = new StackPanel();
        var validRanges = reading.Days.Where(day => day.Low is { } low && day.High is { } high && high >= low).ToArray();
        double min = validRanges.Length == 0 ? 0 : validRanges.Min(day => day.Low!.Value);
        double max = validRanges.Length == 0 ? 1 : validRanges.Max(day => day.High!.Value);
        foreach (var day in reading.Days)
        {
            var (mark, summary) = WeatherCondition(day.Code, true);
            var row = new Grid { Height = 22, Background = Brushes.Transparent };
            foreach (double width in new[] { 48d, 21, 30, -1, 25, 45 })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            void Add(int column, FrameworkElement element)
            {
                Grid.SetColumn(element, column);
                row.Children.Add(element);
            }
            bool isToday = day == today;
            var label = WeatherText(isToday ? Loc.T("今天", "Today") : day.Date.ToString("ddd", Loc.Culture), 12);
            label.Foreground = isToday ? primary : secondary;
            label.FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal;
            Add(0, label);
            Add(1, ForecastIcon(mark, 21, primary));
            var lowText = WeatherText(WeatherTemperature(day.Low), 12);
            lowText.HorizontalAlignment = HorizontalAlignment.Right;
            lowText.Foreground = secondary;
            Add(2, lowText);
            var track = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            track.Children.Add(new Border { Background = UI.Brush(Look.Hex("fillSelected")), CornerRadius = new CornerRadius(3) });
            if (day.Low is { } low && day.High is { } high && high >= low)
            {
                var span = new Grid();
                foreach (double length in new[] { low - min, Math.Max(.15, high - low), max - high })
                    span.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(length, GridUnitType.Star) });
                Brush tint = Look.HighContrast ? primary : new LinearGradientBrush(Color.FromRgb(45, 190, 155), Color.FromRgb(128, 209, 75), 0);
                var bar = new Border { Background = tint, CornerRadius = new CornerRadius(3) };
                Grid.SetColumn(bar, 1);
                span.Children.Add(bar);
                track.Children.Add(span);
            }
            Add(3, track);
            var highText = WeatherText(WeatherTemperature(day.High), 12);
            highText.FontWeight = FontWeights.SemiBold;
            Add(4, highText);
            if (day.PrecipitationMillimetres is > 0 and { } amount)
            {
                var total = WeatherText($"{amount:0.#} mm", 10);
                total.Foreground = Look.HighContrast ? primary : UI.Brush(Look.Hex("accent"));
                total.HorizontalAlignment = HorizontalAlignment.Right;
                Add(5, total);
            }
            string details = $"{day.Date.ToString("D", Loc.Culture)} · {summary} · {WeatherExtremes(day)}";
            if (day.PrecipitationProbability is { } probability)
                details += $" · {Loc.T("最高降水概率", "Peak precipitation chance")} {probability:0}%";
            row.ToolTip = details;
            AutomationProperties.SetName(row, details);
            daily.Children.Add(row);
        }
        var dailyCard = ForecastCard(daily, new Thickness(8, 4, 8, 4));
        dailyCard.Margin = new Thickness(0, 6, 0, 0);
        forecast.Children.Add(dailyCard);
    }

    private Border ForecastCard(FrameworkElement body, Thickness padding) => new()
    {
        Child = body, Padding = padding, Background = UI.Brush(Look.Hex("fill")), CornerRadius = new CornerRadius(Corner(10))
    };

    private FrameworkElement ForecastIcon(string icon, double size, Brush ink, bool animate = false)
    {
        var picture = Look.HighContrast ? null : ColourfulWeatherIcon(icon, animate);
        return new Viewbox { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Child = picture ?? WeatherLineIcon(icon, size, ink) };
    }

    private static FrameworkElement WeatherLineIcon(string icon, double size, Brush ink, bool matchHeight = false)
    {
        if (icon == RainIcon && !matchHeight) return WeatherRainIcon(size, ink);
        const string cloud = "M 5,19 A 3.5,3.5 0 0 1 5,12 A 5,5 0 0 1 14.8,11 A 4,4 0 1 1 17,19 Z";
        return new Path
        {
            Data = Geometry.Parse(icon switch
            {
                "☀" => "M 12,7 A 5,5 0 1 0 12,17 A 5,5 0 1 0 12,7 M 12,2 V 4 M 12,20 V 22 M 2,12 H 4 M 20,12 H 22 M 5,5 L 6.4,6.4 M 17.6,17.6 L 19,19 M 19,5 L 17.6,6.4 M 6.4,17.6 L 5,19",
                "☾" => "M 19.5,15 A 8,8 0 0 1 9,4.5 A 8.4,8.4 0 1 0 19.5,15 Z",
                "⛅" => cloud + " M 8,8 A 3,3 0 0 1 13,6 M 10,2 V 3 M 4.5,4.5 L 5.5,5.5 M 15.5,4.5 L 14.5,5.5",
                PartlyCloudyNight => cloud + " M 7,8 A 5,5 0 0 1 11,2 A 4,4 0 0 0 15,7",
                "☁" => cloud,
                RainIcon => RainCloud + " " + string.Join(" ", RainDrops),
                "≋" => "M 4,7 H 18 M 7,12 H 21 M 3,17 H 17",
                "❄" => "M 12,3 V 21 M 4.2,7.5 L 19.8,16.5 M 4.2,16.5 L 19.8,7.5 M 9,5 L 12,8 L 15,5 M 9,19 L 12,16 L 15,19",
                WaterIcon => "M 12,2 C 10,6 5,10 5,15 A 7,7 0 0 0 19,15 C 19,10 14,6 12,2 Z",
                "ϟ" => "M 13,2 L 5,14 H 11 L 10,22 L 19,10 H 13 Z",
                _ => "M 7,12 H 17"
            }),
            Width = matchHeight ? size * 1.25 : size, Height = size, Stretch = Stretch.Uniform, Stroke = ink, StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
    }

    private static Viewbox WeatherRainIcon(double size, Brush ink)
    {
        Path Stroke(string data) => new()
        {
            Data = Geometry.Parse(data), Stroke = ink, StrokeThickness = 1.65,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        var drawing = new Canvas { Width = 24, Height = 27, Children = { Stroke(RainCloud) } };
        foreach (var drop in RainDrops) drawing.Children.Add(Stroke(drop));
        return new Viewbox { Child = drawing, Width = size, Height = size, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    }

    private (Brush Background, Brush Foreground) WeatherCardColours(string? icon)
    {
        var surface = Look.Current.Surface;
        var tint = icon switch
        {
            "☀" => "#C88216",
            "☾" => "#46469A",
            "⛅" => "#AF782C",
            PartlyCloudyNight => "#565597",
            "☁" => "#687582",
            "≋" => "#648785",
            RainIcon => "#236BAB",
            "❄" => "#5396B9",
            "ϟ" => "#8C49AB",
            _ => null
        };
        var baseBrush = new SolidColorBrush(PanelTheme.ToColor(surface));
        if (tint is null) return (baseBrush, UI.Brush(Look.Primary));

        var hue = ThemeColor.Parse(tint) with { A = Look.Current.IsDark ? (byte)166 : (byte)70 };
        var highlight = hue.Over(surface);
        var midpoint = (hue with { A = (byte)(hue.A / 3) }).Over(surface);
        var ink = Look.Current["textPrimary"].EnsureContrast([highlight, midpoint, surface], 4.5);
        var wash = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, .8),
            GradientStops =
            {
                new GradientStop(PanelTheme.ToColor(highlight), 0),
                new GradientStop(PanelTheme.ToColor(midpoint), .52),
                new GradientStop(PanelTheme.ToColor(surface), 1)
            }
        };
        return (wash, new SolidColorBrush(PanelTheme.ToColor(ink)));
    }

    private string WeatherExtremes(WeatherDay? day, bool abbreviated = false) =>
        $"{(abbreviated ? Loc.T("低", "L") : Loc.T("最低", "Low"))} {WeatherTemperature(day?.Low)}  " +
        $"{(abbreviated ? Loc.T("高", "H") : Loc.T("最高", "High"))} {WeatherTemperature(day?.High)}";
}
