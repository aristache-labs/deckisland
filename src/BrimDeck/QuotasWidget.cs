using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    private sealed record QuotaPeriodReading(string Label, string Value, string Color);
    private sealed record QuotaWidgetReading(string Name, string? Icon, QuotaPeriodReading Daily,
        QuotaPeriodReading Weekly, string Detail, bool Stale);

    private static readonly Dictionary<string, BitmapImage> QuotaIcons = [];

    private Border QuotasWidget(Border frame, bool detailed)
    {
        frame.CornerRadius = new CornerRadius(16);
        string title = WidgetLayout.Name(WidgetKind.Quotas);
        var tiles = new UniformGrid { Columns = 3, VerticalAlignment = VerticalAlignment.Center };
        var viewport = new ScrollViewer
        {
            Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true,
            FocusVisualStyle = (Style)FindResource("PanelKeyboardFocus")
        };
        viewport.SizeChanged += (_, _) => tiles.Columns = Math.Clamp((int)((viewport.ActualWidth - 10) / 88), 1, AppPresets.MaximumShownApps);
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
            viewport.ScrollToVerticalOffset(offset);
        });
        return frame;
    }

    private QuotaWidgetReading WidgetQuotaReading(AppEntry entry, bool remaining, DateTimeOffset now)
    {
        var snapshot = DashboardUsage.Quota(entry, Snapshots);
        var dailyUsed = UsedFor(TimeSpan.FromDays(1));
        var daily = dailyUsed is null && entry.QuotaSource is ProviderId.Codex or ProviderId.Claude &&
            UsedFor(TimeSpan.FromHours(5)) is { } shortUsed
            ? Period(shortUsed, Loc.T("5 小时", "5-hour"))
            : Period(dailyUsed, Loc.T("每日", "Daily"));
        var weekly = Period(UsedFor(TimeSpan.FromDays(7)), Loc.T("每周", "Weekly"));
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
            if (used is not { } percent) return new(label, "—", Look.Secondary);
            double shown = remaining ? 100 - percent : percent;
            return new(label, $"{shown:0.#}%", Look.Readable(Look.QuotaColor(entry, percent), 4.5));
        }
    }

    private FrameworkElement QuotaWidgetTile(QuotaWidgetReading reading, bool detailed)
    {
        var identity = new Grid { Height = 28 };
        if (reading.Icon is { } icon && !Look.HighContrast)
        {
            if (!QuotaIcons.TryGetValue(icon, out var bitmap))
            {
                bitmap = new BitmapImage(new Uri($"pack://application:,,,/BrimDeck;component/Assets/QuotaAgents/{icon}.png"));
                bitmap.Freeze();
                QuotaIcons[icon] = bitmap;
            }
            identity.Children.Add(new Image { Source = bitmap, Height = 28, Width = 28, Stretch = System.Windows.Media.Stretch.Uniform });
        }
        else
        {
            string initials = CompactMusicLayout.RingLetterFor(true, reading.Name);
            identity.Children.Add(new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(5), Background = UI.Brush(Look.Hex("fillSelected")),
                Child = UI.Centered(initials, 14, Look.Primary, FontWeights.SemiBold)
            });
        }

        var daily = Label(reading.Daily);
        var weekly = Label(reading.Weekly);
        var stack = new StackPanel { Margin = new Thickness(2, 3, 2, 3), Children = { identity, daily, weekly } };
        string mode = Settings.QuotaRingRemaining ? Loc.T("剩余", "Left") : Loc.T("已用", "Used");
        string summary = $"{reading.Name} · {mode} · {daily.Text} · {weekly.Text}";
        if (reading.Stale) summary += $" · {Loc.T("数据未更新", "Data is stale")}";
        string tooltip = $"{summary}\n{reading.Detail}";
        stack.ToolTip = tooltip;
        AutomationProperties.SetName(stack, summary);
        if (!detailed) return stack;
        var link = WidgetSurface(stack, summary, () => SelectPage(DeckPage.Usage));
        link.ToolTip = tooltip;
        return link;

        TextBlock Label(QuotaPeriodReading period)
        {
            var label = WeatherText("", 13);
            label.Inlines.Add(new Run(period.Label + " ") { FontSize = 10, Foreground = UI.Brush(Look.Secondary) });
            label.Inlines.Add(new Run(period.Value) { FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(period.Color) });
            label.TextAlignment = TextAlignment.Center;
            label.TextWrapping = TextWrapping.NoWrap;
            Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
            return label;
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
