using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private const double ScreenTimeBar = 6, ScreenTimeGap = 2;

    // Today's time in front of the PC: the total large, a bar split by the longest apps, and the longest one named. Counted on
    // this PC only (App.ScreenTime). Redrawn when a minute has been added or the order of the apps changes.
    private Border ScreenTimeWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        frame.Padding = new Thickness(WidgetSide, 8, WidgetSide, 8);
        string? drawn = null;
        _widgetUpdates.Add(() =>
        {
            var settings = _app.Settings;
            var today = ScreenTime.Today(settings.ScreenTimeDate, settings.ScreenTimeApps, DateTime.Now);
            string key = ScreenTime.Total(today) / 60 + ":" + string.Join(",", today.Take(3).Select(pair => pair.Key + pair.Value / 60));
            if (key == drawn && frame.Child is not null) return;
            drawn = key;
            frame.Child = ScreenTimeContent(today);
        });
        return frame;
    }

    private FrameworkElement ScreenTimeContent(IReadOnlyList<KeyValuePair<string, int>> today)
    {
        int total = ScreenTime.Total(today);
        var number = WeatherText(ScreenTime.Duration(total), 28);
        number.FontWeight = FontWeights.SemiBold;
        Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
        var monitor = new Path
        {
            Data = Geometry.Parse("M 2,3 H 14 A 1,1 0 0 1 15,4 V 11 A 1,1 0 0 1 14,12 H 2 A 1,1 0 0 1 1,11 V 4 A 1,1 0 0 1 2,3 Z M 5,15 H 11 M 8,12 V 15"),
            Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Width = 16, Height = 16, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0)
        };
        var top = new Grid { Children = { number, monitor } };

        // The bar: the longest app, the second, and everything else, each as wide as its share; the track shows through when nothing is counted.
        var bar = new Grid { Height = ScreenTimeBar, Margin = new Thickness(0, 2, 0, 6), ClipToBounds = true };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(ScreenTimeBar / 2), Background = UI.Brush(Look.Hex("track")) });
        if (total > 0)
        {
            var colors = new[] { Look.Primary, Look.Secondary, Look.Tertiary };
            int[] seconds = [today[0].Value, today.Count > 1 ? today[1].Value : 0, Math.Max(0, total - today[0].Value - (today.Count > 1 ? today[1].Value : 0))];
            var segments = new Grid();
            for (int i = 0; i < seconds.Length; i++)
            {
                if (seconds[i] <= 0) continue;
                segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(seconds[i], GridUnitType.Star) });
                var piece = new Border { CornerRadius = new CornerRadius(ScreenTimeBar / 2), Background = UI.Brush(colors[i]), Margin = new Thickness(segments.Children.Count == 0 ? 0 : ScreenTimeGap, 0, 0, 0) };
                Grid.SetColumn(piece, segments.ColumnDefinitions.Count - 1);
                segments.Children.Add(piece);
            }
            bar.Children.Add(segments);
        }

        // One line per piece of the bar, in its colour: the longest app, the second, and the rest together.
        var footer = new StackPanel();
        if (total > 0)
        {
            var colors = new[] { Look.Primary, Look.Secondary, Look.Tertiary };
            var lines = new List<(string Name, int Seconds)> { (today[0].Key, today[0].Value) };
            if (today.Count > 1) lines.Add((today[1].Key, today[1].Value));
            int rest = total - lines.Sum(line => line.Seconds);
            if (rest > 0) lines.Add((Loc.T("其他", "Other"), rest));
            for (int i = 0; i < lines.Count; i++)
            {
                var time = WeatherText(ScreenTime.Duration(lines[i].Seconds), 12);
                time.Foreground = UI.Brush(Look.Secondary); time.FontWeight = FontWeights.SemiBold;
                var dot = new Ellipse { Width = 7, Height = 7, Fill = UI.Brush(colors[i]), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                var name = WeatherText(lines[i].Name, 12);
                name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(0, 0, 8, 0);
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                DockPanel.SetDock(time, System.Windows.Controls.Dock.Right);
                row.Children.Add(time);
                row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, name } });
                footer.Children.Add(row);
            }
        }
        else
        {
            var none = WeatherText(Loc.T("今天还没有记录。", "Nothing counted yet today."), 13);
            none.Foreground = UI.Brush(Look.Secondary);
            footer.Children.Add(none);
        }
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, bar, footer } };
        AutomationProperties.SetName(content, $"{WidgetLayout.Name(WidgetKind.ScreenTime)} · {ScreenTime.Duration(total)}"
            + (total > 0 ? $" · {today[0].Key} {ScreenTime.Duration(today[0].Value)}" : ""));
        return content;
    }
}
