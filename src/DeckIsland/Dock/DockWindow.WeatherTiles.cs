using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

internal sealed partial class DockWindow
{
    private (FrameworkElement, Action) WeatherTile(DockWidgetKind kind, double tile, bool preview)
    {
        var sample = preview ? WeatherPreview() : null;
        var (content, render) = kind switch
        {
            DockWidgetKind.HourlyWeather => ForecastCells(tile, hourly: true),
            DockWidgetKind.SunriseSunset => SunArc(tile),
            DockWidgetKind.WeatherStatus => WeatherStatusTile(tile),
            _ => CurrentWeatherTile(kind, tile)
        };
        WeatherReading? previous = null;
        bool units = false;
        DateTime minute = default;
        bool first = true;
        return (content, () =>
        {
            var live = _app.Settings.WeatherEnabled ? _app.CurrentWeather : null;
            var reading = live ?? sample;
            var now = WeatherLocalTime(reading);
            var rounded = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
            bool fahrenheit = _app.Settings.WeatherFahrenheit;
            if (!first && ReferenceEquals(previous, reading) && units == fahrenheit &&
                (kind is not (DockWidgetKind.SunriseSunset or DockWidgetKind.HourlyWeather) || rounded == minute)) return;
            first = false;
            previous = reading;
            units = fahrenheit;
            minute = rounded;
            render(reading);
        });
    }

    private static DateTime WeatherLocalTime(WeatherReading? reading) => reading?.UtcOffset is { } offset
        ? DateTime.UtcNow.Add(offset) : DateTime.Now;

    private string WeatherDegrees(double? value) => value is { } celsius
        ? $"{Math.Round(_app.Settings.WeatherFahrenheit ? celsius * 1.8 + 32 : celsius, MidpointRounding.AwayFromZero):0}°" : "–";

