using DeckIsland.Core;

internal static class ScreenTimeExtrasTests
{
    internal static void Run(Action<string, bool> check)
    {
        var morning = new DateTime(2026, 11, 27, 9, 30, 0);
        var days = ScreenTimeExtras.Record(null, morning, "Chrome", 5, switched: false, run: 5, site: "GitHub");
        days = ScreenTimeExtras.Record(days, morning.AddSeconds(5), "Code", 5, switched: true, run: 5, site: null);
        days = ScreenTimeExtras.Record(days, morning.AddSeconds(10), "Code", 5, switched: false, run: 10, site: null);
        var day = days["2026-11-27"];
        check("Screen time extras keep each app's seconds per hour", day.AppHours["chrome"][9] == 5 && day.AppHours["Code"][9] == 10);
        check("Screen time extras count switches and the longest stretch", day.Switches == 1 && day.LongestFocus == 10 && day.LongestApp == "Code");
        check("Screen time extras keep the first minute of use", day.FirstMinute == 9 * 60 + 29);
        check("Screen time extras count sites by name", day.Sites["GitHub"] == 5);
        var old = ScreenTimeExtras.Record(days, morning.AddDays(40), "Code", 5, false, 5, null);
        check("Screen time extras forget days past the history", !old.ContainsKey("2026-11-27") && old.Count == 1);
        var stacks = ScreenTimeExtras.ByCategory(day);
        check("Apps fall into the timeline's categories", stacks[ScreenCategory.Browser][9] == 5 && stacks[ScreenCategory.Code][9] == 10
            && ScreenTimeExtras.Category("Claude") == ScreenCategory.ChatMail && ScreenTimeExtras.Category("WINWORD") == ScreenCategory.Docs
            && ScreenTimeExtras.Category("Paint") == ScreenCategory.Other);
        var hours = new int[24]; hours[8] = 1200; hours[9] = 20;
        var estimated = ScreenTimeExtras.ByCategory(day, hours, [new("Chrome", 605), new("Code", 10), new("Outlook", 600)]);
        check("Hours kept before per-app hours are split by what each kind is missing",
            estimated[ScreenCategory.Browser][8] == 600 && estimated[ScreenCategory.ChatMail][8] == 600 && estimated[ScreenCategory.Code][9] == 10);
        check("A site comes from a browser title, never a private window or a bare page",
            ScreenTimeExtras.SiteFromTitle("Inbox (3) - Gmail - Google Chrome") == "Gmail"
            && ScreenTimeExtras.SiteFromTitle("New Tab - Google Chrome") is null
            && ScreenTimeExtras.SiteFromTitle("Watch - YouTube - InPrivate - Microsoft Edge") is null
            && ScreenTimeExtras.SiteFromTitle("Notes.txt - Notepad") is null);
    }
}
