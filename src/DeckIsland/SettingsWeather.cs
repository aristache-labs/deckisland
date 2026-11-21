using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DeckIsland.Core;

namespace DeckIsland;

public partial class SettingsWindow
{
    internal const int WeatherSettingsPage = 19;

    private void WeatherSettings()
    {
        var mode = new ComboBox
        {
            Style = (Style)FindResource("SettingsCombo"), MinWidth = 144,
            ItemsSource = new[] { Loc.T("自动", "Automatic"), Loc.T("手动", "Manual") },
            SelectedIndex = S.ManualWeatherLocation ? 1 : 0
        };
        AutomationProperties.SetName(mode, Loc.T("天气位置", "Weather location"));
        PopupMotion.Attach(mode);
        var units = new ComboBox
        {
            Style = (Style)FindResource("SettingsCombo"), MinWidth = 144,
            ItemsSource = new[] { "°C", "°F" }, SelectedIndex = S.WeatherFahrenheit ? 1 : 0
        };
        AutomationProperties.SetName(units, Loc.T("温度单位", "Temperature unit"));
        PopupMotion.Attach(units);
        var cardStyle = new ComboBox
        {
            Style = (Style)FindResource("SettingsCombo"), MinWidth = 144,
            ItemsSource = new[] { Loc.T("简约", "Minimal"), Loc.T("多彩", "Colourful") },
            SelectedIndex = S.ColourfulWeatherCard ? 1 : 0
        };
        AutomationProperties.SetName(cardStyle, Loc.T("展开卡片样式", "Expanded card style"));
        PopupMotion.Attach(cardStyle);
        cardStyle.SelectionChanged += (_, _) => Change(s => s.ColourfulWeatherCard = cardStyle.SelectedIndex == 1);
        var options = new StackPanel();
        options.Children.Add(Row(Loc.T("天气位置", "Weather location"), mode));
        options.Children.Add(Row(Loc.T("温度单位", "Temperature unit"), units));
        options.Children.Add(Row(Loc.T("展开卡片样式", "Expanded card style"), cardStyle));
        var disclosure = TextLine(Loc.T("自动定位将近似坐标发送给 Open-Meteo 获取预报，并发送给 BigDataCloud 查询城市和地区。",
            "Automatic location sends approximate coordinates to Open-Meteo for the forecast and to BigDataCloud for the city and region."), 12, TextSecondary);
        disclosure.TextWrapping = TextWrapping.Wrap;
        options.Children.Add(disclosure);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var retry = ActionButton(Loc.T("重试自动定位", "Retry automatic location"), async () => await _app.RequestWeatherLocation());
        actions.Children.Add(retry);
        actions.Children.Add(Link(Loc.T("Windows 位置隐私设置", "Windows location privacy settings"), "ms-settings:privacy-location"));
        options.Children.Add(actions);

        var query = new TextBox { Style = (Style)FindResource("SettingsNumber"), MinWidth = 200, MaxLength = 100, TextAlignment = TextAlignment.Left };
        AutomationProperties.SetName(query, Loc.T("城市名称", "City name"));
        var results = new ComboBox { Style = (Style)FindResource("SettingsCombo"), MinWidth = 210, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(results, Loc.T("城市搜索结果", "City search results"));
        PopupMotion.Attach(results);
        var status = TextLine("", 12, TextSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        void ShowCity() => status.Text = S.WeatherCity is { } city
            ? (S.ManualWeatherLocation ? Loc.T("所选城市", "Selected city") : Loc.T("备用城市", "Fallback city")) + $" · {city}"
            : Loc.T("搜索并选择一个城市。", "Search for and choose a city.");
        ShowCity();
        units.SelectionChanged += (_, _) => Change(s => s.WeatherFahrenheit = units.SelectedIndex == 1);
        CancellationTokenSource? search = null;
        void CancelSearch()
        {
            search?.Cancel();
            search?.Dispose();
            search = null;
        }
        async Task Search()
        {
            CancelSearch();
            results.ItemsSource = null;
            results.Visibility = Visibility.Collapsed;
            string name = query.Text.Trim();
            if (name.Length < 2)
            {
                status.Text = Loc.T("请输入至少两个字符。", "Enter at least two characters.");
                return;
            }
            search = new();
            var token = search.Token;
            status.Text = Loc.T("正在查找城市…", "Searching for cities…");
            try
            {
                var cities = await Weather.Search(name, Loc.Language == Loc.Chinese, token);
                if (token.IsCancellationRequested) return;
                results.ItemsSource = cities;
                results.Visibility = cities.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                status.Text = cities.Count > 0 ? Loc.T("请选择城市。", "Choose a city from the results.")
                    : Loc.T("未找到城市。", "No cities found.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                if (!token.IsCancellationRequested)
                    status.Text = Loc.T("无法搜索城市。请重试。", "City search failed. Try again.");
            }
        }
        var searchButton = ActionButton(Loc.T("搜索城市", "Search cities"), async () => await Search());
        void UpdateWeatherControls()
        {
            retry.IsEnabled = S.WeatherEnabled && !S.ManualWeatherLocation;
            query.IsEnabled = searchButton.IsEnabled = results.IsEnabled = S.WeatherEnabled;
            if (!S.WeatherEnabled) CancelSearch();
        }
        UpdateWeatherControls();
        Group(Toggle(Loc.T("启用天气", "Enable weather"), S.WeatherEnabled, async enabled =>
        {
            var permission = enabled && !S.ManualWeatherLocation ? _app.RequestWeatherLocation() : Task.CompletedTask;
            Change(s => s.WeatherEnabled = enabled);
            UpdateWeatherControls();
            await permission;
        }));
        Target.Children.Add(options);
        mode.SelectionChanged += async (_, _) =>
        {
            var permission = mode.SelectedIndex == 0 && S.WeatherEnabled ? _app.RequestWeatherLocation() : Task.CompletedTask;
            Change(s => s.ManualWeatherLocation = mode.SelectedIndex == 1);
            UpdateWeatherControls();
            ShowCity();
            await permission;
        };
        query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Search(); } };
        query.TextChanged += (_, _) => { CancelSearch(); results.ItemsSource = null; results.Visibility = Visibility.Collapsed; ShowCity(); };
        options.Unloaded += (_, _) => CancelSearch();
        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedItem is not WeatherCity city) return;
            Change(s => s.WeatherCity = city);
            ShowCity();
        };
        var citySearch = new StackPanel { Orientation = Orientation.Horizontal };
        citySearch.Children.Add(new Border
        {
            Child = query, Background = UI.Brush(Inset), BorderBrush = UI.Brush(Divider),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4)
        });
        searchButton.Margin = new Thickness(8, 0, 0, 0);
        citySearch.Children.Add(searchButton);
        options.Children.Add(Row(Loc.T("城市名称", "City name"), citySearch));
        options.Children.Add(results);
        options.Children.Add(status);
        Group(Row(Loc.T("天气数据", "Weather data"), Link("Open-Meteo ↗", Weather.Attribution)),
            Row(Loc.T("地理数据", "Location data"), Link("GeoNames ↗", Weather.LocationAttribution)),
            Row(Loc.T("自动位置名称", "Automatic location names"), Link("BigDataCloud ↗", Weather.AutomaticLocationAttribution)));
    }

    private void WeatherSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dimensions.ColumnDefinitions.Add(new());
        dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("宽度", "Width"), S.WeatherWidth, DeckPages.MinWidth, DeckPages.MaxWidth,
            value => Resize(s => s.WeatherWidth = value, DeckPage.Weather));
        var height = Dimension(Loc.T("高度", "Height"), S.WeatherHeight, DeckPages.MinHeight, DeckPages.MaxHeight,
            value => Resize(s => s.WeatherHeight = value, DeckPage.Weather));
        width.Margin = new Thickness(0, 0, 20, 0);
        height.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(height, 1);
        dimensions.Children.Add(width);
        dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
