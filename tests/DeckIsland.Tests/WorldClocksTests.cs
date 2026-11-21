using DeckIsland.Core;

internal static class WorldClocksTests
{
    internal static void Run(Action<string, bool> check)
    {
        var cities = WorldClocks.Normalize(["Tokyo", "Atlantis", "", "London"]);
        check("World clocks keep three positions", cities.Count == 3);
        check("World clocks drop unknown cities in place", cities[0] == "Tokyo" && cities[1] == "" && cities[2] == "");
        check("World clocks resolve every listed zone", WorldClocks.Cities.All(item => WorldClocks.Zone(item.City) is not null));
        check("World clocks reject unknown city", WorldClocks.Zone("Atlantis") is null);
    }
}
