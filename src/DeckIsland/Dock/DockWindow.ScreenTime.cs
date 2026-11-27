using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// The Screen Time widget and its panel, after lumabar's. The widget shows today's total (or a pinned app's, chosen in the panel's
// Most used list and kept as the widget's option) beside a bar per hour. The panel shows a day (or the last seven days), its
// longest stretch, app switches and first use, the most used apps and sites, and the daily summary's switch and time.
internal sealed partial class DockWindow
{
    private const string ScreenTimeGrey = "#FF3A3A3F";

    // The day's seconds in each hour for one app, or for all of them.
    private IReadOnlyList<int> ScreenHours(DateOnly day, string? app)
    {
        var settings = _app.Settings;
        if (app is null)
            return ScreenTime.Day(day, settings.ScreenTimeDate, settings.ScreenTimeApps, settings.ScreenTimeHours, settings.ScreenTimeHistory, DateTime.Now).Hours;
        return ScreenExtra(day)?.AppHours.TryGetValue(app, out var hours) == true && hours.Count == 24 ? hours : new int[24];
    }

    private IReadOnlyList<KeyValuePair<string, int>> ScreenApps(DateOnly day)
    {
        var settings = _app.Settings;
        return ScreenTime.Day(day, settings.ScreenTimeDate, settings.ScreenTimeApps, settings.ScreenTimeHours, settings.ScreenTimeHistory, DateTime.Now).Apps;
    }

    private ScreenTimeExtra? ScreenExtra(DateOnly day) =>
        _app.Settings.ScreenTimeDetails.GetValueOrDefault(ScreenTime.DayKey(day.ToDateTime(TimeOnly.MinValue)));

    private string? ScreenPath(string app) => _app.Settings.ScreenTimePaths.GetValueOrDefault(app);

