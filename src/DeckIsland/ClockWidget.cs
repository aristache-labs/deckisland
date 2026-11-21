using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    // Your own time large with the weekday, then up to three chosen cities; redrawn when the minute changes.
    private Border ClockWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        frame.Padding = new Thickness(22, 8, 22, 8);
        var zones = Settings.WorldClocks.Where(city => city.Length > 0).Distinct()
            .Select(city => (City: city, Zone: WorldClocks.Zone(city))).Where(item => item.Zone is not null).ToList();
        string pattern = Settings.TimePattern;
        DateTime shown = default;
        _widgetUpdates.Add(() =>
        {
            var now = DateTime.Now;
            var minute = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
            if (minute == shown && frame.Child is not null) return;
            shown = minute;
            frame.Child = ClockContent(now, pattern, zones!);
        });
        return frame;
    }

    private FrameworkElement ClockContent(DateTime now, string pattern, List<(string City, TimeZoneInfo Zone)> zones)
    {
        var culture = Loc.Culture;
        string accent = Look.Readable(Look.Hex("accent"), 4.5);
        var time = WeatherText(now.ToString(pattern, culture), zones.Count > 0 ? 26 : 34);
        time.FontWeight = FontWeights.SemiBold;
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        var weekday = WeatherText(culture.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek).ToUpper(culture), 13);
        weekday.FontWeight = FontWeights.SemiBold;
        weekday.Foreground = UI.Brush(accent);
        weekday.HorizontalAlignment = HorizontalAlignment.Right;
        weekday.VerticalAlignment = VerticalAlignment.Center;
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new Grid { Children = { time, weekday } } } };
        var spoken = new List<string> { now.ToString(pattern, culture) };
        foreach (var (city, zone) in zones)
        {
            var there = TimeZoneInfo.ConvertTime(now, zone);
            var name = WeatherText(city, 13);
            name.Foreground = UI.Brush(Look.Secondary);
            var value = WeatherText(there.ToString(pattern, culture), 13);
            value.FontWeight = FontWeights.SemiBold;
            value.HorizontalAlignment = HorizontalAlignment.Right;
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            content.Children.Add(new Grid { Margin = new Thickness(0, 2, 0, 0), Children = { name, value } });
            spoken.Add($"{city} {value.Text}");
        }
        AutomationProperties.SetName(content, $"{WidgetLayout.Name(WidgetKind.Clock)} · {string.Join(" · ", spoken)}");
        return content;
    }
}
