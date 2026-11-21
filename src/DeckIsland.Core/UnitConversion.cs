namespace DeckIsland.Core;

public enum ConverterMode { Units, Currency }
public enum UnitCategory { Length, Weight, Temperature, Volume, Speed }

// One unit: its id (kept in the settings), its short label, and how a value in it becomes the category's base unit and back.
public sealed record Unit(string Id, string Label, Func<double, double> ToBase, Func<double, double> FromBase);

// The Converter's units. Each category converts through one base unit: metres, kilograms, kelvin, litres, metres per second.
public static class UnitConversion
{
    private static Unit Scaled(string id, string label, double factor) => new(id, label, v => v * factor, v => v / factor);

    private static readonly IReadOnlyDictionary<UnitCategory, IReadOnlyList<Unit>> Units = new Dictionary<UnitCategory, IReadOnlyList<Unit>>
    {
        [UnitCategory.Length] =
        [
            Scaled("mm", "mm · millimetre", 0.001), Scaled("cm", "cm · centimetre", 0.01), Scaled("m", "m · metre", 1), Scaled("km", "km · kilometre", 1000),
            Scaled("in", "in · inch", 0.0254), Scaled("ft", "ft · foot", 0.3048), Scaled("yd", "yd · yard", 0.9144), Scaled("mi", "mi · mile", 1609.344),
            Scaled("nmi", "nmi · nautical mile", 1852)
        ],
        [UnitCategory.Weight] =
        [
            Scaled("mg", "mg · milligram", 1e-6), Scaled("g", "g · gram", 0.001), Scaled("kg", "kg · kilogram", 1), Scaled("t", "t · tonne", 1000),
            Scaled("oz", "oz · ounce", 0.028349523125), Scaled("lb", "lb · pound", 0.45359237), Scaled("st", "st · stone", 6.35029318)
        ],
        [UnitCategory.Temperature] =
        [
            new("c", "°C · Celsius", v => v + 273.15, v => v - 273.15),
            new("f", "°F · Fahrenheit", v => (v - 32) * 5 / 9 + 273.15, v => (v - 273.15) * 9 / 5 + 32),
            new("k", "K · kelvin", v => v, v => v)
        ],
        [UnitCategory.Volume] =
        [
            Scaled("ml", "ml · millilitre", 0.001), Scaled("l", "l · litre", 1), Scaled("m3", "m³ · cubic metre", 1000),
            Scaled("tsp", "tsp · teaspoon (US)", 0.00492892159375), Scaled("tbsp", "tbsp · tablespoon (US)", 0.01478676478125),
            Scaled("floz", "fl oz · fluid ounce (US)", 0.0295735295625), Scaled("cup", "cup · cup (US)", 0.2365882365),
            Scaled("pt", "pt · pint (US)", 0.473176473), Scaled("gal", "gal · gallon (US)", 3.785411784), Scaled("ukgal", "gal · gallon (UK)", 4.54609)
        ],
        [UnitCategory.Speed] =
        [
            Scaled("ms", "m/s · metres per second", 1), Scaled("kmh", "km/h · kilometres per hour", 1 / 3.6), Scaled("mph", "mph · miles per hour", 0.44704),
            Scaled("kn", "kn · knot", 1852 / 3600.0), Scaled("fts", "ft/s · feet per second", 0.3048)
        ]
    };

    // The pair a category opens on when nothing was chosen yet.
    private static readonly IReadOnlyDictionary<UnitCategory, (string From, string To)> Defaults = new Dictionary<UnitCategory, (string, string)>
    {
        [UnitCategory.Length] = ("km", "mi"), [UnitCategory.Weight] = ("kg", "lb"), [UnitCategory.Temperature] = ("c", "f"),
        [UnitCategory.Volume] = ("l", "gal"), [UnitCategory.Speed] = ("kmh", "mph")
    };

    public static string Name(UnitCategory category) => category switch
    {
        UnitCategory.Weight => Loc.T("重量", "Weight"), UnitCategory.Temperature => Loc.T("温度", "Temperature"),
        UnitCategory.Volume => Loc.T("体积", "Volume"), UnitCategory.Speed => Loc.T("速度", "Speed"), _ => Loc.T("长度", "Length")
    };

    public static IReadOnlyList<Unit> In(UnitCategory category) => Units[category];

    public static Unit Find(UnitCategory category, string? id, bool to = false)
        => Units[category].FirstOrDefault(unit => unit.Id == id) ?? Units[category].First(unit => unit.Id == (to ? Defaults[category].To : Defaults[category].From));

    public static double Convert(double value, Unit from, Unit to) => to.FromBase(from.ToBase(value));
}
