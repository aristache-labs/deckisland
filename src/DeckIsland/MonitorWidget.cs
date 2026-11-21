using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using BrimDeck.Core;
using BrimDeck.Native;

namespace BrimDeck;

public partial class MainWindow
{
    private Border MonitorWidget(Border frame, bool detailed)
    {
        frame.CornerRadius = new CornerRadius(16);
        var colors = MonitorThemeColors.For(Settings, Look);
        var metrics = new Grid { VerticalAlignment = VerticalAlignment.Center };
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        metrics.ColumnDefinitions.Add(new ColumnDefinition());
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cpu = AddMetric("CPU", colors.Performance, 0);
        var gpu = AddMetric("GPU", colors.Gpu, 1);
        var ram = AddMetric("RAM", colors.Memory, 2);
        var temperature = WeatherText("", 12);
        temperature.Margin = new Thickness(6, 0, 0, 0);
        temperature.HorizontalAlignment = HorizontalAlignment.Right;
        Typography.SetNumeralAlignment(temperature, FontNumeralAlignment.Tabular);
        metrics.Children.Add(temperature);
        Grid.SetColumn(temperature, 3);

        var traffic = new UniformGrid { Columns = 2, Margin = new Thickness(0, 3, 0, 0) };
        var down = WeatherText("", 11);
        var up = WeatherText("", 11);
        down.Foreground = up.Foreground = UI.Brush(Look.Secondary);
        up.Margin = new Thickness(5, 0, 0, 0);
        traffic.Children.Add(down);
        traffic.Children.Add(up);
        metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(traffic, 3);
        Grid.SetColumnSpan(traffic, 4);
        metrics.Children.Add(traffic);

        Button? link = detailed ? WidgetSurface(metrics, "", () => SelectPage(DeckPage.Monitor)) : null;
        frame.Child = link is null ? metrics : link;
        var memoryHistory = new Queue<double>(SystemSampler.HistoryLength);
        MonitorSnapshot? sampled = null;
        _widgetUpdates.Add(() =>
        {
            var snapshot = _app.Monitor?.Latest;
            var processor = snapshot?.Cpu.OrDefault;
            var graphics = snapshot?.Gpu.OrDefault;
            var memory = snapshot?.Memory.OrDefault;
            var network = snapshot?.Network.OrDefault;
            cpu.Value.Text = Percent(processor?.Usage);
            gpu.Value.Text = Percent(graphics?.Usage);
            ram.Value.Text = Percent(memory?.UsedFraction);
            temperature.Text = Settings.MonitorTemperatures && processor?.Temperature is { } celsius ? $"{Temperatures.Format(celsius)}°" : "";
            temperature.Visibility = temperature.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            string download = Rate(network?.DownBytesPerSecond), upload = Rate(network?.UpBytesPerSecond);
            down.Text = $"↓ {download}";
            up.Text = $"↑ {upload}";
            string downLabel = Loc.T("下载", "Download"), upLabel = Loc.T("上传", "Upload");
            AutomationProperties.SetName(down, $"{downLabel} {download}");
            AutomationProperties.SetName(up, $"{upLabel} {upload}");
            if (link is not null)
                AutomationProperties.SetName(link, $"{WidgetLayout.Name(WidgetKind.Monitor)} · CPU {cpu.Value.Text} {temperature.Text} · GPU {gpu.Value.Text} · RAM {ram.Value.Text} · {downLabel} {download} · {upLabel} {upload}");

            if (ReferenceEquals(sampled, snapshot)) return;
            sampled = snapshot;
            if (memory is null) memoryHistory.Clear();
            else
            {
                if (memoryHistory.Count == SystemSampler.HistoryLength) memoryHistory.Dequeue();
                memoryHistory.Enqueue(memory.UsedFraction);
            }
            cpu.Chart.SetHistory(snapshot?.CpuHistory ?? [], processor?.Usage);
            gpu.Chart.SetHistory(snapshot?.GpuHistory ?? [], graphics?.Usage);
            ram.Chart.SetHistory(memoryHistory.ToArray(), memory?.UsedFraction);
        });
        return frame;

        static string Percent(double? share) => share is { } value ? $"{MonitorFormat.Percent(value)}%" : "—";
        static string Rate(double? measured) => measured is { } value ? MonitorFormat.Rate(value).Text : "—";

        (TextBlock Value, WidgetSparkline Chart) AddMetric(string name, string color, int row)
        {
            metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = WeatherText(name, 12);
            var chart = new WidgetSparkline(color) { Height = 12, Margin = new Thickness(7, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, ClipToBounds = true };
            var value = WeatherText("", 12);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            Grid.SetRow(label, row);
            Grid.SetRow(chart, row); Grid.SetColumn(chart, 1);
            Grid.SetRow(value, row); Grid.SetColumn(value, 2);
            metrics.Children.Add(label); metrics.Children.Add(chart); metrics.Children.Add(value);
            return (value, chart);
        }
    }

    private sealed class WidgetSparkline(string color) : FrameworkElement
    {
        private IReadOnlyList<double> _history = [];

        public void SetHistory(IReadOnlyList<double> history, double? current)
        {
            // Extend the oldest real reading leftward until enough samples fill the chart.
            _history = current is not { } value ? []
                : history.Count >= SystemSampler.HistoryLength ? history
                : [.. Enumerable.Repeat(history.Count > 0 ? history[0] : value, SystemSampler.HistoryLength - history.Count), .. history];
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            if (ActualWidth < 10) return;
            MonitorDraw.Sparkline(drawing, new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), _history, 1, color, 0);
            if (_history.Count > 0)
            {
                double y = ActualHeight - 2 - Math.Clamp(_history[^1], 0, 1) * (ActualHeight - 4);
                drawing.DrawEllipse(UI.Brush(color), null, new Point(ActualWidth - 1.5, y), 1.5, 1.5);
            }
        }
    }
}
