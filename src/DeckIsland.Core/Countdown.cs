using System.Globalization;

namespace DeckIsland.Core;

// The Countdown widget's date is kept as year-month-day text; anything else counts as not set.
public static class Countdown
{
    public const string Format = "yyyy-MM-dd";

    public static DateOnly? Parse(string? text)
        => DateOnly.TryParseExact((text ?? "").Trim(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static string Normalize(string? text) => Parse(text)?.ToString(Format, CultureInfo.InvariantCulture) ?? "";
}
