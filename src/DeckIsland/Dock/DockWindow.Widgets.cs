using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// The dock's widgets (DockWidgets): cards on the bar after the apps, each built once and refreshed with the clock's tick. While the
// widget picker is open each carries a remove badge; dragged, they change places among themselves.
internal sealed partial class DockWindow
{
    private readonly List<Action> _widgetUpdates = [];

    // Builds the profile's widgets into the tiles; called while the tiles are rebuilt.
    private void AddWidgets(DockSettings settings, double tile)
    {
        _widgetUpdates.Clear();
        foreach (var widget in settings.Widgets)
        {
            var (content, update) = WidgetContent(widget, tile);
            _apps.Children.Add(WidgetFrame(widget, content, tile));
            _widgetUpdates.Add(update);
            update();
        }
    }

    private void UpdateWidgets() { foreach (var update in _widgetUpdates) update(); }

    // Preview: the picker's sample tile, which takes no clicks.
    internal (FrameworkElement Content, Action Update) WidgetContent(DockWidget widget, double tile) => widget.Kind switch
    {
        DockWidgetKind.Cpu => Meter(tile, "CPU", () => _app.DockMonitor?.Cpu.OrDefault is { } cpu ? cpu.Usage : null),
        DockWidgetKind.Memory => Meter(tile, "RAM", () => _app.DockMonitor?.Memory.OrDefault is { } memory ? memory.UsedFraction : null),
        DockWidgetKind.Battery => Battery(tile),
        DockWidgetKind.AiUsage => AiUsage(tile, widget.Option),
        DockWidgetKind.Network => Network(tile),
        DockWidgetKind.Storage => Storage(tile),
        DockWidgetKind.Volume => Volume(tile),
        DockWidgetKind.Weather => Weather(tile),
        DockWidgetKind.AnalogClock => AnalogClock(tile),
        DockWidgetKind.NowPlaying => NowPlaying(tile),
        DockWidgetKind.Calendar => Calendar(tile),
        DockWidgetKind.AiRing => AiRing(tile, widget.Option),
        _ => NextEvent(tile)
    };