    private static WeatherReading WeatherPreview()
    {
        var now = DateTime.Now;
        var hourStart = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerHour));
        var days = Enumerable.Range(0, 4).Select(i => new WeatherDay(DateOnly.FromDateTime(now.AddDays(i)), i == 1 ? 61 : 3, 8 - i, 3 - i,
            PrecipitationMillimetres: 0, Sunrise: now.Date.AddHours(7.5), Sunset: now.Date.AddHours(17))).ToArray();
        var hours = Enumerable.Range(1, 5).Select(i => new WeatherHour(hourStart.AddHours(i), 6 + i % 3, 3, true, 0)).ToArray();
        return new WeatherReading(new WeatherPoint(Loc.T("示例地点", "Example location"), 0, 0), 6, 3, true, days) { Hours = hours, WindSpeed = 6 };
    }

    private static (Border Icon, Action<int?, bool> Update) StaticWeatherPicture(double size)
    {
        var host = new Border { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        string? previous = null;
        return (host, (code, daylight) =>
        {
            string symbol = code.HasValue ? MainWindow.WeatherCondition(code, daylight).Item1 : "";
            if (previous == symbol) return;
            previous = symbol;
            var picture = MainWindow.ColourfulWeatherIcon(symbol, animate: false);
            host.Child = picture is null ? null : new Viewbox { Child = picture, IsHitTestVisible = false };
        });
    }

    private static StackPanel WeatherRow(double gap = 0) => new()
    {
        Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(gap, 0, gap, 0)
    };

    private (FrameworkElement, Action<WeatherReading?>) CurrentWeatherTile(DockWidgetKind kind, double tile)
    {
        var (icon, setIcon) = StaticWeatherPicture(tile * 0.56);
        icon.Margin = new Thickness(0, 0, tile * 0.10, 0);
        var degree = Label("", tile * 0.38, bold: true);
        var subtitle = Label("", tile * 0.19, dim: true);
        var current = WeatherRow();
        current.Children.Add(icon);
        current.Children.Add(TimeStack(degree, subtitle));
        var row = WeatherRow(tile * 0.13);
        row.Children.Add(current);
        Action<WeatherReading?> extra = _ => { };
        if (kind is DockWidgetKind.WeatherDetails or DockWidgetKind.TodayForecast)
        {
            row.Children.Add(new Rectangle { Width = 1, Height = tile * 0.58, Fill = UI.Brush(_look.Divider), Margin = new Thickness(tile * 0.14, 0, tile * 0.14, 0) });
            if (kind == DockWidgetKind.TodayForecast)
            {
                var (forecast, update) = ForecastCells(tile, hourly: false);
                row.Children.Add(forecast);
                extra = update;
            }
            else
            {
                var wind = Label("", tile * 0.19, bold: true);
                var rain = Label("", tile * 0.19, bold: true);
                var range = Label("", tile * 0.19, bold: true);
                var rangeMark = Label("H/L", tile * 0.15, bold: true);
                rangeMark.Foreground = UI.Brush("#FFFF9F0A");
                var details = TimeStack(WeatherDetail(MonitorIcons.Icon("wind", tile * 0.2, UI.Brush("#FF64B5FF")), wind, tile),
                    WeatherDetail(MonitorIcons.Icon("droplet", tile * 0.2, UI.Brush("#FF64B5FF")), rain, tile),
                    WeatherDetail(rangeMark, range, tile));
                details.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(details);
                extra = reading =>
                {
                    var day = reading?.Days.FirstOrDefault();
                    wind.Text = reading?.WindSpeed is { } speed ? $"{speed:0} km/h" : "–";
                    rain.Text = day?.PrecipitationMillimetres is { } amount ? $"{amount:0.#} mm" : "–";
                    range.Text = $"{WeatherDegrees(day?.High)} / {WeatherDegrees(day?.Low)}";
                };
            }
        }
        return (WeatherFit(row), reading =>
        {
            degree.Text = WeatherDegrees(reading?.Temperature);
            setIcon(reading?.Code, reading?.Daylight ?? true);
            var day = reading?.Days.FirstOrDefault();
            subtitle.Text = kind switch
            {
                DockWidgetKind.Weather => HighLow(day),
                DockWidgetKind.TodayForecast => Loc.T("今天", "Today"),
                _ => reading is null ? Loc.T("暂无天气", "Weather unavailable") : MainWindow.WeatherCondition(reading.Code, reading.Daylight).Item2
            };
            extra(reading);
        });
    }

    // A detail line: a small mark in a fixed column, then its value.
    private static StackPanel WeatherDetail(FrameworkElement mark, TextBlock value, double tile)
    {
        var column = new Border { Width = tile * 0.36, Child = mark, VerticalAlignment = VerticalAlignment.Center };
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        value.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { column, value } };
    }

    private string HighLow(WeatherDay? day) =>
        Loc.T($"高 {WeatherDegrees(day?.High)} 低 {WeatherDegrees(day?.Low)}", $"H {WeatherDegrees(day?.High)} L {WeatherDegrees(day?.Low)}");

    private static Viewbox WeatherFit(FrameworkElement child) =>
        new() { Child = child, StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };

    private (FrameworkElement, Action<WeatherReading?>) ForecastCells(double tile, bool hourly)
    {
        var row = new UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Center };
        var cells = Enumerable.Range(0, hourly ? 6 : 3).Select(_ =>
        {
            var title = Label("", tile * 0.20, bold: true, dim: true);
            var degree = Label("", tile * 0.20, bold: true);
            title.TextAlignment = degree.TextAlignment = TextAlignment.Center;
            var (icon, set) = StaticWeatherPicture(tile * 0.30);
            var cell = TimeStack(title, icon, degree);
            cell.Width = tile * (hourly ? 0.78 : 0.90);
            row.Children.Add(cell);
            return (title, degree, set);
        }).ToArray();
        return (WeatherFit(row), reading =>
        {
            var hours = hourly ? (reading?.Hours ?? []).Where(hour => hour.Time > WeatherLocalTime(reading)).Take(5).ToArray() : [];
            var days = hourly ? [] : (reading?.Days ?? []).Skip(1).Take(3).ToArray();
            for (int i = 0; i < cells.Length; i++)
            {
                var (title, degree, set) = cells[i];
                if (hourly && i == 0)
                {
                    title.Text = Loc.T("现在", "Now");
                    degree.Text = WeatherDegrees(reading?.Temperature);
                    set(reading?.Code, reading?.Daylight ?? true);
                }
                else if (hourly && i - 1 < hours.Length)
                {
                    var hour = hours[i - 1];
                    title.Text = TimeText(hour.Time);
                    degree.Text = WeatherDegrees(hour.Temperature);
                    set(hour.Code, hour.Daylight);
                }
                else if (!hourly && i < days.Length)
                {
                    var day = days[i];
                    title.Text = day.Date.ToString("ddd", CultureInfo.CurrentCulture);
                    // The high in full, the low dimmed, as on the forecast cards elsewhere.
                    degree.Inlines.Clear();
                    degree.Inlines.Add(new System.Windows.Documents.Run(WeatherDegrees(day.High) + " "));
                    degree.Inlines.Add(new System.Windows.Documents.Run(WeatherDegrees(day.Low)) { Foreground = UI.Brush(_look.Dim), FontWeight = FontWeights.Normal });
                    set(day.Code, true);
                }
                else { title.Text = degree.Text = "–"; set(null, true); }
            }
        });
    }

    private (FrameworkElement, Action<WeatherReading?>) WeatherStatusTile(double tile)
    {
        var (icon, setIcon) = StaticWeatherPicture(tile * 0.66);
        var sky = Label("", tile * 0.25, bold: true);
        var place = Label("", tile * 0.17);
        var degree = Label("", tile * 0.40);
        sky.Foreground = degree.Foreground = Brushes.White;
        place.Foreground = UI.Brush("#FFDCE7FA");
        degree.VerticalAlignment = VerticalAlignment.Center;
        var captions = TimeStack(sky, place);
        captions.HorizontalAlignment = HorizontalAlignment.Left;
        var row = new DockPanel { Margin = new Thickness(tile * 0.18, 0, tile * 0.12, 0) };
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Right);
        DockPanel.SetDock(degree, System.Windows.Controls.Dock.Right);
        row.Children.Add(icon); row.Children.Add(degree); row.Children.Add(captions);
        var card = new Border { CornerRadius = new CornerRadius(TileRadius()), Child = row };
        return (card, reading =>
        {
            Color start = reading?.Daylight == false ? Color.FromRgb(45, 52, 85)
                : reading?.Code is 0 or 1 ? Color.FromRgb(34, 104, 147) : Color.FromRgb(64, 87, 122);
            card.Background = new LinearGradientBrush(start, Color.FromRgb(43, 56, 83), 25);
            sky.Text = reading is null ? Loc.T("暂无天气", "Weather unavailable") : MainWindow.WeatherCondition(reading.Code, reading.Daylight).Item2;
            place.Text = reading?.Place.Name ?? "";
            degree.Text = WeatherDegrees(reading?.Temperature);
            setIcon(reading?.Code, reading?.Daylight ?? true);
        });
    }

    private (FrameworkElement, Action<WeatherReading?>) SunArc(double tile)
    {
        var canvas = new Canvas { Width = 160, Height = 22, FlowDirection = FlowDirection.LeftToRight };
        var curve = new Path { Data = Geometry.Parse("M 3,20 Q 80,-12 157,20"), Stroke = UI.Brush(_look.Dim), Opacity = 0.5, StrokeThickness = 1.4 };
        var active = new Path { Data = curve.Data, Stroke = UI.Brush("#FFEDAD46"), StrokeThickness = 2 };
        var clip = new RectangleGeometry();
        active.Clip = clip;
        var dot = new Ellipse { Width = 6, Height = 6, Fill = UI.Brush("#FFFFCE66") };
        canvas.Children.Add(curve); canvas.Children.Add(active); canvas.Children.Add(dot);
        var rise = Label("–", tile * 0.19, bold: true);
        var set = Label("–", tile * 0.19, bold: true);
        var labels = new DockPanel { Margin = new Thickness(tile * 0.12, 0, tile * 0.12, 0) };
        DockPanel.SetDock(set, System.Windows.Controls.Dock.Right);
        labels.Children.Add(set); labels.Children.Add(rise);
        var layout = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        layout.Children.Add(new Viewbox { Child = canvas, Height = tile * 0.35, Stretch = Stretch.Fill, Margin = new Thickness(tile * 0.10, 0, tile * 0.10, 0) });
        layout.Children.Add(labels);
        return (layout, reading =>
        {
            var day = reading?.Days.FirstOrDefault();
            bool valid = day?.Sunrise is { } start && day.Sunset is { } finish && finish > start;
            rise.Text = valid ? $"↑ {TimeText(day!.Sunrise!.Value)}" : "↑ –";
            set.Text = valid ? $"{TimeText(day!.Sunset!.Value)} ↓" : "– ↓";
            dot.Visibility = active.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
            if (!valid) return;
            double fraction = Math.Clamp((WeatherLocalTime(reading) - day!.Sunrise!.Value).TotalSeconds / (day.Sunset!.Value - day.Sunrise.Value).TotalSeconds, 0, 1);
            double x = 3 + 154 * fraction;
            clip.Rect = new Rect(0, -12, x, 35);
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, 20 - 64 * fraction * (1 - fraction) - 3);
        });
    }
}
