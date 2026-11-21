using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    private sealed record QuotaWidgetReading(Guid Agent, string Name, string Window, string Value, double? Share,
        string Color, string Detail, bool Stale);

    private Border QuotasWidget(Border frame, bool detailed)
    {
        frame.CornerRadius = new CornerRadius(16);
        var content = new DockPanel();
        frame.Child = content;
        string title = WidgetLayout.Name(WidgetKind.Quotas);
        var heading = WeatherText(title, 12);
        heading.FontWeight = FontWeights.SemiBold;
        FrameworkElement header = heading;
        if (detailed)
        {
            var link = WidgetSurface(heading, Loc.T("打开配额与用量", "Open Quotas and usage"), () => SelectPage(DeckPage.Usage));
            link.MinHeight = 24;
            link.HorizontalAlignment = HorizontalAlignment.Left;
            header = link;
        }
        header.Margin = new Thickness(0, 0, 0, 3);
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        content.Children.Add(header);

        var rows = new StackPanel();
        var viewport = new ScrollViewer
        {
            Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true,
            FocusVisualStyle = (Style)FindResource("PanelKeyboardFocus")
        };
        AutomationProperties.SetName(viewport, title);
        content.Children.Add(viewport);
        QuotaWidgetReading[] previous = [];
        _widgetUpdates.Add(() =>
        {
            var now = DateTimeOffset.Now;
            bool remaining = Settings.QuotaRingRemaining;
            var readings = Settings.ConfiguredApps.Select(entry => WidgetQuotaReading(entry, remaining, now)).ToArray();
            if (readings.SequenceEqual(previous)) return;
            previous = readings;
            double offset = viewport.VerticalOffset;
            rows.Children.Clear();
            foreach (var reading in readings) rows.Children.Add(QuotaWidgetRow(reading));
            viewport.ScrollToVerticalOffset(offset);
        });
        return frame;
    }

    private QuotaWidgetReading WidgetQuotaReading(AppEntry entry, bool remaining, DateTimeOffset now)
    {
        var snapshot = DashboardUsage.Quota(entry, Snapshots);
        var choice = snapshot.Metrics.Select(metric => (Metric: metric, Used: QuotaRing.Used(metric, entry)))
            .Where(candidate => candidate.Used.HasValue)
            .OrderByDescending(candidate => candidate.Used)
            .ThenBy(candidate => candidate.Metric.Window is > 0 ? candidate.Metric.Window.Value : int.MaxValue)
            .FirstOrDefault();
        var metric = choice.Metric ?? snapshot.Metrics.FirstOrDefault();
        double? share = choice.Used is { } used ? remaining ? 100 - used : used : null;
        string value = share is { } percent
            ? remaining ? Loc.T($"剩余 {percent:0.#}%", $"{percent:0.#}% left") : Loc.T($"已用 {percent:0.#}%", $"{percent:0.#}% used")
            : metric?.ValueText is { Length: > 0 } figure ? figure : "—";
        string window = metric is not null ? MetricLabel(metric) : Loc.T("无配额数据", "No quota data");
        string color = choice.Used is { } usage ? Look.QuotaColor(entry, usage) : Look.QuotaTheme(entry);
        string detail = snapshot.Metrics.Count == 0 ? snapshot.StatusLabel
            : string.Join("\n\n", snapshot.Metrics.Select(item => item.Description));
        bool stale = snapshot.LastSeenAt is not null || snapshot.IsStale;
        if (snapshot.LastSeenAt is { } seen) detail += "\n" + UsageLastSeen.Label(seen, now);
        else if (stale) detail += "\n" + Loc.T("数据未更新", "Data is stale");
        return new(entry.InstanceId, entry.Name, window, value, share, color, detail, stale);
    }

    private FrameworkElement QuotaWidgetRow(QuotaWidgetReading reading)
    {
        var value = WeatherText(reading.Value, 11);
        value.FontWeight = FontWeights.SemiBold;
        value.Margin = new Thickness(6, 0, 0, 0);
        value.TextWrapping = TextWrapping.NoWrap;
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
        var caption = WeatherText("", 11);
        caption.TextWrapping = TextWrapping.NoWrap;
        caption.Inlines.Add(new Run(reading.Name) { FontWeight = FontWeights.SemiBold });
        caption.Inlines.Add(new Run($" · {reading.Window}{(reading.Stale ? " *" : "")}") { Foreground = UI.Brush(Look.Secondary) });
        var row = new StackPanel { Margin = new Thickness(0, 0, 5, 4), ToolTip = $"{reading.Name}\n{reading.Detail}" };
        row.Children.Add(new DockPanel { Children = { value, caption } });
        row.Children.Add(new QuotaBar($"widget:{reading.Agent:N}", (reading.Share ?? 0) / 100, reading.Color, Animates)
        {
            Height = 3, Margin = new Thickness(0, 2, 0, 0)
        });
        AutomationProperties.SetName(row, $"{reading.Name} · {reading.Window} · {reading.Value}{(reading.Stale ? $" · {Loc.T("数据未更新", "Data is stale")}" : "")}");
        return row;
    }
}
