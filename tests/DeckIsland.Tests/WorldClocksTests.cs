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
        var hours = ScreenTime.AddHour(date, [], noon, 120);
        hours = ScreenTime.AddHour(date, hours, noon.AddMinutes(1), 60);
        check("Screen time counts each hour, and a new day's hours start from nothing",
            hours.Count == 24 && hours[12] == 180 && ScreenTime.AddHour(date, hours, noon.AddDays(1), 5).Sum() == 5);
        var tomorrow = noon.AddDays(1);
        var history = ScreenTime.Archive(new Dictionary<string, ScreenTimeDay> { ["2026-10-01"] = new() { Apps = new() { ["old"] = 60 } }, ["junk"] = new() }, date, apps, hours, tomorrow);
        var (pastApps, pastHours) = ScreenTime.Day(new DateOnly(2026, 11, 21), "", new Dictionary<string, int>(), [], history, tomorrow);
        check("Screen time history: a finished day joins it, days past 30 and keys that are not days leave",
            history.Keys.SequenceEqual(["2026-11-21"]) && pastApps[0].Key == "codex" && ScreenTime.Total(pastApps) == 210 && pastHours[12] == 180);
        check("Screen time history: today's own count is not archived, and today reads from the running count",
            ScreenTime.Archive(null, date, apps, hours, noon).Count == 0 && ScreenTime.Total(ScreenTime.Day(new DateOnly(2026, 11, 21), date, apps, hours, history, noon).Apps) == 210);
        check("Screen time history: a day with nothing counted reads as empty",
            ScreenTime.Day(new DateOnly(2026, 11, 15), "", new Dictionary<string, int>(), [], history, tomorrow).Apps.Count == 0);
        var saved = new DeckSettings { ScreenTimeDate = "2020-01-01", ScreenTimeApps = new() { ["a"] = 5 }, ScreenTimePaths = new() { ["a"] = @"C:.exe", ["gone"] = @"C:\g.exe" } };
        saved.Normalize();
        check("Screen time settings: an old day's count leaves today's fields, and paths of apps no longer counted are dropped",
            saved.ScreenTimeDate == "" && saved.ScreenTimeApps.Count == 0 && saved.ScreenTimeHours.Count == 24 && saved.ScreenTimePaths.Count == 0);
        check("Screen time reads a saved count back, dropping what cannot be", ScreenTime.Normalize("2026-11-21", new Dictionary<string, int> { ["a"] = 5, [" "] = 9, ["b"] = -3 }).Count == 1
            && ScreenTime.Normalize("yesterday", new Dictionary<string, int> { ["a"] = 5 }).Count == 0);
        check("Screen time writes durations in minutes", ScreenTime.Duration(0) == "0m" && ScreenTime.Duration(30) == "<1m" && ScreenTime.Duration(360) == "6m"
            && ScreenTime.Duration(4320) == "1h 12m" && ScreenTime.Duration(10800) == "3h");
        check("Screen time capitalizes an app's name", ScreenTime.Display("codex") == "Codex" && ScreenTime.Display("") == "");
    }
}

internal static class SunMoonTests
{
    internal static void Run(Action<string, bool> check)
    {
        // Known moons: new moon 8 April 2024 18:21 UTC, full moon 24 April 2024 23:49 UTC, first quarter 15 April 2024 19:13 UTC.
        var newMoon = SunMoon.Phase(new DateTime(2024, 4, 8, 18, 21, 0, DateTimeKind.Utc));
        var fullMoon = SunMoon.Phase(new DateTime(2024, 4, 24, 23, 49, 0, DateTimeKind.Utc));
        var quarter = SunMoon.Phase(new DateTime(2024, 4, 15, 19, 13, 0, DateTimeKind.Utc));
        check("Moon: the new moon is dark and named so", newMoon.Lit < .05 && newMoon.Kind == MoonPhaseKind.New);
        check("Moon: the full moon is lit and named so", fullMoon.Lit > .95 && fullMoon.Kind == MoonPhaseKind.Full);
        check("Moon: a first quarter is half lit and waxing", quarter.Kind == MoonPhaseKind.FirstQuarter && Math.Abs(quarter.Lit - .5) < .08 && quarter.Waxing);
        var gibbous = SunMoon.Phase(new DateTime(2024, 4, 20, 12, 0, 0, DateTimeKind.Utc));
        check("Moon: five days before full it is a waxing gibbous, more than half lit", gibbous.Kind == MoonPhaseKind.WaxingGibbous && gibbous.Lit is > .5 and < 1 && gibbous.Waxing);
        var waning = SunMoon.Phase(new DateTime(2024, 4, 28, 12, 0, 0, DateTimeKind.Utc));
        check("Moon: after full it wanes", waning.Kind == MoonPhaseKind.WaningGibbous && !waning.Waxing);
        check("Moon: every phase has a name and a meaning", Enum.GetValues<MoonPhaseKind>().All(kind => new MoonPhase(0, 0, kind) is { } p && p.Name.Length > 0 && p.Meaning.Length > 0));

        // London on the June solstice 2026: sunrise about 04:43 and sunset about 21:21 British summer time, that is 03:43 and 20:21 UTC.
        var london = SunMoon.Sun(new DateOnly(2026, 6, 21), 51.5074, -0.1278);
        check("Sun: London's solstice sunrise is within four minutes", london is { } l && Math.Abs((l.Rise - new DateTime(2026, 6, 21, 3, 43, 0, DateTimeKind.Utc)).TotalMinutes) < 4);
        check("Sun: London's solstice sunset is within four minutes", london is { } s && Math.Abs((s.Set - new DateTime(2026, 6, 21, 20, 21, 0, DateTimeKind.Utc)).TotalMinutes) < 4);
        var equator = SunMoon.Sun(new DateOnly(2026, 3, 20), 0, 0);
        check("Sun: on the equator at an equinox the day is about twelve hours", equator is { } e && Math.Abs((e.Set - e.Rise).TotalHours - 12.1) < .2);
        check("Sun: the polar night has no sunrise", SunMoon.Sun(new DateOnly(2026, 12, 21), 80, 10) is null);
        check("Sun: the midnight sun has no sunset", SunMoon.Sun(new DateOnly(2026, 6, 21), 80, 10) is null);
        check("Sun: an impossible place gives nothing", SunMoon.Sun(new DateOnly(2026, 6, 21), 120, 0) is null);
        var east = SunMoon.Sun(new DateOnly(2026, 11, 21), 44.5, 26.2);
        var west = SunMoon.Sun(new DateOnly(2026, 11, 21), 44.5, -26.2);
        check("Sun: it rises earlier in the east of the same latitude", east is { } ea && west is { } we && ea.Rise < we.Rise.AddHours(-3));
    }
}

