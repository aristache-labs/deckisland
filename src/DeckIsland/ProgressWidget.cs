using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private const double ProgressBar = 6;

    // How much of today, this week, this month and this year has gone: a label, a bar and the percent on each line. The week
    // starts on the language's own first day. Redrawn when a percent changes, which is at most once in a few minutes.
    private Border ProgressWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        frame.Padding = new Thickness(WidgetSide, 8, WidgetSide, 8);
        string? drawn = null;
        _widgetUpdates.Add(() =>
        {
            var now = DateTime.Now;
            var first = Loc.Culture.DateTimeFormat.FirstDayOfWeek;
            var shares = PeriodProgress.At(now, first);
            string key = $"{PeriodProgress.Shares.Percent(shares.Day)}:{PeriodProgress.Shares.Percent(shares.Week)}:{PeriodProgress.Shares.Percent(shares.Month)}:{PeriodProgress.Shares.Percent(shares.Year)}:{now.Year}:{first}";
            if (key == drawn && frame.Child is not null) return;
            drawn = key;
            frame.Child = ProgressContent(shares, now.Year);
        });
        return frame;
    }

    private FrameworkElement ProgressContent(PeriodProgress.Shares shares, int year)
    {
        var rows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetIsSharedSizeScope(rows, true);
        var lines = new (string Label, double Share)[]
        {
            (Loc.T("今天", "Day"), shares.Day), (Loc.T("本周", "Week"), shares.Week), (Loc.T("本月", "Month"), shares.Month), (year.ToString(CultureInfo.InvariantCulture), shares.Year)
        };
        var spoken = new List<string>();
        foreach (var (label, share) in lines)
        {
            int percent = PeriodProgress.Shares.Percent(share);
            var name = WeatherText(label, 13);
            name.Foreground = UI.Brush(Look.Secondary);
            name.VerticalAlignment = VerticalAlignment.Center;
            var value = WeatherText($"{percent}%", 13);
            value.FontWeight = FontWeights.SemiBold;
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            var track = new Grid { Height = ProgressBar, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(ProgressBar / 2), Background = UI.Brush(Look.Hex("track")) });
            if (percent > 0)
            {
                var filled = new Grid();
                filled.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(percent, GridUnitType.Star) });
                filled.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - percent, GridUnitType.Star) });
                filled.Children.Add(new Border { CornerRadius = new CornerRadius(ProgressBar / 2), Background = UI.Brush(Look.Primary) });
                track.Children.Add(filled);
            }
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Label" });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Percent" });
            Grid.SetColumn(track, 1); Grid.SetColumn(value, 2);
            row.Children.Add(name); row.Children.Add(track); row.Children.Add(value);
            rows.Children.Add(row);
            spoken.Add($"{label} {percent}%");
        }
        AutomationProperties.SetName(rows, $"{WidgetLayout.Name(WidgetKind.Progress)} · {string.Join(" · ", spoken)}");
        return rows;
    }
}
