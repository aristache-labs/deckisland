using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    private const string RainIcon = "rain";
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
        1 or 2 => ("⛅", Loc.T("局部多云", "Partly cloudy")),
        3 => ("☁", Loc.T("阴天", "Overcast")),
        45 or 48 => ("≋", Loc.T("雾", "Fog")),
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => (RainIcon, Loc.T("雨", "Rain")),
        71 or 73 or 75 or 77 or 85 or 86 => ("❄", Loc.T("雪", "Snow")),
        95 or 96 or 99 => ("ϟ", Loc.T("雷暴", "Thunderstorm")),
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
        var text = WeatherText($"{(icon == RainIcon ? "" : icon + " ")}{temperature}{(_app.WeatherStale ? "*" : "")}", 11 * scale);
        FrameworkElement content = text;
        if (icon == RainIcon)
        {
            var mark = WeatherLineIcon(icon, 14 * scale, text.Foreground);
            mark.Margin = new Thickness(0, 0, 3 * scale, 0);
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { mark, text } };
        }
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
        var accent = colourful ? SkyInk(icon, reading.Daylight) : UI.Brush(Look.Primary);
        var hero = new StackPanel();
        var title = new DockPanel { LastChildFill = true };
        // The page can be built while collapsed. The icon starts when the expanded content becomes visible.
        var skyIcon = colourful && icon == RainIcon
            ? ColourfulRainIcon(48, Animates)
            : WeatherLineIcon(icon, 36, accent, animate: colourful && Animates);
        skyIcon.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(skyIcon, System.Windows.Controls.Dock.Right);
        title.Children.Add(skyIcon);
        var temperature = WeatherText(WeatherTemperature(reading.Temperature, true), 34);
        temperature.Foreground = accent;
        temperature.FontWeight = FontWeights.SemiBold;
        title.Children.Add(temperature);
        hero.Children.Add(title);
        var today = reading.Days.FirstOrDefault();
        var summary = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 7, 0, 0) };
        var extremes = WeatherText(WeatherExtremes(today), 13);
        extremes.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(extremes, System.Windows.Controls.Dock.Right);
        summary.Children.Add(extremes);
        summary.Children.Add(WeatherText($"{condition} · {WeatherPlace(reading)}", 13));
        hero.Children.Add(summary);
        rows.Children.Add(new Border
        {
            Child = hero, Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(12),
            BorderBrush = UI.Brush(Look.Hex("rim")), BorderThickness = new Thickness(1),
            Background = WeatherCardBackground(colourful ? accent : null)
        });
        foreach (var day in reading.Days.Skip(1))
        {
            var (dayIcon, dayCondition) = WeatherCondition(day.Code, true);
            var dayRow = new DockPanel { LastChildFill = true };
            var when = WeatherText(day.Date.ToString("ddd d", Loc.Culture), 12);
            when.Width = 78;
            dayRow.Children.Add(when);
            var mark = WeatherLineIcon(dayIcon, 17, UI.Brush(Look.Primary));
            mark.Margin = new Thickness(0, 0, 8, 0);
            dayRow.Children.Add(mark);
            var range = WeatherText(WeatherExtremes(day), 12);
            range.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(range, System.Windows.Controls.Dock.Right);
            dayRow.Children.Add(range);
            dayRow.Children.Add(WeatherText(dayCondition, 12));
            rows.Children.Add(new Border
            {
                Child = dayRow, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(14, 5, 14, 5),
                BorderBrush = UI.Brush(Look.Hex("rim")), BorderThickness = new Thickness(0, 0, 0, 1)
            });
        }
        if (_app.WeatherStale) rows.Children.Add(WeatherText(Loc.T("数据未更新", "Weather data is stale"), 12));
        var credit = new Button { Style = (Style)FindResource("PanelTextButton"), Content = "Open-Meteo ↗",
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 3, 0, 0) };
        credit.Click += (_, _) => UI.OpenUrl(Weather.Attribution);
        rows.Children.Add(credit);
    }

    private Brush SkyInk(string icon, bool daylight) => new SolidColorBrush(Look.Readable(icon switch
    {
        "☀" => Color.FromRgb(245, 173, 49),
        "☾" => Color.FromRgb(169, 164, 255),
        RainIcon or "❄" => Color.FromRgb(105, 181, 230),
        "ϟ" => Color.FromRgb(190, 156, 245),
        _ => daylight ? Color.FromRgb(120, 174, 230) : Color.FromRgb(157, 163, 228)
    }, 5.5));

    private static FrameworkElement WeatherLineIcon(string icon, double size, Brush ink, bool animate = false)
    {
        if (icon == RainIcon) return WeatherRainIcon(size, ink);
        const string cloud = "M 5,19 A 3.5,3.5 0 0 1 5,12 A 5,5 0 0 1 14.8,11 A 4,4 0 1 1 17,19 Z";
        var mark = new Path
        {
            Data = Geometry.Parse(icon switch
            {
                "☀" => "M 12,7 A 5,5 0 1 0 12,17 A 5,5 0 1 0 12,7 M 12,2 V 4 M 12,20 V 22 M 2,12 H 4 M 20,12 H 22 M 5,5 L 6.4,6.4 M 17.6,17.6 L 19,19 M 19,5 L 17.6,6.4 M 6.4,17.6 L 5,19",
                "☾" => "M 19.5,15 A 8,8 0 0 1 9,4.5 A 8.4,8.4 0 1 0 19.5,15 Z",
                "⛅" => cloud + " M 8,8 A 3,3 0 0 1 13,6 M 10,2 V 3 M 4.5,4.5 L 5.5,5.5 M 15.5,4.5 L 14.5,5.5",
                "☁" => cloud,
                "≋" => "M 4,7 H 18 M 7,12 H 21 M 3,17 H 17",
                "❄" => "M 12,3 V 21 M 4.2,7.5 L 19.8,16.5 M 4.2,16.5 L 19.8,7.5 M 9,5 L 12,8 L 15,5 M 9,19 L 12,16 L 15,19",
                "ϟ" => "M 13,2 L 5,14 H 11 L 10,22 L 19,10 H 13 Z",
                _ => "M 7,12 H 17"
            }),
            Width = size, Height = size, Stretch = Stretch.Uniform, Stroke = ink, StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        if (animate)
        {
            var shift = new TranslateTransform();
            mark.RenderTransform = shift;
            mark.IsVisibleChanged += (_, _) => shift.BeginAnimation(TranslateTransform.YProperty, mark.IsVisible
                ? new DoubleAnimation(0, 4, TimeSpan.FromSeconds(1.4))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Soft.InOut }
                : null);
        }
        return mark;
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

    private Brush WeatherCardBackground(Brush? accent)
    {
        var surface = PanelTheme.ToColor(Look.Current["surface"]);
        if (accent is not SolidColorBrush brush) return new SolidColorBrush(surface);
        var tint = brush.Color;
        return new LinearGradientBrush(
            Color.FromRgb((byte)(surface.R + (tint.R - surface.R) * .12),
                (byte)(surface.G + (tint.G - surface.G) * .12),
                (byte)(surface.B + (tint.B - surface.B) * .12)),
            surface, new Point(0, 0), new Point(1, 1));
    }

    private string WeatherExtremes(WeatherDay? day) =>
        $"{Loc.T("高", "H")} {WeatherTemperature(day?.High)}  {Loc.T("低", "L")} {WeatherTemperature(day?.Low)}";
}