    // A widget's content on a card in this dock's style, for the picker's live tiles.
    internal Border PreviewCard(FrameworkElement content, int width, double tile) => new()
    {
        Width = tile * width + 4 * (width - 1), Height = tile, CornerRadius = new CornerRadius(TileRadius()), Child = content, IsHitTestVisible = false,
        Background = UI.Brush(_look.Card), BorderBrush = UI.Brush(_look.CardRim), BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    private Border WidgetFrame(DockWidget widget, FrameworkElement content, double tile)
    {
        int width = DockWidgets.Info(widget.Kind).Width;
        var grid = new Grid(); grid.Children.Add(content);
        if (_app.DockPickerOpen)
        {
            // Edit mode: a badge removes the widget.
            var badge = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = UI.Brush("#FF8E8E93"), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-5, -5, 0, 0),
                Child = new Rectangle { Width = 8, Height = 1.6, Fill = Brushes.White } };
            badge.MouseLeftButtonDown += (_, e) => e.Handled = true;
            badge.MouseLeftButtonUp += (_, e) => { _app.UpdateDockSettings(_app.DockSettings.WithWidgets(DockWidgets.Remove(_app.DockSettings.Widgets, widget.Id))); e.Handled = true; };
            grid.Children.Add(badge);
        }
        var frame = new Border
        {
            Width = tile * width + _spacing * (width - 1), Height = tile, CornerRadius = new CornerRadius(TileRadius()), Margin = new Thickness(_spacing / 2, 0, _spacing / 2, 0),
            Background = UI.Brush(_look.Card), BorderBrush = UI.Brush(_look.CardRim), BorderThickness = new Thickness(1), Child = grid, Tag = widget,
            ToolTip = _app.DockSettings.NamesOnHover ? DockWidgets.Info(widget.Kind).Name : null
        };
        frame.MouseLeftButtonDown += (_, e) => { e.Handled = true; _press = (frame, e.GetPosition(this)); frame.CaptureMouse(); };
        frame.MouseMove += (_, e) => Drag(frame, e.GetPosition(this));
        frame.LostMouseCapture += (_, _) =>
        {
            if (_press?.Tile != frame) return;
            _press = null; _dragging = false; frame.Opacity = 1; RefreshSoon();
        };
        frame.MouseLeftButtonUp += (_, e) => { ReleaseWidget(frame, widget); e.Handled = true; };
        return frame;
    }

    // A widget let go after a drag takes its new place among the widgets; a plain click does nothing yet.
    private void ReleaseWidget(Border frame, DockWidget widget)
    {
        if (_press?.Tile != frame) return;
        bool dragged = _dragging;
        _press = null; _dragging = false; frame.Opacity = 1;
        frame.ReleaseMouseCapture();
        if (!dragged) return;
        var order = _apps.Children.OfType<Border>().Select(child => child.Tag).OfType<DockWidget>().ToList();
        _app.UpdateDockSettings(_app.DockSettings.WithWidgets(DockWidgets.Move(_app.DockSettings.Widgets, widget.Id, order.FindIndex(w => w.Id == widget.Id))));
        RefreshSoon();
    }

    private TextBlock Label(string text, double size, bool bold = false, bool dim = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = UI.Brush(dim ? _look.Dim : _look.Text),
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap
    };

    // A square tile: a ring that fills with the share, the figure inside and its name under it.
    private (FrameworkElement, Action) Meter(double tile, string name, Func<double?> share)
    {
        double size = tile * 0.62;
        var track = new Ellipse { Width = size, Height = size, Stroke = UI.Brush(_look.Divider), StrokeThickness = 3 };
        var arc = new Path { Width = size, Height = size, Stroke = UI.Brush(_app.DockSettings.Accent), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var figure = Label("–", tile * 0.2, bold: true); figure.HorizontalAlignment = HorizontalAlignment.Center; figure.VerticalAlignment = VerticalAlignment.Center;
        var ring = new Grid { Width = size, Height = size, Margin = new Thickness(0, tile * 0.06, 0, 0) };
        ring.Children.Add(track); ring.Children.Add(arc); ring.Children.Add(figure);
        var caption = Label(name, tile * 0.15, dim: true); caption.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(ring); stack.Children.Add(caption);
        return (stack, () =>
        {
            var value = share();
            figure.Text = value is { } v ? Math.Round(v * 100).ToString(CultureInfo.CurrentCulture) : "–";
            arc.Data = RingArc(size, 3, value ?? 0);
        });
    }

    // The arc of a ring of this size drawn for a share between 0 and 1, from the top, clockwise.
    private static Geometry RingArc(double size, double thickness, double share)
    {
        share = Math.Clamp(share, 0, 0.9999);
        if (share <= 0) return Geometry.Empty;
        double radius = (size - thickness) / 2, centre = size / 2, angle = share * 2 * Math.PI;
        var start = new Point(centre, centre - radius);
        var end = new Point(centre + radius * Math.Sin(angle), centre - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, share > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AcLine, Flag, Percent, SaverStatus; public int LifeTime, FullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);

    // lumabar's battery tile: the card fills like water to the charge, the figure on it, a bolt while charging.
    private (FrameworkElement, Action) Battery(double tile)
    {
        var water = new Border { VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, TileRadius() - 1, TileRadius() - 1),
            Background = new LinearGradientBrush(Color.FromRgb(0x5A, 0xB8, 0xFF), Color.FromRgb(0x1E, 0x7B, 0xE6), 90) };
        var figure = Label("–", tile * 0.27, bold: true); figure.Foreground = Brushes.White; figure.HorizontalAlignment = HorizontalAlignment.Center; figure.VerticalAlignment = VerticalAlignment.Center;
        var bolt = new Path { Data = Geometry.Parse("M 6,0 L 0,9 H 4 L 3,16 L 10,6 H 6 Z"), Fill = Brushes.White, Width = tile * 0.12, Height = tile * 0.2, Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, tile * 0.42, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ClipToBounds = true };
        grid.Children.Add(water); grid.Children.Add(figure); grid.Children.Add(bolt);
        return (grid, () =>
        {
            bool known = GetSystemPowerStatus(out var status) && status.Percent <= 100 && (status.Flag & 128) == 0;
            figure.Text = known ? status.Percent.ToString(CultureInfo.CurrentCulture) : Loc.T("电源", "AC");
            figure.Foreground = known ? Brushes.White : UI.Brush(_look.Text);
            water.Height = known ? Math.Max(2, (tile - 2) * status.Percent / 100.0) : 0;
            bolt.Visibility = known && status.AcLine == 1 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    // lumabar's AI limits card: the tool's name, then its limits as bars with how much is used.
    private (FrameworkElement, Action) AiUsage(double tile, string option)
    {
        var name = Label("AI", tile * 0.2, bold: true);
        var rows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(tile * 0.18, 0, tile * 0.18, 0) };
        var panel = new DockPanel { LastChildFill = true };
        name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(tile * 0.2, 0, 0, 0); name.Width = tile * 0.9;
        DockPanel.SetDock(name, System.Windows.Controls.Dock.Left); panel.Children.Add(name); panel.Children.Add(rows);
        return (panel, () =>
        {
            var snapshots = _app.Deck?.Snapshots ?? [];
            var snapshot = snapshots.FirstOrDefault(s => s.Id.ToString() == option && s.Quotas.Count > 0) ?? snapshots.FirstOrDefault(s => s.Quotas.Count > 0);
            rows.Children.Clear();
            if (snapshot is null) { name.Text = "AI"; rows.Children.Add(Label(Loc.T("没有用量数据", "No usage yet"), tile * 0.17, dim: true)); return; }
            name.Text = snapshot.Name;
            foreach (var quota in snapshot.Quotas.Take(2)) rows.Children.Add(QuotaRow(quota, tile));
        });
    }

    private FrameworkElement QuotaRow(Quota quota, double tile)
    {
        // Data labels are kept in Chinese and worded for the interface when shown.
        var label = Label(Loc.Label(quota.Label), tile * 0.16, bold: true); label.Width = tile * 1.1;
        var percent = Label($"{Math.Round(quota.UsedPercent)}%", tile * 0.16, bold: true); percent.Width = tile * 0.55; percent.TextAlignment = TextAlignment.Right;
        double share = Math.Clamp(quota.UsedPercent / 100, 0, 1);
        var bar = new Grid { Height = 3.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = UI.Brush(_look.Divider) });
        var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = UI.Brush(share > 0.85 ? "#FFFF453A" : "#FF30D158") };
        bar.Children.Add(fill);
        bar.SizeChanged += (_, _) => fill.Width = bar.ActualWidth * share;
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1, 0, 1) };
        DockPanel.SetDock(label, System.Windows.Controls.Dock.Left); DockPanel.SetDock(percent, System.Windows.Controls.Dock.Right);
        row.Children.Add(label); row.Children.Add(percent); row.Children.Add(bar);
        return row;
    }

    // lumabar's calendar card: a small date leaf, then the next event's title and when it starts.
    private (FrameworkElement, Action) NextEvent(double tile)
    {
        var weekday = Label("", tile * 0.13, bold: true); weekday.Foreground = UI.Brush(DockLook.Weekday); weekday.HorizontalAlignment = HorizontalAlignment.Center;
        var day = Label("", tile * 0.3, bold: true); day.HorizontalAlignment = HorizontalAlignment.Center;
        var leafStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        leafStack.Children.Add(weekday); leafStack.Children.Add(day);
        var leaf = new Border { Width = tile * 0.72, Height = tile * 0.72, CornerRadius = new CornerRadius(TileRadius() - 2), Background = UI.Brush(_look.CardHover),
            Margin = new Thickness(tile * 0.14, 0, tile * 0.14, 0), Child = leafStack };
        var title = Label("", tile * 0.18, bold: true);
        var when = Label("", tile * 0.15, dim: true);
        var marker = new Rectangle { Width = 2.5, Fill = UI.Brush("#FFFF9F0A"), Margin = new Thickness(0, 2, 6, 2) };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title); text.Children.Add(when);
        var body = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, tile * 0.14, 0) };
        DockPanel.SetDock(marker, System.Windows.Controls.Dock.Left); body.Children.Add(marker); body.Children.Add(text);
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(leaf, System.Windows.Controls.Dock.Left); panel.Children.Add(leaf); panel.Children.Add(body);
        return (panel, () =>
        {
            var now = DateTime.Now;
            weekday.Text = now.ToString("ddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
            day.Text = now.Day.ToString(CultureInfo.CurrentCulture);
            var next = _app.CalendarEvents.FirstOrDefault(e => !e.AllDay && e.End > now);
            marker.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
            // One line: titles may hold line breaks.
            title.Text = next is null ? Loc.T("今天没有日程", "Nothing scheduled") : string.Join(' ', next.Title.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            when.Text = next is null ? "" : next.Start <= now ? Loc.T("进行中", "Now")
                : $"{next.Start.ToString("t", CultureInfo.CurrentCulture)} · {Loc.T("还有", "in")} {Until(next.Start - now)}";
        });
    }

    private static string Until(TimeSpan span) => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))}m";
}
