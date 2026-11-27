using System.Globalization;
using System.Net.Http;
using System.Text.Json;
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
    private (FrameworkElement, Action) WeatherPopupContent()
    {
        var host = new ContentControl();
        Action refresh = () => { };
        void ShowForecast()
        {
            var (forecast, update) = WeatherForecastPane(ShowLocation);
            host.Content = forecast;
            refresh = update;
            update();
        }
        void ShowLocation()
        {
            var (location, search, update) = WeatherLocationPane(ShowForecast);
            host.Content = location;
            refresh = update;
            update();
            search.Focus();
        }
        ShowForecast();
        return (host, () => refresh());
    }

    private (FrameworkElement, Action) WeatherForecastPane(Action changeLocation)
    {
        var city = Label("", 15, bold: true);
        city.VerticalAlignment = VerticalAlignment.Center;
        var change = DetailButton(Loc.T("更改位置", "Change location"), Loc.T("更改天气位置", "Change weather location"), changeLocation);
        change.Foreground = UI.Brush(Accent);
        change.Width = 105;
        var title = new DockPanel { Margin = new Thickness(0, 0, 35, 7) };
        DockPanel.SetDock(change, System.Windows.Controls.Dock.Right);
        title.Children.Add(change); title.Children.Add(city);

        var (symbol, setSymbol) = StaticWeatherPicture(64);
        symbol.Margin = new Thickness(0, 0, 12, 0);
        var degrees = Label("", 39, bold: true);
        var condition = Label("", 12, dim: true);
        var current = WeatherRow();
        current.Children.Add(symbol);
        var figures = TimeStack(degrees, condition);
        figures.HorizontalAlignment = HorizontalAlignment.Left;
        current.Children.Add(figures);
        var hours = new UniformGrid { Rows = 1, Margin = new Thickness(6) };
        var hourCard = new Border { CornerRadius = new CornerRadius(9), Background = UI.Brush(_look.CardHover),
            Margin = new Thickness(0, 12, 0, 8), Child = hours };
        var days = new StackPanel();
        var content = new StackPanel();
        foreach (var child in new UIElement[] { title, current, hourCard, days }) content.Children.Add(child);
        var hourFormat = _app.DockSettings.Clock24Hour;
        (WeatherReading? Reading, bool Enabled, bool Loading, bool Stale, bool Fahrenheit, DateTime Hour)? shown = null;
        return (new ScrollViewer { MaxHeight = 510, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content }, () =>
        {
            var reading = _app.Settings.WeatherEnabled ? _app.CurrentWeather : null;
            var now = WeatherLocalTime(reading);
            var hour = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerHour));
            var state = (reading, _app.Settings.WeatherEnabled, _app.WeatherLoading, _app.WeatherStale, _app.Settings.WeatherFahrenheit, hour);
            if (shown == state && hourFormat == _app.DockSettings.Clock24Hour) return;
            shown = state;
            hourFormat = _app.DockSettings.Clock24Hour;
            city.Text = WeatherPlaceName();
            degrees.Text = WeatherDegrees(reading?.Temperature);
            setSymbol(reading?.Code, reading?.Daylight ?? true);
            hours.Children.Clear(); days.Children.Clear();
            hourCard.Visibility = reading is null ? Visibility.Collapsed : Visibility.Visible;
            if (reading is null)
            {
                condition.Text = _app.WeatherLoading ? Loc.T("正在读取预报…", "Loading forecast…")
                    : !_app.Settings.WeatherEnabled || _app.WeatherNeedsLocation
                        ? Loc.T("选择位置以查看预报。", "Choose a location to see the forecast.")
                        : Loc.T("暂时没有预报。", "Forecast unavailable.");
                return;
            }
            var today = reading.Days.FirstOrDefault(day => day.Date == DateOnly.FromDateTime(now));
            string words = MainWindow.WeatherCondition(reading.Code, reading.Daylight).Item2;
            condition.Text = $"{words} · {HighLow(today)}";
            if (_app.WeatherStale) condition.Text += Loc.T(" · 上次更新", " · Last available");
            AddHour(Loc.T("现在", "Now"), reading.Code, reading.Daylight, reading.Temperature);
            foreach (var item in reading.Hours.Where(item => item.Time > now).Take(6))
                AddHour(TimeText(item.Time), item.Code, item.Daylight, item.Temperature);

            foreach (var day in reading.Days.Take(7))
            {
                var name = Label(day.Date == DateOnly.FromDateTime(now) ? Loc.T("今天", "Today") : day.Date.ToString("ddd", CultureInfo.CurrentCulture), 13, bold: true);
                name.Width = 65; name.VerticalAlignment = VerticalAlignment.Center;
                var (icon, setIcon) = StaticWeatherPicture(24);
                setIcon(day.Code, true);
                var low = Label(WeatherDegrees(day.Low), 13, dim: true);
                var high = Label(WeatherDegrees(day.High), 13, bold: true);
                foreach (var value in new[] { low, high })
                {
                    value.Width = 48;
                    value.TextAlignment = TextAlignment.Right;
                    value.VerticalAlignment = VerticalAlignment.Center;
                }
                var row = new DockPanel { Height = 30, LastChildFill = false };
                DockPanel.SetDock(high, System.Windows.Controls.Dock.Right);
                DockPanel.SetDock(low, System.Windows.Controls.Dock.Right);
                row.Children.Add(high); row.Children.Add(low); row.Children.Add(name); row.Children.Add(icon);
                days.Children.Add(new Border { BorderBrush = UI.Brush(_look.Divider), BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(4, 0, 4, 0), Child = row });
            }

            void AddHour(string time, int? code, bool daylight, double? temperature)
            {
                var (icon, setIcon) = StaticWeatherPicture(24);
                setIcon(code, daylight);
                hours.Children.Add(TimeStack(Label(time, 10.5, dim: true), icon, Label(WeatherDegrees(temperature), 12, bold: true)));
            }
        });
    }

    private (FrameworkElement, TextBox, Action) WeatherLocationPane(Action back)
    {
        var content = new StackPanel { MinHeight = 250 };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 34, 12) };
        var backButton = DetailButton("‹", Loc.T("返回预报", "Back to forecast"), back);
        backButton.Width = 30;
        var title = Label(Loc.T("天气位置", "Weather location"), 15, bold: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(backButton);
        heading.Children.Add(title);
        content.Children.Add(heading);

        string prompt = Loc.T("搜索城市", "Search for a city");
        var search = new TextBox { Height = 36, Padding = new Thickness(10, 7, 8, 0), FontSize = 13, Foreground = UI.Brush(_look.Text),
            CaretBrush = UI.Brush(_look.Text), Background = UI.Brush(_look.CardHover), BorderBrush = UI.Brush(_look.Divider) };
        AutomationProperties.SetName(search, prompt);
        var hint = Label(prompt, 13, dim: true);
        hint.Margin = new Thickness(11, 0, 0, 0);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        var field = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        field.Children.Add(search); field.Children.Add(hint);
        content.Children.Add(field);
        var results = new StackPanel();
        content.Children.Add(new ScrollViewer { MaxHeight = 146, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = results });
        var current = Label("", 14, bold: true);
        var source = Label("", 12, dim: true);
        current.Margin = new Thickness(0, 8, 0, 3);
        content.Children.Add(current); content.Children.Add(source);
        var locate = DetailButton(Loc.T("使用我的位置", "Use my location"), Loc.T("允许 Windows 定位", "Use Windows location"), () =>
        {
            SaveWeatherSettings(settings => { settings.ManualWeatherLocation = false; settings.WeatherEnabled = true; });
            _ = _app.RequestWeatherLocation();
        });
        locate.Height = 34;
        locate.Margin = new Thickness(0, 12, 0, 10);
        locate.Background = UI.Brush(_look.CardHover);
        content.Children.Add(locate);
        var units = new DockPanel();
        var switches = new StackPanel { Orientation = Orientation.Horizontal };
        var celsius = DetailButton("°C", "Celsius", () => SaveWeatherSettings(settings => settings.WeatherFahrenheit = false));
        var fahrenheit = DetailButton("°F", "Fahrenheit", () => SaveWeatherSettings(settings => settings.WeatherFahrenheit = true));
        celsius.Width = fahrenheit.Width = 42;
        switches.Children.Add(celsius); switches.Children.Add(fahrenheit);
        DockPanel.SetDock(switches, System.Windows.Controls.Dock.Right);
        units.Children.Add(switches);
        var unitLabel = Label(Loc.T("单位", "Units"), 13, bold: true);
        unitLabel.VerticalAlignment = VerticalAlignment.Center;
        units.Children.Add(unitLabel);
        content.Children.Add(units);

        CancellationTokenSource? searchRequest = null;
        void CancelSearch()
        {
            searchRequest?.Cancel();
            searchRequest?.Dispose();
            searchRequest = null;
        }
        content.Unloaded += (_, _) => CancelSearch();
        search.TextChanged += async (_, _) =>
        {
            CancelSearch();
            results.Children.Clear();
            string query = search.Text.Trim();
            hint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (query.Length < 2) return;
            searchRequest = new CancellationTokenSource();
            var token = searchRequest.Token;
            try
            {
                await Task.Delay(300, token);
                var cities = await Weather.Search(query, !Loc.IsEnglish, token);
                if (token.IsCancellationRequested) return;
                foreach (var city in cities.Where(city => city.Valid))
                {
                    string name = city.ToString();
                    results.Children.Add(DetailButton(name, name, () =>
                    {
                        CancelSearch();
                        SaveWeatherSettings(settings => { settings.WeatherCity = city; settings.ManualWeatherLocation = true; settings.WeatherEnabled = true; });
                        back();
                    }));
                }
                if (results.Children.Count == 0) results.Children.Add(Label(Loc.T("找不到城市", "No matching cities"), 12, dim: true));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                if (!token.IsCancellationRequested) results.Children.Add(Label(Loc.T("无法搜索城市，请重试", "Could not search for cities. Try again."), 12, dim: true));
            }
        };
        search.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Down or Key.Enter) || results.Children.OfType<Button>().FirstOrDefault() is not { } first) return;
            if (e.Key == Key.Down) first.Focus();
            else first.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            e.Handled = true;
        };
        return (content, search, () =>
        {
            current.Text = WeatherPlaceName();
            source.Text = _app.WeatherLoading ? Loc.T("正在更新…", "Updating…")
                : _app.Settings.ManualWeatherLocation ? Loc.T("手动选择的城市", "Manually selected city")
                : _app.WeatherNeedsLocation ? Loc.T("未获得位置，请选择城市", "Location unavailable. Choose a city.")
                : Loc.T("使用 Windows 位置", "Using Windows location");
            bool isFahrenheit = _app.Settings.WeatherFahrenheit;
            celsius.Background = isFahrenheit ? Brushes.Transparent : UI.Brush(_look.CardHover);
            fahrenheit.Background = isFahrenheit ? UI.Brush(_look.CardHover) : Brushes.Transparent;
            celsius.Foreground = UI.Brush(isFahrenheit ? _look.Dim : _look.Text);
            fahrenheit.Foreground = UI.Brush(isFahrenheit ? _look.Text : _look.Dim);
        });
    }

    private string WeatherPlaceName()
    {
        var settings = _app.Settings;
        if (settings.ManualWeatherLocation && settings.WeatherCity is { } city) return city.Label;
        return _app.CurrentWeather?.Place.Name is { Length: > 0 } name ? name : Loc.T("天气", "Weather");
    }

    private void SaveWeatherSettings(Action<DeckSettings> change)
    {
        var settings = _app.Settings.Copy();
        change(settings);
        _app.UpdateSettings(settings);
    }
}
