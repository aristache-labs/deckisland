using System.Globalization;
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
    private Popup? _widgetPopup;
    private Action? _widgetPopupUpdate;
    private FrameworkElement? _dismissedAnchor;
    private long _dismissedAt;

    private static bool HasWidgetDetails(DockWidgetKind kind) => kind is DockWidgetKind.Calendar or DockWidgetKind.Date
        or DockWidgetKind.Today or DockWidgetKind.Weather or DockWidgetKind.ClockWeather;

    private void CloseWidgetDetails()
    {
        if (_widgetPopup is not { } popup) return;
        _widgetPopup = null;
        _widgetPopupUpdate = null;
        popup.IsOpen = false;
    }

    private void ToggleWidgetDetails(FrameworkElement anchor, DockWidgetKind kind)
    {
        bool same = _widgetPopup?.PlacementTarget == anchor;
        CloseWidgetDetails();
        if (same || _dismissedAnchor == anchor && Environment.TickCount64 - _dismissedAt < 350) return;
        _dismissedAnchor = null;
        _preview.HideNow();

        bool weather = kind is DockWidgetKind.Weather or DockWidgetKind.ClockWeather;
        var (content, update) = weather ? WeatherDetails() : CalendarDetails();
        string title = weather ? Loc.T("天气", "Weather") : Loc.T("日历", "Calendar");
        var heading = Label(title, 16, bold: true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        var close = DetailButton("×", Loc.T("关闭", "Close"), CloseWidgetDetails);
        close.Width = 30;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
        header.Children.Add(close);
        header.Children.Add(heading);
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(content);
        var card = new Border
        {
            Width = 340, Padding = new Thickness(14), CornerRadius = new CornerRadius(14),
            Background = UI.Brush(_look.Text == "#FF1D1D1F" ? "#FFF8F8FA" : "#FF202024"),
            BorderBrush = UI.Brush(_look.CardRim), BorderThickness = new Thickness(1), Child = stack
        };
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            CloseWidgetDetails();
            e.Handled = true;
        };
        bool top = _app.DockSettings.Edge == DockEdge.Top;
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = top ? PlacementMode.Bottom : PlacementMode.Top,
            VerticalOffset = top ? 8 : -8, AllowsTransparency = true, StaysOpen = false, Child = card
        };
        popup.Opened += (_, _) => close.Focus();
        popup.Closed += (_, _) =>
        {
            if (_widgetPopup != popup) return;
            // WPF releases capture before the click on the anchor is delivered to its tile.
            var point = Mouse.GetPosition(anchor);
            if (Mouse.LeftButton == MouseButtonState.Pressed && new Rect(anchor.RenderSize).Contains(point))
            {
                _dismissedAnchor = anchor;
                _dismissedAt = Environment.TickCount64;
            }
            _widgetPopup = null;
            _widgetPopupUpdate = null;
        };
        _widgetPopup = popup;
        _widgetPopupUpdate = update;
        update();
        DeckIsland.Native.DockAppWindows.Focus(_handle);
        popup.IsOpen = true;
    }

    private Button DetailButton(string text, string label, Action click)
    {
        var button = new Button
        {
            Content = text, Height = 28, Cursor = Cursors.Hand, Foreground = UI.Brush(_look.Text),
            Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent
        };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, UI.Brush(_look.CardHover)));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, UI.Brush(Accent)));
        template.Triggers.Add(hover);
        template.Triggers.Add(focus);
        button.Template = template;
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }

    private (FrameworkElement, Action) CalendarDetails()
    {
        var culture = CultureInfo.CurrentCulture;
        DateTime selected = DateTime.Today;
        DateTime month = selected.AddDays(1 - selected.Day);
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        IReadOnlyList<CalendarEvent>? shownEvents = null;
        DateTime today = default;
        var caption = Label("", 13, bold: true);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 4, 0, 2) };
        var days = new UniformGrid { Columns = 7 };
        var agendaTitle = Label("", 12, bold: true);
        agendaTitle.Margin = new Thickness(0, 10, 0, 5);
        var agenda = new StackPanel();
        var body = new StackPanel();
        var bar = new DockPanel();
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        void MoveMonth(int direction)
        {
            month = month.AddMonths(direction);
            selected = month;
            Render();
        }
        var previous = DetailButton("‹", Loc.T("上个月", "Previous month"), () => MoveMonth(-1));
        var next = DetailButton("›", Loc.T("下个月", "Next month"), () => MoveMonth(1));
        previous.Width = next.Width = 30;
        nav.Children.Add(previous);
        nav.Children.Add(next);
        DockPanel.SetDock(nav, System.Windows.Controls.Dock.Right);
        bar.Children.Add(nav);
        bar.Children.Add(caption);
        body.Children.Add(bar);
        body.Children.Add(weekdays);
        body.Children.Add(days);
        body.Children.Add(agendaTitle);
        body.Children.Add(new ScrollViewer { MaxHeight = 132, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = agenda });
        for (int i = 0; i < 7; i++)
        {
            var text = Label(culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + i) % 7)), 11, dim: true);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            weekdays.Children.Add(text);
        }
        void RenderAgenda()
        {
            agendaTitle.Text = selected.ToString("dddd, MMMM d", culture);
            agenda.Children.Clear();
            var events = CalendarFeed.On(_app.CalendarEvents, DateOnly.FromDateTime(selected));
            foreach (var item in events)
            {
                var row = new StackPanel { Margin = new Thickness(0, 2, 0, 5), ToolTip = item.Title };
                string when = item.AllDay ? Loc.T("全天", "All day")
                    : $"{TimeText(item.Start)} – {TimeText(item.End)}";
                row.Children.Add(Label(item.Title, 12, bold: true));
                row.Children.Add(Label(string.IsNullOrWhiteSpace(item.Location) ? when : $"{when} · {item.Location}", 11, dim: true));
                agenda.Children.Add(row);
            }
            if (agenda.Children.Count == 0) agenda.Children.Add(Label(Loc.T("当天没有日程", "No events this day"), 12, dim: true));
        }
        void Render()
        {
            shownEvents = _app.CalendarEvents;
            today = DateTime.Today;
            caption.Text = month.ToString("Y", culture);
            days.Children.Clear();
            int offset = ((int)month.DayOfWeek - first + 7) % 7;
            for (int i = 0; i < 42; i++)
            {
                var day = month.AddDays(i - offset);
                var cell = DetailButton(day.Day.ToString(culture), day.ToString("D", culture), () =>
                {
                    selected = day;
                    if (day.Month != month.Month) month = day.AddDays(1 - day.Day);
                    Render();
                    days.Children.OfType<Button>().First(button => Equals(button.Tag, day)).Focus();
                });
                cell.Tag = day;
                bool hasEvents = CalendarFeed.On(shownEvents, DateOnly.FromDateTime(day)).Any();
                if (day == selected) cell.Background = UI.Brush(_look.CardHover);
                if (day == today || hasEvents) cell.Foreground = UI.Brush(Accent);
                else if (day.Month != month.Month) cell.Foreground = UI.Brush(_look.Dim);
                if (hasEvents) cell.FontWeight = FontWeights.Bold;
                days.Children.Add(cell);
            }
            RenderAgenda();
        }
        return (body, () =>
        {
            if (!ReferenceEquals(shownEvents, _app.CalendarEvents) || today != DateTime.Today) Render();
        });
    }

    private (FrameworkElement, Action) WeatherDetails()
    {
        var body = new StackPanel();
        var summary = Label("", 15, bold: true);
        var location = Label("", 12, dim: true);
        var hours = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 8) };
        var days = new StackPanel();
        var hourlyTitle = Label(Loc.T("接下来几小时", "Next hours"), 13, bold: true);
        hourlyTitle.Margin = new Thickness(0, 12, 0, 6);
        var dailyTitle = Label(Loc.T("未来几天", "Daily forecast"), 13, bold: true);
        dailyTitle.Margin = new Thickness(0, 8, 0, 4);
        foreach (var child in new UIElement[] { summary, location, hourlyTitle, hours, dailyTitle, days }) body.Children.Add(child);
        (WeatherReading?, bool, bool, bool, bool)? shown = null;
        return (body, () =>
        {
            var state = (_app.CurrentWeather, _app.Settings.WeatherEnabled, _app.Settings.WeatherFahrenheit, _app.WeatherStale, _app.WeatherNeedsLocation);
            if (shown == state) return;
            shown = state;
            var (reading, enabled, fahrenheit, stale, needsLocation) = state;
            string Temp(double? celsius) => celsius is { } value
                ? $"{Math.Round(fahrenheit ? value * 1.8 + 32 : value, MidpointRounding.AwayFromZero):0}°"
                : "–";
            hours.Children.Clear();
            days.Children.Clear();
            if (!enabled || reading is null)
            {
                summary.Text = !enabled ? Loc.T("天气未启用", "Weather is off") : needsLocation
                    ? Loc.T("在设置中选择位置", "Choose a location in Settings") : Loc.T("暂时没有天气数据", "No weather data yet");
                location.Text = "";
            }
            else
            {
                var (icon, condition) = MainWindow.WeatherCondition(reading.Code, reading.Daylight);
                summary.Text = $"{icon}  {Temp(reading.Temperature)}  ·  {condition}";
                location.Text = stale ? $"{reading.Place.Name} · {Loc.T("上次更新", "Last available reading")}" : reading.Place.Name;
                foreach (var hour in reading.Hours.Take(6))
                {
                    var (symbol, _) = MainWindow.WeatherCondition(hour.Code, hour.Daylight);
                    var time = Label(TimeText(hour.Time), 10, dim: true);
                    time.HorizontalAlignment = HorizontalAlignment.Center;
                    hours.Children.Add(TimeStack(time, Label(symbol + " " + Temp(hour.Temperature), 11, bold: true)));
                }
                foreach (var day in reading.Days.Take(7))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 4, 0, 1) };
                    var highLow = Label($"{Temp(day.Low)}  /  {Temp(day.High)}", 12, bold: true);
                    DockPanel.SetDock(highLow, System.Windows.Controls.Dock.Right);
                    row.Children.Add(highLow);
                    var (symbol, _) = MainWindow.WeatherCondition(day.Code, true);
                    row.Children.Add(Label($"{day.Date:ddd d}  ·  {symbol}", 12, dim: true));
                    days.Children.Add(row);
                }
            }
            hourlyTitle.Visibility = hours.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            dailyTitle.Visibility = days.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
    }
}
