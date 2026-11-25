using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

// The Screen Time page: one day of the last 30 at a time (the arrows move between them). A timeline of the day's 24 hours with
// the time counted in each and the daylight where a weather city is set; the day's total with the last seven days beside it; and
// the apps longest first. Counted by App.ScreenTime; the widget shows today alone.
public partial class MainWindow
{
    internal static string ScreenTimeTitle => Loc.T("屏幕使用时间", "Screen Time");
    private const int ScreenTimeTopApps = 5;
    private DateOnly _screenTimeDay = DateOnly.FromDateTime(DateTime.Now);
    private string _screenTimeBuilt = "";
    private DispatcherTimer? _screenTimeTimer;

    private void ScreenTimePage_Click(object sender, RoutedEventArgs e) { _screenTimeDay = DateOnly.FromDateTime(DateTime.Now); SelectPage(DeckPage.ScreenTime); }

    private void RenderScreenTime()
    {
        foreach (var page in DeckPages.Order)
            if (page != DeckPage.ScreenTime) PageContent(page).Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        if (!double.IsFinite(ContentCanvas.Height)) return;
        var header = PanelHeader.For(ToolbarFor(ContentCanvas.Height), 18);
        ApplyHeader(header);
        ShowHeaderPin(header);
        ScreenTimeContent.Margin = new Thickness(header.Side, 0, header.Side, MonitorBottom(ContentCanvas.Height));
        // Today's count grows while the page is open: it is looked at again every half minute.
        if (_screenTimeTimer is null)
        {
            _screenTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _screenTimeTimer.Tick += (_, _) => { if (_expanded && CurrentPage == DeckPage.ScreenTime && IsVisible) BuildScreenTime(); else _screenTimeTimer.Stop(); };
        }
        _screenTimeTimer.Start();
        BuildScreenTime();
    }

    private void BuildScreenTime()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var oldest = today.AddDays(-(ScreenTime.KeepDays - 1));
        _screenTimeDay = _screenTimeDay > today ? today : _screenTimeDay < oldest ? oldest : _screenTimeDay;
        var s = _app.Settings;
        var (apps, hours) = ScreenTime.Day(_screenTimeDay, s.ScreenTimeDate, s.ScreenTimeApps, s.ScreenTimeHours, s.ScreenTimeHistory, now);
        int total = ScreenTime.Total(apps);
        string key = string.Join("|", _screenTimeDay, total / 60, string.Join(",", apps.Take(ScreenTimeTopApps).Select(a => a.Key + a.Value / 60)),
            string.Join(",", hours.Select(h => h / 60)), now.Hour * 60 / 10 + now.Minute / 10, Look.Generation, Loc.Language, ContentCanvas.Width);
        if (key == _screenTimeBuilt && ScreenTimeContent.Children.Count > 0) return;
        _screenTimeBuilt = key;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        // The day's name and the arrows to the day before and after, within the days kept.
        var title = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var next = ScreenTimeArrow(false, _screenTimeDay < today, () => { _screenTimeDay = _screenTimeDay.AddDays(1); BuildScreenTime(); });
        var back = ScreenTimeArrow(true, _screenTimeDay > oldest, () => { _screenTimeDay = _screenTimeDay.AddDays(-1); BuildScreenTime(); });
        DockPanel.SetDock(next, System.Windows.Controls.Dock.Right); DockPanel.SetDock(back, System.Windows.Controls.Dock.Right);
        title.Children.Add(next); title.Children.Add(back);
        title.Children.Add(new TextBlock { Text = DayName(_screenTimeDay, today), FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(Look.Primary), VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(title);

        var sun = s.WeatherCity is { } city ? SunMoon.Sun(_screenTimeDay, city.Latitude, city.Longitude) : null;
        var timeline = new ScreenTimeTimeline(hours, sun, _screenTimeDay == today ? now : null, Look) { Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetName(timeline, Loc.T("每小时的使用时间", "Screen time by hour"));
        Grid.SetRow(timeline, 1); root.Children.Add(timeline);

        FrameworkElement body = total > 0 ? ScreenTimeBody(apps, total, today, s) : ScreenTimeEmpty();
        Grid.SetRow(body, 2); root.Children.Add(body);
        ScreenTimeContent.Children.Clear();
        ScreenTimeContent.Children.Add(root);
    }

    private static string DayName(DateOnly day, DateOnly today) =>
        day == today ? Loc.T("今天", "Today") : day == today.AddDays(-1) ? Loc.T("昨天", "Yesterday")
        : day.ToDateTime(TimeOnly.MinValue).ToString(Loc.T("M月d日 dddd", "dddd, MMMM d"), Loc.IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("zh-CN"));

    private Button ScreenTimeArrow(bool back, bool enabled, Action go)
    {
        var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse(back ? "M 15,6 L 9,12 L 15,18" : "M 9,6 L 15,12 L 9,18"), StrokeThickness = 2, Width = 14, Height = 14, Stretch = Stretch.Uniform,
            Stroke = UI.Brush(Look.Secondary), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        var button = new Button { Style = (Style)FindResource("PanelGlyphButton"), Content = glyph, Width = 28, Height = 26, IsEnabled = enabled, Opacity = enabled ? 1 : .35, Margin = new Thickness(4, 0, 0, 0) };
        AutomationProperties.SetName(button, back ? Loc.T("前一天", "Previous day") : Loc.T("后一天", "Next day"));
        button.Click += (_, _) => go();
        return button;
    }

    private FrameworkElement ScreenTimeEmpty()
    {
        var icon = MonitorIcons.Icon("hourglass", 22, UI.Brush(Look.Tertiary));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var text = new TextBlock { Text = Loc.T("这一天没有屏幕使用时间", "No Screen Time on this day"), FontSize = 14, Foreground = UI.Brush(Look.Secondary), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { icon, text } };
    }

    // The day's total with the seven days up to it, and its apps.
    private FrameworkElement ScreenTimeBody(IReadOnlyList<KeyValuePair<string, int>> apps, int total, DateOnly today, DeckSettings s)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 280 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });

