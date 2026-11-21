using System.Windows;
using System.Windows.Automation;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    // "SAT 21 NOV" for the collapsed Minimal row; null unless Minimal chose the date.
    private FrameworkElement? CompactDate(double scale)
    {
        if (MinimalParts() is not { } parts || !parts.Contains(CompactPart.Date)) return null;
        var culture = Loc.Culture;
        var today = DateTime.Now;
        string day = culture.DateTimeFormat.GetAbbreviatedDayName(today.DayOfWeek).ToUpper(culture);
        string month = culture.DateTimeFormat.GetAbbreviatedMonthName(today.Month).ToUpper(culture);
        var text = WeatherText($"{day} {today.Day} {month}", CompactMusicLayout.CollapsedTextSize(Settings.QuotaRingStyle, scale, 11));
        text.FontWeight = FontWeights.SemiBold;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        text.Width = Math.Ceiling(text.DesiredSize.Width + 2 * scale);
        text.Height = Math.Ceiling(text.DesiredSize.Height);
        AutomationProperties.SetName(text, today.ToString("dddd d MMMM", culture));
        return text;
    }
}
