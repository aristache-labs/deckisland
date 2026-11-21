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

internal static class CountdownTests
{
    internal static void Run(Action<string, bool> check)
    {
        check("Countdown reads year-month-day", Countdown.Parse(" 2026-12-25 ") == new DateOnly(2026, 12, 25));
        check("Countdown rejects other formats", Countdown.Parse("25.12.2026") is null && Countdown.Parse("2026-02-30") is null);
        check("Countdown clears an invalid date", Countdown.Normalize("tomorrow") == "");
    }
}

internal static class TodosTests
{
    internal static void Run(Action<string, bool> check)
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var list = Todos.Add(Todos.Add(Todos.Add([], "A", a), "B", b), "C", c);
        check("To do adds at the end of the open items", string.Join("", list.Select(i => i.Text)) == "ABC");
        list = Todos.Toggle(list, a);
        check("To do moves a ticked item below the open ones", string.Join("", list.Select(i => i.Text)) == "BCA" && list[2].Done);
        list = Todos.Add(list, "D");
        check("To do adds new items above the done ones", string.Join("", list.Select(i => i.Text)) == "BCDA");
        list = Todos.Move(list, list[2].Id, -2);
        check("To do moves an open item up", string.Join("", list.Select(i => i.Text)) == "DBCA");
        list = Todos.Move(list, list[0].Id, 10);
        check("To do stops an open item at the last open place", string.Join("", list.Select(i => i.Text)) == "BCDA");
        check("To do never moves a done item", Todos.Move(list, a, -3).SequenceEqual(list));
        list = Todos.Toggle(list, a);
        check("To do puts an unticked item back as the last open one", string.Join("", list.Select(i => i.Text)) == "BCDA" && !list[3].Done);
        list = Todos.ClearDone(Todos.Toggle(list, b));
        check("To do clears only done items", string.Join("", list.Select(i => i.Text)) == "CDA");
        check("To do ignores empty text", Todos.Add(list, "   ").Count == list.Count);
        var mixed = Todos.Normalize([new TodoItem(a, "x", true), new TodoItem(b, " y ", false), new TodoItem(Guid.Empty, "z", false), new TodoItem(b, "dup", false)]);
        check("To do normalizes order, text and duplicates", mixed.Count == 2 && mixed[0].Text == "y" && mixed[1].Done);
    }
}

internal static class TemperatureUnitTests
{
    internal static void Run(Action<string, bool> check)
    {
        try
        {
            Temperatures.Fahrenheit = false;
            check("Celsius shows whole degrees and °C", Temperatures.Format(47.4) == "47" && Temperatures.Detail(62) == "62 °C");
            Temperatures.Fahrenheit = true;
            check("Fahrenheit converts a reading", Temperatures.Format(0) == "32" && Temperatures.Format(100) == "212" && Temperatures.Format(-40) == "-40");
            check("Fahrenheit changes the unit of a detail", Temperatures.Detail(62) == "144 °F" && Temperatures.Detail(null) == "");
            check("Fahrenheit widest figure fits 125 °C", Temperatures.Widest == "257");
        }
        finally { Temperatures.Fahrenheit = false; }
        check("Celsius widest figure is the highest reading", Temperatures.Widest == "125");
    }
}
