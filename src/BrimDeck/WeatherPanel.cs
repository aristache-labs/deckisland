using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    private const string RainIcon = "rain";
    private const string WaterIcon = "water";
    private const string PartlyCloudyNight = "partly-cloudy-night";
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

    private FrameworkElement? CompactWeather(double scale)
    {
        if (!Settings.ShowsWeather(EffectiveStyle) || _app.CurrentWeather is not { } reading) return null;
        var (icon, condition) = WeatherCondition(reading.Code, reading.Daylight);
        string temperature = WeatherTemperature(reading.Temperature);
        var text = WeatherText($"{temperature}{(_app.WeatherStale ? "*" : "")}", 11 * scale);
        var mark = WeatherLineIcon(icon, 14 * scale, text.Foreground);
        mark.Margin = new Thickness(0, 0, 3 * scale, 0);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { mark, text } };
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
        WeatherContent.Visibility = ClockLabel.Visibility = Visibility.Visible;
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
        var hero = new StackPanel();
        var title = new DockPanel { LastChildFill = true };
        // The page can be built while collapsed. The icon starts when the expanded content becomes visible.
        var skyIcon = colourful ? ColourfulWeatherIcon(icon, Animates) ?? WeatherLineIcon(icon, 36, foreground)
            : WeatherLineIcon(icon, 36, foreground);
        skyIcon.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(skyIcon, System.Windows.Controls.Dock.Right);
        title.Children.Add(skyIcon);
        var today = reading.Days.FirstOrDefault();
        var todayPrecipitation = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        todayPrecipitation.Children.Add(WeatherPrecipitation(today, foreground, probability: true));
        var todayTotal = WeatherPrecipitation(today, foreground, probability: false);
        todayTotal.Margin = new Thickness(14, 0, 0, 0);
        todayPrecipitation.Children.Add(todayTotal);
        DockPanel.SetDock(todayPrecipitation, System.Windows.Controls.Dock.Right);
        title.Children.Add(todayPrecipitation);
        var temperature = WeatherText(WeatherTemperature(reading.Temperature, true), 34);
        temperature.Foreground = foreground;
        temperature.FontWeight = FontWeights.SemiBold;
        title.Children.Add(temperature);
        hero.Children.Add(title);
        var summary = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 7, 0, 0) };
        var extremes = WeatherText(WeatherExtremes(today), 13);
        extremes.Foreground = foreground;
        extremes.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(extremes, System.Windows.Controls.Dock.Right);
        summary.Children.Add(extremes);
        var location = WeatherText($"{condition} · {WeatherPlace(reading)}", 13);
        location.Foreground = foreground;
        summary.Children.Add(location);
        hero.Children.Add(summary);
        rows.Children.Add(new Border
        {
            Child = hero, Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(12),
            BorderBrush = UI.Brush(Look.Hex("rim")), BorderThickness = new Thickness(1),
            Background = background
        });
        foreach (var day in reading.Days.Skip(1))
        {
            var (dayIcon, dayCondition) = WeatherCondition(day.Code, true);
            var dayRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "WeatherDate" },
                    new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "WeatherChance" },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "WeatherTotal" },
                    new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "WeatherRange" }
                }
            };
            void AddColumn(FrameworkElement element, int column)
            {
                if (column != 0) element.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(element, column);
                dayRow.Children.Add(element);
            }
            var when = WeatherText(day.Date.ToString("ddd d", Loc.Culture), 12);
            AddColumn(when, 0);
            var ink = when.Foreground;
            AddColumn(WeatherPrecipitation(day, ink, probability: true, sharedIcon: dayIcon), 1);
            var mark = WeatherLineIcon(dayIcon, 17, ink);
            mark.Margin = new Thickness(0, 0, 8, 0);
            AddColumn(new DockPanel { Children = { mark, WeatherText(dayCondition, 12) } }, 2);
            AddColumn(WeatherPrecipitation(day, ink, probability: false), 3);
            AddColumn(WeatherText(WeatherExtremes(day), 12), 4);
            rows.Children.Add(new Border
            {
                Child = dayRow, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(14, 5, 14, 5),
                BorderBrush = UI.Brush(Look.Hex("rim")), BorderThickness = new Thickness(0, 0, 0, 1)
            });
        }
        if (_app.WeatherStale) rows.Children.Add(WeatherText(Loc.T("数据未更新", "Weather data is stale"), 12));
    }

    private static FrameworkElement WeatherLineIcon(string icon, double size, Brush ink)
    {
        if (icon == RainIcon) return WeatherRainIcon(size, ink);
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
                "≋" => "M 4,7 H 18 M 7,12 H 21 M 3,17 H 17",
                "❄" => "M 12,3 V 21 M 4.2,7.5 L 19.8,16.5 M 4.2,16.5 L 19.8,7.5 M 9,5 L 12,8 L 15,5 M 9,19 L 12,16 L 15,19",
                WaterIcon => "M 12,2 C 10,6 5,10 5,15 A 7,7 0 0 0 19,15 C 19,10 14,6 12,2 Z",
                "ϟ" => "M 13,2 L 5,14 H 11 L 10,22 L 19,10 H 13 Z",
                _ => "M 7,12 H 17"
            }),
            Width = size, Height = size, Stretch = Stretch.Uniform, Stroke = ink, StrokeThickness = 1.6,
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

    private FrameworkElement WeatherPrecipitation(WeatherDay? day, Brush foreground, bool probability, string? sharedIcon = null)
    {
        string value = (probability, day?.PrecipitationProbability, day?.PrecipitationMillimetres) switch
        {
            (true, { } chance, _) => $"{chance:0}%",
            (false, _, { } amount) => $"{amount:0.##} mm",
            _ => "—"
        };
        var figures = WeatherText(value, 12);
        figures.Foreground = foreground;
        string label = probability ? Loc.T("最高降水概率", "Peak precipitation probability")
            : Loc.T("总降水量（雨、阵雨及雪）", "Total precipitation (rain, showers and snow)");
        label += $" {value}";
        AutomationProperties.SetName(figures, label);
        var metric = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = label };
        bool snow = day?.SnowfallCentimetres > 0 || day?.Code is 71 or 73 or 75 or 77 or 85 or 86;
        string? icon = null;
        if (probability && day?.PrecipitationProbability > 0)
            icon = snow ? "❄" : day?.PrecipitationMillimetres > 0 ? RainIcon : null;
        else if (!probability && day?.PrecipitationMillimetres is not null)
            icon = snow ? "❄" : WaterIcon;
        if (probability) metric.Children.Add(figures);
        if (icon is not null && icon != sharedIcon)
        {
            var mark = WeatherLineIcon(icon, 14, foreground);
            mark.Margin = probability ? new Thickness(4, 0, 0, 0) : new Thickness(0, 0, 4, 0);
            metric.Children.Add(mark);
        }
        if (!probability) metric.Children.Add(figures);
        return metric;
    }

    private string WeatherExtremes(WeatherDay? day) =>
        $"{Loc.T("最低", "Low")} {WeatherTemperature(day?.Low)}  {Loc.T("最高", "High")} {WeatherTemperature(day?.High)}";
}
