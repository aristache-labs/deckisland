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

internal static class NetworkUnitTests
{
    internal static void Run(Action<string, bool> check)
    {
        try
        {
            MonitorFormat.Megabits = false;
            check("Rates read in bytes by default", MonitorFormat.Rate(8_000).Text == "8 KB/s" && MonitorFormat.Rate(2_500_000).Text == "2.5 MB/s");
            MonitorFormat.Megabits = true;
            check("Megabits multiply a byte rate by eight", MonitorFormat.Rate(8_000).Text == "64 Kbps" && MonitorFormat.Rate(2_500_000).Text == "20.0 Mbps");
            check("Megabits roll over to Gbps", MonitorFormat.Rate(200_000_000).Text == "1.6 Gbps");
            check("The collapsed strip's letter follows the bits", MonitorFormat.CompactRate(1_250_000) is { Unit: "M" } rate && rate.Number == "10.0");
            check("A rate limit is worded in the unit shown", MonitorFormat.RateLimit(10) == "80 Mbps");
            check("An unreadable rate is zero in either unit", MonitorFormat.Rate(double.NaN).Text == "0 Kbps");
            MonitorFormat.Megabits = false;
            check("A rate limit stays in bytes by default", MonitorFormat.RateLimit(10) == "10 MB/s");
        }
        finally { MonitorFormat.Megabits = false; }
    }
}

internal static class ScreenTimeTests
{
    internal static void Run(Action<string, bool> check)
    {
        var noon = new DateTime(2026, 11, 21, 12, 0, 0);
        var (date, apps) = ScreenTime.Add("", new Dictionary<string, int>(), noon, "codex", 120);
        (date, apps) = ScreenTime.Add(date, apps, noon.AddMinutes(1), "Codex", 60);
        (date, apps) = ScreenTime.Add(date, apps, noon.AddMinutes(2), "chrome", 30);
        check("Screen time adds seconds to an app whatever its case", date == "2026-11-21" && apps.Count == 2 && apps["codex"] == 180);
        var today = ScreenTime.Today(date, apps, noon.AddHours(1));
        check("Screen time lists the longest app first and totals the day", today[0].Key == "codex" && ScreenTime.Total(today) == 210);
        check("Screen time shows nothing of another day", ScreenTime.Today(date, apps, noon.AddDays(1)).Count == 0);
        var (next, fresh) = ScreenTime.Add(date, apps, noon.AddDays(1), "chrome", 15);
        check("Screen time starts a new day from nothing", next == "2026-11-22" && fresh.Count == 1 && fresh["chrome"] == 15);
        check("Screen time ignores an empty app and a non-positive stretch", ScreenTime.Add(date, apps, noon, " ", 10).Apps.Count == 2 && ScreenTime.Add(date, apps, noon, "x", 0).Apps.Count == 2);
        var many = Enumerable.Range(0, ScreenTime.MaxApps).ToDictionary(i => "app" + i, i => 100 + i);
        var (_, capped) = ScreenTime.Add("2026-11-21", many, noon, "new", 50);
        check("Screen time keeps at most sixty apps and drops a newcomer that is smaller than all", capped.Count == ScreenTime.MaxApps && !capped.ContainsKey("new"));
        (_, capped) = ScreenTime.Add("2026-11-21", many, noon, "new", 500);
        check("Screen time lets a bigger newcomer replace the smallest app", capped.Count == ScreenTime.MaxApps && capped.ContainsKey("new") && !capped.ContainsKey("app0"));
        check("Screen time reads a saved count back, dropping what cannot be", ScreenTime.Normalize("2026-11-21", new Dictionary<string, int> { ["a"] = 5, [" "] = 9, ["b"] = -3 }).Count == 1
            && ScreenTime.Normalize("yesterday", new Dictionary<string, int> { ["a"] = 5 }).Count == 0);
        check("Screen time writes durations in minutes", ScreenTime.Duration(0) == "0m" && ScreenTime.Duration(30) == "<1m" && ScreenTime.Duration(360) == "6m"
            && ScreenTime.Duration(4320) == "1h 12m" && ScreenTime.Duration(10800) == "3h");
        check("Screen time capitalizes an app's name", ScreenTime.Display("codex") == "Codex" && ScreenTime.Display("") == "");
    }
}
