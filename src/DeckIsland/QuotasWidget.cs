using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private sealed record QuotaPeriodReading(string Label, string Value, string Color, double Fraction);
    private sealed record QuotaWidgetReading(string Name, string? Icon, QuotaPeriodReading Daily,
        QuotaPeriodReading Weekly, string Detail, bool Stale);

    private static readonly Dictionary<string, BitmapImage> QuotaIcons = [];

    private Border QuotasWidget(Border frame, bool detailed)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        string title = WidgetLayout.Name(WidgetKind.Quotas);
        var tiles = new UniformGrid { Columns = 1, VerticalAlignment = VerticalAlignment.Top };
        var viewport = new ScrollViewer
        {
            Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true,
            FocusVisualStyle = (Style)FindResource("PanelKeyboardFocus")
        };
        viewport.SizeChanged += (_, _) => tiles.Columns = Math.Clamp((int)((viewport.ActualWidth - 10) / 150), 1, Math.Max(1, tiles.Children.Count));
        AutomationProperties.SetName(viewport, title);
        var empty = WeatherText(Loc.T("没有正在运行的代理", "No agents running"), 12);
        empty.TextAlignment = TextAlignment.Center;
        empty.Foreground = UI.Brush(Look.Secondary);
        frame.Child = new Grid { Children = { viewport, empty } };
        QuotaWidgetReading[] previous = [];
        _widgetUpdates.Add(() =>
        {
            var now = DateTimeOffset.Now;
            bool remaining = Settings.QuotaRingRemaining;
            var readings = RunningQuotaApps().Select(entry => WidgetQuotaReading(entry, remaining, now)).ToArray();
            viewport.Visibility = readings.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = readings.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (readings.SequenceEqual(previous)) return;
            previous = readings;
            double offset = viewport.VerticalOffset;
            tiles.Children.Clear();
            foreach (var reading in readings) tiles.Children.Add(QuotaWidgetTile(reading, detailed));
            tiles.Columns = Math.Clamp((int)((viewport.ActualWidth - 10) / 150), 1, Math.Max(1, readings.Length));
            viewport.ScrollToVerticalOffset(offset);
        });
        return frame;
    }

    private QuotaWidgetReading WidgetQuotaReading(AppEntry entry, bool remaining, DateTimeOffset now)
    {
        var snapshot = DashboardUsage.Quota(entry, Snapshots);
        var dailyUsed = UsedFor(TimeSpan.FromDays(1));
        // Cursor bills by period, so its rows show the same metrics as Quotas and usage instead of Daily and Weekly.
        var cursor = entry.QuotaSource == ProviderId.Cursor
            ? snapshot.Metrics.Where(metric => QuotaRing.Used(metric, entry) is not null).Take(2).ToArray() : [];
        var daily = cursor.Length > 0 ? Period(QuotaRing.Used(cursor[0], entry), MetricLabel(cursor[0]))
            : dailyUsed is null && entry.QuotaSource is ProviderId.Codex or ProviderId.Claude &&
              UsedFor(TimeSpan.FromHours(5)) is { } shortUsed
            ? Period(shortUsed, Loc.T("5 小时", "5-hour"))
            : Period(dailyUsed, Loc.T("每日", "Daily"));
        var weekly = cursor.Length > 1 ? Period(QuotaRing.Used(cursor[1], entry), MetricLabel(cursor[1]))
            : Period(UsedFor(TimeSpan.FromDays(7)), Loc.T("每周", "Weekly"));
        string detail = snapshot.Metrics.Count == 0 ? snapshot.StatusLabel
            : string.Join("\n\n", snapshot.Metrics.Select(item => item.Description));
        bool stale = snapshot.LastSeenAt is not null || snapshot.IsStale;
        if (snapshot.LastSeenAt is { } seen) detail += "\n" + UsageLastSeen.Label(seen, now);
        else if (stale) detail += "\n" + Loc.T("数据未更新", "Data is stale");
        return new(entry.Name, QuotaAgentIcon(entry), daily, weekly, detail, stale);

        double? UsedFor(TimeSpan period) => snapshot.Metrics.Where(metric => metric.Window == period.TotalMinutes)
            .Select(metric => QuotaRing.Used(metric, entry)).Max();

        QuotaPeriodReading Period(double? used, string label)
        {
            if (used is not { } percent) return new(label, "—", Look.Secondary, 0);
            double shown = remaining ? 100 - percent : percent;
            return new(label, $"{shown:0.#}%", Look.Readable(Look.QuotaColor(entry, percent), 4.5), Math.Clamp(shown / 100, 0, 1));
        }
    }

    private FrameworkElement QuotaWidgetTile(QuotaWidgetReading reading, bool detailed)
    {
        FrameworkElement badge;
        if (reading.Icon is { } icon && !Look.HighContrast)
        {
            if (!QuotaIcons.TryGetValue(icon, out var bitmap))
            {
                bitmap = new BitmapImage(new Uri($"pack://application:,,,/DeckIsland;component/Assets/QuotaAgents/{icon}.png"));
                bitmap.Freeze();
                QuotaIcons[icon] = bitmap;
            }
            badge = new Image { Source = bitmap, Height = 20, Width = 20, Stretch = System.Windows.Media.Stretch.Uniform };
        }
        else
            badge = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = UI.Brush(Look.Hex("fillSelected")),
                Child = UI.Centered(CompactMusicLayout.RingLetterFor(true, reading.Name), 11, Look.Primary, FontWeights.SemiBold)
            };
        badge.Margin = new Thickness(0, 0, 7, 0);
        var name = WeatherText(reading.Name + (reading.Stale ? " *" : ""), 12);
        name.FontWeight = FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        name.VerticalAlignment = VerticalAlignment.Center;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 5), Children = { badge, name } };
        DockPanel.SetDock(badge, System.Windows.Controls.Dock.Left);

        var card = new StackPanel { Children = { header, PeriodRow(reading.Daily, 0), PeriodRow(reading.Weekly, 1) } };
        string mode = Settings.QuotaRingRemaining ? Loc.T("剩余", "Left") : Loc.T("已用", "Used");
        string summary = $"{reading.Name} · {mode} · {reading.Daily.Label} {reading.Daily.Value} · {reading.Weekly.Label} {reading.Weekly.Value}";
        if (reading.Stale) summary += $" · {Loc.T("数据未更新", "Data is stale")}";
        string tooltip = $"{summary}\n{reading.Detail}";
        var frame = new Border
        {
            Margin = new Thickness(3), Padding = new Thickness(9, 7, 9, 7), CornerRadius = new CornerRadius(Corner(11)),
            Background = UI.Brush(Look.Hex("fillSelected")), Child = card, ToolTip = tooltip
        };
        AutomationProperties.SetName(frame, summary);
        if (!detailed) return frame;
        var link = WidgetSurface(frame, summary, () => SelectPage(DeckPage.Usage));
        link.ToolTip = tooltip;
        return link;

        FrameworkElement PeriodRow(QuotaPeriodReading period, int index)
        {
            var value = WeatherText(period.Value, 13);
            value.FontWeight = FontWeights.SemiBold;
            value.Foreground = UI.Brush(period.Color);
            value.Margin = new Thickness(6, 0, 0, 0);
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
            var label = WeatherText(period.Label, 10.5);
            label.Foreground = UI.Brush(Look.Secondary);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextWrapping = TextWrapping.NoWrap;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            var bar = new QuotaBar($"widget:{reading.Name}:{index}", period.Fraction, period.Color, Animates)
                { Height = 3, Margin = new Thickness(0, 2, 0, index == 0 ? 6 : 0) };
            return new StackPanel { Children = { new DockPanel { Children = { value, label } }, bar } };
        }
    }

    private string? QuotaAgentIcon(AppEntry entry)
    {
        string variant = Look.Current.IsDark ? "dark" : "light";
        return entry.QuotaSource switch
        {
            ProviderId.Codex => $"codex-{variant}",
            ProviderId.Claude => "claude",
            ProviderId.Cursor => $"cursor-{variant}",
            ProviderId.Antigravity => "antigravity",
            ProviderId.GrokBot => $"grok-{variant}",
            ProviderId.DeepSeek or ProviderId.Dsh => "deepseek",
            ProviderId.Kimi or ProviderId.KimiApi or ProviderId.KimiPlatform => "kimi",
            ProviderId.Custom when entry.Name.Contains("Gemini", StringComparison.OrdinalIgnoreCase) => "gemini",
            ProviderId.Custom when entry.Name.Contains("Hermes", StringComparison.OrdinalIgnoreCase) => $"hermes-{variant}",
            _ => null
        };
    }
}