internal static class PeriodProgressTests
{
    internal static void Run(Action<string, bool> check)
    {
        var noon = new DateTime(2026, 11, 18, 12, 0, 0);   // Wednesday
        var monday = PeriodProgress.At(noon, DayOfWeek.Monday);
        var sunday = PeriodProgress.At(noon, DayOfWeek.Sunday);
        check("Progress: noon is half a day", Math.Abs(monday.Day - .5) < 1e-9);
        check("Progress: Wednesday noon is 2.5 days into a Monday week", Math.Abs(monday.Week - 2.5 / 7) < 1e-9);
        check("Progress: Wednesday noon is 3.5 days into a Sunday week", Math.Abs(sunday.Week - 3.5 / 7) < 1e-9);
        check("Progress: the 18th at noon of a 30-day month", Math.Abs(monday.Month - 17.5 / 30) < 1e-9);
        check("Progress: 18 November noon is 321.5 days into a 365-day year", Math.Abs(monday.Year - 321.5 / 365) < 1e-9);
        var leap = PeriodProgress.At(new DateTime(2028, 2, 29, 12, 0, 0), DayOfWeek.Monday);
        check("Progress: a leap day counts in a 29-day February and a 366-day year", Math.Abs(leap.Month - 28.5 / 29) < 1e-9 && Math.Abs(leap.Year - 59.5 / 366) < 1e-9);
        var start = PeriodProgress.At(new DateTime(2026, 1, 1, 0, 0, 0), DayOfWeek.Monday);
        check("Progress: the first instant of a year is zero everywhere it starts", start.Day == 0 && start.Month == 0 && start.Year == 0);
        check("Progress: percents are whole and to the nearest", PeriodProgress.Shares.Percent(.699) == 70 && PeriodProgress.Shares.Percent(.694) == 69 && PeriodProgress.Shares.Percent(.0004) == 0);
        check("Progress: 100% only once the period is over", PeriodProgress.Shares.Percent(.9999) == 99 && PeriodProgress.Shares.Percent(1) == 100);
        check("Progress: a bad share is zero", PeriodProgress.Shares.Percent(double.NaN) == 0 && PeriodProgress.Shares.Percent(-1) == 0 && PeriodProgress.Shares.Percent(5) == 100);
        var local = PeriodProgress.At(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Local), DayOfWeek.Monday);
        check("Progress: a local time is read in its own zone", local.Day is > .4 and < .6);
    }
}

internal static class SunPlaceTests
{
    internal static void Run(Action<string, bool> check)
    {
        // Bucharest on 21 November: sunrise about 07:15 and sunset about 16:50 in Romania (UTC+2), which is 05:15 and 14:50 UTC.
        var bucharest = SunMoon.Sun(new DateOnly(2026, 11, 21), 44.4268, 26.1025);
        check("Sun: Bucharest in November rises about 05:15 UTC", bucharest is { } b && Math.Abs((b.Rise.TimeOfDay - new TimeSpan(5, 15, 0)).TotalMinutes) < 8);
        check("Sun: Bucharest in November sets about 14:50 UTC", bucharest is { } c && Math.Abs((c.Set.TimeOfDay - new TimeSpan(14, 50, 0)).TotalMinutes) < 8);
        var newYork = SunMoon.Sun(new DateOnly(2026, 6, 21), 40.7128, -74.006);
        check("Sun: New York's solstice sunrise is about 09:25 UTC on the same date", newYork is { } n && n.Rise.Date == new DateTime(2026, 6, 21) && Math.Abs((n.Rise.TimeOfDay - new TimeSpan(9, 25, 0)).TotalMinutes) < 8);
        var sydney = SunMoon.Sun(new DateOnly(2026, 6, 21), -33.8688, 151.2093);
        check("Sun: Sydney's June day is the short one, about ten hours", sydney is { } y && Math.Abs((y.Set - y.Rise).TotalHours - 9.9) < .4);
    }
}
