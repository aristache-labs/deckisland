using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// The Day Timeline widget and its panel, after lumabar's: today's hours as stacked bars by kind of app (ScreenTimeExtras), the
// day's events as marks above them (an AI limit resetting, sunrise and sunset), and a line at the current time.
internal sealed partial class DockWindow
{
    private static readonly ScreenCategory[] StackOrder = [ScreenCategory.ChatMail, ScreenCategory.Other, ScreenCategory.Browser, ScreenCategory.Code, ScreenCategory.Docs];

    private string CategoryColour(ScreenCategory category) => category switch
    {
        ScreenCategory.ChatMail => "#FF30D158", ScreenCategory.Browser => "#FFFF9F0A", ScreenCategory.Code => Accent,
        ScreenCategory.Docs => "#FF64D2FF", _ => "#FF8E8E93"
    };

    private enum DayMark { Reset, Sun }

    // Today's events: each AI limit resetting, sunrise and sunset where the weather's place is.
    private List<(DateTime At, string Text, DayMark Kind)> DayEvents()
    {
        var events = _app.TodayQuotaResets().Select(reset => (reset.At, Loc.T($"{reset.Name} 限额重置", $"{reset.Name} limit resets"), DayMark.Reset)).ToList();
        if (_app.CurrentWeather?.Place is { } place && SunMoon.Sun(DateOnly.FromDateTime(DateTime.Today), place.Latitude, place.Longitude) is { } sun)
        {
            events.Add((sun.Rise.ToLocalTime(), Loc.T("日出", "Sunrise"), DayMark.Sun));
            events.Add((sun.Set.ToLocalTime(), Loc.T("日落", "Sunset"), DayMark.Sun));
        }
        return events.Where(e => e.At.Date == DateTime.Today).OrderBy(e => e.At).ToList();
    }

    // The first hour drawn: 7 in the morning, or earlier if the day started earlier.
    private static int FirstTimelineHour(Dictionary<ScreenCategory, int[]> stacks)
    {
        for (int hour = 0; hour < 7; hour++)
            if (stacks.Values.Any(hours => hours[hour] > 0)) return hour;
        return 7;
    }

