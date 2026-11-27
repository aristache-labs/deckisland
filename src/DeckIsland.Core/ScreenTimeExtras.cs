using System.Globalization;

namespace DeckIsland.Core;

// The kinds of app the Day Timeline colours its hours by, after lumabar's.
public enum ScreenCategory { ChatMail, Other, Browser, Code, Docs }

// What a day's screen time holds beyond its app totals and hours (ScreenTime): each app's seconds in each hour (for the timeline's
// category stacks), how often the app in front changed, the minute of the first use, the longest unbroken stretch in one app,
// and (only when the person turned on Count websites) the seconds on each site, by name only, read from browser tab titles.
public sealed class ScreenTimeExtra
{
    public Dictionary<string, List<int>> AppHours { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Switches { get; set; }
    public int FirstMinute { get; set; } = -1;
    public int LongestFocus { get; set; }
    public string LongestApp { get; set; } = "";
    public Dictionary<string, int> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ScreenTimeExtras
{
    public const int MaxSites = 60, MaxSiteLength = 40;

    // The days' extras with this stretch of `seconds` in `app` counted; `switched` when the app in front changed from another,
    // `run` the seconds of the unbroken stretch in it so far, `site` the site in front (null when not counted). Days older than
    // ScreenTime.KeepDays leave. Returns a new dictionary; the one given is not changed.
    public static Dictionary<string, ScreenTimeExtra> Record(IReadOnlyDictionary<string, ScreenTimeExtra>? days, DateTime now, string app, int seconds,
        bool switched, int run, string? site)
    {
        string key = ScreenTime.DayKey(now);
        var oldest = DateOnly.FromDateTime(now).AddDays(-ScreenTime.KeepDays);
        var result = (days ?? new Dictionary<string, ScreenTimeExtra>())
            .Where(pair => DateOnly.TryParseExact(pair.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d > oldest)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var day = result.TryGetValue(key, out var old) ? Copy(old) : new ScreenTimeExtra();
        if (seconds > 0 && app.Length > 0)
        {
            var hours = day.AppHours.TryGetValue(app, out var h) && h.Count == 24 ? h : [.. new int[24]];
            hours[now.Hour] = Math.Min(3600, hours[now.Hour] + seconds);
            day.AppHours[app] = hours;
            if (day.FirstMinute < 0) day.FirstMinute = Math.Max(0, now.Hour * 60 + now.Minute - (seconds + 59) / 60);
            if (switched) day.Switches++;
            if (run > day.LongestFocus) { day.LongestFocus = run; day.LongestApp = app; }
            if (site is { Length: > 0 } && (day.Sites.ContainsKey(site) || day.Sites.Count < MaxSites))
                day.Sites[site] = Math.Min(ScreenTime.MaxSeconds, (day.Sites.TryGetValue(site, out var s) ? s : 0) + seconds);
        }
        result[key] = day;
        return result;
    }

    private static ScreenTimeExtra Copy(ScreenTimeExtra day) => new()
    {
        AppHours = (day.AppHours ?? []).Where(pair => pair.Value is { Count: 24 })
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.OrdinalIgnoreCase),
        Switches = day.Switches, FirstMinute = day.FirstMinute, LongestFocus = day.LongestFocus, LongestApp = day.LongestApp ?? "",
        Sites = new Dictionary<string, int>(day.Sites ?? [], StringComparer.OrdinalIgnoreCase)
    };

    // Each category's seconds in each of the 24 hours of a day.
    public static Dictionary<ScreenCategory, int[]> ByCategory(ScreenTimeExtra? day)
    {
        var result = Enum.GetValues<ScreenCategory>().ToDictionary(category => category, _ => new int[24]);
        foreach (var (app, hours) in day?.AppHours ?? [])
        {
            var into = result[Category(app)];
            for (int i = 0; i < 24 && i < hours.Count; i++) into[i] += hours[i];
        }
        return result;
    }

    private static readonly (ScreenCategory Category, string[] Names)[] Known =
    [
        (ScreenCategory.Browser, ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "iexplore", "zen", "librewolf", "waterfox", "chromium", "thorium"]),
        (ScreenCategory.Code, ["code", "devenv", "cursor", "rider", "idea", "pycharm", "webstorm", "clion", "goland", "datagrip", "phpstorm", "rubymine", "android studio",
            "windowsterminal", "terminal", "powershell", "pwsh", "cmd", "wezterm", "alacritty", "warp", "notepad++", "sublime", "vim", "nvim", "emacs", "zed",
            "codex", "windsurf", "github desktop", "githubdesktop", "fork", "sourcetree", "postman", "insomnia", "docker", "wsl", "mintty", "git-bash"]),
        (ScreenCategory.ChatMail, ["outlook", "olk", "thunderbird", "mail", "teams", "ms-teams", "slack", "discord", "whatsapp", "telegram", "signal", "messenger",
            "zoom", "skype", "claude", "chatgpt", "copilot", "element", "viber", "webex", "mattermost", "spark", "mailbird", "em client"]),
        (ScreenCategory.Docs, ["winword", "word", "excel", "powerpnt", "powerpoint", "onenote", "acrobat", "acrord32", "sumatrapdf", "foxit", "notion", "obsidian",
            "notepad", "wordpad", "libreoffice", "soffice", "scalc", "swriter", "logseq", "evernote", "typora", "joplin", "anytype", "affine", "craft"])
    ];

    // An app's kind by its process name; anything not known is Other.
    public static ScreenCategory Category(string app)
    {
        string name = app.Trim().ToLowerInvariant();
        foreach (var (category, names) in Known)
            if (names.Any(known => name == known || name.StartsWith(known + " ", StringComparison.Ordinal) || name.StartsWith(known + ".", StringComparison.Ordinal)
                || name.StartsWith(known + "64", StringComparison.Ordinal)))
                return category;
        return ScreenCategory.Other;
    }

    public static string Name(ScreenCategory category) => category switch
    {
        ScreenCategory.ChatMail => Loc.T("聊天与邮件", "Chat & mail"), ScreenCategory.Browser => Loc.T("浏览器", "Browser"),
        ScreenCategory.Code => Loc.T("代码", "Code"), ScreenCategory.Docs => Loc.T("文档", "Docs"), _ => Loc.T("其他", "Other")
    };

    private static readonly string[] BrowserSuffixes = ["Google Chrome", "Microsoft Edge", "Microsoft​ Edge", "Mozilla Firefox", "Firefox", "Brave", "Opera",
        "Vivaldi", "Arc", "Chromium", "Zen Browser", "LibreWolf", "Waterfox"];
    private static readonly string[] PrivateMarks = ["InPrivate", "Incognito", "Private Browsing", "Navigare privată", "Private"];

    // A site's name from a browser window's title ("Inbox - Gmail - Google Chrome" is Gmail): the browser's name is cut off and
    // the last part kept. Null for a private window, a bare browser, or anything that is not a short name; a page title or an
    // address is never kept.
    public static string? SiteFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || PrivateMarks.Any(mark => title.Contains(mark, StringComparison.OrdinalIgnoreCase))) return null;
        string text = title.Trim();
        foreach (var browser in BrowserSuffixes)
            foreach (var separator in new[] { " - ", " — ", " – " })
                if (text.EndsWith(separator + browser, StringComparison.OrdinalIgnoreCase)) { text = text[..^(separator.Length + browser.Length)].TrimEnd(); goto cut; }
        return null;
        cut:
        var parts = text.Split([" - ", " — ", " – ", " | ", " · "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;
        string site = parts[^1];
        if (site.Length is 0 or > MaxSiteLength || site.Contains('/') || site.Contains("://") || site.Any(char.IsDigit) && site.Contains('.')) return null;
        return site;
    }

    // "every <1m", "every 4m": how often the app in front changed over the day's screen time.
    public static string SwitchEvery(int switches, int seconds)
    {
        if (switches <= 0) return "";
        int every = seconds / switches;
        return every < 60 ? Loc.T("每次不到 1 分钟", "one every <1m") : Loc.T($"每 {ScreenTime.Duration(every)} 一次", $"one every {ScreenTime.Duration(every)}");
    }
}
