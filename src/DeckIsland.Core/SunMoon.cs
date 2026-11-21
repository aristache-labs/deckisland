namespace DeckIsland.Core;

// The eight phases the moon is told apart by, in the order it goes through them.
public enum MoonPhaseKind { New, WaxingCrescent, FirstQuarter, WaxingGibbous, Full, WaningGibbous, LastQuarter, WaningCrescent }

// Where the moon is in its month: Cycle runs from 0 (new moon) through 0.5 (full moon) back to 1; Lit is the share of the
// side facing us that the sun reaches, from 0 to 1.
public sealed record MoonPhase(double Cycle, double Lit, MoonPhaseKind Kind)
{
    public bool Waxing => Cycle < .5;

    // "Waxing" is a moon getting brighter night by night, "waning" one getting darker; a crescent is less than half lit, a
    // gibbous more than half lit.
    public string Name => Kind switch
    {
        MoonPhaseKind.New => Loc.T("新月", "New moon"),
        MoonPhaseKind.WaxingCrescent => Loc.T("娥眉月", "Waxing crescent"),
        MoonPhaseKind.FirstQuarter => Loc.T("上弦月", "First quarter"),
        MoonPhaseKind.WaxingGibbous => Loc.T("盈凸月", "Waxing gibbous"),
        MoonPhaseKind.Full => Loc.T("满月", "Full moon"),
        MoonPhaseKind.WaningGibbous => Loc.T("亏凸月", "Waning gibbous"),
        MoonPhaseKind.LastQuarter => Loc.T("下弦月", "Last quarter"),
        _ => Loc.T("残月", "Waning crescent")
    };

    public string Meaning => Kind switch
    {
        MoonPhaseKind.New => Loc.T("朝向我们的一面是暗的。", "The side facing us is dark."),
        MoonPhaseKind.WaxingCrescent => Loc.T("一弯细月，正在变亮。", "A thin sliver, getting brighter."),
        MoonPhaseKind.FirstQuarter => Loc.T("一半受光，正在变亮。", "Half lit and getting brighter."),
        MoonPhaseKind.WaxingGibbous => Loc.T("超过一半受光，正朝满月变亮。", "More than half lit and getting brighter toward full."),
        MoonPhaseKind.Full => Loc.T("整面受光。", "Fully lit."),
        MoonPhaseKind.WaningGibbous => Loc.T("超过一半受光，正从满月变暗。", "More than half lit and getting darker since full."),
        MoonPhaseKind.LastQuarter => Loc.T("一半受光，正在变暗。", "Half lit and getting darker."),
        _ => Loc.T("一弯细月，正朝新月变暗。", "A thin sliver, getting darker toward new.")
    };

    public string Detail => $"{Name}: {Meaning} " + Loc.T($"受光 {Math.Round(Lit * 100):0}%。", $"{Math.Round(Lit * 100):0}% lit.");
}

// Sunrise, sunset and the moon, worked out on the PC from the date and the place: no network is asked.
public static class SunMoon
{
    // A new moon the mean month is counted from (6 January 2000, 18:14 UTC), and the mean length of a month in days. The real
    // month varies by hours either way, which moves a phase by a fraction of a day: enough for its name, not for an eclipse.
    private static readonly DateTime NewMoon = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
    private const double Month = 29.530588853;

    public static MoonPhase Phase(DateTime utc)
    {
        double cycle = (utc.ToUniversalTime() - NewMoon).TotalDays / Month;
        cycle -= Math.Floor(cycle);
        double lit = (1 - Math.Cos(2 * Math.PI * cycle)) / 2;
        return new(cycle, lit, (MoonPhaseKind)((int)Math.Round(cycle * 8) % 8));
    }

    // The sun's rise and set on a date at a place (degrees, east and north positive), in UTC; null when it does not rise or does
    // not set that day (polar day and night). From the sunrise equation, with the sun's disc and the air's bending of its light
    // (the sun is up while its centre is within 0.833° of the horizon). Good to a couple of minutes outside the polar circles.
    public static (DateTime Rise, DateTime Set)? Sun(DateOnly date, double latitude, double longitude)
    {
        if (!WeatherPoint.Valid(latitude, longitude)) return null;
        double julian = date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc).Subtract(DateTime.UnixEpoch).TotalDays + 2440587.5;
        // Days since the year 2000 began at noon; the date is taken at noon UTC, which is a whole Julian day number.
        double n = Math.Round(julian - 2451545.0 + .0008);
        // Solar noon comes earlier the further east a place is: an hour for every 15 degrees.
        double meanNoon = n - longitude / 360;
        double anomaly = Mod(357.5291 + .98560028 * meanNoon, 360), radians = anomaly * Math.PI / 180;
        double center = 1.9148 * Math.Sin(radians) + .02 * Math.Sin(2 * radians) + .0003 * Math.Sin(3 * radians);
        double eclipticLongitude = Mod(anomaly + center + 180 + 102.9372, 360) * Math.PI / 180;
        double transit = 2451545.0 + meanNoon + .0053 * Math.Sin(radians) - .0069 * Math.Sin(2 * eclipticLongitude);
        double declination = Math.Asin(Math.Sin(eclipticLongitude) * Math.Sin(23.4397 * Math.PI / 180));
        double phi = latitude * Math.PI / 180;
        double cosHour = (Math.Sin(-.833 * Math.PI / 180) - Math.Sin(phi) * Math.Sin(declination)) / (Math.Cos(phi) * Math.Cos(declination));
        if (cosHour is < -1 or > 1) return null;
        double hour = Math.Acos(cosHour) * 180 / Math.PI / 360;
        static DateTime Utc(double julianDay) => DateTime.UnixEpoch.AddDays(julianDay - 2440587.5);
        return (Utc(transit - hour), Utc(transit + hour));
    }

    private static double Mod(double value, double by) => value - by * Math.Floor(value / by);
}