    // The chart: a column per hour from `from` to midnight, stacked by kind, a dash where an hour is empty, marks above for events
    // and a line at the current time.
    private void DrawTimeline(Canvas canvas, Dictionary<ScreenCategory, int[]> stacks, List<(DateTime At, string Text, DayMark Kind)> events, double barTop,
        int from, string[]? labels)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth, height = canvas.ActualHeight - (labels is null ? 0 : 20);
        if (width <= 0 || height <= 0) return;
        int count = 24 - from;
        double step = width / count, bar = Math.Max(2, step * 0.82), chart = height - barTop;
        for (int i = 0; i < count; i++)
        {
            int hour = from + i;
            double x = i * step + (step - bar) / 2, y = height;
            bool any = false;
            foreach (var category in StackOrder)
            {
                double share = stacks[category][hour] / 3600.0;
                if (share <= 0) continue;
                any = true;
                double h = Math.Max(1, chart * share);
                var block = new Rectangle { Width = bar, Height = h, Fill = UI.Brush(CategoryColour(category)) };
                Canvas.SetLeft(block, x); Canvas.SetTop(block, y - h);
                canvas.Children.Add(block);
                y -= h;
            }
            if (!any)
            {
                var dash = new Rectangle { Width = bar, Height = 1.5, Fill = UI.Brush(_look.Dim), Opacity = 0.45 };
                Canvas.SetLeft(dash, x); Canvas.SetTop(dash, height - 1.5);
                canvas.Children.Add(dash);
            }
            if (labels is not null && hour % 3 == 0)
            {
                var label = Label(hour.ToString(CultureInfo.InvariantCulture), 12.5, dim: true);
                label.Width = 30; label.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(label, i * step + step / 2 - 15); Canvas.SetTop(label, height + 4);
                canvas.Children.Add(label);
            }
        }
        double X(DateTime at) => (at.Hour + at.Minute / 60.0 - from) / count * width;
        double mark = Math.Max(5, barTop * 0.42);
        foreach (var (at, _, kind) in events)
        {
            if (at.Hour < from) continue;
            var dot = kind == DayMark.Reset
                ? new Ellipse { Width = mark, Height = mark, Fill = UI.Brush("#FF64D2FF") }
                : new Ellipse { Width = mark, Height = mark, Stroke = UI.Brush("#FFFF9F0A"), StrokeThickness = 1.3 };
            Canvas.SetLeft(dot, X(at) - mark / 2); Canvas.SetTop(dot, barTop * 0.2);
            canvas.Children.Add(dot);
        }
        var now = DateTime.Now;
        if (now.Hour >= from)
        {
            double x = X(now);
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = barTop * 0.1, Y2 = height, Stroke = UI.Brush(Accent), StrokeThickness = 1.6 });
            var pin = new Ellipse { Width = 5, Height = 5, Fill = UI.Brush(Accent) };
            Canvas.SetLeft(pin, x - 2.5); Canvas.SetTop(pin, height - 2.5);
            canvas.Children.Add(pin);
        }
    }

    private (FrameworkElement, Action) DayTimelineTile(double tile)
    {
        var date = Label("", tile * 0.18, bold: true);
        var total = Label("", tile * 0.16, dim: true);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, tile * 0.02) };
        DockPanel.SetDock(total, System.Windows.Controls.Dock.Right);
        header.Children.Add(total); header.Children.Add(date);
        var canvas = new Canvas { Height = tile * 0.56, ClipToBounds = false };
        var body = new DockPanel { Margin = new Thickness(tile * 0.18, tile * 0.08, tile * 0.18, tile * 0.1) };
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        body.Children.Add(header); body.Children.Add(canvas);
        Dictionary<ScreenCategory, int[]> stacks = ScreenTimeExtras.ByCategory(null);
        List<(DateTime, string, DayMark)> events = [];
        void Draw() => DrawTimeline(canvas, stacks, events, canvas.ActualHeight * 0.3, FirstTimelineHour(stacks), null);
        canvas.SizeChanged += (_, _) => Draw();
        int minute = -1;
        return (body, () =>
        {
            var now = DateTime.Now;
            if (now.Minute == minute) return;
            minute = now.Minute;
            var today = DateOnly.FromDateTime(now);
            date.Text = now.ToString("ddd d MMM", CultureInfo.CurrentCulture);
            total.Text = ScreenTime.Duration(ScreenTime.Total(ScreenApps(today)));
            stacks = ScreenTimeExtras.ByCategory(ScreenExtra(today));
            events = DayEvents();
            Draw();
        });
    }

    private void OpenDayTimelinePanel(FrameworkElement anchor)
    {
        double offset = anchor.TranslatePoint(new Point(0, 0), _bar).X;
        if (!BeginDockPanel(_bar)) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var title = new StackPanel { Children = { Label(Loc.T("今天", "Today"), 20, bold: true),
            Label(today.ToString("dddd d MMMM", CultureInfo.CurrentCulture), 13.5, dim: true) } };
        var close = RoundButton(MonitorIcons.Icon("close", 15, UI.Brush(_look.Dim)), CloseWidgetDetails, Loc.T("关闭", "Close"));
        close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top;
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14), Children = { title, close } };

        var stacks = ScreenTimeExtras.ByCategory(ScreenExtra(today));
        var events = DayEvents();
        var canvas = new Canvas { Height = 160, Margin = new Thickness(0, 0, 0, 14) };
        int from = FirstTimelineHour(stacks);
        canvas.SizeChanged += (_, _) => DrawTimeline(canvas, stacks, events, 32, from, []);

        int total = ScreenTime.Total(ScreenApps(today));
        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        var lead = Label(Loc.T($"电脑上 {ScreenTime.Duration(total)}", $"{ScreenTime.Duration(total)} on the PC"), 13.5, bold: true);
        lead.Margin = new Thickness(0, 0, 16, 6);
        legend.Children.Add(lead);
        foreach (var (category, seconds) in StackOrder.Select(c => (c, stacks[c].Sum())).Where(item => item.Item2 > 0).OrderByDescending(item => item.Item2))
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 6) };
            item.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = UI.Brush(CategoryColour(category)), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(Label(ScreenTimeExtras.Name(category), 13.5));
            var time = Label(ScreenTime.Duration(seconds), 13.5, dim: true);
            time.Margin = new Thickness(8, 0, 0, 0);
            item.Children.Add(time);
            legend.Children.Add(item);
        }
        var list = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var (at, text, kind) in events)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var time = Label(TimeText(at), 14, dim: true);
            time.Width = 64;
            row.Children.Add(time);
            row.Children.Add(new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = UI.Brush(kind == DayMark.Reset ? "#FF5A9BB5" : "#FFA0700F") });
            row.Children.Add(Label(text, 14));
            list.Children.Add(row);
        }
        if (events.Count == 0) list.Children.Add(Label(Loc.T("今天没有事件", "Nothing else today"), 13.5, dim: true));
        var body = new StackPanel { Children = { header, canvas, legend,
            new Border { Height = 1, Background = UI.Brush(_look.Divider), Margin = new Thickness(-24, 0, -24, 0) }, list } };
        ShowDockPanel(_bar, new Border { MinHeight = 440, Child = body }, () => { }, 650, 24, CalendarRadius(16, 30), null, Math.Max(0, offset - 150));
    }
}
