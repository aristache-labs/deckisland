using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    // Today's date large, with the week it falls in below; rebuilt only when the day changes.
    private Border TodayWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(16);
        frame.Padding = new Thickness(14, 8, 14, 8);
        DateOnly shown = default;
        _widgetUpdates.Add(() =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (today == shown && frame.Child is not null) return;
            shown = today;
            frame.Child = TodayContent(today);
        });
        return frame;
    }

    private FrameworkElement TodayContent(DateOnly today)
    {
        var culture = Loc.Culture;
        string accent = Look.Readable(Look.Hex("accent"), 4.5);
        var day = WeatherText(today.Day.ToString(culture), 34);
        day.FontWeight = FontWeights.SemiBold;
        day.VerticalAlignment = VerticalAlignment.Center;
        Typography.SetNumeralAlignment(day, FontNumeralAlignment.Tabular);
        var weekday = WeatherText(culture.DateTimeFormat.GetAbbreviatedDayName(today.DayOfWeek).ToUpper(culture), 13);
        weekday.FontWeight = FontWeights.SemiBold;
        weekday.Foreground = UI.Brush(accent);
        var month = WeatherText(culture.DateTimeFormat.GetAbbreviatedMonthName(today.Month).ToUpper(culture), 13);
        month.Foreground = UI.Brush(Look.Secondary);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Children = { weekday, month } };
        int week = ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue));
        var weekLabel = WeatherText(Loc.T($"第 {week} 周", $"Week {week}"), 13);
        weekLabel.Foreground = UI.Brush(Look.Secondary);
        weekLabel.FontWeight = FontWeights.SemiBold;
        weekLabel.HorizontalAlignment = HorizontalAlignment.Right;
        weekLabel.VerticalAlignment = VerticalAlignment.Top;
        var top = new Grid { Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { day, names } }, weekLabel } };

        var days = new UniformGrid { Columns = 7, Margin = new Thickness(0, 6, 0, 0) };
        int offset = ((int)today.DayOfWeek - (int)culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        for (int i = 0; i < 7; i++)
        {
            var date = today.AddDays(i - offset);
            bool current = date == today;
            var letter = WeatherText(culture.DateTimeFormat.GetShortestDayName(date.DayOfWeek), 10.5);
            letter.Foreground = UI.Brush(current ? accent : Look.Secondary);
            var number = WeatherText(date.Day.ToString(culture), 13);
            number.Foreground = UI.Brush(current ? accent : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? Look.Secondary : Look.Primary);
            number.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
            foreach (var text in new[] { letter, number }) text.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(0, 4, 0, 5), Width = 28, HorizontalAlignment = HorizontalAlignment.Center,
                Background = current ? UI.Brush(Look.Hex("rangeFill")) : null,
                Child = new StackPanel { Children = { letter, number } }
            };
            AutomationProperties.SetName(cell, date.ToString("dddd d MMMM", culture) + (current ? $" · {Loc.T("今天", "Today")}" : ""));
            days.Children.Add(cell);
        }
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, days } };
        AutomationProperties.SetName(content, $"{Loc.T("今天", "Today")} · {today.ToString("dddd d MMMM", culture)} · {weekLabel.Text}");
        return content;
    }
}