        var week = Enumerable.Range(0, 7).Select(i => _screenTimeDay.AddDays(i - 6))
            .Select(day => ScreenTime.Total(ScreenTime.Day(day, s.ScreenTimeDate, s.ScreenTimeApps, s.ScreenTimeHours, s.ScreenTimeHistory, DateTime.Now).Apps)).ToList();
        var counted = week.Where(seconds => seconds > 0).ToList();
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { MinHeight = 24 });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var number = new TextBlock { Text = ScreenTimeLong(total), FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(Look.Primary) };
        var label = new TextBlock { Text = ScreenTimeTitle, FontSize = 12, Foreground = UI.Brush(Look.Tertiary) };
        var chart = new ScreenTimeWeek(week, Look) { Margin = new Thickness(0, 6, 0, 4) };
        AutomationProperties.SetName(chart, Loc.T("最近 7 天", "The last 7 days"));
        var average = new TextBlock { Text = Loc.T("最近 7 天平均：", "Average of the last 7 days: ") + ScreenTimeLong(counted.Count > 0 ? (int)counted.Average() : 0),
            FontSize = 12, Foreground = UI.Brush(Look.Tertiary), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(label, 1); Grid.SetRow(chart, 2); Grid.SetRow(average, 3);
        left.Children.Add(number); left.Children.Add(label); left.Children.Add(chart); left.Children.Add(average);
        grid.Children.Add(left);

        // The longest apps, each with its share of the longest as a bar, and the rest together.
        var list = new StackPanel();
        int top = apps[0].Value;
        foreach (var (name, seconds) in apps.Take(ScreenTimeTopApps))
            list.Children.Add(ScreenTimeApp(name, s.ScreenTimePaths.GetValueOrDefault(name), seconds, top));
        int rest = apps.Skip(ScreenTimeTopApps).Sum(pair => pair.Value);
        if (rest > 0) list.Children.Add(ScreenTimeApp(Loc.T("其他应用", "Other apps"), null, rest, Math.Max(top, rest)));
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 2); grid.Children.Add(scroll);
        return grid;
    }

    private FrameworkElement ScreenTimeApp(string name, string? path, int seconds, int top)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2), MinHeight = 22 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        if (path is not null) ProcessIcons.Prepare(path);
        if (ProcessIcons.For(path) is { } source)
            row.Children.Add(new Image { Source = source, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = name, FontSize = 13, Foreground = UI.Brush(Look.Primary), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
        var bar = new ShareBar { Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        bar.SetTrack(Look.Track); bar.Set([((double)seconds / Math.Max(1, top), Look.Accent)]);
        var time = new TextBlock { Text = ScreenTime.Duration(seconds), FontSize = 13, Foreground = UI.Brush(Look.Secondary), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1); Grid.SetColumn(bar, 2); Grid.SetColumn(time, 3);
        row.Children.Add(label); row.Children.Add(bar); row.Children.Add(time);
        AutomationProperties.SetName(row, $"{name} · {ScreenTime.Duration(seconds)}");
        return row;
    }

    // "35 min", "1 h 12 min": the page's total, spelled out more than the widget's.
    private static string ScreenTimeLong(int seconds)
    {
        int minutes = Math.Max(0, seconds) / 60;
        return minutes < 60 ? Loc.T($"{minutes} 分钟", $"{minutes} min") : minutes % 60 == 0 ? Loc.T($"{minutes / 60} 小时", $"{minutes / 60} h") : Loc.T($"{minutes / 60} 小时 {minutes % 60} 分钟", $"{minutes / 60} h {minutes % 60} min");
    }
}

// The day's 24 hours: daylight shaded between sunrise and sunset (labelled above), each hour's counted time as a bar under the
// track, lines at 6, 12 and 18, the time now on today, and the hours below.
internal sealed class ScreenTimeTimeline(IReadOnlyList<int> hours, (DateTime Rise, DateTime Set)? sun, DateTime? now, PanelLook look) : FrameworkElement
{
    private const double SunRow = 18, Track = 28, Usage = 6, Labels = 18;
    protected override Size MeasureOverride(Size availableSize) => new(0, SunRow + Track + Usage + 2 + Labels);

    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, ppd = MonitorDraw.PixelsPerDip(this), top = SunRow;
        if (width <= 0) return;
        double X(double hour) => Math.Round(Math.Clamp(hour / 24, 0, 1) * width);
        var track = new Rect(0, top, width, Track);
        drawing.DrawRoundedRectangle(UI.Brush(look.Fill), null, track, 6, 6);
        if (sun is { } day)
        {
            double rise = day.Rise.TimeOfDay.TotalHours, set = day.Set.TimeOfDay.TotalHours;
            drawing.PushClip(new RectangleGeometry(track, 6, 6));
            drawing.DrawRectangle(UI.Brush(MonitorDraw.Faint(look.WarningDefault, .38)), null, new Rect(X(rise), top, Math.Max(0, X(set) - X(rise)), Track));
            drawing.Pop();
            SunLabel(drawing, "☀ " + Clock(day.Rise), X(rise), look.WarningDefault, ppd);
            SunLabel(drawing, "☾ " + Clock(day.Set), X(set), look.Tertiary, ppd);
        }
        var line = new Pen(UI.Brush(look.Hairline), 1);
        foreach (int hour in new[] { 6, 12, 18 }) drawing.DrawLine(line, new Point(X(hour) + .5, top), new Point(X(hour) + .5, top + Track));
        // Each hour's counted time, as tall as its share of the hour, under the track.
        var used = UI.Brush(look.Accent);
        double cell = width / 24;
        for (int hour = 0; hour < 24 && hour < hours.Count; hour++)
        {
            if (hours[hour] <= 0) continue;
            double share = Math.Clamp(hours[hour] / 3600d, .15, 1);
            drawing.DrawRoundedRectangle(used, null, new Rect(hour * cell + 1, top + Track + 2 + Usage * (1 - share), Math.Max(1, cell - 2), Usage * share), 1, 1);
        }
        if (now is { } at)
        {
            double x = X(at.TimeOfDay.TotalHours);
            drawing.DrawRectangle(UI.Brush(look.Primary), null, new Rect(x - 1, top - 3, 2, Track + 6));
        }
        double labels = top + Track + Usage + 4;
        for (int hour = 0; hour <= 24; hour += 6)
        {
            var text = MonitorDraw.Text(HourLabel(hour), 11.5, look.Tertiary, ppd);
            double x = hour == 0 ? 0 : hour == 24 ? width - text.Width : X(hour) - text.Width / 2;
            drawing.DrawText(text, new Point(x, labels));
        }
    }

    private void SunLabel(DrawingContext drawing, string label, double x, string color, double ppd)
    {
        var text = MonitorDraw.Text(label, 11.5, color, ppd);
        drawing.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, Math.Max(0, ActualWidth - text.Width)), 0));
    }

    private static bool TwentyFour => !CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h') || !Loc.IsEnglish;
    private static string Clock(DateTime time) => time.ToString(TwentyFour ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);
    private static string HourLabel(int hour) => TwentyFour ? $"{hour % 24:00}:00" : $"{(hour % 12 == 0 ? 12 : hour % 12)} {(hour % 24 < 12 ? "AM" : "PM")}";
}

// The seven days up to the shown one, as bars against the longest; the shown day in the primary colour, a dashed line under them.
internal sealed class ScreenTimeWeek(IReadOnlyList<int> days, PanelLook look) : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize) => new(0, Math.Min(56, double.IsFinite(availableSize.Height) ? availableSize.Height : 56));

    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 4 || days.Count == 0) return;
        int longest = Math.Max(1, days.Max());
        double slot = width / days.Count, bar = Math.Min(8, slot * .5);
        var dashed = new Pen(UI.Brush(look.Hairline), 1) { DashStyle = new DashStyle([2, 3], 0) };
        drawing.DrawLine(dashed, new Point(0, height - .5), new Point(width, height - .5));
        for (int i = 0; i < days.Count; i++)
        {
            double tall = days[i] <= 0 ? 0 : Math.Max(2, (height - 2) * days[i] / longest);
            if (tall <= 0) continue;
            bool shown = i == days.Count - 1;
            drawing.DrawRoundedRectangle(UI.Brush(shown ? look.Primary : look.Tertiary), null, new Rect(i * slot + (slot - bar) / 2, height - 1 - tall, bar, tall), 1.5, 1.5);
        }
    }
}