    private FrameworkElement ScreenAppIcon(string app, double size)
    {
        if (ScreenPath(app) is { } path && DockAppWindows.Icon(path, 64) is { } image)
        {
            var picture = new Image { Source = image, Width = size, Height = size };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            return picture;
        }
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.22), Background = UI.Brush(_look.CardActive),
            Child = Label(app.Length > 0 ? app[..1].ToUpperInvariant() : "?", size * 0.5, bold: true) };
    }

    // The watch the widget and the empty panel show: an accent body with straps and the hands cut into it.
    private static FrameworkElement ScreenWatch(double size, string accent)
    {
        var body = new System.Windows.Shapes.Path { Fill = UI.Brush(accent), Stretch = Stretch.Fill, Width = size * 0.62, Height = size,
            Data = Geometry.Parse("M4,4 L5,0 H11 L12,4 A3,3 0 0 1 15,7 V15 A3,3 0 0 1 12,18 L11,22 H5 L4,18 A3,3 0 0 1 1,15 V7 A3,3 0 0 1 4,4 Z") };
        var hands = new System.Windows.Shapes.Path { Stroke = UI.Brush("#99000000"), StrokeThickness = Math.Max(1.2, size * 0.07), StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, Width = size * 0.62, Height = size, Stretch = Stretch.None,
            Data = Geometry.Parse(FormattableString.Invariant($"M{size * 0.31},{size * 0.36} V{size * 0.5} L{size * 0.41},{size * 0.58}")) };
        return new Grid { Width = size * 0.62, Height = size, Children = { body, hands } };
    }

    // Bars for the hours, scaled to the busiest hour (an hour at most); hours with nothing are a dotted base.
    private Canvas HourSpark(IReadOnlyList<int> hours, double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        double step = width / 24, bar = Math.Max(1, step * 0.55), top = Math.Max(1, hours.Count == 0 ? 1 : hours.Max());
        for (int i = 0; i < 24; i++)
        {
            int seconds = i < hours.Count ? hours[i] : 0;
            var shape = seconds > 0
                ? new Rectangle { Width = bar, Height = Math.Max(2, height * seconds / top), Fill = UI.Brush(Accent), RadiusX = bar / 2, RadiusY = bar / 2 }
                : new Rectangle { Width = bar, Height = 1, Fill = UI.Brush(_look.Dim), Opacity = 0.6 };
            Canvas.SetLeft(shape, i * step + (step - bar) / 2);
            Canvas.SetTop(shape, height - shape.Height);
            canvas.Children.Add(shape);
        }
        return canvas;
    }

    private (FrameworkElement, Action) ScreenTimeTile(DockWidget widget, double tile)
    {
        string? pinned = widget.Option.Length > 0 ? widget.Option : null;
        var badge = new Border { Width = tile * 0.62, Height = tile * 0.62, Margin = new Thickness(tile * 0.18, 0, tile * 0.16, 0), VerticalAlignment = VerticalAlignment.Center };
        var caption = Label(pinned ?? Loc.T("屏幕使用时间", "Screen Time"), tile * 0.15, bold: true, dim: true);
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        var total = Label("", tile * 0.34, bold: true);
        total.Margin = new Thickness(0, -tile * 0.03, 0, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { caption, total } };
        var spark = new Border { Width = tile * 1.05, Height = tile * 0.42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(tile * 0.08, tile * 0.06, tile * 0.16, 0) };
        var row = new DockPanel();
        DockPanel.SetDock(badge, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(spark, System.Windows.Controls.Dock.Right);
        row.Children.Add(badge); row.Children.Add(spark); row.Children.Add(text);
        badge.Child = pinned is null ? ScreenWatch(tile * 0.6, Accent) : ScreenAppIcon(pinned, tile * 0.56);
        string shown = "";
        return (row, () =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var apps = ScreenApps(today);
            int seconds = pinned is null ? ScreenTime.Total(apps) : apps.FirstOrDefault(pair => string.Equals(pair.Key, pinned, StringComparison.OrdinalIgnoreCase)).Value;
            string next = ScreenTime.Duration(seconds);
            if (next == shown) return;
            shown = next;
            total.Text = next;
            spark.Child = HourSpark(ScreenHours(today, pinned), tile * 1.05, tile * 0.42);
        });
    }

    // --- The panel ---

    private sealed class ScreenPanelState
    {
        public bool Week;
        public int Back;
        public bool Sites;
    }

    private FrameworkElement Segmented(string[] names, Func<int> selected, Action<int> choose, double fontSize = 14)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        var host = new Border { Background = UI.Brush(_look.CardHover), CornerRadius = new CornerRadius(10), Padding = new Thickness(3), Child = bar,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var buttons = names.Select((name, i) =>
        {
            var label = Label(name, fontSize, bold: true);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var button = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(fontSize * 1.7, 5, fontSize * 1.7, 6), Child = label, Cursor = Cursors.Hand,
                Background = Brushes.Transparent };
            button.MouseLeftButtonUp += (_, _) => choose(i);
            bar.Children.Add(button);
            return (button, label);
        }).ToArray();
        void Paint()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].button.Background = UI.Brush(i == selected() ? _look.CardActive : "#00000000");
                buttons[i].label.Opacity = i == selected() ? 1 : 0.6;
            }
        }
        host.Tag = (Action)Paint;
        Paint();
        return host;
    }

    private static void Repaint(FrameworkElement element) => (element.Tag as Action)?.Invoke();

    private Border RoundButton(FrameworkElement icon, Action click, string name, double size = 32)
    {
        var button = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = UI.Brush(_look.CardHover), Cursor = Cursors.Hand,
            Child = icon, ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.MouseEnter += (_, _) => button.Background = UI.Brush(_look.CardActive);
        button.MouseLeave += (_, _) => button.Background = UI.Brush(_look.CardHover);
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    private void OpenScreenTimePanel(DockWidget widget, FrameworkElement anchor)
    {
        double offset = anchor.TranslatePoint(new Point(0, 0), _bar).X;
        if (!BeginDockPanel(_bar)) return;
        var state = new ScreenPanelState();
        string? pinned = widget.Option.Length > 0 ? widget.Option : null;

        var title = Label(Loc.T("屏幕使用时间", "Screen Time"), 17, bold: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        FrameworkElement? modes = null;
        Action render = () => { };
        modes = Segmented([Loc.T("日", "Day"), Loc.T("周", "Week")], () => state.Week ? 1 : 0, i => { state.Week = i == 1; state.Back = 0; Repaint(modes!); render(); });
        var close = RoundButton(MonitorIcons.Icon("close", 15, UI.Brush(_look.Dim)), CloseWidgetDetails, Loc.T("关闭", "Close"));
        close.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(title); header.Children.Add(modes); header.Children.Add(close);

        var left = new ContentControl();
        var right = new ContentControl();
        var columns = new Grid { Height = 500 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var divider = new Border { Background = UI.Brush(_look.Divider), Margin = new Thickness(0, 0, 0, 0) };
        Grid.SetColumn(divider, 1); Grid.SetColumn(right, 2);
        left.Margin = new Thickness(0, 0, 26, 0); right.Margin = new Thickness(24, 0, 0, 0);
        columns.Children.Add(left); columns.Children.Add(divider); columns.Children.Add(right);
        var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14), Child = ScreenWatch(40, Accent) },
                Label(Loc.T("这一天没有屏幕使用时间。", "No screen time on this day."), 14, dim: true) } };
        var emptyHost = new Grid { Height = 500, Visibility = Visibility.Collapsed };
        var emptyNav = new DockPanel { VerticalAlignment = VerticalAlignment.Top };
        emptyHost.Children.Add(emptyNav); emptyHost.Children.Add(empty);
        var body = new Grid { Children = { columns, emptyHost } };

        render = () =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            bool nothing;
            if (state.Week)
            {
                var days = Enumerable.Range(0, 7).Select(i => today.AddDays(-state.Back * 7 - 6 + i)).ToList();
                nothing = days.All(day => ScreenTime.Total(ScreenApps(day)) == 0);
                left.Content = WeekColumn(days, state, render);
                right.Content = MostUsed(days, state, render, widget, pinned, p => pinned = p);
            }
            else
            {
                var day = today.AddDays(-state.Back);
                nothing = ScreenTime.Total(ScreenApps(day)) == 0;
                left.Content = DayColumn(day, state, render, pinned);
                right.Content = MostUsed([day], state, render, widget, pinned, p => pinned = p);
                emptyNav.Children.Clear();
                emptyNav.Children.Add(DayNavigation(day, state, render));
            }
            columns.Visibility = nothing && !state.Week ? Visibility.Collapsed : Visibility.Visible;
            emptyHost.Visibility = nothing && !state.Week ? Visibility.Visible : Visibility.Collapsed;
        };
        render();

        var layout = new DockPanel();
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        var footer = SummaryFooter();
        DockPanel.SetDock(footer, System.Windows.Controls.Dock.Bottom);
        layout.Children.Add(header); layout.Children.Add(footer); layout.Children.Add(body);
        ShowDockPanel(_bar, layout, () => { }, 1000, 24, CalendarRadius(16, 30), null, Math.Max(0, offset - 300));
    }

    private FrameworkElement DayNavigation(DateOnly day, ScreenPanelState state, Action render)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        string name = day == today ? Loc.T("今天", "Today") : day == today.AddDays(-1) ? Loc.T("昨天", "Yesterday")
            : day.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        return Navigation(name, state.Back < ScreenTime.KeepDays - 1, state.Back > 0, () => { state.Back++; render(); }, () => { state.Back--; render(); });
    }

    private FrameworkElement Navigation(string caption, bool canBack, bool canForward, Action back, Action forward)
    {
        var row = new DockPanel();
        var arrows = new StackPanel { Orientation = Orientation.Horizontal };
        FrameworkElement Arrow(string icon, bool enabled, Action go)
        {
            var arrow = new Border { Background = Brushes.Transparent, Padding = new Thickness(8, 2, 8, 2), Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
                Opacity = enabled ? 1 : 0.35, Child = MonitorIcons.Icon(icon, 16, UI.Brush(_look.Text)) };
            if (enabled) arrow.MouseLeftButtonUp += (_, _) => go();
            return arrow;
        }
        arrows.Children.Add(Arrow("chevron-left", canBack, back));
        arrows.Children.Add(Arrow("chevron-right", canForward, forward));
        DockPanel.SetDock(arrows, System.Windows.Controls.Dock.Right);
        row.Children.Add(arrows);
        row.Children.Add(Label(caption, 14, dim: true));
        return row;
    }

    private static string HourLabel(int seconds) => seconds >= 3600 ? $"{seconds / 3600}h" : $"{seconds / 60}m";

    // A bar chart with dashed guides: bars (optionally two-tone) over labels, guide values at the right.
    private FrameworkElement BarChart(IReadOnlyList<(double Lit, double Others)> bars, double top, int[] guides, string[] labels, double? average, int highlight = -1)
    {
        const double height = 230, labelHeight = 22;
        var canvas = new Canvas { Height = height + labelHeight, ClipToBounds = false };
        void Draw()
        {
            canvas.Children.Clear();
            double width = canvas.ActualWidth - 42;
            if (width <= 0) return;
            foreach (int guide in guides)
            {
                double y = height - height * guide / top;
                canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = UI.Brush(_look.Divider), StrokeDashArray = [3, 3], StrokeThickness = 1 });
                var value = Label(HourLabel(guide), 11.5, dim: true);
                Canvas.SetLeft(value, width + 8); Canvas.SetTop(value, y - 9);
                canvas.Children.Add(value);
            }
            canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = height, Y2 = height, Stroke = UI.Brush(_look.Divider), StrokeThickness = 1 });
            double step = width / bars.Count, bar = Math.Min(28, step * 0.66);
            for (int i = 0; i < bars.Count; i++)
            {
                double lit = height * bars[i].Lit / top, rest = height * bars[i].Others / top, x = i * step + (step - bar) / 2;
                if (rest > 0.5)
                {
                    var grey = new Rectangle { Width = bar, Height = rest + lit, Fill = UI.Brush(ScreenTimeGrey), RadiusX = 2, RadiusY = 2 };
                    Canvas.SetLeft(grey, x); Canvas.SetTop(grey, height - rest - lit); canvas.Children.Add(grey);
                }
                if (lit > 0.5)
                {
                    var accent = new Rectangle { Width = bar, Height = lit, Fill = UI.Brush(Accent), RadiusX = 2, RadiusY = 2 };
                    Canvas.SetLeft(accent, x); Canvas.SetTop(accent, height - lit); canvas.Children.Add(accent);
                }
                if (i < labels.Length && labels[i].Length > 0)
                {
                    var label = Label(labels[i], 12, bold: i == highlight, dim: i != highlight);
                    label.Width = step; label.TextAlignment = TextAlignment.Center;
                    if (bars.Count > 7) { label.Width = 40; label.TextAlignment = TextAlignment.Left; Canvas.SetLeft(label, i * step); }
                    else Canvas.SetLeft(label, i * step);
                    Canvas.SetTop(label, height + 6);
                    canvas.Children.Add(label);
                }
            }
            if (average is { } avg && avg > 0)
            {
                double y = height - height * avg / top;
                canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = UI.Brush(Accent), StrokeDashArray = [3, 3], StrokeThickness = 1, Opacity = 0.8 });
                var value = Label(Loc.T("平均", "avg"), 11.5, dim: true);
                Canvas.SetLeft(value, width + 8); Canvas.SetTop(value, y - 9);
                canvas.Children.Add(value);
            }
        }
        canvas.SizeChanged += (_, _) => Draw();
        return canvas;
    }

    private FrameworkElement Stat(string caption, string value, string note)
    {
        var stack = new StackPanel { Children = { Label(caption, 12.5, dim: true), Label(value, 20, bold: true) } };
        if (note.Length > 0) stack.Children.Add(Label(note, 11.5, dim: true));
        return stack;
    }

    private FrameworkElement Stats(params FrameworkElement[] stats)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = stats.Length };
        foreach (var stat in stats) grid.Children.Add(stat);
        return new Border { BorderBrush = UI.Brush(_look.Divider), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 14, 0, 0), Margin = new Thickness(0, 8, 0, 0), Child = grid };
    }

    private FrameworkElement DayColumn(DateOnly day, ScreenPanelState state, Action render, string? pinned)
    {
        var apps = ScreenApps(day);
        int total = ScreenTime.Total(apps);
        var hours = ScreenHours(day, null);
        var mine = pinned is null ? null : ScreenHours(day, pinned);
        var bars = Enumerable.Range(0, 24).Select(i => mine is null ? (hours[i] / 1.0, 0.0) : (mine[i] / 1.0, Math.Max(0, hours[i] - mine[i]) / 1.0)).ToList();
        string[] labels = Enumerable.Range(0, 24).Select(i => i % 6 == 0 ? i.ToString("00", CultureInfo.InvariantCulture) : "").ToArray();
        var extra = ScreenExtra(day);
        var today = DateOnly.FromDateTime(DateTime.Now);
        string first = extra is { FirstMinute: >= 0 } ? TimeText(day.ToDateTime(new TimeOnly(extra.FirstMinute / 60, extra.FirstMinute % 60))) : "–";
        string firstNote = extra is { FirstMinute: >= 0 } && day == today
            ? Loc.T($"{ScreenTime.Duration((int)(DateTime.Now - day.ToDateTime(new TimeOnly(extra.FirstMinute / 60, extra.FirstMinute % 60))).TotalSeconds)} 前",
                $"{ScreenTime.Duration((int)(DateTime.Now - day.ToDateTime(new TimeOnly(extra.FirstMinute / 60, extra.FirstMinute % 60))).TotalSeconds)} ago") : "";
        var big = Label(ScreenTime.Duration(total), 44, bold: true);
        big.Margin = new Thickness(0, 2, 0, 18);
        return new StackPanel
        {
            Children =
            {
                DayNavigation(day, state, render), big,
                BarChart(bars, 3600, [1800, 3600], labels, null),
                Stats(Stat(Loc.T("最长专注", "Longest focus"), extra is { LongestFocus: > 0 } ? ScreenTime.Duration(extra.LongestFocus) : "–", extra?.LongestApp ?? ""),
                    Stat(Loc.T("应用切换", "App switches"), (extra?.Switches ?? 0).ToString("N0", CultureInfo.CurrentCulture), ScreenTimeExtras.SwitchEvery(extra?.Switches ?? 0, total)),
                    Stat(Loc.T("首次使用", "First use"), first, firstNote))
            }
        };
    }

    private FrameworkElement WeekColumn(List<DateOnly> days, ScreenPanelState state, Action render)
    {
        var totals = days.Select(day => ScreenTime.Total(ScreenApps(day))).ToList();
        int sum = totals.Sum(), average = sum / 7, busiest = totals.IndexOf(totals.Max());
        double top = Math.Max(8 * 3600, totals.Max() * 1.1);
        int guide = top > 8 * 3600 ? (int)Math.Ceiling(top / 3600 / 2) * 3600 : 4 * 3600;
        var labels = days.Select(day => day.ToString("ddd", CultureInfo.CurrentCulture)[..1]).ToArray();
        var today = DateOnly.FromDateTime(DateTime.Now);
        int appsUsed = days.SelectMany(day => ScreenApps(day).Select(pair => pair.Key)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        string caption = state.Back == 0 ? Loc.T("每日平均 · 最近 7 天", "Daily average · Last 7 days")
            : Loc.T($"每日平均 · {days[0]:d MMM} – {days[^1]:d MMM}", $"Daily average · {days[0].ToString("d MMM", CultureInfo.CurrentCulture)} – {days[^1].ToString("d MMM", CultureInfo.CurrentCulture)}");
        var big = Label(ScreenTime.Duration(average), 44, bold: true);
        big.Margin = new Thickness(0, 2, 0, 18);
        return new StackPanel
        {
            Children =
            {
                Navigation(caption, state.Back < 3, state.Back > 0, () => { state.Back++; render(); }, () => { state.Back--; render(); }), big,
                BarChart(totals.Select(t => (t / 1.0, 0.0)).ToList(), top, [guide, guide * 2], labels, average, days.IndexOf(today)),
                Stats(Stat(Loc.T("总计", "Total"), ScreenTime.Duration(sum), Loc.T("最近 7 天", "last 7 days")),
                    Stat(Loc.T("最忙的一天", "Busiest day"), ScreenTime.Duration(totals[busiest]), sum > 0 ? days[busiest].ToString("ddd", CultureInfo.CurrentCulture) : ""),
                    Stat(Loc.T("使用的应用", "Apps used"), appsUsed.ToString(CultureInfo.CurrentCulture), ""))
            }
        };
    }

    private FrameworkElement MostUsed(IReadOnlyList<DateOnly> days, ScreenPanelState state, Action render, DockWidget widget, string? pinned, Action<string?> pin)
    {
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        FrameworkElement? tabs = null;
        tabs = Segmented([Loc.T("应用", "Apps"), Loc.T("网站", "Websites")], () => state.Sites ? 1 : 0, i => { state.Sites = i == 1; render(); }, 12.5);
        DockPanel.SetDock(tabs, System.Windows.Controls.Dock.Right);
        top.Children.Add(tabs);
        var caption = Label(Loc.T("最常用", "Most used").ToUpperInvariant(), 12.5, bold: true, dim: true);
        caption.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(caption);
        var list = new StackPanel();
        var panel = new DockPanel();
        DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        if (state.Sites && !_app.Settings.ScreenTimeSites)
        {
            var text = Label(Loc.T("从浏览器标签页标题读取，只保留网站名称（如 YouTube 或 GitHub），绝不保留页面、地址或隐私窗口中的任何内容。只保存在这台电脑上。",
                "Read from browser tab titles - only a site's name, like YouTube or GitHub, is kept; never a page, an address or anything from private windows. Stays on this PC."), 12.5, dim: true);
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Thickness(0, 6, 0, 14);
            var button = new Border { Background = UI.Brush(Accent), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 7, 16, 8), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = Loc.T("统计网站", "Count websites"), FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White } };
            button.MouseLeftButtonUp += (_, _) => { _app.SetScreenTimeOptions(sites: true); render(); };
            list.Children.Add(new Border { Background = UI.Brush(_look.CardHover), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 18),
                Child = new StackPanel { Children = { Label(Loc.T("查看你在哪些网站上花了时间", "See the sites you spend time on"), 15, bold: true), text, button } } });
            return panel;
        }
        var entries = state.Sites
            ? days.SelectMany(day => ScreenExtra(day)?.Sites ?? []).GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(g => (Name: g.Key, Seconds: g.Sum(p => p.Value)))
            : days.SelectMany(ScreenApps).GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(g => (Name: g.Key, Seconds: g.Sum(p => p.Value)));
        var sorted = entries.OrderByDescending(entry => entry.Seconds).Take(40).ToList();
        if (sorted.Count == 0) list.Children.Add(Label(state.Sites ? Loc.T("还没有统计到网站", "No sites counted yet") : Loc.T("没有应用", "No apps"), 13, dim: true));
        double most = Math.Max(1, sorted.Count == 0 ? 1 : sorted[0].Seconds);
        foreach (var (name, seconds) in sorted)
        {
            var icon = state.Sites ? new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Background = UI.Brush(_look.CardActive),
                Child = MonitorIcons.Icon("globe", 18, UI.Brush(_look.Dim)) } : ScreenAppIcon(name, 34);
            icon.Margin = new Thickness(0, 0, 14, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            var nameLine = new DockPanel();
            var time = Label(ScreenTime.Duration(seconds), 14, dim: true);
            DockPanel.SetDock(time, System.Windows.Controls.Dock.Right);
            nameLine.Children.Add(time);
            var label = Label(name, 15, bold: true);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            nameLine.Children.Add(label);
            var track = new Grid { Height = 5, Margin = new Thickness(0, 8, 0, 0) };
            track.Children.Add(new Border { Background = UI.Brush(_look.CardActive), CornerRadius = new CornerRadius(2.5) });
            track.Children.Add(new Border { Background = UI.Brush(Accent), CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0, Tag = seconds / most });
            track.SizeChanged += (_, e) => ((Border)track.Children[1]).Width = e.NewSize.Width * (double)((Border)track.Children[1]).Tag;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nameLine, track } };
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
            row.Children.Add(icon);
            if (!state.Sites)
            {
                // Pin: the widget shows this app's time; pinned, a click puts the total back.
                bool isPinned = string.Equals(pinned, name, StringComparison.OrdinalIgnoreCase);
                var button = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center, Background = UI.Brush(isPinned ? _look.CardActive : "#00000000"),
                    Child = MonitorIcons.Icon("pin", 15, UI.Brush(isPinned ? Accent : _look.Dim)), Opacity = isPinned ? 1 : 0,
                    ToolTip = isPinned ? Loc.T($"{name} 显示在 Dock 上 - 点击显示总计", $"{name} is on the dock - click for the total") : Loc.T("在 Dock 上显示", "Show on the dock") };
                row.MouseEnter += (_, _) => button.Opacity = 1;
                row.MouseLeave += (_, _) => button.Opacity = isPinned ? 1 : 0;
                button.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    string? next = isPinned ? null : name;
                    pin(next);
                    var settings = _app.DockSettings;
                    _app.UpdateDockSettings(settings.WithWidgets(settings.Widgets.Select(w => w.Id == widget.Id ? w with { Option = next ?? "" } : w).ToList()), keepPanels: true);
                    render();
                };
                DockPanel.SetDock(button, System.Windows.Controls.Dock.Right);
                row.Children.Add(button);
            }
            row.Children.Add(text);
            list.Children.Add(new Border { BorderBrush = UI.Brush(_look.Divider), BorderThickness = new Thickness(0, 0, 0, 1), Child = row, Background = Brushes.Transparent });
        }
        return panel;
    }

    private FrameworkElement SummaryFooter()
    {
        var text = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = UI.Brush(_look.Text) };
        text.Inlines.Add(new System.Windows.Documents.Run(Loc.T("每日总结", "Daily summary")) { FontWeight = FontWeights.Bold });
        text.Inlines.Add(new System.Windows.Documents.Run(Loc.T(" · 每天结束时，周一还有上周的", " · at day's end, and the week's on Mondays")) { Foreground = UI.Brush(_look.Dim) });
        var time = Label("", 15, bold: true);
        time.VerticalAlignment = VerticalAlignment.Center;
        time.Margin = new Thickness(12, 0, 12, 0);
        void Show() => time.Text = TimeText(DateTime.Today.AddMinutes(_app.Settings.ScreenTimeSummaryMinute));
        FrameworkElement Step(string glyph, int minutes)
        {
            var step = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(8, 0, 8, 2), Child = Label(glyph, 18, dim: true) };
            step.MouseLeftButtonUp += (_, _) => { _app.SetScreenTimeOptions(summaryMinute: _app.Settings.ScreenTimeSummaryMinute + minutes); Show(); };
            return step;
        }
        Show();
        var stepper = new Border { Background = UI.Brush(_look.CardHover), CornerRadius = new CornerRadius(18), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 14, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { Step("−", -30), time, Step("+", 30) } } };
        Border? toggle = null;
        toggle = QuickSwitch(() => _app.Settings.ScreenTimeSummary, () => { _app.SetScreenTimeOptions(summary: !_app.Settings.ScreenTimeSummary); RepaintSwitch(toggle!); });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Children = { stepper, toggle } };
        var row = new DockPanel();
        DockPanel.SetDock(controls, System.Windows.Controls.Dock.Right);
        row.Children.Add(controls); row.Children.Add(text);
        return new Border { BorderBrush = UI.Brush(_look.Divider), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(-24, 14, -24, -8),
            Padding = new Thickness(24, 14, 24, 0), Child = row };
    }
}
