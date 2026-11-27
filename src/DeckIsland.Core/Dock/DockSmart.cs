using System.Globalization;
using System.Text.RegularExpressions;

namespace DeckIsland.Core;

// What the dock's search can do with a typed line, after lumabar's Smart Search: settings for the dock and the volume said in plain
// words ("volume 30", "dock to the top", "make the dock bigger"), in English, Romanian or Chinese, a sum worked out, or a web search.
public enum DockCommandKind { None, Volume, Edge, Bigger, Smaller, Style, AutoHide, Lock, OpenSettings, OpenWidgets, TaskManager, Answer, WebSearch }
public sealed record DockCommand(DockCommandKind Kind, string Text, int Number = 0, DockEdge Edge = DockEdge.Bottom, DockStyle Style = DockStyle.Black, bool On = false);

public static partial class DockSmart
{
    // The command a line asks for; None when it asks for nothing the dock knows (apps are matched apart).
    public static DockCommand Parse(string line)
    {
        var text = line.Trim();
        var lower = text.ToLowerInvariant();
        if (lower.Length == 0) return new(DockCommandKind.None, text);
        if (VolumePattern().Match(lower) is { Success: true } volume && int.TryParse(volume.Groups["n"].Value, out var level))
            return new(DockCommandKind.Volume, text, Math.Clamp(level, 0, 100));
        bool dock = Has(lower, "dock", "bara", "程序坞", "任务栏");
        if (dock && Has(lower, "top", "sus", "顶")) return new(DockCommandKind.Edge, text, Edge: DockEdge.Top);
        if (dock && Has(lower, "bottom", "jos", "底")) return new(DockCommandKind.Edge, text, Edge: DockEdge.Bottom);
        if (dock && Has(lower, "bigger", "larger", "mai mare", "大")) return new(DockCommandKind.Bigger, text);
        if (dock && Has(lower, "smaller", "mai mic", "小")) return new(DockCommandKind.Smaller, text);
        if (Has(lower, "auto-hide", "autohide", "auto hide", "ascunde automat", "自动隐藏"))
            return new(DockCommandKind.AutoHide, text, On: !Has(lower, " off", "dezactiv", "opreste", "关"));
        if (dock && Has(lower, "unlock", "deblocheaza", "解锁")) return new(DockCommandKind.Lock, text, On: false);
        if (dock && Has(lower, "lock", "blocheaza", "锁定")) return new(DockCommandKind.Lock, text, On: true);
        foreach (var style in Enum.GetValues<DockStyle>())
            if (Has(lower, "style", "stil", "样式", "theme", "tema") && Has(lower, StyleWords(style)))
                return new(DockCommandKind.Style, text, Style: style);
        if (Has(lower, "task manager", "taskmgr", "manager de activitati", "任务管理器")) return new(DockCommandKind.TaskManager, text);
        if (Has(lower, "widget", "小组件")) return new(DockCommandKind.OpenWidgets, text);
        if (Has(lower, "settings", "setari", "setări", "设置")) return new(DockCommandKind.OpenSettings, text);
        if (Sum(lower) is { } answer) return new(DockCommandKind.Answer, answer.ToString("0.########", CultureInfo.InvariantCulture));
        return new(DockCommandKind.None, text);
    }

    private static string[] StyleWords(DockStyle style) => style switch
    {
        DockStyle.White => ["white", "alb", "白"], DockStyle.Liquid => ["liquid", "glass", "sticla", "玻璃"], DockStyle.IOS => ["ios"],
        DockStyle.Midnight => ["midnight", "miezul noptii", "午夜"], DockStyle.Clear => ["clear", "transparent", "透明"], _ => ["black", "negru", "dark", "黑"]
    };

    private static bool Has(string text, params string[] words) => words.Any(word => text.Contains(word, StringComparison.Ordinal));

    [GeneratedRegex(@"^(volume|volum|vol|音量)\s*(?<n>\d{1,3})\s*%?$")]
    private static partial Regex VolumePattern();

    [GeneratedRegex(@"^[\d\s\.\+\-\*/\(\)]+$")]
    private static partial Regex SumPattern();

    // A sum of numbers, + - * / and brackets, worked out; null for anything else, a sum that does not parse or a division by zero.
    public static double? Sum(string text)
    {
        if (!SumPattern().IsMatch(text) || !text.Any(char.IsDigit) || !text.Any(c => c is '+' or '-' or '*' or '/')) return null;
        int at = 0;
        try
        {
            double value = Expression(text.Replace(" ", ""), ref at);
            return at == text.Replace(" ", "").Length && double.IsFinite(value) ? value : null;
        }
        catch (FormatException) { return null; }
    }

    private static double Expression(string s, ref int at)
    {
        double value = Term(s, ref at);
        while (at < s.Length && s[at] is '+' or '-') { char op = s[at++]; double right = Term(s, ref at); value = op == '+' ? value + right : value - right; }
        return value;
    }

    private static double Term(string s, ref int at)
    {
        double value = Factor(s, ref at);
        while (at < s.Length && s[at] is '*' or '/') { char op = s[at++]; double right = Factor(s, ref at); value = op == '*' ? value * right : value / right; }
        return value;
    }

    private static double Factor(string s, ref int at)
    {
        if (at < s.Length && s[at] == '-') { at++; return -Factor(s, ref at); }
        if (at < s.Length && s[at] == '(')
        {
            at++;
            double inner = Expression(s, ref at);
            if (at >= s.Length || s[at] != ')') throw new FormatException();
            at++;
            return inner;
        }
        int start = at;
        while (at < s.Length && (char.IsDigit(s[at]) || s[at] == '.')) at++;
        return double.Parse(s[start..at], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // Quiet hours from one minute of the day to another, across midnight when the end is before the start.
    public static bool Quiet(DateTime now, int from, int to)
    {
        int minute = now.Hour * 60 + now.Minute;
        return from == to ? false : from < to ? minute >= from && minute < to : minute >= from || minute < to;
    }

    // The event Meeting mode warns of: the first one, not all-day and not warned of yet, that starts within the lead time.
    public static CalendarEvent? DueMeeting(IEnumerable<CalendarEvent> events, DateTime now, TimeSpan lead, IReadOnlySet<string> warned) =>
        events.Where(e => !e.AllDay && e.Start > now && e.Start - now <= lead && !warned.Contains(e.Id)).OrderBy(e => e.Start).FirstOrDefault();

    // Sunset dark mode: light (White) between sunrise and sunset, the chosen style otherwise (Black when White was chosen).
    public static DockStyle Effective(DockStyle chosen, bool sunsetMode, bool? daylight) => !sunsetMode || daylight is null ? chosen
        : daylight.Value ? DockStyle.White : chosen == DockStyle.White ? DockStyle.Black : chosen;
}
