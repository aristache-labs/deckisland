using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Media;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

public partial class MainWindow
{
    private DispatcherTimer? _widgetTick;
    private string _widgetKey = "";
    private readonly List<Action> _widgetUpdates = [];
    // Every widget card keeps this much room at its left and right edges.
    private const double WidgetSide = 22;

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
            DevicesContent.Visibility = WeatherContent.Visibility = RemindersContent.Visibility = NotificationsContent.Visibility = CallsContent.Visibility = ScratchpadContent.Visibility = ConverterContent.Visibility = TerminalContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        double height = ContentCanvas.Height;
        if (!double.IsFinite(height)) return;
        ApplyHeader(PanelHeader.For(ToolbarFor(height), 14));
        WidgetsContent.Margin = new Thickness(_header.Side, 0, _header.Side, MonitorBottom(height));
        var slots = Settings.WidgetSlots;
        string key = $"{string.Join(',', slots)}:{Look.Generation}:{Loc.Language}:{Settings.RemindersPage}:{Settings.WeatherEnabled}:{Settings.UsagePage}:{Settings.MonitorPage}:{Settings.DevicesPage}:{Settings.WeatherPage}:{Settings.WeatherFahrenheit}:{Settings.NetworkMegabits}:{Settings.TimePattern}:{string.Join('|', Settings.WorldClocks)}:{Settings.CountdownTitle}:{Settings.CountdownDate}:{_pageWidth:0}";
        if (slots.Contains(WidgetKind.Monitor)) key += $":{MonitorThemeColors.For(Settings, Look)}";
        if (_widgetKey != key || WidgetsContent.Children.Count == 0)
        {
            _widgetKey = key;
            _widgetUpdates.Clear();
            WidgetsContent.Children.Clear();
            var grid = new UniformGrid { Columns = 2, Rows = 2 };
            const double Bell = 44, Outer = 5, Inner = 4;
            for (int i = 0; i < slots.Length; i++)
            {
                var card = WidgetCard(slots[i]);
                // A tight gap; the round centre button overlaps only the inner corners.
                if (Settings.RemindersPage)
                    card.Margin = new Thickness(i % 2 == 0 ? Outer : Inner, i < 2 ? Outer : Inner, i % 2 == 0 ? Inner : Outer, i < 2 ? Inner : Outer);
                grid.Children.Add(card);
            }
            WidgetsContent.Children.Add(grid);
            if (Settings.RemindersPage)
            {
                var glyph = WidgetGlyph("M3,11 C4.5,9.5 4.5,8.5 4.5,6.5 A3.5,3.5 0 0 1 11.5,6.5 C11.5,8.5 11.5,9.5 13,11 Z M6.5,13.5 Q8,15.5 9.5,13.5",
                    UI.Brush(Look.Primary), filled: false);
                // The bell drawn larger inside the same circle: scaled where it is drawn, so its place and the circle stay as they are; its
                // line thinned by the same factor, so it is not heavier than the other glyphs.
                const double BellGlyphScale = 1.8;
                glyph.RenderTransformOrigin = new Point(.5, .5);
                glyph.RenderTransform = new ScaleTransform(BellGlyphScale, BellGlyphScale);
                if (glyph is Canvas { Children: [System.Windows.Shapes.Path line] }) line.StrokeThickness = 1.5 / BellGlyphScale * 1.15;
                var countdown = WeatherText("", 13);
                countdown.FontWeight = FontWeights.SemiBold;
                countdown.HorizontalAlignment = HorizontalAlignment.Center;
                countdown.VerticalAlignment = VerticalAlignment.Center;
                Typography.SetNumeralAlignment(countdown, FontNumeralAlignment.Tabular);
                var face = new Grid { Children = { new System.Windows.Shapes.Ellipse { Stroke = UI.Brush(Look.Hex("popupBorder")), }, glyph, countdown } };
                string label = Loc.T("提醒", "Reminders");
                var bell = WidgetSurface(face, label, () => SelectPage(DeckPage.Reminders));
                bell.ToolTip = label;
                // The bell shows the time left to the soonest reminder instead of the glyph, when one is waiting.
                _widgetUpdates.Add(() =>
                {
                    var now = DateTimeOffset.Now;
                    var next = Settings.Reminders.Where(reminder => reminder.Due > now).MinBy(reminder => reminder.Due);
                    glyph.Visibility = next is null ? Visibility.Visible : Visibility.Collapsed;
                    countdown.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
                    if (next is null) { countdown.Text = ""; AutomationProperties.SetName(bell, label); bell.ToolTip = label; return; }
                    var left = next.Due - now;
                    countdown.Text = left.TotalMinutes < 60 ? $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m"
                        : left.TotalHours < 24 ? $"{(int)Math.Ceiling(left.TotalHours)}h" : $"{(int)Math.Ceiling(left.TotalDays)}d";
                    string full = $"{label} · {next.Title} · {countdown.Text}";
                    AutomationProperties.SetName(bell, full);
                    bell.ToolTip = full;
                });
                bell.Height = bell.Width = Bell;
                bell.Background = UI.Brush(Look.Hex("popup"));
                bell.HorizontalAlignment = HorizontalAlignment.Center;
                bell.VerticalAlignment = VerticalAlignment.Center;
                bell.Clip = new EllipseGeometry(new Point(Bell / 2, Bell / 2), Bell / 2, Bell / 2);
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

    // The settings gallery shows each widget as the island draws it: built once, filled once, and not kept on the tick.
    internal (FrameworkElement Card, string Surface) WidgetPreview(WidgetKind kind)
    {
        int start = _widgetUpdates.Count;
        var card = WidgetCard(kind);
        var added = _widgetUpdates.GetRange(start, _widgetUpdates.Count - start);
        _widgetUpdates.RemoveRange(start, added.Count);
        foreach (var update in added) update();
        card.Width = 290; card.Height = 132;
        card.IsHitTestVisible = false; card.Focusable = false;
        return (card, Look.Hex("surface"));
    }

    private FrameworkElement WidgetCard(WidgetKind kind)
    {
        var frame = new Border { Margin = new Thickness(5), Padding = new Thickness(WidgetSide, 7, WidgetSide, 7), CornerRadius = new CornerRadius(Corner(12)),
            Background = UI.Brush(Look.Hex("fill")), BorderBrush = UI.Brush(Look.Hex("rim")), BorderThickness = new Thickness(1), ClipToBounds = true };
        if (kind == WidgetKind.Empty) return frame;
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        frame.Child = body;
        var (page, settingsPage) = kind switch
        {
            WidgetKind.Weather => (DeckPage.Weather, SettingsWindow.WeatherSettingsPage),
            WidgetKind.Monitor => (DeckPage.Monitor, SettingsWindow.MonitorSettingsPage),
            WidgetKind.Devices => (DeckPage.Devices, SettingsWindow.DevicesSettingsPage),
            _ => (DeckPage.Usage, 3)
        };
        string name = WidgetLayout.Name(kind);
        string configureLabel = Loc.T("设置", "Configure");
        bool detailed = DeckPages.Shown(Settings, page);
        bool setup = kind == WidgetKind.Weather && !Settings.WeatherEnabled ||
            kind == WidgetKind.Quotas && Settings.ConfiguredApps.Count == 0;
        if (kind == WidgetKind.Monitor) return MonitorWidget(frame, detailed);
        if (kind == WidgetKind.Today) return TodayWidget(frame);
        if (kind == WidgetKind.Clock) return ClockWidget(frame);
        if (kind == WidgetKind.Countdown) return CountdownWidget(frame);
        if (kind == WidgetKind.Todo) return TodoWidget(frame);
        if (kind == WidgetKind.Note) return NoteWidget(frame);
        if (kind == WidgetKind.ScreenTime) return ScreenTimeWidget(frame);
        if (kind == WidgetKind.SunMoon) return SunMoonWidget(frame);
        if (kind == WidgetKind.Progress) return ProgressWidget(frame);
        if (kind == WidgetKind.Devices) return DevicesWidget(frame, detailed);
        if (!setup && kind == WidgetKind.Quotas) return QuotasWidget(frame, detailed);
        if (!setup && kind == WidgetKind.Weather) return WeatherWidget(frame, detailed);
        if (kind == WidgetKind.Focus) return FocusWidget(frame);
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
        return frame;
    }

    // card: the button is a whole widget card, so its hover covers the card in the card's own corner.
    private Button WidgetSurface(object content, string label, Action action, CornerRadius? card = null)
    {
        var button = WidgetAction(label, action);
        button.Style = (Style)FindResource(card is null ? "WidgetSurfaceButton" : "WidgetCardSurfaceButton");
        if (card is { } corner)
        {
            button.Tag = corner;
            // A card's hover only lightens (or, on a light panel, darkens) the colour it already has, never the theme's own hover fill.
            button.Resources["PanelHover"] = new SolidColorBrush(Look.Current.IsDark ? Color.FromArgb(0x1C, 255, 255, 255) : Color.FromArgb(0x14, 0, 0, 0));
        }
        button.Content = content;
        return button;
    }

    private static FrameworkElement WidgetGlyph(string geometry, Brush ink, bool filled = true)
    {
        var canvas = new Canvas { Width = 16, Height = 16, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometry), Fill = filled ? ink : null, Stroke = filled ? null : ink,
            StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        });
        return canvas;
    }

