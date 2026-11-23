using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

public partial class SettingsWindow
{
    internal const int WeatherSettingsPage = 19;
    private const int MinimumSearchLength = 2;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(350);

    private static string PlaceLabel(WeatherCity city) => string.Join(", ", new[] { city.Name, city.Region, city.Country }.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct());

    // One field decides the whole thing. A place you pick from the results turns the weather on for that place; "Use my location"
    // follows the PC's location; an emptied field, saved, turns the weather off. The unit and the look sit under it.
    private Border WeatherOptionsCard()
    {
        string here = Loc.T("我的位置", "My location");
        string shown() => !S.WeatherEnabled ? "" : S.ManualWeatherLocation && S.WeatherCity is { } city ? PlaceLabel(city) : S.ManualWeatherLocation ? "" : here;

        var place = new TextBox { Style = (Style)FindResource("FormInput"), MaxLength = 100, Text = shown(), Tag = Loc.T("搜索城市", "Search for a city") };
        AutomationProperties.SetName(place, Loc.T("天气地点", "Weather place"));
        var save = new Button { Style = (Style)FindResource("FormButton"), Content = Loc.T("保存", "Save"), Width = 84, Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = false };
        var status = TextLine("", 12, TextTertiary);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        WeatherCity? chosen = null;
        string committed = place.Text;
        bool programmatic = false, cleared = false;
        void SetText(string text) { programmatic = true; place.Text = text; place.CaretIndex = text.Length; programmatic = false; committed = text; }
        void Describe()
        {
            status.Text = !S.WeatherEnabled ? Loc.T("天气已关闭。搜索一个城市，或使用你的位置。", "Weather is off. Search for a city, or use your location.")
                : S.ManualWeatherLocation ? Loc.T("天气显示的是这个城市。", "The weather shows this city.")
                : Loc.T("天气跟随这台电脑的位置。", "The weather follows this PC's location.");
        }
        void UpdateSave() => save.IsEnabled = chosen is not null || cleared;
        Describe();

        // The results: a list under the field, filled as you type.
        var list = new StackPanel();
        var results = new Popup { PlacementTarget = place, Placement = PlacementMode.Bottom, VerticalOffset = 6, StaysOpen = false, AllowsTransparency = true, Focusable = false };
        var resultsBox = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("SettingsPopup"), BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
            Child = new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false }
        };
        results.Child = resultsBox;
        place.SizeChanged += (_, _) => resultsBox.MinWidth = place.ActualWidth;

        CancellationTokenSource? search = null;
        var wait = new DispatcherTimer { Interval = SearchDelay };
        void CancelSearch() { wait.Stop(); search?.Cancel(); search?.Dispose(); search = null; }
        void ShowMessage(string text)
        {
            list.Children.Clear();
            list.Children.Add(new TextBlock { Text = text, Foreground = UI.Brush(TextTertiary), FontSize = 13, Margin = new Thickness(10, 8, 10, 8), TextWrapping = TextWrapping.Wrap });
            results.IsOpen = true;
        }
        void Pick(WeatherCity city)
        {
            chosen = city; cleared = false;
            SetText(PlaceLabel(city)); committed = "";
            results.IsOpen = false;
            UpdateSave();
            status.Text = Loc.T("点保存后，天气会显示这个城市。", "Press Save to show the weather for this city.");
        }
        async Task Search()
        {
            string name = place.Text.Trim();
            CancelSearch();
            search = new();
            var token = search.Token;
            ShowMessage(Loc.T("正在查找城市…", "Searching for cities…"));
            try
            {
                var cities = await Weather.Search(name, Loc.Language == Loc.Chinese, token);
                if (token.IsCancellationRequested) return;
                if (cities.Count == 0) { ShowMessage(Loc.T("未找到城市。", "No cities found.")); return; }
                list.Children.Clear();
                foreach (var city in cities)
                {
                    var option = new Button { Style = (Style)FindResource("SettingsButton"), Content = PlaceLabel(city), HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(10, 7, 10, 7), FontSize = 13, Foreground = UI.Brush(TextPrimary) };
                    AutomationProperties.SetName(option, city.ToString());
                    option.Click += (_, _) => Pick(city);
                    list.Children.Add(option);
                }
                results.IsOpen = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                if (!token.IsCancellationRequested) ShowMessage(Loc.T("无法搜索城市，请重试。", "Could not search for cities. Try again."));
            }
        }
        wait.Tick += async (_, _) => { wait.Stop(); await Search(); };
        place.TextChanged += (_, _) =>
        {
            if (programmatic) return;
            string text = place.Text.Trim();
            chosen = null;
            cleared = text.Length == 0 && committed.Length > 0;
            UpdateSave();
            CancelSearch();
            results.IsOpen = false;
            if (text.Length >= MinimumSearchLength && text != committed.Trim()) wait.Start();
            if (cleared) status.Text = Loc.T("保存后，天气会关闭。", "Press Save to turn the weather off.");
            else if (text.Length > 0 && text.Length < MinimumSearchLength) status.Text = Loc.T("再输入一个字符。", "Keep typing…");
            else if (text.Length == 0) Describe();
        };
        place.KeyDown += (_, e) => { if (e.Key == Key.Enter && save.IsEnabled) { e.Handled = true; save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); } };
        place.Unloaded += (_, _) => { CancelSearch(); results.IsOpen = false; };

        save.Click += (_, _) =>
        {
            if (chosen is { } city)
            {
                Change(s => { s.WeatherCity = city; s.ManualWeatherLocation = true; s.WeatherEnabled = true; });
                SetText(PlaceLabel(city));
            }
            else if (cleared)
            {
                Change(s => s.WeatherEnabled = false);
                SetText("");
            }
            chosen = null; cleared = false;
            UpdateSave(); Describe();
        };

        var locate = new Button { Style = (Style)FindResource("FormButton"), Content = Loc.T("使用我的位置", "Use my location"), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 0, 14, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        locate.Click += async (_, _) =>
        {
            CancelSearch(); results.IsOpen = false; chosen = null; cleared = false;
            Change(s => { s.ManualWeatherLocation = false; s.WeatherEnabled = true; });
            SetText(here); UpdateSave(); Describe();
            await _app.RequestWeatherLocation();
        };

        var searchRow = new DockPanel { Children = { results } };
        DockPanel.SetDock(save, Dock.Right);
        searchRow.Children.Add(save); searchRow.Children.Add(place);
        var disclosure = TextLine(Loc.T("预报来自 Open-Meteo。只有地点会离开这台电脑；自动定位还会把大致坐标发给 BigDataCloud，用来查城市名称。",
            "Forecasts come from Open-Meteo. Only the place leaves this PC; locating you also sends rough coordinates to BigDataCloud to name the city."), 12, TextTertiary);
        disclosure.TextWrapping = TextWrapping.Wrap;
        disclosure.Margin = new Thickness(0, 10, 0, 0);
        var placeField = Field(Loc.T("地点", "Place"), new StackPanel { Children = { searchRow, status, locate, disclosure } }, "");

        var looks = new UniformGrid { Columns = 2, Width = 210, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (colourful, name) in new[] { (false, Loc.T("简约", "Minimal")), (true, Loc.T("多彩", "Colourful")) })
        {
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = name, IsChecked = S.ColourfulWeatherCard == colourful, GroupName = "WeatherLook",
                FontSize = 13, Padding = new Thickness(8, 7, 8, 7) };
            AutomationProperties.SetName(choice, Loc.T("天气外观 ", "Weather look ") + name);
            choice.Checked += (_, _) => Change(s => s.ColourfulWeatherCard = colourful);
            looks.Children.Add(choice);
        }
        var look = SegmentBorder(looks);

        // Where the data comes from, on one quiet line.
        var sources = new WrapPanel { Margin = new Thickness(0, 4, 0, 18) };
        var from = TextLine(Loc.T("数据来源", "Data from"), 12, TextTertiary);
        from.VerticalAlignment = VerticalAlignment.Center;
        from.Margin = new Thickness(0, 0, 8, 0);
        sources.Children.Add(from);
        foreach (var (name, url) in new[] { ("Open-Meteo (CC BY 4.0)", Weather.Attribution), ("GeoNames", Weather.LocationAttribution), ("BigDataCloud", Weather.AutomaticLocationAttribution) })
        {
            var link = Link(name + " ↗", url); link.FontSize = 12; link.Margin = new Thickness(0, 0, 14, 0);
            sources.Children.Add(link);
        }

        return OptionsCard(WidgetLayout.Name(WidgetKind.Weather), Loc.T("当前天气，以及今天的最低和最高气温。", "Current conditions, with today's low and high."),
            placeField,
            Rule(),
            SwitchRow(Loc.T("华氏度", "Fahrenheit"), Loc.T("用 °F 显示所有温度，而不是 °C：天气，以及系统监控里 CPU、GPU 和硬盘的温度。", "Show every temperature in °F instead of °C: the weather, and the CPU, GPU and drive temperatures in the system monitor."), S.WeatherFahrenheit, value => Change(s => s.WeatherFahrenheit = value)),
            Rule(),
            ChoiceRow(Loc.T("天气外观", "Weather look"), Loc.T("简约使用中性底色；多彩按天气状况给当前天气和小组件着色。", "Minimal uses neutral cards. Colourful tints the current conditions and widget to match the weather."), look),
            sources);
    }

    private void WeatherSizeControls() => PageSizeControls(DeckPage.Weather, Loc.T("天气", "Weather"));
}
