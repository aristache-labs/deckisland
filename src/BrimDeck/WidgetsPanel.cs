using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using BrimDeck.Core;
using BrimDeck.Native;

namespace BrimDeck;

public partial class MainWindow
{
    private DispatcherTimer? _widgetTick;
    private string _widgetKey = "";
    private readonly List<Action> _widgetUpdates = [];

    private void WidgetsPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Widgets);

    internal bool WidgetVisible(WidgetKind kind) => _expanded && !_preview && CurrentPage == DeckPage.Widgets &&
        IsVisible && !IsSuppressed && !DisplayPower.Off && ReminderAlertId is null && Settings.WidgetSlots.Contains(kind);

    private void UpdateWidgetTimer()
    {
        if (_widgetTick is not null)
            _widgetTick.IsEnabled = _expanded && CurrentPage == DeckPage.Widgets && IsVisible && !IsSuppressed && !DisplayPower.Off && ReminderAlertId is null;
    }

    private void RenderWidgets()
    {
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility = ShelfContent.Visibility =
            DevicesContent.Visibility = WeatherContent.Visibility = FocusContent.Visibility = RemindersContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        double height = ContentCanvas.Height;
        if (!double.IsFinite(height)) return;
        ApplyHeader(PanelHeader.For(ToolbarFor(height), 14));
        WidgetsContent.Margin = new Thickness(_header.Side, 0, _header.Side, MonitorBottom(height));
        var slots = Settings.WidgetSlots;
        string key = $"{string.Join(',', slots)}:{Look.Generation}:{Loc.Language}:{Settings.RemindersPage}:{Settings.FocusPage}:{Settings.WeatherEnabled}:{Settings.UsagePage}:{Settings.MonitorPage}:{Settings.DevicesPage}:{Settings.WeatherPage}:{_pageWidth:0}";
        if (_widgetKey != key || WidgetsContent.Children.Count == 0)
        {
            _widgetKey = key;
            _widgetUpdates.Clear();
            WidgetsContent.Children.Clear();
            var grid = new UniformGrid { Columns = 2, Rows = 2 };
            foreach (var kind in slots) grid.Children.Add(WidgetCard(kind));
            WidgetsContent.Children.Add(grid);
            if (Settings.RemindersPage)
            {
                var bell = WidgetAction("", () => SelectPage(DeckPage.Reminders));
                bell.Content = new System.Windows.Shapes.Path
                {
                    Data = System.Windows.Media.Geometry.Parse("M 2,11 C 4,9 4,8 4,6 A 4,4 0 0 1 12,6 C 12,8 12,9 14,11 Z M 6,14 A 2,2 0 0 0 10,14"),
                    Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.4, Width = 16, Height = 16
                };
                AutomationProperties.SetName(bell, Loc.T("提醒", "Reminders"));
                bell.Height = 34; bell.Width = 34; bell.Padding = new Thickness(0);
                bell.HorizontalAlignment = HorizontalAlignment.Center;
                bell.VerticalAlignment = VerticalAlignment.Center;
                bell.Background = UI.Brush(Look.Hex("popup"));
                WidgetsContent.Children.Add(bell);
            }
        }
        if (_widgetTick is null)
        {
            _widgetTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _widgetTick.Tick += (_, _) =>
            {
                UpdateWidgetTimer();
                if (_widgetTick.IsEnabled) foreach (var update in _widgetUpdates) update();
            };
            Closed += (_, _) => _widgetTick.Stop();
        }
        foreach (var update in _widgetUpdates) update();
        UpdateWidgetTimer();
    }

    private Button WidgetAction(string label, Action action)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("PanelOutlineButton"), Foreground = UI.Brush(Look.Primary) };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private FrameworkElement WidgetCard(WidgetKind kind)
    {
        var frame = new Border { Margin = new Thickness(5), Padding = new Thickness(11, 7, 11, 7), CornerRadius = new CornerRadius(12),
            Background = UI.Brush(Look.Hex("fill")), BorderBrush = UI.Brush(Look.Hex("rim")), BorderThickness = new Thickness(1), ClipToBounds = true };
        if (kind == WidgetKind.Empty) return frame;
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        frame.Child = body;
        var (page, settingsPage) = kind switch
        {
            WidgetKind.Weather => (DeckPage.Weather, SettingsWindow.WeatherSettingsPage),
            WidgetKind.Monitor => (DeckPage.Monitor, SettingsWindow.MonitorSettingsPage),
            WidgetKind.Focus => (DeckPage.Focus, SettingsWindow.FocusSettingsPage),
            WidgetKind.Devices => (DeckPage.Devices, SettingsWindow.DevicesSettingsPage),
            _ => (DeckPage.Usage, 3)
        };
        string name = WidgetLayout.Name(kind);
        string configureLabel = Loc.T("设置", "Configure");
        bool detailed = DeckPages.Shown(Settings, page);
        bool setup = kind == WidgetKind.Weather && !Settings.WeatherEnabled || kind == WidgetKind.Focus && !Settings.FocusPage ||
            kind == WidgetKind.Quotas && Settings.ConfiguredApps.Count == 0;
        if (detailed || setup)
        {
            var link = WidgetAction(setup ? $"{name} · {configureLabel}" : name,
                () => { if (setup) _app.OpenSettingsPage(settingsPage); else SelectPage(page); });
            link.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(link);
        }
        else body.Children.Add(WeatherText(name, 12));
        if (setup) return frame;

        var value = WeatherText("", 16);
        var detail = WeatherText("", 12);
        detail.Foreground = UI.Brush(Look.Secondary);
        body.Children.Add(value);
        body.Children.Add(detail);
        if (kind == WidgetKind.Weather)
        {
            body.Children.Remove(value);
            var sky = new Border { Margin = new Thickness(0, 0, 7, 0) };
            body.Children.Insert(body.Children.IndexOf(detail), new StackPanel
            {
                Orientation = Orientation.Horizontal, Children = { sky, value }
            });
            (string Icon, bool Colourful)? drawn = null;
            var configure = WidgetAction(configureLabel, () => _app.OpenSettingsPage(settingsPage));
            configure.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(configure);
            _widgetUpdates.Add(() =>
            {
                configure.Visibility = _app.WeatherNeedsLocation ? Visibility.Visible : Visibility.Collapsed;
                if (_app.CurrentWeather is not { } reading)
                {
                    value.Text = _app.WeatherLoading ? Loc.T("正在加载天气…", "Loading weather…") : Loc.T("天气不可用", "Weather unavailable");
                    sky.Child = null; drawn = null; detail.Text = ""; return;
                }
                var (icon, condition) = WeatherCondition(reading.Code, reading.Daylight);
                var appearance = (icon, Settings.ColourfulWeatherCard);
                if (drawn != appearance)
                {
                    drawn = appearance;
                    sky.Child = (appearance.ColourfulWeatherCard ? ColourfulWeatherIcon(icon, Animates) : null)
                        ?? WeatherLineIcon(icon, 20, value.Foreground);
                    sky.Width = sky.Height = 24;
                }
                value.Text = $"{WeatherTemperature(reading.Temperature, true)}{(_app.WeatherStale ? "*" : "")} · {WeatherExtremes(reading.Days.FirstOrDefault(), abbreviated: true)}";
                detail.Text = $"{condition} · {WeatherPlace(reading)}";
            });
        }
        else if (kind == WidgetKind.Monitor)
        {
            _widgetUpdates.Add(() =>
            {
                var latest = _app.Monitor?.Latest;
                var memory = latest?.Memory.OrDefault;
                string percent(double? share) => share is { } reading ? $"{reading:P0}" : "–";
                value.Text = $"CPU  {percent(latest?.Cpu.OrDefault?.Usage)}    GPU  {percent(latest?.Gpu.OrDefault?.Usage)}";
                detail.Text = $"RAM  {percent(memory is { Total: > 0 } ? (double)memory.Used / memory.Total : null)}";
            });
        }
        else if (kind == WidgetKind.Devices)
        {
            _widgetUpdates.Add(() =>
            {
                var devices = ShownDevices.Where(device => device.Percent is not null).Take(2).ToArray();
                value.Text = devices.Length == 0 ? Loc.T("没有电量读数", "No battery readings") : $"{devices[0].Name} · {devices[0].Percent}%";
                detail.Text = devices.Length < 2 ? "" : $"{devices[1].Name} · {devices[1].Percent}%";
            });
        }
        else if (kind == WidgetKind.Focus)
        {
            var toggle = WidgetAction("", () => _app.ToggleFocus(Profile));
            var reset = WidgetAction(Loc.T("重置", "Reset"), _app.ResetFocus);
            var less = WidgetAction("−", () => AdjustFocus(-1));
            var more = WidgetAction("+", () => AdjustFocus(1));
            AutomationProperties.SetName(less, Loc.T("减少一分钟", "Decrease focus by one minute"));
            AutomationProperties.SetName(more, Loc.T("增加一分钟", "Increase focus by one minute"));
            body.Children.Add(new WrapPanel { Children = { toggle, reset, less, more } });
            _widgetUpdates.Add(() =>
            {
                var session = _app.FocusSession;
                bool idle = session.Phase == FocusPhase.Idle;
                value.Text = $"{FocusTime(idle ? TimeSpan.FromMinutes(Settings.FocusMinutes) : session.Remaining(DateTimeOffset.UtcNow))} · {FocusPhaseLabel()}";
                toggle.Content = idle ? Loc.T("开始", "Start") : session.Running ? Loc.T("暂停", "Pause") : Loc.T("继续", "Resume");
                AutomationProperties.SetName(toggle, (string)toggle.Content);
                reset.IsEnabled = !idle;
                less.IsEnabled = idle && Settings.FocusMinutes > FocusTimer.MinimumMinutes;
                more.IsEnabled = idle && Settings.FocusMinutes < FocusTimer.MaximumMinutes;
            });
        }
        else
        {
            body.Children.Remove(value); body.Children.Remove(detail);
            var host = new Border { HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(host);
            string last = "";
            _widgetUpdates.Add(() =>
            {
                var rings = QuotaRings(Settings.ConfiguredApps).Take(4).ToArray();
                string next = string.Join('|', rings.Select(ring => $"{ring.Color}:{ring.Tip}:{ring.Letter}:{ring.Stale}:{string.Join(',', ring.Shares)}"));
                if (last == next) return;
                last = next;
                host.Child = new QuotaCarousel(rings, 26, 2.5, 1, 7, rings.Length, 2, animate: false);
            });
        }
        return frame;
    }

    private CompactPart[]? MinimalParts()
    {
        var style = EffectiveStyle;
        if (!Settings.Minimal(style)) return null;
        bool capsule = style == CompactStyle.Capsule;
        return WidgetLayout.MinimalParts((Settings.ShowsFocus(style) || Settings.FocusSimplifiesCompact(style)) && _app.FocusSession.Phase != FocusPhase.Idle,
            !FocusCompactOnly && Settings.ShowsTransfer(style) && (_draggingOut || _dropping) && ShelfData.Files.Count > 0,
            !FocusCompactOnly && Settings.ShowsMusic(style) && _mediaTrack is { State: BrimDeck.Core.MediaState.Playing },
            capsule ? Settings.CapsuleMinimalFirst : Settings.NotchMinimalFirst,
            capsule ? Settings.CapsuleMinimalSecond : Settings.NotchMinimalSecond);
    }
}
