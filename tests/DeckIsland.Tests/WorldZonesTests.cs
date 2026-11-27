using DeckIsland.Core;

internal static class WorldZonesTests
{
    internal static void Run(Action<string, bool> check)
    {
        check("World clocks know far more than the curated cities, Bangkok and Salta among them",
            WorldClocks.Cities.Count > 300 && WorldClocks.Zone("Bangkok") is not null && WorldClocks.Zone("Salta") is not null);
        check("World clock cities are listed once each", WorldClocks.Cities.Select(c => c.City).Distinct(StringComparer.OrdinalIgnoreCase).Count() == WorldClocks.Cities.Count);
        check("An IANA city keeps its place in the saved cities", WorldClocks.Normalize(["Kathmandu", "Nowhere"]).SequenceEqual(["Kathmandu", ""]));
    }
}
