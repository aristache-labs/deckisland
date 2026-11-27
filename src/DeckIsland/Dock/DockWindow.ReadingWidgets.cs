using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

internal sealed partial class DockWindow
{
    private (FrameworkElement, Action) TopProcessTile(double tile, bool memory)
    {
        var name = Label("", tile * 0.22, bold: true);
        var value = Label("", tile * 0.17, dim: true);
        var stack = TimeStack(name, value);
        stack.Margin = new Thickness(tile * 0.12, 0, tile * 0.12, 0);
        return (stack, () =>
        {
            var processes = _app.DockMonitor?.Processes.OrDefault;
            var process = (memory ? processes?.ByMemory : processes?.Top)?.FirstOrDefault();
            name.Text = process?.Name ?? (memory ? "Top RAM" : "Top CPU");
            value.Text = process is null ? "–" : memory ? MonitorFormat.ProcessMemory(process.MemoryBytes)
                : process.CpuPercent.ToString("0.#", CultureInfo.CurrentCulture) + "% CPU";
        });
    }

    private (FrameworkElement, Action) TemperatureTile(double tile) => ReadingPair(tile, "CPU", "GPU", () =>
    {
        var monitor = _app.DockMonitor;
        string Format(double? celsius) => celsius is { } value ? Temperatures.Format(value) + "°" : "–";
        return (Format(monitor?.Cpu.OrDefault?.Temperature), Format(monitor?.Gpu.OrDefault?.Temperature));
    });

    private (FrameworkElement, Action) ReadingPair(double tile, string first, string second, Func<(string, string)> values)
    {
        var left = Label("", tile * 0.22, bold: true);
        var right = Label("", tile * 0.22, bold: true);
        var content = new UniformGrid { Columns = 2, Margin = new Thickness(tile * 0.08, 0, tile * 0.08, 0) };
        content.Children.Add(TimeStack(Label(first, tile * 0.16, dim: true), left));
        content.Children.Add(TimeStack(Label(second, tile * 0.16, dim: true), right));
        return (content, () => (left.Text, right.Text) = values());
    }

    private (FrameworkElement, Action) ProgressTile(DockWidgetKind kind, double tile)
    {
        string title = kind switch
        {
            DockWidgetKind.DayProgress => Loc.T("今天", "Day"),
            DockWidgetKind.MonthProgress => Loc.T("本月", "Month"),
            _ => Loc.T("今年", "Year")
        };
        var value = Label("", tile * 0.24, bold: true);
        var caption = Label(title, tile * 0.19, dim: true);
        var heading = new DockPanel();
        DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
        heading.Children.Add(value);
        heading.Children.Add(caption);
        var track = new Grid { Height = Math.Max(2, tile * 0.08), Margin = new Thickness(0, 4, 0, 0) };
        var elapsed = new ColumnDefinition();
        var remaining = new ColumnDefinition();
        track.ColumnDefinitions.Add(elapsed);
        track.ColumnDefinitions.Add(remaining);
        var background = new Border { CornerRadius = new CornerRadius(2), Background = UI.Brush(_look.Divider) };
        Grid.SetColumnSpan(background, 2);
        track.Children.Add(background);
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = UI.Brush(Accent) });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(tile * 0.22, 0, tile * 0.22, 0) };
        stack.Children.Add(heading);
        stack.Children.Add(track);
        return (stack, () =>
        {
            var spans = PeriodProgress.At(DateTime.Now, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
            int percent = PeriodProgress.Shares.Percent(kind switch
            {
                DockWidgetKind.DayProgress => spans.Day,
                DockWidgetKind.MonthProgress => spans.Month,
                _ => spans.Year
            });
            string text = $"{percent}%";
            if (value.Text == text) return;
            value.Text = text;
            elapsed.Width = new GridLength(percent, GridUnitType.Star);
            remaining.Width = new GridLength(100 - percent, GridUnitType.Star);
        });
    }
}