    private Border WeatherWidget(Border frame, bool detailed)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        var baseFill = frame.Background;
        var ink = UI.Brush(Look.Primary);
        var temperature = WeatherText("", 32);
        temperature.FontWeight = FontWeights.SemiBold;
        var sky = new Border { Width = 35, Height = 35, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        // The sky drawn a little larger than its box, from its centre: the temperature and the rows around keep their places.
        sky.RenderTransformOrigin = new Point(.5, .5); sky.RenderTransform = new ScaleTransform(1.22, 1.22);
        DockPanel.SetDock(sky, System.Windows.Controls.Dock.Left);
        // At the foot, left: how it feels, the humidity and the wind, in one small line.
        var feels = WeatherText("", 12);
        feels.TextWrapping = TextWrapping.NoWrap; feels.Visibility = Visibility.Collapsed;
        // At the right, the next hours as the Weather page's strip has them: the hour, its sky and its temperature. As many as the
        // widget's width has room for, none on a narrow one.
        var hours = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var hoursRoom = new Border { Child = hours, Margin = new Thickness(16, 0, 0, 0), Background = Brushes.Transparent };

        var where = WeatherText("", 12);
        where.TextWrapping = TextWrapping.NoWrap; where.FontWeight = FontWeights.SemiBold;
        var pin = new System.Windows.Shapes.Path { Data = Geometry.Parse("M8,14 C5,10 3,8 3,5 A5,5 0 0 1 13,5 C13,8 11,10 8,14 Z M10,5 A2,2 0 1 1 6,5 A2,2 0 1 1 10,5"),
            StrokeThickness = 1.3, Width = 10, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        var placeRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { pin, where }, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 2) };
        // The condition, under the sky and the temperature.
        var place = WeatherText("", 12);
        place.TextWrapping = TextWrapping.NoWrap; place.Margin = new Thickness(0, 2, 0, 0); place.FontWeight = FontWeights.SemiBold;
        var now = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new DockPanel { Children = { sky, temperature } }, place } };
        var headline = new Grid();
        headline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headline.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(hoursRoom, 1);
        headline.Children.Add(now); headline.Children.Add(hoursRoom);
        var range = WeatherText("", 12);
        range.FontWeight = FontWeights.SemiBold;
        range.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(range, System.Windows.Controls.Dock.Right);
        var footer = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { range, feels } };
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(headline, 1); Grid.SetRow(footer, 2);
        body.Children.Add(placeRow);
        body.Children.Add(headline);
        body.Children.Add(footer);
        var trigger = WidgetSurface(body, Loc.T("打开天气", "Open weather"), () =>
        {
            if (detailed && _app.CurrentWeather is not null) SelectPage(DeckPage.Weather);
            else _app.OpenSettingsPage(SettingsWindow.WeatherSettingsPage);
        }, frame.CornerRadius);
        frame.Padding = new Thickness(0);
        trigger.Padding = new Thickness(WidgetSide, 8, WidgetSide, 10);
        frame.Child = trigger;

        (string Icon, bool Colourful, bool Motion)? drawn = null;
        // A resize changes how many hours fit.
        hoursRoom.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged && _app.CurrentWeather is { } shown) WeatherWidgetHours(hours, shown, temperature.Foreground, drawn?.Colourful == true, hoursRoom.ActualWidth);
        };
        _widgetUpdates.Add(() =>
        {
            if (_app.CurrentWeather is not { } reading)
            {
                temperature.FontSize = 15;
                temperature.Text = _app.WeatherLoading ? Loc.T("正在加载天气…", "Loading weather…")
                    : _app.WeatherNeedsLocation ? Loc.T("选择城市", "Choose a city") : Loc.T("天气不可用", "Weather unavailable");
                place.Text = _app.WeatherLoading ? "" : Loc.T("天气设置", "Weather settings");
                sky.Visibility = Visibility.Collapsed;
                range.Text = "";
                feels.Visibility = Visibility.Collapsed; hours.Children.Clear(); placeRow.Visibility = Visibility.Collapsed;
                frame.Background = baseFill;
                temperature.Foreground = place.Foreground = range.Foreground = ink;
                AutomationProperties.SetName(trigger, Loc.T("打开天气设置", "Open weather settings"));
                trigger.ClearValue(ToolTipProperty);
                drawn = null;
                return;
            }
            var (icon, condition) = WeatherCondition(reading.Code, reading.Daylight);
            var appearance = (Icon: icon, Colourful: Settings.ColourfulWeatherCard && !Look.HighContrast, Motion: Animates);
            if (drawn != appearance)
            {
                drawn = appearance;
                var (background, foreground) = appearance.Colourful ? WeatherCardColours(icon) : (baseFill, ink);
                frame.Background = background;
                temperature.Foreground = place.Foreground = range.Foreground = foreground;
                var graphic = Look.HighContrast ? null : ColourfulWeatherIcon(icon, appearance.Motion);
                sky.Child = new Viewbox { Child = graphic ?? WeatherLineIcon(icon, 32, foreground) };
                sky.Visibility = Visibility.Visible;
            }
            string location = WeatherPlace(reading);
            temperature.FontSize = 32;
            temperature.Text = WeatherTemperature(reading.Temperature);
            place.Text = condition;
            where.Text = location; where.Foreground = temperature.Foreground; pin.Stroke = temperature.Foreground;
            placeRow.Visibility = Visibility.Visible; placeRow.ToolTip = location;
            range.Text = WeatherExtremes(reading.Days.FirstOrDefault(), abbreviated: true);
            var details = new List<string>();
            if (reading.FeelsLike is { } feelsLike) details.Add(Loc.T("体感 ", "Feels ") + WeatherTemperature(feelsLike));
            if (reading.Humidity is { } humidity) details.Add($"{humidity:0}%");
            if (reading.WindSpeed is { } wind) details.Add($"{wind:0} km/h");
            // The separators as heavier dots than the text's own "·", at the usual spacing, so they can be seen.
            feels.Inlines.Clear();
            for (int i = 0; i < details.Count; i++)
            {
                if (i > 0) feels.Inlines.Add(new System.Windows.Documents.Run(" • ") { FontWeight = FontWeights.Bold, FontSize = feels.FontSize + 1.5 });
                feels.Inlines.Add(new System.Windows.Documents.Run(details[i]));
            }
            feels.Visibility = details.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            feels.Foreground = temperature.Foreground; feels.Opacity = .85;
            WeatherWidgetHours(hours, reading, temperature.Foreground, appearance.Colourful, hoursRoom.ActualWidth);
            if (_app.WeatherStale) place.Text += " *";
            string action = detailed ? Loc.T("打开天气", "Open weather") : Loc.T("打开天气设置", "Open weather settings");
            string stale = _app.WeatherStale ? $" · {Loc.T("数据未更新", "Weather data is stale")}" : "";
            AutomationProperties.SetName(trigger, $"{action} · {WeatherTemperature(reading.Temperature, true)} · {condition} · {location} · {range.Text}{stale}");
            trigger.ToolTip = $"{condition} · {location}{stale}";
        });
        return frame;
    }

    // The widget's next hours: from the hour after this one, each a column of the hour, its sky and its temperature; as many as the
    // room right of the temperature holds (WidgetHourWidth each), up to WidgetHours, none when it has no room for one.
    private const int WidgetHours = 8;
    private const double WidgetHourWidth = 40;
    private void WeatherWidgetHours(System.Windows.Controls.Primitives.UniformGrid hours, WeatherReading reading, Brush ink, bool colourful, double width)
    {
        if (width <= 0) return;
        int room = (int)Math.Clamp(width / WidgetHourWidth, 0, WidgetHours);
        var now = DateTime.Now;
        var next = reading.Hours.Where(hour => hour.Time > now).Take(room).ToList();
        hours.Children.Clear();
        hours.Columns = Math.Max(1, next.Count);
        hours.Visibility = next.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var hour in next)
        {
            var (icon, condition) = WeatherCondition(hour.Code, hour.Daylight);
            var label = WeatherText(hour.Time.ToString(Settings.Uses24HourClock ? "HH" : "h tt", System.Globalization.CultureInfo.InvariantCulture), 11);
            label.Foreground = ink; label.Opacity = .75; label.HorizontalAlignment = HorizontalAlignment.Center;
            var picture = colourful && !Look.HighContrast ? ColourfulWeatherIcon(icon, false) : null;
            var mark = new Viewbox { Width = 20, Height = 20, Margin = new Thickness(0, 2, 0, 2), Child = picture ?? WeatherLineIcon(icon, 18, ink) };
            var degrees = WeatherText(hour.Temperature is { } t ? WeatherTemperature(t) : "—", 12);
            degrees.Foreground = ink; degrees.FontWeight = FontWeights.SemiBold; degrees.HorizontalAlignment = HorizontalAlignment.Center;
            var column = new StackPanel { Width = WidgetHourWidth, Children = { label, mark, degrees } };
            AutomationProperties.SetName(column, $"{label.Text} · {condition} · {degrees.Text}");
            hours.Children.Add(column);
        }
    }

    private Border FocusWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        frame.Padding = new Thickness(WidgetSide, 6, WidgetSide, 6);
        var face = new Grid { Width = 62, Height = 62, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var dial = new FocusDial { IsHitTestVisible = false };
        var reading = WeatherText("", 14);
        reading.HorizontalAlignment = HorizontalAlignment.Center;
        reading.FontWeight = FontWeights.SemiBold;
        Typography.SetNumeralAlignment(reading, FontNumeralAlignment.Tabular);
        face.Children.Add(dial);
        face.Children.Add(reading);

        var heading = WeatherText("", 15);
        heading.FontWeight = FontWeights.SemiBold;
        var detail = WeatherText("", 11);
        detail.Foreground = UI.Brush(Look.Secondary);
        var controls = BuildFocusControls();
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(heading);
        content.Children.Add(detail);
        content.Children.Add(controls.View);
        frame.Child = new DockPanel { Children = { face, content } };
        _widgetUpdates.Add(() =>
        {
            var session = _app.FocusSession;
            bool idle = session.Phase == FocusPhase.Idle;
            reading.Text = FocusTime(idle ? TimeSpan.FromMinutes(Settings.FocusMinutes) : session.Remaining(DateTimeOffset.UtcNow));
            heading.Text = FocusPhaseLabel();
            detail.Text = idle ? Loc.T($"{Settings.FocusMinutes} + {Settings.BreakMinutes} 分钟", $"{Settings.FocusMinutes} + {Settings.BreakMinutes} min")
                : session.Running ? Loc.T("进行中", "In progress") : Loc.T("已暂停", "Paused");
            dial.Update(session, Animates);
            controls.Update(session);
        });
        return frame;
    }

    private (StackPanel View, Action<FocusTimer> Update) BuildFocusControls()
    {
        var primary = UI.Brush(Look.Primary);
        var play = WidgetGlyph("M5,2.5 L13,8 L5,13.5 Z", UI.Brush(Look.Hex("surface")));
        var pause = WidgetGlyph("M4,3 H6.5 V13 H4 Z M9.5,3 H12 V13 H9.5 Z", UI.Brush(Look.Hex("surface")));
        var toggle = RoundAction(play, () => _app.ToggleFocus(Profile), true);
        var reset = RoundAction(WidgetGlyph("M3,7 A5,5 0 1 1 4.5,11.5 M3,3.5 V7 H6.5", primary, filled: false), _app.ResetFocus);
        var less = RoundAction("−", () => AdjustFocus(-1));
        var more = RoundAction("+", () => AdjustFocus(1));
        AutomationProperties.SetName(reset, Loc.T("重置", "Reset timer"));
        AutomationProperties.SetName(less, Loc.T("减少一分钟", "Decrease focus by one minute"));
        AutomationProperties.SetName(more, Loc.T("增加一分钟", "Increase focus by one minute"));
        reset.ToolTip = AutomationProperties.GetName(reset);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0),
            Children = { toggle, less, more, reset } };
        return (actions, session =>
        {
            bool idle = session.Phase == FocusPhase.Idle;
            toggle.Content = session.Running ? pause : play;
            string action = idle ? Loc.T("开始", "Start") : session.Running ? Loc.T("暂停", "Pause") : Loc.T("继续", "Resume");
            AutomationProperties.SetName(toggle, action);
            toggle.ToolTip = action;
            reset.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
            less.Visibility = more.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
            less.IsEnabled = Settings.FocusMinutes > FocusTimer.MinimumMinutes;
            more.IsEnabled = Settings.FocusMinutes < FocusTimer.MaximumMinutes;
        });

        Button RoundAction(object glyph, Action action, bool main = false)
        {
            var button = WidgetSurface(glyph, "", action);
            button.Width = button.Height = 27;
            button.Margin = new Thickness(0, 0, 4, 0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Background = main ? primary : UI.Brush(Look.Hex("fillSelected"));
            button.Foreground = main ? UI.Brush(Look.Hex("surface")) : primary;
            button.FontSize = 14;
            return button;
        }
    }

    private CompactPart[]? MinimalParts()
    {
        var style = EffectiveStyle;
        if (!Settings.Minimal(style)) return null;
        bool capsule = style == CompactStyle.Capsule;
        return WidgetLayout.MinimalParts((Settings.ShowsFocus(style) || Settings.FocusSimplifiesCompact(style)) && _app.FocusSession.Phase != FocusPhase.Idle,
            !FocusCompactOnly && Settings.ShowsTransfer(style) && (_draggingOut || _dropping) && ShelfData.Files.Count > 0,
            !FocusCompactOnly && Settings.ShowsMusic(style) && _mediaTrack is { State: DeckIsland.Core.MediaState.Playing },
            capsule ? Settings.CapsuleMinimalFirst : Settings.NotchMinimalFirst,
            capsule ? Settings.CapsuleMinimalSecond : Settings.NotchMinimalSecond);
    }
}
