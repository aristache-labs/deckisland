using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DeckIsland.Core;

public enum CompactStyle { Notch, Capsule, Line }
// The screen edge the notch hangs from (DeckSettings.NotchEdge); see EdgeFrame.
public enum ScreenEdge { Top, Bottom, Left, Right }
// What the notch or the line hangs from on its edge where a taskbar is: the screen's edge, over the taskbar, or the taskbar's inner
// edge. Without a taskbar on that edge the two are the same line. See DeckSettings.Dock.
public enum EdgeAnchor { Screen, Taskbar }
// LineOverTabs: Normal, but the indicator while a tab of the window sits under the island (Behavior's tabsUnder).
public enum WindowBehavior { Normal, Line, Hide, LineOverTabs }
// BackgroundFullscreen: a full-screen window on the panel's monitor that is not the foreground window.
// Occupied: a window that neither is maximized nor fills the screen overlaps the place of the collapsed panel.
public enum ScreenContext { Desktop, Maximized, Borderless, Exclusive, BackgroundFullscreen, Occupied }
// The monitors that show the panel. Chosen uses DisplayMonitors and falls back to the primary monitor while none of them is connected.
public enum DisplayPlacement { Primary, Chosen }
// A chosen monitor: its device path, and its name at the time, so that a disconnected monitor can still be named. Profile is
// the profile its panel shows, or null for the current one (DeckSettings.CurrentProfile).
public sealed record ChosenDisplay(string Id, string Name)
{
    public Guid? Profile { get; init; }
}
// One app's own answers to the three window rules, as "app rules": for each of the three contexts the app can be in, the
// panel can follow the global rules (null) or choose one of the behaviors they offer. An app with all three set to Normal
// is the full-screen exception as it began: its covering windows leave the panel alone. Path is the executable when it is
// known; an entry with only a name matches any process of that name.
public sealed record FullscreenRule(string Name = "", string Path = "")
{
    public WindowBehavior? Maximized { get; init; }
    public WindowBehavior? Borderless { get; init; }
    public WindowBehavior? Exclusive { get; init; }
}
// An application whose playback the panel leaves out: AppId is the id its media session reports (MediaAppTarget.Ignores),
// Name the name the panel gave it when it was added, and Path its executable when one was found, for the list's icon.
public sealed record IgnoredMediaApp(string AppId = "", string Name = "", string Path = "");
// ZCode reads the ZCode desktop application's local request records and its signed-in Coding Plan quota.
// Dsh reads the DeepSeek account or API key that DeepSeek Harness (desktop app or local web app) signed in with; DeepSeek uses a key entered by hand.
// GrokBot reads the weekly allowance of the Grok Bot desktop app; it is a quota source only.
// Kimi reads the plan quota of the account Kimi Code (CLI, desktop app or VS Code extension) signed in with, and its local session records;
// KimiApi reads the same plan quota with a Kimi Code API key entered by hand. KimiPlatform reads the pay-as-you-go balance of a Kimi
// Open Platform (Moonshot) API key entered by hand, which is billed apart from the membership.
// OpenCode reads the OpenCode Go allowance of the workspace OpenCode (CLI or desktop app) signed in to OpenCode Console with, and its
// local records; OpenCodeGo reads the same allowance with an OpenCode Console API key entered by hand.
// CommandCode reads the plan quota of the account Command Code (CLI or desktop app) signed in with, and its local session records;
// CommandCodeApi reads the same quota with a Command Code API key entered by hand.
// WorkBuddy reads the credits of a WorkBuddy China edition account signed in on its website from settings, and the local session
// records of the WorkBuddy desktop app; WorkBuddyAi does the same for the international edition (WorkBuddy AI).
// MiMo reads the Xiaomi MiMo Desktop membership quota with a Xiaomi account signed in to in DeckIsland, and the local records that
// MiMo Code (CLI) and MiMo Desktop share. MiMoApi reads the Xiaomi MiMo Open Platform account of a pay-as-you-go key entered by
// hand: its balances and the month's charges. MiMoTokenPlan reads the quota of a Token Plan key entered by hand. MiMoLinked reads
// both with the keys Xiaomi MiMo Desktop and MiMo Code linked (MiMoUsage.ReadLinkedKeys); its statistics are MiMo's.
// Statistics sources are saved as numbers. 8 and 9 were a short-lived split of Claude and are retired:
// SettingsMigrations maps them back to Claude, and later members keep their saved numbers.
public enum ProviderId { Claude, Codex, Antigravity, Cursor, GlmChina, NewApi, Sub2Api, Custom, ZCode = 10, GlmGlobal = 11, Dsh = 12, DeepSeek = 13, GrokBot = 14, Kimi = 15, KimiApi = 16, OpenCode = 17, OpenCodeGo = 18, OpenRouter = 19, Pi = 20, CodeAgent = 21, CommandCode = 22, CommandCodeApi = 23, WorkBuddy = 24, WorkBuddyAi = 25, MiMo = 26, MiMoApi = 27, MiMoTokenPlan = 28, MiMoLinked = 29, KimiPlatform = 30 }
public enum SettingsTheme { System, Light, Dark }
// Pages are saved as numbers, so new members go at the end; their order in the page switch is DeckSettings.PageOrder.
public enum DeckPage { Usage, Music, Monitor, Clipboard, Devices, Weather, Focus, Reminders, Widgets, Notifications, Calls, Scratchpad, Converter, Terminal, Colour, Capture, Vault, ScreenTime, Calendar }
// How the Capture tab lists past captures, as Explorer's View menu: Grid (small icons), List (one line, in columns), Details (one row each, with columns), Tiles.
public enum CaptureLayout { Grid, List, Details, Tiles }
public enum CaptureImageFormat { Png, Jpeg }
// The bit rate a recording is encoded at, per pixel and frame (CaptureQualities).
public enum CaptureQuality { Low, Standard, High }
public enum DefaultDeckPage { Last, Usage, Music, Monitor, Clipboard, Devices, Weather, Focus, Reminders, Widgets, Notifications, Calls, Scratchpad, Converter, Terminal, Colour, Capture, Vault, ScreenTime, Calendar }
// What the mouse wheel does over the expanded panel. Saved as numbers, so new members go at the end.
public enum MouseWheelAction { None, Pages, Volume }
// What a double click on an end of the collapsed notch or capsule does: turn its auto width on or off, or start moving it
// (MainWindow.Move.cs); moving is the default. Off shows no handle at its ends at all. Saved as numbers, so new members go at the end.
public enum GripDoubleClick { AutoWidth, Move, Off }
public enum CompactMusicText { None, Title, Lyrics }
// What the collapsed music hides while the song is paused: the title or lyric, the cover, or both. Saved as numbers, so new
// members go at the end.
public enum PausedMusicHide { None, Text, Cover, Both }
// How the level bars show the sound. Flow: one value for the whole sound that runs from one end of the bars to the other, so a
// drum beat or a voice is seen crossing them. Spectrum: the sound split by pitch, each bar one band, low on the left and high
// on the right. Saved as numbers, so new members go at the end; a file without it, or with a number not listed, is Flow.
public enum EqualizerMode { Flow = 0, Spectrum = 1 }
// The music card's progress bar (DeckSettings.MusicProgressLook) and what is behind the card (DeckSettings.MusicBackground).
public enum MusicProgressStyle { Wave, Pixels, Comet, SongShape, Simple, Hearts }
public enum MusicCardBackground { AlbumColours, Plain }
// How the collapsed quota rings are drawn (QuotaRing). Nested: up to three rings about one centre, 20 DIP across, each quota in
// a ring of its own. Single: one 12 DIP ring per application with its highest share, as before there were nested rings. Saved
// as numbers, so new members go at the end; a file without it, or with a number not listed, is Single.
public enum QuotaRingStyle { Nested = 0, Single = 1 }
// The parts of the collapsed notch and capsule that are put in order, left to right. The cover stays at the left; Music places
// only the title or lyric. Saved as numbers, so new members go at the end.
// 9 was the retired Reminders part, still found in saved files, so Date is 10.
// FPS and usage in the collapsed row: never, while an app is full screen, or always.
public enum GamingMode { Off, FullScreen, Always }
public enum CompactPart { Rings, Music, Monitor, Equalizer, Clock, Transfer, Devices, Weather, Focus, Date = 10, ScreenTime = 11, NextEvent = 12 }
public static class CompactParts
{
    // The order of new settings and of restoring the defaults: the equalizer and the clock come last.
    public static IReadOnlyList<CompactPart> Default { get; } = [CompactPart.Rings, CompactPart.Music, CompactPart.Monitor, CompactPart.Transfer, CompactPart.Devices, CompactPart.Equalizer, CompactPart.Clock, CompactPart.Weather, CompactPart.ScreenTime, CompactPart.NextEvent, CompactPart.Focus];
    // The order in which parts missing from a saved file are added after its own. A file saved before the equalizer and the clock
    // were put in order adds them after its parts, the clock last, where they stood; the devices' batteries (1.3.0) join last.
    private static readonly IReadOnlyList<CompactPart> Missing = [CompactPart.Rings, CompactPart.Music, CompactPart.Monitor, CompactPart.Equalizer, CompactPart.Clock, CompactPart.Transfer, CompactPart.Devices, CompactPart.Weather, CompactPart.Focus, CompactPart.ScreenTime, CompactPart.NextEvent];
    // Each known part once, in the saved order; a part missing from the file takes its place after the others.
    public static List<CompactPart> Normalize(IEnumerable<CompactPart>? order) => [.. (order ?? []).Where(part => Enum.IsDefined(part)).Concat(Missing).Distinct()];
}
// Saved settings keep the number, so a new style goes at the end.
public enum ClockTimeFormat { System, Hour12, Hour24 }
public enum ClockStyle { Minimal, Stacked, Digits, Dots, Segment, Neon, Serif, Wide, Condensed, Pixel, Rounded }
// Every clock style takes this one color. Gradient spreads ClockGradient across the digits, Custom is ClockCustomColor,
// and Cover follows the color picked from the playing song's cover. Saved as numbers, so new members go at the end.
public enum ClockColor { White, Amber, Cyan, Pink, Lime, Gradient, Cover, Custom }

public static class ClockColors
{
    public const string DefaultCustom = "#B9A8FF";
    // The clock colors a panel theme may give its own shade (see PanelPalette.ClockPreset), and their names in theme files.
    public static readonly IReadOnlyList<ClockColor> Swatches = [ClockColor.Amber, ClockColor.Cyan, ClockColor.Pink, ClockColor.Lime];
    public static string Key(ClockColor color) => color.ToString().ToLowerInvariant();
    // The built-in colors of the amber, cyan, pink and lime swatches; a panel theme can replace them (PanelPalette.ClockPreset).
    public static string Preset(ClockColor color) => color switch
    { ClockColor.Amber => "#FF9F43", ClockColor.Cyan => "#5CE1E6", ClockColor.Pink => "#FF8FB1", ClockColor.Lime => "#E4FF6A", _ => "#F5F5F7" };
    // Every gradient has three colors: start, middle and end.
    public static readonly string[] Sunset = ["#FFB38A", "#FF7EB3", "#B69CFF"];
    public static IReadOnlyList<(string Name, string[] Stops)> Gradients =>
    [
        (Loc.T("日落", "Sunset"), Sunset),
        (Loc.T("海洋", "Ocean"), ["#7CF3FF", "#4FA8FF", "#6A6CFF"]),
        (Loc.T("极光", "Aurora"), ["#8BFFB0", "#5CE1E6", "#B69CFF"]),
        (Loc.T("火焰", "Ember"), ["#FFE27A", "#FF9F43", "#FF5E5E"]),
        (Loc.T("霓虹", "Neon"), ["#FF5CF0", "#A66BFF", "#5CE1E6"])
    ];
    // Under Windows' high contrast the clock is the system's window text (the high contrast palette's clock color, which "white"
    // stands for) whatever color, gradient or cover was chosen, at full strength and with no glow. The choice itself is kept,
    // so the clock is drawn as chosen again when high contrast is turned off.
    public static ClockColor Shown(ClockColor chosen, bool highContrast) => highContrast ? ClockColor.White : chosen;
    public static bool Glows(ClockStyle style, bool highContrast) => !highContrast && style is ClockStyle.Neon or ClockStyle.Segment;
}

// A dashboard column has an identity independent of either data source.
public sealed class AppEntry
{
    private ProviderId? _usageSource;
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    [JsonConverter(typeof(QuotaSourceConverter))]
    public ProviderId QuotaSource { get; set; }
    public ProviderId UsageSource { get => _usageSource ?? (ProviderCatalog.IsUsageSource(QuotaSource) ? QuotaSource : QuotaSource == ProviderId.MiMoLinked ? ProviderId.MiMo : ProviderId.Claude); set => _usageSource = value; }
    public string Site { get; set; } = "";
    public string Script { get; set; } = "";
    public Guid SecretRevision { get; set; }
    // Claude and Grok Bot rows only: read the quota from the account API. When off, the row sends no request and shows the
    // quota the desktop app (Claude or Grok Bot) recorded on this PC. Each row has its own choice.
    public bool QuotaOnline { get; set; } = true;
    // New API, Sub2API and custom rows: also send requests, and so the API key, over plain HTTP to any address, such as a
    // gateway on a NAS or reached through Tailscale. Off by default; HTTP to this PC is allowed either way.
    public bool AllowHttp { get; set; }
    // Rows whose source reports an account balance without a total (ProviderCatalog.HasAccountBalance): the balance that counts
    // as 0% used, in the balance's own currency. The account balance is then drawn as (target − balance) / target used, with
    // the warning and critical levels of any quota, and 0% while the balance is above the target. New rows start at 100;
    // null, saved when the field is cleared, shows the bare amount.
    public decimal? BalanceTarget { get; set; } = 100;
    [JsonIgnore] public string ConfigurationKey => ProviderCatalog.ConfigurationKey(this);
    // Shorthand for QuotaSource in code. The "Id" member of old files is converted by SettingsMigrations.
    [JsonIgnore] public ProviderId Id { get => QuotaSource; set => QuotaSource = value; }
    // Empty means the automatically supplied quota-source name.
    public string DisplayName { get; set; } = "";
    [JsonIgnore] public string Name => string.IsNullOrWhiteSpace(DisplayName) ? AppPresets.Name(QuotaSource) : DisplayName;
    public bool Enabled { get; set; } = true;
    public string ThemeColor { get; set; } = "";
    public string WarningColor { get; set; } = "";
    public string CriticalColor { get; set; } = "";
    // The chosen color or the application's preset, before any panel theme; the panel draws PanelTheme.AppTheme and friends.
    [JsonIgnore] public string Theme => ThemeColor.Length > 0 ? ThemeColor : AppPresets.ThemeColor(QuotaSource);
    [JsonIgnore] public string Warning => WarningColor.Length > 0 ? WarningColor : AppPresets.WarningColor;
    [JsonIgnore] public string Critical => CriticalColor.Length > 0 ? CriticalColor : AppPresets.CriticalColor;
    // Daily off-peak periods in local time. Inside one, the panel draws this row's quotas in the off-peak colors: below the
    // warning level in OffPeakColor, and at the warning and critical levels in OffPeakWarningColor and OffPeakCriticalColor.
    // Each one left unchosen keeps the row's usual color for that level. Empty periods mean no off-peak time.
    public List<OffPeakPeriod> OffPeak { get; set; } = [];
    public string OffPeakColor { get; set; } = "";
    public string OffPeakWarningColor { get; set; } = "";
    public string OffPeakCriticalColor { get; set; } = "";
    // This row's collapsed quota ring (QuotaRing.For). SingleRingQuota: the quota (UsageMetric.Id) the single ring shows; empty
    // shows the default. RingQuotas: the quotas the nested rings show by place, outer, middle and inner, an empty string for a ring
    // left out (QuotaRing.Slots); an empty list shows the default rings. The letter inside always comes from the display name
    // (CompactMusicLayout.RingLetterFor).
    // QuotaTargets: the amount that counts as 100% for each other balance or amount spent (UsageMetric.Id → amount), such as a gift
    // balance, a key's balance or today's spending, as BalanceTarget is for the account balance; the rings, the panel's bars and the
    // notices all follow it (DashboardUsage.Quota).
    public string SingleRingQuota { get; set; } = "";
    public List<string> RingQuotas { get; set; } = [];
    public Dictionary<string, decimal> QuotaTargets { get; set; } = [];
    public bool IsOffPeak(DateTime local) => OffPeak?.Any(period => period.Contains(local)) == true;
    // The next minute at which this row enters or leaves its off-peak time; null without periods, or when they cover the
    // whole day. A boundary inside another period changes nothing, so the first boundary that flips the state is taken.
    public DateTime? NextOffPeakChange(DateTime local)
    {
        if (OffPeak is not { Count: > 0 }) return null;
        bool now = IsOffPeak(local);
        var minute = local.Date.AddMinutes(local.Hour * 60 + local.Minute);
        return OffPeak.SelectMany(period => new[] { period.Start, period.End }).Distinct()
            .Select(boundary => local.Date.AddMinutes(boundary)).Select(at => at <= minute ? at.AddDays(1) : at)
            .Order().Cast<DateTime?>().FirstOrDefault(at => IsOffPeak(at!.Value) != now);
    }
    public AppEntry Copy()
    {
        var copy = (AppEntry)MemberwiseClone();
        copy.UsageSource = UsageSource;
        copy.OffPeak = [.. (OffPeak ?? []).Select(period => period.Copy())];
        copy.RingQuotas = [.. RingQuotas ?? []];
        copy.QuotaTargets = new(QuotaTargets ?? []);
        return copy;
    }
    public void Normalize()
    {
        UsageSource = ProviderCatalog.IsUsageSource(UsageSource) ? UsageSource : ProviderId.Claude;
        Site = ProviderCatalog.NormalizeSite(Site ?? "", QuotaSource != ProviderId.Custom);
        Script ??= "";
        // A custom row always starts from the template, which is the only place its return format is described.
        if (QuotaSource == ProviderId.Custom && Script.Trim().Length == 0) Script = ProviderScripts.Example;
        DisplayName = Regex.Replace(DisplayName ?? "", @"\s+", " ").Trim();
        if (DisplayName.Length > 64) DisplayName = DisplayName[..64];
        ThemeColor = AppPresets.NormalizeColor(ThemeColor);
        WarningColor = AppPresets.NormalizeColor(WarningColor);
        CriticalColor = AppPresets.NormalizeColor(CriticalColor);
        BalanceTarget = BalanceTarget is { } target && decimal.Round(target, 2, MidpointRounding.AwayFromZero) is > 0 and <= AppPresets.MaximumBalanceTarget and var rounded ? rounded : null;
        OffPeak = [.. (OffPeak ?? []).Where(period => period is not null).Take(AppPresets.MaximumOffPeakPeriods).Select(period => period.Normalized())];
        OffPeakColor = AppPresets.NormalizeColor(OffPeakColor);
        OffPeakWarningColor = AppPresets.NormalizeColor(OffPeakWarningColor);
        OffPeakCriticalColor = AppPresets.NormalizeColor(OffPeakCriticalColor);
        SingleRingQuota = (SingleRingQuota ?? "").Trim();
        if (SingleRingQuota.Length > 500) SingleRingQuota = "";
        RingQuotas = QuotaRing.Slots(RingQuotas);
        QuotaTargets = (QuotaTargets ?? []).Where(pair => pair.Key.Length is > 0 and <= 500 && pair.Value > 0 && pair.Value <= AppPresets.MaximumBalanceTarget).Take(32)
            .ToDictionary(pair => pair.Key, pair => decimal.Round(pair.Value, 2, MidpointRounding.AwayFromZero));
    }
}

// One daily off-peak period: Start and End are minutes after local midnight. An End before Start runs past midnight
// into the next day; an End equal to Start covers the whole day, as the settings bar dragged out to both edges gives.
public sealed class OffPeakPeriod
{
    public const int Day = 24 * 60;
    public int Start { get; set; }
    public int End { get; set; } = 8 * 60;
    public OffPeakPeriod Copy() => (OffPeakPeriod)MemberwiseClone();
    public OffPeakPeriod Normalized() => new() { Start = Math.Clamp(Start, 0, Day - 1), End = Math.Clamp(End, 0, Day - 1) };
    public bool Contains(DateTime local)
    {
        int minute = local.Hour * 60 + local.Minute;
        return Start < End ? minute >= Start && minute < End : Start == End || minute >= Start || minute < End;
    }
    public static string Format(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
    // Accepts "8:30", "08:30", "0830" and "8"; anything else, or a time past 23:59, is refused.
    public static bool TryParse(string text, out int minutes)
    {
        minutes = 0;
        var match = Regex.Match(text.Trim().Replace('：', ':'), @"^(\d{1,2})(?::?(\d{2}))?$");
        if (!match.Success) return false;
        int hour = int.Parse(match.Groups[1].Value), minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        if (hour > 23 || minute > 59) return false;
        minutes = hour * 60 + minute;
        return true;
    }
}

public static class AppPresets
{
    // Any number of applications can be configured; at most this many are shown at once, the first ones switched on.
    public const int MaximumShownApps = 7;
    // A long list would be hard to review, and the exceptions are meant for a few shell-level apps.
    public const int MaximumFullscreenExceptions = 32;
    public const int MaximumIgnoredMediaApps = 32;
    public const string WarningColor = "#E5C890", CriticalColor = "#E78284";
    public const int MaximumOffPeakPeriods = 6;
    public const decimal MaximumBalanceTarget = 1_000_000_000;
    public const double WarningPercent = 70, CriticalPercent = 90;
    // Each column needs this much width; the panel's minimum width follows the number of enabled applications.
    public const double MinimumColumnWidth = 150;
    public static readonly string[] Palette =
    [
        "#E5A385", "#F0B37E", "#E5C890", "#98DBB0", "#7FD1C4", "#9CB9FF",
        "#B9A8FF", "#E7A0D0", "#E78284", "#D4D5DF", "#8E8E93", "#F5F5F7"
    ];
    public static string Name(ProviderId id) => ProviderCatalog.Name(id);
    // The original preset colors: coral for Claude, mint for Codex, periwinkle for Antigravity, light gray for Cursor.
    public static string ThemeColor(ProviderId id) => id switch
    { ProviderId.Claude => "#E5A385", ProviderId.Codex => "#98DBB0", ProviderId.Antigravity => "#9CB9FF", ProviderId.ZCode => "#3A538D", ProviderId.Dsh => "#6081DA", ProviderId.Kimi => "#68CDEB", ProviderId.OpenCode => "#8E8E93", ProviderId.CodeAgent => "#22D3C5", ProviderId.CommandCode => "#7A60E3", ProviderId.WorkBuddy or ProviderId.WorkBuddyAi => "#98DBB0", ProviderId.MiMo or ProviderId.MiMoLinked => "#FF6900",
      ProviderId.GlmChina or ProviderId.GlmGlobal or ProviderId.MiMoApi or ProviderId.MiMoTokenPlan or ProviderId.DeepSeek or ProviderId.KimiApi or ProviderId.KimiPlatform or ProviderId.OpenCodeGo or ProviderId.CommandCodeApi or ProviderId.OpenRouter or ProviderId.NewApi or ProviderId.Sub2Api or ProviderId.Custom => ManualColor, _ => "#D4D5DF" };
    // Sources entered by hand share one purple by default.
    public const string ManualColor = "#CC9EED";
    public static int Level(double usedPercent) => usedPercent >= CriticalPercent ? 2 : usedPercent >= WarningPercent ? 1 : 0;
    public static bool IsColor(string value) => Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$");
    public static string NormalizeColor(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 6 && Regex.IsMatch(text, "^[0-9A-Fa-f]{6}$")) text = "#" + text;
        return IsColor(text) ? "#" + text[1..].ToUpperInvariant() : "";
    }
}

public enum PanelSize { Compact, Standard, Spacious }

// The page switch's size setting (DeckSettings.TabBarScale), in percent. Above Max it would not fit the header band.
public static class TabBarScales
{
    public const int Default = 100, Min = 80, Max = 125;
    public static double Factor(DeckSettings settings) => Math.Clamp(settings.TabBarScale, Min, Max) / 100d;
}

// Quick sizes depend on the number of enabled applications at the moment they are chosen; the stored width
// and height do not move afterwards. One to four applications use the values settled with the user; more
// applications extend the same pattern per column, capped at the 1200 DIP maximum width.
public static class PanelSizes
{
    public static (double Width, double Height) For(PanelSize size, int apps)
    {
        int columns = Math.Max(1, apps);
        static double Round(double value) => Math.Ceiling(value / 10) * 10;
        var (width, height) = (size, columns) switch
        {
            (PanelSize.Compact, 1) => (500d, 170d), (PanelSize.Standard, 1) => (550d, 200d), (PanelSize.Spacious, 1) => (630d, 220d),
            (PanelSize.Compact, 2) => (500d, 150d), (PanelSize.Standard, 2) => (520d, 200d), (PanelSize.Spacious, 2) => (760d, 310d),
            (PanelSize.Compact, 3) => (530d, 160d), (PanelSize.Standard, 3) => (730d, 240d), (PanelSize.Spacious, 3) => (860d, 310d),
            (PanelSize.Compact, 4) => (700d, 170d), (PanelSize.Standard, 4) => (760d, 240d), (PanelSize.Spacious, 4) => (1100d, 310d),
            (PanelSize.Compact, _) => (Round(columns * 170 + 12), 170d),
            (PanelSize.Spacious, _) => (Round(columns * 270 + 12), 310d),
            _ => (Round(columns * 187 + 12), 240d)
        };
        return (Math.Clamp(width, 440, 1200), Math.Clamp(height, 140, 400));
    }
    public static string Name(PanelSize size) => size switch { PanelSize.Compact => Loc.T("紧凑", "Compact"), PanelSize.Spacious => Loc.T("宽敞", "Spacious"), _ => Loc.T("标准", "Standard") };
}

// The clipboard and file transfer page's default size, the same as the system monitor's; the quick sizes are DeckPages.QuickSize.
public static class ShelfSizes
{
    public static readonly (double Width, double Height) Default = (620, 220);
}

public static class MonitorSizes
{
    public static readonly (double Width, double Height) Default = (620, 220);
}

public sealed class DeckSettings
{
    private List<AppEntry>? _apps;
    private List<CompactReading>? _readings;
    private List<ExpandedReading>? _expanded;
    private List<CompactPart> _notchOrder = [.. CompactParts.Default], _capsuleOrder = [.. CompactParts.Default];
    private List<DeckPage> _pageOrder = [.. DeckPages.Order];
    private int _equalizerBars = CompactMusicLayout.DefaultBars, _musicEqualizerBars = 5;
    private EqualizerMode _equalizerMode = EqualizerMode.Flow;
    private PausedMusicHide _musicPausedHide;
    private MusicGestureAction _musicDragLeft = MusicGestureAction.PreviousTrack, _musicDragRight = MusicGestureAction.NextTrack, _musicDragUp, _musicDragDown,
        _musicSwipeLeft = MusicGestureAction.PreviousTrack, _musicSwipeRight = MusicGestureAction.NextTrack;
    // Version of the DeckIsland that last saved settings.json; see SettingsMigrations. Empty in files saved before 0.1.0.
    public string AppVersion { get; set; } = "";
    public bool LaunchAtStartup { get; set; }
    // Off, the notification area has no DeckIsland icon. Starting DeckIsland again then shows the panels and the settings
    // (App.ShowFromRelaunch), and the settings window has its own exit button.
    public bool ShowTrayIcon { get; set; } = true;
    public SettingsTheme Theme { get; set; } = SettingsTheme.System;
    // The panel's color theme (see PanelThemes): "system", "dark", "light", a built-in theme or a theme file's id.
    // The dark default is the original look. An id whose file is missing is kept and shown as the dark theme meanwhile.
    public string PanelTheme { get; set; } = PanelThemes.Dark;
    // What the user changed under each panel theme, by theme id (see ThemeChoices); the theme in use keeps its choices in the
    // ordinary settings, and they are saved here when another theme is chosen.
    public Dictionary<string, ThemeChoices> ThemeChoices { get; set; } = [];
    // When a theme with a night look shows its day look: "HH:mm-HH:mm" local time, "system" to follow Windows' dark and light
    // mode, or empty for the theme's own choice (PanelThemes.DaylightModeOf).
    public string ThemeDaylight { get; set; } = "";
    // A first start replaces the default with the Windows display language (SettingsStore.Load); files saved before
    // English existed are Chinese (SettingsMigrations).
    public string Language { get; set; } = Loc.English;
    // The family the interface's text is drawn in (UiFonts), in front of the app's own fonts; empty is those fonts alone, as before
    // the setting existed. Icons, figures in code and the clock styles' own faces are not affected. A file without it reads as empty.
    public string UiFont { get; set; } = "";
    // On, the settings window is drawn in the chosen font too; off (the default), it keeps the app's own fonts. Either way the
    // font row itself keeps them, so a font that is hard to read can still be changed back.
    public bool SettingsFollowsFont { get; set; }
    public CompactStyle Style { get; set; } = CompactStyle.Notch;
    // The notch or the capsule last chosen, which the line style hotkey (HotkeyKind.LineStyle) returns to from the line. Normalize
    // keeps it up to date.
    public CompactStyle ShapeStyle { get; set; } = CompactStyle.Notch;
    // Size and position of each collapsed style in DIPs; limits and defaults are in CollapsedLimits. OffsetX moves the surface
    // from the centre; each monitor narrows the offset to what fits (CollapsedGeometry) without changing the saved value.
    public double NotchWidth { get; set; } = CollapsedLimits.NotchWidthDefault;
    public int NotchPartSpacing { get; set; } = CompactMusicLayout.DefaultPartSpacing;
    public double NotchHeight { get; set; } = CollapsedLimits.NotchHeightDefault;
    public double NotchOffsetX { get; set; } = CollapsedLimits.OffsetDefault;
    // The edge the notch hangs from. On the other edges it keeps its width (along the edge), height and offset (along the edge
    // too); see EdgeFrame. The capsule always sits below the top edge.
    public ScreenEdge NotchEdge { get; set; } = ScreenEdge.Top;
    // What the notch hangs from on that edge; null until chosen, which is the screen's edge at the top and the taskbar's elsewhere,
    // as before the choice existed.
    public EdgeAnchor? NotchAnchor { get; set; }
    // The edge and the anchor of the line, as the notch's; the line keeps to the taskbar's side by default. A window rule that turns
    // the notch or the capsule into the line shows it where the line's own settings put it.
    public ScreenEdge LineEdge { get; set; } = ScreenEdge.Top;
    // The capsule's edge. Top keeps its older behavior (a capsule dragged down to the bottom docks there); another edge hangs it from there,
    // its offset and distance measured from that edge as the notch's are.
    public ScreenEdge CapsuleEdge { get; set; } = ScreenEdge.Top;
    public EdgeAnchor? LineAnchor { get; set; }
    // Where a style works from: the edge and whether it hangs from the screen's edge over a taskbar there (EdgeFrame.For). The
    // capsule floats below the top of the screen.
    public (ScreenEdge Edge, bool OverTaskbar) Dock(CompactStyle style) => style switch
    {
        CompactStyle.Notch => (NotchEdge, Anchor(style, NotchEdge, NotchAnchor) == EdgeAnchor.Screen),
        CompactStyle.Line => (LineEdge, Anchor(style, LineEdge, LineAnchor) == EdgeAnchor.Screen),
        _ => (CapsuleEdge, true)
    };
    public static EdgeAnchor Anchor(CompactStyle style, ScreenEdge edge, EdgeAnchor? chosen)
        => chosen ?? (style == CompactStyle.Notch && edge == ScreenEdge.Top ? EdgeAnchor.Screen : EdgeAnchor.Taskbar);
    public ScreenEdge CompactEdge(CompactStyle style) => style == CompactStyle.Line ? LineEdge : style == CompactStyle.Notch ? NotchEdge : CapsuleEdge;
    public double CapsuleWidth { get; set; } = CollapsedLimits.CapsuleWidthDefault;
    public int CapsulePartSpacing { get; set; } = CompactMusicLayout.DefaultPartSpacing;
    public double CapsuleHeight { get; set; } = CollapsedLimits.CapsuleHeightDefault;
    public double CapsuleOffsetX { get; set; } = CollapsedLimits.OffsetDefault;
    // Distance from the top of the work area. Saved up to 4000; each monitor stops the capsule at the bottom of its work area.
    public double CapsuleTop { get; set; } = CollapsedLimits.CapsuleTopDefault;
    public double LineLength { get; set; } = CollapsedLimits.LineLengthDefault;
    // In half-DIP steps.
    public double LineThickness { get; set; } = CollapsedLimits.LineThicknessDefault;
    public double LineTop { get; set; } = CollapsedLimits.LineTopDefault;
    public double LineOffsetX { get; set; } = CollapsedLimits.OffsetDefault;
    // The global shortcuts (HotkeyKind), all unset by default; Hotkey and SetHotkey reach them by kind.
    public string HideHotkey { get; set; } = "";
    public string ExpandHotkey { get; set; } = "";
    // The resize and move shortcuts: modifiers with the arrow keys ("Ctrl+Alt+Arrows", HotkeyText.Arrows).
    public string ResizeHotkey { get; set; } = "";
    public string MoveHotkey { get; set; } = "";
    public string ClickThroughHotkey { get; set; } = "";
    public string SettingsHotkey { get; set; } = "";
    public string Page1Hotkey { get; set; } = "";
    public string Page2Hotkey { get; set; } = "";
    public string Page3Hotkey { get; set; } = "";
    public string Page4Hotkey { get; set; } = "";
    public string Page5Hotkey { get; set; } = "";
    public string Page6Hotkey { get; set; } = "";
    public string Page7Hotkey { get; set; } = "";
    public string Page8Hotkey { get; set; } = "";
    public string Page9Hotkey { get; set; } = "";
    public string Page10Hotkey { get; set; } = "";
    public string RefreshHotkey { get; set; } = "";
    public string CycleStyleHotkey { get; set; } = "";
    public string LineStyleHotkey { get; set; } = "";
    public string NextProfileHotkey { get; set; } = "";
    public string Hotkey(HotkeyKind kind) => (kind switch
    {
        HotkeyKind.Hide => HideHotkey, HotkeyKind.Expand => ExpandHotkey, HotkeyKind.Resize => ResizeHotkey, HotkeyKind.Move => MoveHotkey,
        HotkeyKind.ClickThrough => ClickThroughHotkey, HotkeyKind.OpenSettings => SettingsHotkey, HotkeyKind.Page1 => Page1Hotkey,
        HotkeyKind.Page2 => Page2Hotkey, HotkeyKind.Page3 => Page3Hotkey, HotkeyKind.Page4 => Page4Hotkey,
        HotkeyKind.Page5 => Page5Hotkey, HotkeyKind.Page6 => Page6Hotkey, HotkeyKind.Page7 => Page7Hotkey, HotkeyKind.Page8 => Page8Hotkey,
        HotkeyKind.Page9 => Page9Hotkey, HotkeyKind.Page10 => Page10Hotkey, HotkeyKind.Refresh => RefreshHotkey,
        HotkeyKind.CycleStyle => CycleStyleHotkey, HotkeyKind.LineStyle => LineStyleHotkey, HotkeyKind.NextProfile => NextProfileHotkey, _ => ""
    }) ?? "";
    public void SetHotkey(HotkeyKind kind, string value)
    {
        switch (kind)
        {
            case HotkeyKind.Hide: HideHotkey = value; break;
            case HotkeyKind.Expand: ExpandHotkey = value; break;
            case HotkeyKind.Resize: ResizeHotkey = value; break;
            case HotkeyKind.Move: MoveHotkey = value; break;
            case HotkeyKind.ClickThrough: ClickThroughHotkey = value; break;
            case HotkeyKind.OpenSettings: SettingsHotkey = value; break;
            case HotkeyKind.Page1: Page1Hotkey = value; break;
            case HotkeyKind.Page2: Page2Hotkey = value; break;
            case HotkeyKind.Page3: Page3Hotkey = value; break;
            case HotkeyKind.Page4: Page4Hotkey = value; break;
            case HotkeyKind.Page5: Page5Hotkey = value; break;
            case HotkeyKind.Page6: Page6Hotkey = value; break;
            case HotkeyKind.Page7: Page7Hotkey = value; break;
            case HotkeyKind.Page8: Page8Hotkey = value; break;
            case HotkeyKind.Page9: Page9Hotkey = value; break;
            case HotkeyKind.Page10: Page10Hotkey = value; break;
            case HotkeyKind.Refresh: RefreshHotkey = value; break;
            case HotkeyKind.CycleStyle: CycleStyleHotkey = value; break;
            case HotkeyKind.LineStyle: LineStyleHotkey = value; break;
            case HotkeyKind.NextProfile: NextProfileHotkey = value; break;
        }
    }
    // Mouse pass-through: hovering does not open the panel and every click goes to the window beneath; only the shortcuts open
    // it. ClickThroughPanel lets the open panel take clicks as usual; ClickThroughKeyboard lets a panel opened by a shortcut take
    // the keyboard and Esc, which it otherwise leaves to the window in use (a game).
    public bool ClickThrough { get; set; }
    public bool ClickThroughPanel { get; set; }
    public bool ClickThroughKeyboard { get; set; }
    public bool MusicPage { get; set; } = true;
    public double MusicWidth { get; set; } = 440;
    public double MusicHeight { get; set; } = 200;
    // The notch and the capsule each keep their own choices; the indicator only offers the playback progress.
    public bool NotchSummary { get; set; } = true;
    public bool NotchMusic { get; set; } = true;
    public bool CapsuleSummary { get; set; } = true;
    // Writes each application's first letter (or digit) of its name inside its collapsed quota ring, in the notch, the capsule and
    // the side column. Off by default; a file without it reads as off.
    public bool QuotaRingLetters { get; set; }
    // The quota rings' style (QuotaRingStyle), in every style and in the side column; each application's choice of quotas for it is
    // its own (AppEntry.SingleRingQuota, AppEntry.RingQuotas).
    public QuotaRingStyle QuotaRingStyle { get => _quotaRingStyle; set => _quotaRingStyle = Enum.IsDefined(value) ? value : QuotaRingStyle.Single; }
    private QuotaRingStyle _quotaRingStyle = QuotaRingStyle.Single;
    // The rings' arcs show what is left of each quota instead of what is used. Off by default; a file without it reads as off.
    public bool QuotaRingRemaining { get; set; }
    // A line under each quota's bar with where it is heading at the current pace (QuotaPace).
    public bool QuotaForecast { get; set; } = true;
    // The page switch's icons each in a colour of its own (DeckPages.IconColor) rather than the text's.
    public bool ColouredIcons { get; set; }
    // A readout when a quota that had been used reaches its reset, not during Do not disturb.
    public bool QuotaResetNotify { get; set; } = true;
    public bool CapsuleMusic { get; set; } = true;
    // The notch and the capsule each switch the equalizer on. It shows while a song plays (or is paused), or, with EqualizerAlways,
    // at all times: then it reads the whole system's sound while no song plays.
    public bool NotchEqualizer { get; set; } = true;
    public bool CapsuleEqualizer { get; set; } = true;
    public bool EqualizerAlways { get; set; }
    // How many bars the collapsed equalizer has, which sets its length (CompactMusicLayout.BarsWidth).
    public int EqualizerBars { get => _equalizerBars; set => _equalizerBars = CompactMusicLayout.SafeBars(value); }
    // The color of the collapsed equalizer and the music page's, chosen among the clock's swatches: White is the theme's equalizer
    // color, Cover the playing song's, Custom EqualizerCustomColor. Gradient is not offered and reads as White.
    public ClockColor EqualizerColor { get; set; } = ClockColor.Cover;
    public string EqualizerCustomColor { get; set; } = ClockColors.DefaultCustom;
    // The notch and the capsule each switch the clock on; its style, color, hour format and seconds are shared.
    public bool NotchClock { get; set; }
    public bool CapsuleClock { get; set; }
    // The file transfer's count on the notch and the capsule; shown only while the file transfer holds files.
    public bool NotchTransfer { get; set; } = true;
    public bool CapsuleTransfer { get; set; } = true;
    public ClockStyle ClockStyle { get; set; }
    public ClockColor ClockColor { get; set; }
    public string ClockCustomColor { get; set; } = ClockColors.DefaultCustom;
    public List<string> ClockGradient { get; set; } = [.. ClockColors.Sunset];
    public List<string> WorldClocks { get; set; } = [];
    public string CountdownTitle { get; set; } = "";
    public string CountdownDate { get; set; } = "";
    public List<TodoItem> TodoItems { get; set; } = [];
    public const int MaxNoteLength = 2000;
    public string NoteText { get; set; } = "";
    // Today's time in each app (ScreenTime), counted only while the Screen time widget is on the island or the Screen Time page is
    // shown; ScreenTimeDate is its day and ScreenTimeHours its seconds in each hour. The days before are in ScreenTimeHistory (30 of
    // them), and ScreenTimePaths has each app's executable, for its icon.
    public string ScreenTimeDate { get; set; } = "";
    public Dictionary<string, int> ScreenTimeApps { get; set; } = [];
    public List<int> ScreenTimeHours { get; set; } = [];
    public Dictionary<string, ScreenTimeDay> ScreenTimeHistory { get; set; } = [];
    public Dictionary<string, string> ScreenTimePaths { get; set; } = [];
    public bool ScreenTimePage { get; set; } = true;
    public double ScreenTimeWidth { get; set; } = 700;
    public double ScreenTimeHeight { get; set; } = 340;
    public ClockTimeFormat TimeFormat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Clock24Hour { get; set; }
    [JsonIgnore] public string TimePattern => TimeFormat switch
    {
        ClockTimeFormat.Hour12 => "h:mm tt",
        ClockTimeFormat.Hour24 => "HH:mm",
        _ => System.Globalization.DateTimeFormatInfo.CurrentInfo.ShortTimePattern
    };
    [JsonIgnore] public bool Uses24HourClock => TimePattern.Contains('H');
    public bool ClockSeconds { get; set; }
    public CompactMusicText MusicText { get; set; }
    public bool MusicTrackNotice { get; set; } = true;
    // A song taken as instrumental (Lyrics.Instrumental) shows its title in the collapsed lyric's place instead of "♪".
    public bool MusicInstrumentalTitle { get; set; }
    // With auto width (CompactAutoWidth), the collapsed middle text's room follows the length of what it shows, within
    // CompactMusicLayout.FollowSlot's limits: MusicLyricAutoWidth while the middle text is the lyric (the notch or capsule then
    // changes width from line to line), MusicTitleAutoWidth while it is the title (from song to song). The two are separate
    // choices; only the one for the chosen middle text applies. Off, the text keeps a fixed slot (TextSlot).
    public bool MusicLyricAutoWidth { get; set; }
    public bool MusicTitleAutoWidth { get; set; }
    // The surface narrows without what is hidden; a number not listed shows everything.
    public PausedMusicHide MusicPausedHide { get => _musicPausedHide; set => _musicPausedHide = Enum.IsDefined(value) ? value : PausedMusicHide.None; }
    // The album cover at the left of the collapsed row and on the music page; some players give no cover, only a placeholder.
    public bool MusicCompactCover { get; set; } = true;
    public bool MusicPageCover { get; set; } = true;
    public bool MusicIndicatorProgress { get; set; } = true;
    public bool LyricsEnabled { get; set; } = true;
    // Where the lyrics sit on the page, chosen apart from the cover: under the artist in the titles' column, or in a column of
    // their own right of the titles (the cover then stands above the titles). Without the cover the titles' column may sit at the
    // page's left edge or be centred.
    public MusicLyricsLayout MusicLyricsLayout { get; set; } = MusicLyricsLayout.Columns;
    public MusicLyricsAlign MusicLyricsAlign { get; set; } = MusicLyricsAlign.Left;
    // What dragging with the left button over the expanded music page does in each direction, and a two-finger touchpad swipe
    // to either side (MusicGestures). A number not listed does nothing.
    public MusicGestureAction MusicDragLeft { get => _musicDragLeft; set => _musicDragLeft = Gesture(value); }
    public MusicGestureAction MusicDragRight { get => _musicDragRight; set => _musicDragRight = Gesture(value); }
    public MusicGestureAction MusicDragUp { get => _musicDragUp; set => _musicDragUp = Gesture(value); }
    public MusicGestureAction MusicDragDown { get => _musicDragDown; set => _musicDragDown = Gesture(value); }
    public MusicGestureAction MusicSwipeLeft { get => _musicSwipeLeft; set => _musicSwipeLeft = Gesture(value); }
    public MusicGestureAction MusicSwipeRight { get => _musicSwipeRight; set => _musicSwipeRight = Gesture(value); }
    private static MusicGestureAction Gesture(MusicGestureAction value) => Enum.IsDefined(value) ? value : MusicGestureAction.None;
    // How many bars the music page's equalizer has, beside the title; its bars keep their spacing, so more bars make it longer.
    // The expanded music card: how its progress bar looks, what is behind it (the cover's colours in soft streaks, or the island's surface),
    // and whether it carries the player's own volume slider.
    public MusicProgressStyle MusicProgressLook { get; set; } = MusicProgressStyle.Wave;
    public MusicCardBackground MusicBackground { get; set; } = MusicCardBackground.AlbumColours;
    public bool MusicPlayerVolume { get; set; } = true;
    public int MusicEqualizerBars { get => _musicEqualizerBars; set => _musicEqualizerBars = CompactMusicLayout.SafeBars(value); }
    // How the collapsed equalizer and the music page's equalizer show the sound; one choice for both, while each has its own length.
    public EqualizerMode EqualizerMode { get => _equalizerMode; set => _equalizerMode = Enum.IsDefined(value) ? value : EqualizerMode.Flow; }
    // The music page's current lyric, its color chosen as the equalizer's is (EqualizerColor): White is the theme's primary text color.
    public ClockColor MusicLyricColor { get; set; } = ClockColor.Cover;
    public string MusicLyricCustomColor { get; set; } = ClockColors.DefaultCustom;
    // Offers "网易云音乐 · 完整控制" in the source menu, or alone in its place when nothing plays.
    public bool NeteaseFullControlEntry { get; set; }
    // Restarts a player started the normal way with full control, without asking again; switching it on asks once.
    public bool NeteaseAutoFullControl { get; set; }
    // NetEase Cloud Music's full control, which the user confirmed when switching it on: its libcef.dll is kept changed, also after
    // an update replaces it (a running player then restarts once), so every start opens the port. Switching it off restores the file.
    public bool NeteaseFullControl { get; set; }
    // KuGou's full control, which the user confirmed when switching it on: its libcef.dll is kept changed, also after a KuGou
    // update replaces it (a running KuGou then restarts once). Switching it off restores the file.
    public bool KugouFullControl { get; set; }
    // Enables Apple Music's full control when DeckIsland starts, without the entry and its explanation.
    public bool AppleMusicFullControl { get; set; }
    // Applications whose playback is not shown: their songs stay off the panel and out of the source menu.
    public List<IgnoredMediaApp> IgnoredMediaApps { get; set; } = [];
    public DefaultDeckPage DefaultPage { get; set; }
    // A right click on the expanded panel opens the settings window.
    public bool RightClickSettings { get; set; } = true;
    // A notch or capsule wider than every page opens its panel under the pointer when hovered, instead of at its middle.
    public bool ExpandAtPointer { get; set; }
    // What the mouse wheel does while the panel is expanded; scrolling parts of a page keep their wheel.
    public MouseWheelAction MouseWheel { get; set; } = MouseWheelAction.Pages;
    // Read only to carry the switch of a file saved before MouseWheel existed; Normalize moves it across and the member
    // then disappears from the saved file.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MouseWheelPages { get; set; }
    public DeckPage LastPage { get; set; }
    public double Width { get; set; } = 520;
    public double Height { get; set; } = 200;
    // The quick size last chosen in settings; cleared when the width or height is edited by hand.
    public PanelSize? QuickSize { get; set; }
    // DeckIsland drawn larger, in percent (PanelScales): the collapsed surface and the expanded panel, its pages, header and menus
    // grow together, as Windows' display scaling would for DeckIsland alone. Sizes stay saved before the scale.
    public int PanelScale { get; set; } = PanelScales.Default;
    // How much of what lies behind shows through the panel's surface, collapsed and expanded, in percent: 0 is opaque. Only the
    // surface and its theme layers are see-through; text, icons and the rim are drawn as before.
    public int SurfaceTransparency { get; set; }
    public const int MaxSurfaceTransparency = 90;
    // Glass (MainWindow.Glass.cs): the screen behind shows through the open panel, blurred; its transparency (0 to 40 %, readable up to
    // 13 %), refraction, frost and rim light (0 to 100); also on the closed notch and capsule, and under the status alerts; and how many
    // times a second the screen behind is read.
    public bool Glass { get; set; }
    public int GlassTransparency { get; set; } = 9;
    public int GlassRefraction { get; set; } = 40;
    public int GlassFrost { get; set; } = 60;
    public int GlassRim { get; set; } = 70;
    public bool GlassCollapsed { get; set; }
    public bool GlassAlerts { get; set; } = true;
    public int GlassFps { get; set; } = 10;
    public int OpenDelay { get; set; } = 0;
    // The collapsed panel opens on a click instead of on hover; OpenDelay then does not apply.
    public bool ClickToExpand { get; set; }
    public int CloseDelay { get; set; } = 0;
    public int AnimationDuration { get; set; } = 360;
    public bool Animations { get; set; } = true;
    // The panel stays above other windows. Off, it is an ordinary window that others cover: it comes to the front when it
    // opens, and over the desktop while the desktop is shown; ForceTopmost then does not apply. The window rules still do:
    // Normal leaves it covered, Line puts the indicator above the window that fills the screen, Hide hides it.
    public bool AlwaysOnTop { get; set; } = true;
    // The panel stays above other always-on-top windows too: the part one covers is shown above it in a window of a higher band
    // (TaskbarMirror), so the panel stays on top without raising itself again and again. Where that window cannot be made, the
    // panel is raised again whenever it is covered.
    public bool ForceTopmost { get; set; }
    // The panel window can be captured on its own by recording apps (OBS window capture). Off, the window keeps the no-activate
    // style that recording apps cannot capture, and that keeps it out of Alt+Tab.
    public bool WindowCapture { get; set; }
    public WindowBehavior Maximized { get; set; } = WindowBehavior.Line;
    public WindowBehavior Borderless { get; set; } = WindowBehavior.Hide;
    public WindowBehavior Exclusive { get; set; } = WindowBehavior.Hide;
    // A window that overlaps the place of the collapsed panel in its own style, such as one dragged under it. Maximized and
    // full-screen windows follow the rules above; the app rules do not choose for this one.
    public WindowBehavior Occupied { get; set; } = WindowBehavior.Normal;
    // Whether a window behind the foreground one counts too: maximized or borderless across the panel's monitor, it calls for
    // the same rule as if it were in front. Off, only the foreground window is read, as before the background was looked at.
    public bool BackgroundWindows { get; set; } = true;
    // Applications whose windows are not counted as full screen, so that a desktop beautification tool does not hide the
    // panel. Empty by default: every app follows the rules above.
    public List<FullscreenRule> FullscreenRules { get; set; } = [];
    // Read only to carry the exceptions of a file saved before app rules existed; Normalize moves them across and the
    // member then disappears from the saved file.
    public sealed record LegacyFullscreenException(string Name = "", string Path = "");
    public List<LegacyFullscreenException> FullscreenExceptions { get; set; } = [];
    public DisplayPlacement Display { get; set; }
    // Kept while Display is Primary, so that choosing displays again restores them.
    public List<ChosenDisplay> DisplayMonitors { get; set; } = [];
    public bool UsagePage { get; set; } = true;
    // The system monitor page; it reads the system only while it is open on screen.
    public bool MonitorPage { get; set; } = true;
    public double MonitorWidth { get; set; } = MonitorSizes.Default.Width;
    // The clipboard and file transfer page. The clipboard is recorded from the start; a settings file saved before keeps its choice.
    public bool ClipboardPage { get; set; } = true;
    public double ClipboardWidth { get; set; } = ShelfSizes.Default.Width;
    public double ClipboardHeight { get; set; } = ShelfSizes.Default.Height;
    public bool ClipboardDecode { get; set; } = true;
    public bool ClipboardPresentationMode { get; set; }
    public List<string> RecentColours { get; set; } = [];
    public bool ClipboardRecord { get; set; } = true;
    public bool WeTypeClipboardSync { get; set; }
    public bool DoubaoClipboardSync { get; set; }
    public bool LanClipboardSync { get; set; }
    // The phone page: a web page on the local network (ClipboardSyncService.Phone.cs), reached by a link with this secret.
    public bool PhoneClipboardSync { get; set; }
    public string PhoneSyncToken { get; set; } = "";
    // Applications whose copies are not recorded: the executable's path, or a process name (ClipboardExclusions).
    public List<string> ClipboardExcluded { get; set; } = [];
    // Off, the clipboard history goes when DeckIsland exits; on, it stays, and each copy goes once it is older than ClipboardKeepFor.
    // What is kept (ShelfData.Kept: phrases, 常用, the categories set to keep) stays either way.
    public bool ClipboardKeepHistory { get; set; }
    public HistoryLength ClipboardKeepFor { get; set; } = HistoryLength.Month;
    // What the left button does with files dragged out of the file transfer; the right button does the other of the two.
    public TransferDragOut TransferDragOut { get; set; } = TransferDragOut.Copy;
    // On, files dragged out to be moved that a target took as copies (a chat window, a browser) leave the list as well.
    public bool TransferRemoveAfterCopyDrop { get; set; }
    // Which columns the page shows: both, or only the clipboard or only the file transfer over the whole width. Only with both are
    // they arranged (ShelfArrangement): side by side or stacked as cards (ShelfLayout), and side by side the file transfer's share
    // of the width in percent, set by dragging the divider between them or in the settings.
    public ShelfShown ShelfShown { get; set; } = ShelfShown.Both;
    public ShelfArrangement ShelfArrangement { get; set; } = ShelfArrangement.Split;
    public bool ShelfSameSideStack { get; set; } = true;
    public double ShelfFileShare { get; set; } = ShelfLayout.DefaultShare;
    // Stacked, further ways to bring the card behind forward, besides a click on its side strip, on the title of the card in
    // front or on the page's own button: resting the pointer on the side strip, a middle click anywhere on the page, and a
    // two-finger sideways swipe or Shift with the wheel.
    public bool ShelfHoverSwitch { get; set; }
    public bool ShelfMiddleSwitch { get; set; } = true;
    public bool ShelfSwipeSwitch { get; set; }
    // How each column shows its entries (ClipView, FileView), chosen for each by itself. At first the clipboard shows one line an
    // entry, and the file transfer two, under a larger icon.
    public ClipView ShelfClipView { get; set; } = ClipView.Single;
    public FileView ShelfFileView { get; set; } = FileView.Two;
    public double MonitorHeight { get; set; } = MonitorSizes.Default.Height;
    public MonitorPalette MonitorPalette { get; set; } = MonitorPalette.Theme;
    // The Custom scheme's colors; kept while another scheme is chosen.
    public MonitorCustomColors MonitorCustomColors { get; set; } = new();
    // The collapsed readings' colors at the warning and critical levels (ReadingLevels), "#RRGGBB", or null for the panel
    // theme's warning and critical colors. The expanded page does not change color by level.
    public string? CompactWarningColor { get; set; }
    public string? CompactCriticalColor { get; set; }
    // The collapsed readings' dials and core columns ease to each reading at this many frames a second, or not at all.
    public CompactSmoothing CompactSmoothing { get; set; } = CompactSmoothing.Ten;
    // How the processor's cores are drawn, collapsed and expanded: columns, a dot matrix, or by the count and the room (Auto).
    public CoreDisplay CoreDisplay { get; set; } = CoreDisplay.Auto;
    // Seconds between readings: 1 or 2.
    public int MonitorInterval { get; set; } = 1;
    // The span the traffic shown beside the network's rates is summed over, from Windows' own record (TrafficPeriods).
    public TrafficPeriod MonitorTrafficPeriod { get; set; } = TrafficPeriod.Today;
    public bool MonitorProcesses { get; set; } = true;
    public ProcessSort MonitorSort { get; set; }
    public MonitorMain MonitorMain { get; set; }
    // The graphics adapter the monitor shows, by its key (GpuSample.Key), and its name to find it by should its ids change and to
    // name it while it is not connected; null for 自动 (GpuChoice.Auto). Files saved before there was a choice read as 自动.
    public string? MonitorGpu { get; set; }
    public string? MonitorGpuName { get; set; }
    // Where the graphics adapters' usage comes from, on the page and the collapsed row alike (GpuUsageSource).
    public GpuUsageSource MonitorGpuUsage { get; set; }
    public MonitorProcessView MonitorProcessView { get; set; }
    // The expanded page shows the temperatures the system reports (Temperatures); off, none are read for it. Off by default: asking
    // a driver for a temperature froze one laptop (SensorStops), so a new user turns them on knowingly.
    public bool MonitorTemperatures { get; set; }
    // Network speeds in megabits a second (Mbps) rather than megabytes (MB/s), in the system monitor, its widget and the strip.
    public bool NetworkMegabits { get; set; }
    // The expanded page shows the internal drives' space, one line per physical drive (Storage); off, the drives are not read for it.
    public bool MonitorStorage { get; set; } = true;
    // The expanded page shows the whole machine's power draw with its curve, and the machine's own battery when it has one (PowerResolver,
    // BatteryModel); off, neither is read for it. The collapsed row's Power and Charge readings are chosen in its table.
    public bool MonitorPower { get; set; } = true;
    // The devices page: the battery of the connected wireless devices (Devices); it reads them only while it shows.
    public bool WeatherEnabled { get; set; }
    public bool ManualWeatherLocation { get; set; }
    public bool WeatherFahrenheit { get; set; }
    public bool ColourfulWeatherCard { get; set; }
    public WeatherCity? WeatherCity { get; set; }
    public bool WidgetsPage { get; set; }
    public bool VaultPage { get; set; }
    public int VaultLockMinutes { get; set; } = VaultSession.DefaultMinutes;
    public double VaultWidth { get; set; } = 700;
    public double VaultHeight { get; set; } = 340;
    public double WidgetsWidth { get; set; } = 540;
    public double WidgetsHeight { get; set; } = 292;
    public WidgetKind[] WidgetSlots { get => [.. _widgetSlots]; set => _widgetSlots = WidgetLayout.Normalize(value); }
    private WidgetKind[] _widgetSlots = WidgetLayout.Normalize(null);
    public bool NotchMinimal { get; set; }
    public bool CapsuleMinimal { get; set; }
    public CompactPart? NotchMinimalFirst { get; set; } = CompactPart.Clock;
    public CompactPart? NotchMinimalSecond { get; set; } = CompactPart.Weather;
    public CompactPart? CapsuleMinimalFirst { get; set; } = CompactPart.Clock;
    public CompactPart? CapsuleMinimalSecond { get; set; } = CompactPart.Weather;
    public bool Minimal(CompactStyle style) => style == CompactStyle.Notch ? NotchMinimal : style == CompactStyle.Capsule && CapsuleMinimal;
    public bool RemindersPage { get; set; }
    // Notifications and HUDs: short readouts the island shows under the collapsed row, and the notices it mirrors.
    // How round the island, the open panel and the cards are, in percent: 0 is square, 50 the shapes as drawn before the setting
    // existed, 100 as round as the shape allows.
    public int CornerPercent { get; set; } = 50;
    // The page switch at the top of the open panel, in percent of its own size (TabBarScales).
    public int TabBarScale { get; set; } = TabBarScales.Default;
    // The Notifications tab of the open panel (the last 24 things the island said) and how it dates them: the time of day or the time since.
    public bool NotificationsPage { get; set; } = true;
    public double NotificationsWidth { get; set; } = 490;
    public double NotificationsHeight { get; set; } = 280;
    public bool NotificationClockTimes { get; set; }
    // The Calls tab, the call timer (how long the microphone has been open) and the readout when the microphone is muted or unmuted.
    public bool CallsPage { get; set; } = true;
    private const double DefaultCallsWidth = 450, DefaultCallsHeight = 200;
    public double CallsWidth { get; set; } = DefaultCallsWidth;
    public double CallsHeight { get; set; } = DefaultCallsHeight;
    // The Scratchpad tab: a code pad whose language is chosen in its header; each language keeps its own draft, saved as it is typed.
    public bool ScratchpadPage { get; set; } = true;
    private const double DefaultScratchpadWidth = 560, DefaultScratchpadHeight = 260;
    public double ScratchpadWidth { get; set; } = DefaultScratchpadWidth;
    public double ScratchpadHeight { get; set; } = DefaultScratchpadHeight;
    public ScratchLanguage ScratchpadLanguage { get; set; } = ScratchLanguage.JavaScript;
    public Dictionary<string, string> ScratchpadDrafts { get; set; } = [];
    // The Converter tab: units, or currencies at the central banks' reference rates, each pair converted through the base currency.
    public bool ConverterPage { get; set; } = true;
    private const double DefaultConverterWidth = 520, DefaultConverterHeight = 200;
    public double ConverterWidth { get; set; } = DefaultConverterWidth;
    public double ConverterHeight { get; set; } = DefaultConverterHeight;
    public ConverterMode ConverterMode { get; set; } = ConverterMode.Currency;
    public string ConverterBaseCurrency { get; set; } = "EUR";
    public string ConverterCurrencyFrom { get; set; } = "";
    public string ConverterCurrencyTo { get; set; } = "";
    public UnitCategory ConverterUnitCategory { get; set; } = UnitCategory.Length;
    public string ConverterUnitFrom { get; set; } = "";
    public string ConverterUnitTo { get; set; } = "";
    public string ConverterAmount { get; set; } = "100";
    // The Terminal tab: a shell inside the notch, kept running while the notch is closed.
    public bool ColourTool { get; set; }
    // The Capture tab: screenshots and screen recordings.
    public bool CapturePage { get; set; } = true;
    private const double DefaultCaptureWidth = 600, DefaultCaptureHeight = 300;
    public double CaptureWidth { get; set; } = DefaultCaptureWidth;
    public double CaptureHeight { get; set; } = DefaultCaptureHeight;
    public int CaptureFps { get; set; } = 30;
    public bool CaptureMic { get; set; }
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureToShelf { get; set; }
    public bool CaptureCopy { get; set; } = true;
    public CaptureImageFormat CaptureImageFormat { get; set; } = CaptureImageFormat.Png;
    // The most a recording is encoded at, by height; 0 keeps the screens' own size.
    public int CaptureMaxHeight { get; set; }
    public CaptureQuality CaptureQuality { get; set; } = CaptureQuality.Standard;
    // Windows' yellow border around what is being recorded; turning it off needs Windows 11.
    public bool CaptureBorder { get; set; } = true;
    public CaptureLayout CaptureLayout { get; set; } = CaptureLayout.Grid;
    // Settings → Display: every DeckIsland window is left out of screen sharing and recordings (WDA_EXCLUDEFROMCAPTURE).
    public bool HideFromCapture { get; set; }
    private const double DefaultColourWidth = 600, DefaultColourHeight = 310;
    public double ColourWidth { get; set; } = DefaultColourWidth;
    public double ColourHeight { get; set; } = DefaultColourHeight;
    public bool TerminalPage { get; set; } = true;
    private const double DefaultTerminalWidth = 620, DefaultTerminalHeight = 300;
    public double TerminalWidth { get; set; } = DefaultTerminalWidth;
    public double TerminalHeight { get; set; } = DefaultTerminalHeight;
    public TerminalShell TerminalShell { get; set; } = TerminalShell.PowerShell7;
    public string TerminalStartFolder { get; set; } = "";
    public const int MinTerminalFontSize = 8, MaxTerminalFontSize = 24;
    public int TerminalFontSize { get; set; } = 12;
    public bool CallTimer { get; set; } = true;
    public bool MicHud { get; set; } = true;
    public bool MirrorNotifications { get; set; } = true;
    public int NotificationSeconds { get; set; } = 5;
    // The most a notification whose text scrolls (HudScroll) stays, in seconds.
    public int NotificationScrollSeconds { get; set; } = 10;
    public bool KeepNoticesLonger { get; set; }
    public bool HudVolume { get; set; } = true;
    public bool HudBrightness { get; set; } = true;
    public bool HudBattery { get; set; } = true;
    public bool HudBluetooth { get; set; } = true;
    public bool HudLocks { get; set; } = true;
    public bool HudDownloads { get; set; } = true;
    // The status alerts after Crest's: the power mode and Energy saver, the internet, airplane mode, the output device, monitors, the
    // Insert key, Do not disturb and copying; hiding Windows' own indicator, showing the quick ones everywhere, and gaming mode.
    public bool HudPowerMode { get; set; } = true;
    public bool HudNetwork { get; set; } = true;
    public bool HudAirplane { get; set; } = true;
    public bool HudOutput { get; set; } = true;
    public bool HudMonitors { get; set; } = true;
    public bool HudInsert { get; set; }
    public bool HudDoNotDisturb { get; set; } = true;
    public bool HudCopy { get; set; }
    public bool HudHideWindows { get; set; }
    public bool HudEverywhere { get; set; }
    public GamingMode GamingMode { get; set; }
    public bool SensorIndicator { get; set; } = true;
    public List<string> SensorIgnoredApps { get; set; } = [];
    public ReminderSound ReminderSound { get; set; } = ReminderSound.Once;
    public List<Reminder> Reminders { get; set; } = [];
    public double RemindersWidth { get; set; } = 490;
    public double RemindersHeight { get; set; } = 280;
    public bool FocusHideOtherWidgets { get; set; } = true;
    public FocusSound FocusEndSound { get; set; } = FocusSound.Asterisk;
    public FocusSound BreakEndSound { get; set; } = FocusSound.Hand;
    public bool FocusSimplifiesCompact(CompactStyle style) => _widgetSlots.Contains(WidgetKind.Focus) && FocusHideOtherWidgets && style != CompactStyle.Line;
    public bool NotchFocus { get; set; } = true;
    public bool CapsuleFocus { get; set; } = true;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool ShowsFocus(CompactStyle style) => _widgetSlots.Contains(WidgetKind.Focus) && style is CompactStyle.Notch or CompactStyle.Capsule;
    public bool WeatherPage { get; set; } = true;
    public bool NotchWeather { get; set; } = true;
    public bool CapsuleWeather { get; set; } = true;
    private const double DefaultWeatherWidth = 680, DefaultWeatherHeight = 290;
    public double WeatherWidth { get; set; } = DefaultWeatherWidth;
    public double WeatherHeight { get; set; } = DefaultWeatherHeight;
    public bool ShowsWeather(CompactStyle style) => WeatherEnabled &&
        (style == CompactStyle.Notch ? NotchWeather : style == CompactStyle.Capsule && CapsuleWeather);
    // Today's screen time in the collapsed row: an hourglass and the total.
    public bool NotchScreenTime { get; set; } = true;
    public bool CapsuleScreenTime { get; set; } = true;
    public bool ShowsScreenTime(CompactStyle style) => style == CompactStyle.Notch ? NotchScreenTime : style == CompactStyle.Capsule && CapsuleScreenTime;
    // The calendars read from ICS links (CalendarFeed; each link is a secret in App.Secrets under the source's Id), the Calendar page,
    // and the collapsed row's next event, shown from CalendarHorizon minutes before it starts.
    public List<CalendarSource> CalendarSources { get; set; } = [];
    public bool CalendarPage { get; set; } = true;
    public double CalendarWidth { get; set; } = 720;
    public double CalendarHeight { get; set; } = 340;
    public int CalendarHorizon { get; set; } = CalendarFeed.DefaultHorizon;
    // A notice in the island this many minutes (5 or 10) before a timed event starts, until it starts.
    public bool CalendarNotice { get; set; } = true;
    public int CalendarNoticeMinutes { get; set; } = 10;
    public bool NotchNextEvent { get; set; } = true;
    public bool CapsuleNextEvent { get; set; } = true;
    public bool ShowsNextEvent(CompactStyle style) => style == CompactStyle.Notch ? NotchNextEvent : style == CompactStyle.Capsule && CapsuleNextEvent;

    public bool DevicesPage { get; set; } = true;
    public double DevicesWidth { get; set; } = 460;
    public double DevicesHeight { get; set; } = 200;
    // The devices' order, names and where each shows (DeviceLists), by their key (DeviceBattery.Key). Empty until the user changes
    // the list; a device not in it shows everywhere, after those that are.
    public List<DeviceChoice> DeviceChoices { get; set; } = [];
    // The collapsed row shows each device's figure after its ring.
    public bool DeviceFigures { get; set; } = true;
    // At or under these charges a battery shows as low and as getting low (Devices.Level).
    public int DeviceLowPercent { get; set; } = Devices.DefaultLowPercent;
    public int DeviceWarningPercent { get; set; } = Devices.DefaultWarningPercent;
    // The notch and the capsule each show the devices' batteries (CompactPart.Devices).
    public bool NotchDevices { get; set; }
    public bool CapsuleDevices { get; set; }
    // The expanded page shows a line under memory and network with the frame rate of the program in front, its name and its display's
    // refresh rate (FrameRates). Off by default; read only while the page is open, and only where the user may read Windows' frame
    // events (the Performance Log Users group, or DeckIsland elevated).
    public bool MonitorFrames { get; set; }
    // The table of what the expanded page shows and in which order (ExpandedMonitor). A file saved before it has none: the table is then
    // the four switches above in the default order, so the page looks as it did, and follows those switches until a table is set.
    // Once set, the table is what the page follows and Normalize writes the four switches to match it, for older versions.
    [JsonPropertyName("ExpandedReadings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExpandedReading>? ExpandedTable { get => _expanded; set => _expanded = value; }
    // Whether the expanded page shows an item (the table, or the old switches before one is set).
    public bool ShowsExpanded(ExpandedItem item) => ExpandedReadings.Any(reading => reading.Item == item && reading.Visible);
    [JsonIgnore]
    public List<ExpandedReading> ExpandedReadings
    {
        get => _expanded ?? ExpandedMonitor.Defaults(MonitorProcesses, MonitorStorage, MonitorPower, MonitorFrames);
        set => _expanded = value;
    }
    // The one block of the expanded page shown in a card under the process list when the list stands at the side (any of the left
    // column's: the drives by default); null leaves every block at the left. Named by the block's first item (ExpandedMonitor.Blocks).
    public ExpandedItem? ExpandedUnderList { get; set; } = ExpandedItem.Storage;
    // The notch and the capsule each switch the system readings on; what they show is shared.
    public bool NotchMonitor { get; set; }
    public bool CapsuleMonitor { get; set; }
    // The order of the quota rings, the title or lyric, the system readings, the equalizer and the clock, for the notch and the capsule each.
    public List<CompactPart> NotchOrder { get => _notchOrder; set => _notchOrder = CompactParts.Normalize(value); }
    public List<CompactPart> CapsuleOrder { get => _capsuleOrder; set => _capsuleOrder = CompactParts.Normalize(value); }
    // The order of the pages in the open panel's page switch, left to right.
    public List<DeckPage> PageOrder { get => _pageOrder; set => _pageOrder = DeckPages.Normalize(value); }
    // Whether the notch and the capsule widen to their content (CollapsedGeometry.Resolve). Resizing one by dragging an end turns
    // its own off, so the width set by hand stays.
    public bool NotchAutoWidth { get; set; } = true;
    // How much is kept in memory between uses (MemoryPlan, App.Memory.cs).
    private MemoryMode _memoryMode = MemoryMode.Balanced;
    public MemoryMode MemoryMode { get => _memoryMode; set => _memoryMode = Enum.IsDefined(value) ? value : MemoryMode.Balanced; }
    // The on/off switch MemoryMode replaced (SettingsMigrations.MemoryModeFromSwitch). Written to match the mode, for older versions.
    public bool TrimMemoryWhenIdle => MemoryMode == MemoryMode.Saver;
    public bool CapsuleAutoWidth { get; set; } = true;
    public GripDoubleClick GripDoubleClick { get; set; } = GripDoubleClick.Move;
    // What the collapsed readings showed before their table (CompactReadings). Kept to read older files and, written to match the
    // table (the preset it matches and the readings shown), for older versions.
    public CompactMonitorMode CompactMonitor { get; set; }
    public CompactMonitorItems CompactMonitorItems { get; set; } = CompactMonitorItems.Cpu | CompactMonitorItems.Memory | CompactMonitorItems.Download | CompactMonitorItems.Upload;
    // The collapsed readings in their order, each shown or not, with or without its figure, in its own color or the palette's.
    // Files saved before the table have none; they take it from CompactMonitor and CompactMonitorItems.
    public List<CompactReading> CompactReadings
    {
        get => _readings ??= Core.CompactMonitor.Readings(CompactMonitor, CompactMonitorItems);
        set => _readings = value;
    }
    public bool UsageAutoSync { get; set; } = true;
    // The settings window's size when last closed, in DIPs, and whether it was maximized. Empty until it is closed once;
    // it opens at the default size then. The size is fitted to the screen the window opens on.
    public double? SettingsWindowWidth { get; set; }
    public double? SettingsWindowHeight { get; set; }
    public bool SettingsWindowMaximized { get; set; }
    // Shows the compact notice when a quota rises into the warning or critical level.
    public bool QuotaAlerts { get; set; } = true;
    // Also reads the Claude Code and Codex CLI session records inside WSL distributions. Off by default: it starts stopped distributions.
    public bool IncludeWsl { get; set; }
    // The named profiles (DeckProfile, Profiles). The current one's settings are the ordinary members of this class; ReturnProfile is
    // the profile a rule replaced while it holds (ProfileRule), to come back to when it stops holding or DeckIsland starts again.
    public List<DeckProfile> Profiles { get; set; } = [];
    public Guid CurrentProfile { get; set; }
    public Guid? ReturnProfile { get; set; }
    public List<ProfileRule> ProfileRules { get; set; } = [];
    // The ordered application list; the order is the column order of the expanded panel. Claude and Codex are shown
    // at first; the other built-in applications are listed but hidden.
    public List<AppEntry> Apps
    {
        get => _apps ??= ProviderCatalog.BuiltIns.Select(id => new AppEntry { QuotaSource = id, UsageSource = id, Enabled = id is ProviderId.Claude or ProviderId.Codex }).ToList();
        set => _apps = value;
    }

    public bool ShowsSummary(CompactStyle style) => style switch { CompactStyle.Notch => NotchSummary, CompactStyle.Capsule => CapsuleSummary, _ => false };
    public bool ShowsMusic(CompactStyle style) => style switch { CompactStyle.Notch => NotchMusic, CompactStyle.Capsule => CapsuleMusic, _ => false };
    // The indicator shows neither the equalizer nor the clock.
    public bool ShowsEqualizer(CompactStyle style) => style switch { CompactStyle.Notch => NotchEqualizer, CompactStyle.Capsule => CapsuleEqualizer, _ => false };
    // The indicator is too thin for text, so it never shows the clock.
    public bool ShowsClock(CompactStyle style) => style switch { CompactStyle.Notch => NotchClock, CompactStyle.Capsule => CapsuleClock, _ => false };
    // The indicator is too thin for readings, as for the clock.
    public bool ShowsMonitor(CompactStyle style) => style switch { CompactStyle.Notch => NotchMonitor, CompactStyle.Capsule => CapsuleMonitor, _ => false };
    public bool ShowsDevices(CompactStyle style) => style switch { CompactStyle.Notch => NotchDevices, CompactStyle.Capsule => CapsuleDevices, _ => false };
    // The file transfer's count needs the page and its column: with either hidden, nothing can be dropped there.
    public bool ShowsTransfer(CompactStyle style) => ClipboardPage && ShelfShown != ShelfShown.Clipboard && style switch { CompactStyle.Notch => NotchTransfer, CompactStyle.Capsule => CapsuleTransfer, _ => false };
    // The page's columns are stacked as cards only while it shows both.
    public bool ShelfStacked() => ShelfShown == ShelfShown.Both && ShelfArrangement == ShelfArrangement.Stacked;
    public IReadOnlyList<CompactPart> CompactOrder(CompactStyle style) => style == CompactStyle.Capsule ? CapsuleOrder : NotchOrder;
    public void SetCompactOrder(CompactStyle style, IEnumerable<CompactPart> order) { if (style == CompactStyle.Notch) NotchOrder = [.. order]; else if (style == CompactStyle.Capsule) CapsuleOrder = [.. order]; }
    public bool CompactAutoWidth(CompactStyle style) => style switch { CompactStyle.Notch => NotchAutoWidth, CompactStyle.Capsule => CapsuleAutoWidth, _ => false };
    public int CompactPartSpacing(CompactStyle style) => style switch
    {
        CompactStyle.Notch => NotchPartSpacing,
        CompactStyle.Capsule => CapsulePartSpacing,
        _ => CompactMusicLayout.DefaultPartSpacing
    };
    public void SetCompactAutoWidth(CompactStyle style, bool value) { if (style == CompactStyle.Notch) NotchAutoWidth = value; else if (style == CompactStyle.Capsule) CapsuleAutoWidth = value; }
    // The saved width of the notch or the capsule, or the line's length.
    public void SetCompactWidth(CompactStyle style, double width)
    {
        if (style == CompactStyle.Notch) NotchWidth = width; else if (style == CompactStyle.Capsule) CapsuleWidth = width; else LineLength = width;
    }
    // Media is detected for the expanded music page, the chosen style's music or equalizer, or an indicator that shows playback
    // progress; a window rule can turn any style into the indicator.
    [JsonIgnore] public bool MediaWanted => MusicPage || ShowsMusic(Style) || ShowsEqualizer(Style) || MusicIndicatorProgress
        && (Style == CompactStyle.Line || new[] { Maximized, Borderless, Exclusive, Occupied }.Any(b => b is WindowBehavior.Line or WindowBehavior.LineOverTabs));
    // The switched-on applications. Their quotas are read for the AI usage page or for the rings of the chosen compact
    // style; the two are shown independently, so either one keeps the data current.
    [JsonIgnore] public IReadOnlyList<AppEntry> ConfiguredApps => Apps.Where(app => app.Enabled).Take(AppPresets.MaximumShownApps).ToList();
    [JsonIgnore] public bool ShownAppsFull => ConfiguredApps.Count >= AppPresets.MaximumShownApps;
    [JsonIgnore] public bool QuotaWanted => UsagePage || ShowsSummary(Style) || WidgetsPage && _widgetSlots.Contains(WidgetKind.Quotas);
    [JsonIgnore] public IReadOnlyList<AppEntry> EnabledApps => QuotaWanted ? ConfiguredApps : [];
    [JsonIgnore] public IReadOnlySet<ProviderId> RequiredProviders => EnabledApps.SelectMany(app => new[] { app.QuotaSource, app.UsageSource }).ToHashSet();
    [JsonIgnore] public IReadOnlyList<ProviderId> StatisticsProviders => EnabledApps.Select(app => app.UsageSource).Distinct().ToList();
    [JsonIgnore] public double MinimumWidth => Math.Clamp(Math.Max(440, AppPresets.MinimumColumnWidth * Math.Max(1, ConfiguredApps.Count) + 40), 440, 1200);
    public AppEntry? Entry(ProviderId id) => Apps.FirstOrDefault(app => app.QuotaSource == id);
    public AppEntry? Entry(Guid instanceId) => Apps.FirstOrDefault(app => app.InstanceId == instanceId);
    public bool Enabled(ProviderId id) => RequiredProviders.Contains(id);
    public void SetEnabled(ProviderId id, bool enabled)
    {
        var entries = Apps.Where(app => app.QuotaSource == id || app.UsageSource == id).ToList();
        if (entries.Count == 0) { if (enabled) Apps.Add(new AppEntry { QuotaSource = id, UsageSource = id, Enabled = !ShownAppsFull }); }
        else foreach (var entry in entries) entry.Enabled = enabled;
    }
    // The behavior a context calls for: the app's own rule first, where it chooses one for that context, and the global
    // rules everywhere else. The app is the one the context was detected for; a null app, as on the desktop, has no rule.
    // A background full screen has no rules of its own: backgroundKind, the way the window behind the foreground one fills
    // the screen (Maximized or Borderless), picks the rule, the app's own first and the global one after it, as if that
    // window were in front. Without a kind nothing fills the screen, and the panel stays as it is.
    // tabsUnder answers LineOverTabs: the indicator while a tab of that window sits under the island, the panel's style otherwise.
    public WindowBehavior Behavior(ScreenContext context, string? processName = null, string? imagePath = null,
        ScreenContext? backgroundKind = null, bool tabsUnder = false)
    {
        var raw = RawBehavior(context, processName, imagePath, backgroundKind);
        return raw is WindowBehavior.LineOverTabs ? tabsUnder ? WindowBehavior.Line : WindowBehavior.Normal : raw;
    }
    // The chosen behavior before LineOverTabs is answered.
    public WindowBehavior RawBehavior(ScreenContext context, string? processName = null, string? imagePath = null,
        ScreenContext? backgroundKind = null)
    {
        var kind = context == ScreenContext.BackgroundFullscreen ? backgroundKind ?? ScreenContext.Desktop : context;
        if (processName is not null || imagePath is not null)
            foreach (var rule in FullscreenRules)
                if (ScreenPolicy.Matches(rule, processName, imagePath))
                {
                    var own = kind switch
                    {
                        ScreenContext.Maximized => rule.Maximized, ScreenContext.Borderless => rule.Borderless,
                        ScreenContext.Exclusive => rule.Exclusive, _ => null
                    };
                    if (own is { } choice) return choice;
                    break;
                }
        return kind switch
        {
            ScreenContext.Maximized => Maximized, ScreenContext.Borderless => Borderless,
            ScreenContext.Exclusive => Exclusive, ScreenContext.Occupied => Occupied, _ => WindowBehavior.Normal
        };
    }
    public DeckSettings Copy()
    {
        var copy = (DeckSettings)MemberwiseClone();
        copy._apps = _apps?.Select(app => app.Copy()).ToList();
        copy._readings = _readings?.Select(reading => reading?.Copy()!).ToList();
        copy._expanded = _expanded?.Select(reading => reading?.Copy()!).ToList();
        copy.MonitorCustomColors = (MonitorCustomColors ?? new()).Copy();
        copy._notchOrder = [.. _notchOrder]; copy._capsuleOrder = [.. _capsuleOrder]; copy._pageOrder = [.. _pageOrder];
        copy.Reminders = [.. Reminders ?? []];
        copy.SensorIgnoredApps = [.. SensorIgnoredApps ?? []];
        copy.ClockGradient = [.. ClockGradient ?? [.. ClockColors.Sunset]];
        copy.WorldClocks = [.. WorldClocks ?? []];
        copy.TodoItems = [.. TodoItems ?? []];
        copy.ScreenTimeApps = new Dictionary<string, int>(ScreenTimeApps ?? [], StringComparer.OrdinalIgnoreCase);
        copy.ScreenTimeHours = [.. ScreenTimeHours ?? []];
        copy.ScreenTimeHistory = (ScreenTimeHistory ?? []).ToDictionary(pair => pair.Key,
            pair => new ScreenTimeDay { Apps = new Dictionary<string, int>(pair.Value?.Apps ?? [], StringComparer.OrdinalIgnoreCase), Hours = [.. pair.Value?.Hours ?? []] });
        copy.ScreenTimePaths = new Dictionary<string, string>(ScreenTimePaths ?? [], StringComparer.OrdinalIgnoreCase);
        copy.CalendarSources = CalendarFeed.Normalize(CalendarSources);
        copy.ScratchpadDrafts = new Dictionary<string, string>(ScratchpadDrafts ?? []);
        copy.DisplayMonitors = [.. DisplayMonitors ?? []];
        copy.ThemeChoices = (ThemeChoices ?? []).ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        copy.FullscreenRules = FullscreenRules.Select(rule => rule with { }).ToList();
        copy.IgnoredMediaApps = [.. IgnoredMediaApps ?? []];
        copy.DeviceChoices = [.. (DeviceChoices ?? []).Select(device => device.Copy())];
        copy.Profiles = [.. (Profiles ?? []).Select(profile => profile.Copy())];
        copy.ProfileRules = [.. (ProfileRules ?? []).Select(rule => rule.Copy())];
        return copy;
    }
    public void Normalize()
    {
        // Saved per-theme choices: one entry per theme id as the panel names it, each with its lists present.
        ThemeChoices = (ThemeChoices ?? []).Where(pair => pair.Value is not null && pair.Key.Length > 0).GroupBy(pair => PanelThemes.NormalizeId(pair.Key))
            .ToDictionary(group => group.Key, group =>
            {
                var choices = group.Last().Value;
                choices.Apps ??= []; choices.ClockGradient ??= []; choices.ClockCustomColor ??= ClockColors.DefaultCustom;
                return choices;
            });
        var seen = new HashSet<Guid>();
        Apps = Apps.Where(app => app is not null && ProviderCatalog.Sources.Contains(app.QuotaSource)).ToList();
        int shown = 0;
        foreach (var app in Apps)
        {
            // Rows switched on past the limit are kept with their settings but switched off.
            if (app.Enabled && ++shown > AppPresets.MaximumShownApps) app.Enabled = false;
            if (app.InstanceId == Guid.Empty || !seen.Add(app.InstanceId))
            {
                do { app.InstanceId = Guid.NewGuid(); } while (!seen.Add(app.InstanceId));
            }
            app.Normalize();
        }
        Width = double.IsFinite(Width) ? Math.Clamp(Width, DeckPages.MinWidth, DeckPages.MaxWidth) : 760;
        Width = Math.Max(Width, MinimumWidth);
        Height = double.IsFinite(Height) ? Math.Clamp(Height, DeckPages.MinHeight, DeckPages.MaxHeight) : 240;
        MusicWidth = double.IsFinite(MusicWidth) ? Math.Clamp(MusicWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : 440;
        MusicHeight = double.IsFinite(MusicHeight) ? Math.Clamp(MusicHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : 200;
        MonitorWidth = double.IsFinite(MonitorWidth) ? Math.Clamp(MonitorWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : MonitorSizes.Default.Width;
        if (WeatherCity is { Valid: false }) WeatherCity = null;
        WidgetsWidth = double.IsFinite(WidgetsWidth) ? Math.Clamp(WidgetsWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : 540;
        WidgetsHeight = double.IsFinite(WidgetsHeight) ? Math.Clamp(WidgetsHeight, 220, DeckPages.MaxHeight) : 292;
        CornerPercent = Math.Clamp(CornerPercent, 0, 100);
        TabBarScale = Math.Clamp(TabBarScale, TabBarScales.Min, TabBarScales.Max);
        if (!Enum.IsDefined(MusicProgressLook)) MusicProgressLook = MusicProgressStyle.Wave;
        if (!Enum.IsDefined(MusicBackground)) MusicBackground = MusicCardBackground.AlbumColours;
        CallsWidth = double.IsFinite(CallsWidth) ? Math.Clamp(CallsWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : DefaultCallsWidth;
        CallsHeight = double.IsFinite(CallsHeight) ? Math.Clamp(CallsHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : DefaultCallsHeight;
        ScratchpadWidth = double.IsFinite(ScratchpadWidth) ? Math.Clamp(ScratchpadWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : DefaultScratchpadWidth;
        ScratchpadHeight = double.IsFinite(ScratchpadHeight) ? Math.Clamp(ScratchpadHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : DefaultScratchpadHeight;
        if (!Enum.IsDefined(ScratchpadLanguage)) ScratchpadLanguage = ScratchLanguage.JavaScript;
        ScratchpadDrafts = Scratchpad.Normalize(ScratchpadDrafts);
        ConverterWidth = double.IsFinite(ConverterWidth) ? Math.Clamp(ConverterWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : DefaultConverterWidth;
        ConverterHeight = double.IsFinite(ConverterHeight) ? Math.Clamp(ConverterHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : DefaultConverterHeight;
        if (!Enum.IsDefined(ConverterMode)) ConverterMode = ConverterMode.Currency;
        if (!Enum.IsDefined(ConverterUnitCategory)) ConverterUnitCategory = UnitCategory.Length;
        ConverterBaseCurrency = ExchangeRates.NormalizeCode(ConverterBaseCurrency) ?? "EUR";
        ConverterCurrencyFrom = ExchangeRates.NormalizeCode(ConverterCurrencyFrom) ?? "";
        ConverterCurrencyTo = ExchangeRates.NormalizeCode(ConverterCurrencyTo) ?? "";
        ConverterUnitFrom ??= ""; ConverterUnitTo ??= "";
        ConverterAmount = (ConverterAmount ?? "").Length <= 40 ? ConverterAmount ?? "" : "100";
        ColourWidth = double.IsFinite(ColourWidth) ? Math.Clamp(ColourWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : DefaultColourWidth;
        ColourHeight = double.IsFinite(ColourHeight) ? Math.Clamp(ColourHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : DefaultColourHeight;
        VaultWidth = double.IsFinite(VaultWidth) ? Math.Clamp(VaultWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : 700;
        if (!VaultSession.Timeouts.Contains(VaultLockMinutes)) VaultLockMinutes = VaultSession.DefaultMinutes;
        VaultHeight = double.IsFinite(VaultHeight) ? Math.Clamp(VaultHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : 340;
        CaptureWidth = double.IsFinite(CaptureWidth) ? Math.Clamp(CaptureWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : DefaultCaptureWidth;
        CaptureHeight = double.IsFinite(CaptureHeight) ? Math.Clamp(CaptureHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : DefaultCaptureHeight;
        if (CaptureFps is not (30 or 60)) CaptureFps = 30;
        if (!Enum.IsDefined(CaptureLayout)) CaptureLayout = CaptureLayout.Grid;
        if (!Enum.IsDefined(CaptureImageFormat)) CaptureImageFormat = CaptureImageFormat.Png;
        if (!Enum.IsDefined(CaptureQuality)) CaptureQuality = CaptureQuality.Standard;
        if (CaptureMaxHeight is not (0 or 1440 or 1080 or 720)) CaptureMaxHeight = 0;
        TerminalWidth = double.IsFinite(TerminalWidth) ? Math.Clamp(TerminalWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : DefaultTerminalWidth;
        TerminalHeight = double.IsFinite(TerminalHeight) ? Math.Clamp(TerminalHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : DefaultTerminalHeight;
        if (!Enum.IsDefined(TerminalShell)) TerminalShell = TerminalShell.PowerShell7;
        TerminalStartFolder = (TerminalStartFolder ?? "").Trim();
        TerminalFontSize = Math.Clamp(TerminalFontSize, MinTerminalFontSize, MaxTerminalFontSize);
        NotificationsWidth = double.IsFinite(NotificationsWidth) ? Math.Clamp(NotificationsWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : 490;
        NotificationsHeight = double.IsFinite(NotificationsHeight) ? Math.Clamp(NotificationsHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : 280;
        NotificationSeconds = Math.Clamp(NotificationSeconds, 2, 15);
        NotificationScrollSeconds = Math.Clamp(NotificationScrollSeconds, 5, 30);
        SensorIgnoredApps = [.. (SensorIgnoredApps ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
        Reminders = Reminder.Normalize(Reminders);
        if (!Enum.IsDefined(ReminderSound)) ReminderSound = ReminderSound.Once;
        RemindersWidth = double.IsFinite(RemindersWidth) ? Math.Clamp(RemindersWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : 490;
        RemindersHeight = double.IsFinite(RemindersHeight) ? Math.Clamp(RemindersHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : 280;
        FocusMinutes = FocusTimer.ClampMinutes(FocusMinutes);
        BreakMinutes = FocusTimer.ClampMinutes(BreakMinutes);
        // The sounds are not a choice any more: Information when a Focus ends, Critical stop when a Break does.
        FocusEndSound = FocusSound.Asterisk;
        BreakEndSound = FocusSound.Hand;
        WeatherWidth = double.IsFinite(WeatherWidth) ? Math.Clamp(WeatherWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : DefaultWeatherWidth;
        WeatherHeight = double.IsFinite(WeatherHeight) ? Math.Clamp(WeatherHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : DefaultWeatherHeight;
        DevicesWidth = double.IsFinite(DevicesWidth) ? Math.Clamp(DevicesWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : 460;
        DevicesHeight = double.IsFinite(DevicesHeight) ? Math.Clamp(DevicesHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : 200;
        DeviceChoices = [.. (DeviceChoices ?? []).OfType<DeviceChoice>().Where(device => !string.IsNullOrWhiteSpace(device.Key)).DistinctBy(device => device.Key).Take(256)];
        foreach (var device in DeviceChoices)
        {
            device.Name ??= "";
            device.Kind = Enum.IsDefined(device.Kind) ? device.Kind : DeviceKind.Other;
            var label = device.Label?.Trim();
            device.Label = string.IsNullOrEmpty(label) ? null : label.Length > DeviceLists.MaximumLabel ? label[..DeviceLists.MaximumLabel] : label;
            static string? Hex(string? color) => AppPresets.NormalizeColor(color) is { Length: > 0 } valid ? valid : null;
            device.Color = Hex(device.Color); device.ChargeColor = Hex(device.ChargeColor); device.WarningColor = Hex(device.WarningColor); device.CriticalColor = Hex(device.CriticalColor);
        }
        DeviceLowPercent = Devices.LowChoices.Contains(DeviceLowPercent) ? DeviceLowPercent : Devices.DefaultLowPercent;
        // Getting low starts above low: a warning at or under the low threshold takes the next choice above it.
        DeviceWarningPercent = Devices.WarningChoices.Contains(DeviceWarningPercent) ? DeviceWarningPercent : Devices.DefaultWarningPercent;
        if (DeviceWarningPercent <= DeviceLowPercent) DeviceWarningPercent = Devices.WarningChoices.First(percent => percent > DeviceLowPercent);
        MonitorHeight = double.IsFinite(MonitorHeight) ? Math.Clamp(MonitorHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : MonitorSizes.Default.Height;
        RecentColours = [.. (RecentColours ?? []).Where(text => text is { Length: <= 9 } && CssColour.TryParse(text, out _)).Take(12)];
        ClipboardWidth = double.IsFinite(ClipboardWidth) ? Math.Clamp(ClipboardWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : ShelfSizes.Default.Width;
        ClipboardHeight = double.IsFinite(ClipboardHeight) ? Math.Clamp(ClipboardHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : ShelfSizes.Default.Height;
        if (!Enum.IsDefined(TransferDragOut)) TransferDragOut = TransferDragOut.Copy;
        if (!Enum.IsDefined(ShelfShown)) ShelfShown = ShelfShown.Both;
        if (!Enum.IsDefined(ShelfArrangement)) ShelfArrangement = ShelfArrangement.Split;
        if (!Enum.IsDefined(ShelfClipView)) ShelfClipView = ClipView.Single;
        if (!Enum.IsDefined(ShelfFileView)) ShelfFileView = FileView.Two;
        ShelfFileShare = ShelfLayout.ClampShare(Math.Round(ShelfFileShare, 1), ShelfFileView);
        if (!Enum.IsDefined(ClipboardKeepFor)) ClipboardKeepFor = HistoryLength.Month;
        ClipboardExcluded = ClipboardExclusions.Normalize(ClipboardExcluded);
        PanelScale = PanelScales.Fit(PanelScale);
        SurfaceTransparency = Math.Clamp(SurfaceTransparency, 0, MaxSurfaceTransparency);
        GlassTransparency = Math.Clamp(GlassTransparency, 0, 40); GlassRefraction = Math.Clamp(GlassRefraction, 0, 100);
        GlassFrost = Math.Clamp(GlassFrost, 0, 100); GlassRim = Math.Clamp(GlassRim, 0, 100); GlassFps = Math.Clamp(GlassFps, 5, 30);
        if (!Enum.IsDefined(MonitorPalette)) MonitorPalette = MonitorPalette.Theme;
        MonitorCustomColors ??= new();
        MonitorCustomColors.Normalize();
        CompactWarningColor = AppPresets.NormalizeColor(CompactWarningColor) is { Length: > 0 } warning ? warning : null;
        CompactCriticalColor = AppPresets.NormalizeColor(CompactCriticalColor) is { Length: > 0 } critical ? critical : null;
        MonitorInterval = Math.Clamp(MonitorInterval, 1, 2);
        if (!Enum.IsDefined(MonitorTrafficPeriod)) MonitorTrafficPeriod = TrafficPeriod.Today;
        if (!Enum.IsDefined(MonitorSort)) MonitorSort = ProcessSort.Cpu;
        if (!Enum.IsDefined(MonitorMain)) MonitorMain = MonitorMain.Cpu;
        if (!Enum.IsDefined(MonitorGpuUsage)) MonitorGpuUsage = GpuUsageSource.Driver;
        MonitorGpu = string.IsNullOrWhiteSpace(MonitorGpu) ? null : MonitorGpu.Trim();
        MonitorGpuName = MonitorGpu is null || string.IsNullOrWhiteSpace(MonitorGpuName) ? null : MonitorGpuName.Trim();
        if (!Enum.IsDefined(MonitorProcessView)) MonitorProcessView = MonitorProcessView.Auto;
        if (!Enum.IsDefined(CompactSmoothing)) CompactSmoothing = CompactSmoothing.Ten;
        if (!Enum.IsDefined(CoreDisplay)) CoreDisplay = CoreDisplay.Auto;
        if (!Enum.IsDefined(CompactMonitor)) CompactMonitor = CompactMonitorMode.Full;
        CompactMonitorItems &= CompactMonitorItems.All;
        if (CompactMonitorItems == CompactMonitorItems.None) CompactMonitorItems = CompactMonitorItems.Cpu;
        if (_expanded is not null)
        {
            _expanded = ExpandedMonitor.Normalize(_expanded);
            (MonitorProcesses, MonitorStorage, MonitorPower, MonitorFrames) = ExpandedMonitor.Switches(_expanded);
        }
        CompactReadings = Core.CompactMonitor.Normalize(CompactReadings);
        Core.CompactMonitor.MoveColors(this);
        CompactMonitor = Core.CompactMonitor.Preset(CompactReadings) ?? CompactMonitorMode.Custom;
        CompactMonitorItems = Core.CompactMonitor.Shown(CompactReadings);
        if (!Enum.IsDefined(MusicText)) MusicText = CompactMusicText.None;
        if (!Enum.IsDefined(MusicLyricsLayout)) MusicLyricsLayout = MusicLyricsLayout.Columns;
        if (!Enum.IsDefined(MusicLyricsAlign)) MusicLyricsAlign = MusicLyricsAlign.Left;
        if (!Enum.IsDefined(TimeFormat)) TimeFormat = ClockTimeFormat.System;
        if (TimeFormat == ClockTimeFormat.System && Clock24Hour is { } legacyClock)
            TimeFormat = legacyClock ? ClockTimeFormat.Hour24 : ClockTimeFormat.Hour12;
        Clock24Hour = null;
        if (!Enum.IsDefined(ClockStyle)) ClockStyle = ClockStyle.Minimal;
        if (!Enum.IsDefined(ClockColor)) ClockColor = ClockColor.White;
        ClockCustomColor = AppPresets.NormalizeColor(ClockCustomColor) is { Length: > 0 } custom ? custom : ClockColors.DefaultCustom;
        var stops = (ClockGradient ?? []).Select(AppPresets.NormalizeColor).ToList();
        ClockGradient = stops.Count == 3 && stops.All(stop => stop.Length > 0) ? stops : [.. ClockColors.Sunset];
        WorldClocks = Core.WorldClocks.Normalize(WorldClocks);
        CountdownTitle = (CountdownTitle ?? "").Trim();
        CountdownDate = Countdown.Normalize(CountdownDate);
        TodoItems = Todos.Normalize(TodoItems);
        NoteText = (NoteText ?? "").Length <= MaxNoteLength ? NoteText ?? "" : NoteText![..MaxNoteLength];
        ScreenTimeApps = ScreenTime.Normalize(ScreenTimeDate, ScreenTimeApps);
        if (ScreenTimeApps.Count == 0) ScreenTimeDate = "";
        ScreenTimeHours = ScreenTimeHours is { Count: 24 } && ScreenTimeDate.Length > 0 ? [.. ScreenTimeHours.Select(v => Math.Clamp(v, 0, 3600))] : [.. new int[24]];
        // A count from a day that has ended joins the history; days past KeepDays leave it.
        var now = DateTime.Now;
        ScreenTimeHistory = ScreenTime.Archive(ScreenTimeHistory, ScreenTimeDate, ScreenTimeApps, ScreenTimeHours, now);
        if (ScreenTimeDate.Length > 0 && ScreenTimeDate != ScreenTime.DayKey(now)) { ScreenTimeDate = ""; ScreenTimeApps = []; ScreenTimeHours = [.. new int[24]]; }
        var named = ScreenTimeApps.Keys.Concat(ScreenTimeHistory.Values.SelectMany(day => day.Apps.Keys)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ScreenTimePaths = (ScreenTimePaths ?? []).Where(pair => named.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value) && pair.Value.Length <= 400)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (ExpandedUnderList is { } under && (!Enum.IsDefined(under) || under == ExpandedItem.Processes)) ExpandedUnderList = ExpandedItem.Storage;
        if (ExpandedUnderList is { } block) ExpandedUnderList = ExpandedMonitor.BlockOf(block)[0];
        if (!Enum.IsDefined(GamingMode)) GamingMode = GamingMode.Off;
        CalendarSources = CalendarFeed.Normalize(CalendarSources);
        if (!CalendarFeed.Horizons.Contains(CalendarHorizon)) CalendarHorizon = CalendarFeed.DefaultHorizon;
        if (CalendarNoticeMinutes is not (5 or 10)) CalendarNoticeMinutes = 10;
        CalendarWidth = double.IsFinite(CalendarWidth) ? Math.Clamp(CalendarWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : 720;
        CalendarHeight = double.IsFinite(CalendarHeight) ? Math.Clamp(CalendarHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : 340;
        ScreenTimeWidth = double.IsFinite(ScreenTimeWidth) ? Math.Clamp(ScreenTimeWidth, DeckPages.MinWidth, DeckPages.MaxWidth) : 700;
        ScreenTimeHeight = double.IsFinite(ScreenTimeHeight) ? Math.Clamp(ScreenTimeHeight, DeckPages.MinHeight, DeckPages.MaxHeight) : 340;
        if (!Enum.IsDefined(DefaultPage)) DefaultPage = DefaultDeckPage.Last;
        if (DefaultPage == DefaultDeckPage.Focus) DefaultPage = DefaultDeckPage.Widgets;
        if (!Enum.IsDefined(LastPage)) LastPage = DeckPage.Usage;
        OpenDelay = Math.Clamp(OpenDelay, 0, 2000);
        CloseDelay = Math.Clamp(CloseDelay, 0, 2000);
        AnimationDuration = Math.Clamp(AnimationDuration, 100, 1000);
        if (!Enum.IsDefined(Theme)) Theme = SettingsTheme.System;
        PanelTheme = PanelThemes.NormalizeId(PanelTheme) is { Length: > 0 } panelTheme ? panelTheme : PanelThemes.Dark;
        // "system" or two valid, different times; anything else keeps the theme's own choice.
        ThemeDaylight = PanelThemes.NormalizeDaylightSetting(ThemeDaylight);
        Language = Loc.Normalize(Language);
        UiFont = UiFonts.Normalize(UiFont);
        if (!Enum.IsDefined(Style)) Style = CompactStyle.Notch;
        if (Style != CompactStyle.Line) ShapeStyle = Style;
        else if (ShapeStyle is not (CompactStyle.Notch or CompactStyle.Capsule)) ShapeStyle = CompactStyle.Notch;
        static double Fit(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        static double Offset(double value) => Fit(value, -CollapsedLimits.OffsetLimit, CollapsedLimits.OffsetLimit, CollapsedLimits.OffsetDefault);
        NotchWidth = Fit(NotchWidth, CollapsedLimits.NotchWidthMin, CollapsedLimits.NotchWidthMax, CollapsedLimits.NotchWidthDefault);
        NotchPartSpacing = Math.Clamp(NotchPartSpacing, CompactMusicLayout.MinimumPartSpacing, CompactMusicLayout.MaximumPartSpacing);
        CapsulePartSpacing = Math.Clamp(CapsulePartSpacing, CompactMusicLayout.MinimumPartSpacing, CompactMusicLayout.MaximumPartSpacing);
        NotchHeight = Fit(NotchHeight, CollapsedLimits.NotchHeightMin, CollapsedLimits.NotchHeightMax, CollapsedLimits.NotchHeightDefault);
        NotchOffsetX = Offset(NotchOffsetX);
        if (!Enum.IsDefined(NotchEdge)) NotchEdge = ScreenEdge.Top;
        if (!Enum.IsDefined(LineEdge)) LineEdge = ScreenEdge.Top;
        if (!Enum.IsDefined(CapsuleEdge)) CapsuleEdge = ScreenEdge.Top;
        if (NotchAnchor is { } notchAnchor && !Enum.IsDefined(notchAnchor)) NotchAnchor = null;
        if (LineAnchor is { } lineAnchor && !Enum.IsDefined(lineAnchor)) LineAnchor = null;
        if (!Enum.IsDefined(GripDoubleClick)) GripDoubleClick = GripDoubleClick.Move;
        CapsuleWidth = Fit(CapsuleWidth, CollapsedLimits.CapsuleWidthMin, CollapsedLimits.CapsuleWidthMax, CollapsedLimits.CapsuleWidthDefault);
        CapsuleHeight = Fit(CapsuleHeight, CollapsedLimits.CapsuleHeightMin, CollapsedLimits.CapsuleHeightMax, CollapsedLimits.CapsuleHeightDefault);
        CapsuleOffsetX = Offset(CapsuleOffsetX);
        CapsuleTop = Fit(CapsuleTop, CollapsedLimits.CapsuleTopMin, CollapsedLimits.CapsuleTopMax, CollapsedLimits.CapsuleTopDefault);
        LineLength = Fit(LineLength, CollapsedLimits.LineLengthMin, CollapsedLimits.LineLengthMax, CollapsedLimits.LineLengthDefault);
        LineThickness = CollapsedGeometry.RoundLineThickness(LineThickness);
        LineTop = Fit(LineTop, CollapsedLimits.LineTopMin, CollapsedLimits.LineTopMax, CollapsedLimits.LineTopDefault);
        LineOffsetX = Offset(LineOffsetX);
        foreach (var kind in Enum.GetValues<HotkeyKind>()) SetHotkey(kind, Hotkey(kind));
        if (QuickSize is { } quick && !Enum.IsDefined(quick)) QuickSize = null;
        if (!Enum.IsDefined(Maximized)) Maximized = WindowBehavior.Line;
        if (!Enum.IsDefined(Borderless)) Borderless = WindowBehavior.Hide;
        if (!Enum.IsDefined(Exclusive)) Exclusive = WindowBehavior.Hide;
        if (!Enum.IsDefined(Occupied)) Occupied = WindowBehavior.Normal;
        if (!Enum.IsDefined(Display)) Display = DisplayPlacement.Primary;
        // A file saved before MouseWheel had the page switch turned off as MouseWheelPages = false.
        if (MouseWheelPages is false) MouseWheel = MouseWheelAction.None;
        MouseWheelPages = null;
        if (!Enum.IsDefined(MouseWheel)) MouseWheel = MouseWheelAction.Pages;
        // A file saved before app rules carried plain exceptions: each becomes a rule that keeps the panel's own style in
        // all three contexts, which is what an exception did.
        if (FullscreenExceptions is { Count: > 0 } legacy)
        {
            var carried = legacy.Where(entry => entry is not null)
                .Select(entry => new FullscreenRule(ScreenPolicy.NormalizeName(entry.Name), ScreenPolicy.NormalizePath(entry.Path))
                    { Maximized = WindowBehavior.Normal, Borderless = WindowBehavior.Normal, Exclusive = WindowBehavior.Normal });
            FullscreenRules = carried.Concat(FullscreenRules ?? []).ToList();
            FullscreenExceptions = [];
        }
        // A path-only entry takes its name from the executable, so it can also be matched when the path cannot be read.
        FullscreenRules = (FullscreenRules ?? []).OfType<FullscreenRule>()
            .Select(r => new FullscreenRule(ScreenPolicy.NormalizeName(r.Name), ScreenPolicy.NormalizePath(r.Path))
                { Maximized = Valid(r.Maximized), Borderless = Valid(r.Borderless), Exclusive = Valid(r.Exclusive) })
            .Select(r => r.Name.Length == 0 && r.Path.Length > 0 ? r with { Name = System.IO.Path.GetFileNameWithoutExtension(r.Path) } : r)
            .Where(r => r.Name.Length > 0 || r.Path.Length > 0)
            .DistinctBy(ScreenPolicy.Key, StringComparer.OrdinalIgnoreCase)
            .Take(AppPresets.MaximumFullscreenExceptions).ToList();
        static WindowBehavior? Valid(WindowBehavior? choice) => choice is null || Enum.IsDefined(choice.Value) ? choice : null;
        IgnoredMediaApps = (IgnoredMediaApps ?? []).OfType<IgnoredMediaApp>().Where(app => !string.IsNullOrWhiteSpace(app.AppId))
            .Select(app => new IgnoredMediaApp(app.AppId.Trim(), (app.Name ?? "").Trim(), (app.Path ?? "").Trim()))
            .DistinctBy(app => app.AppId, StringComparer.OrdinalIgnoreCase).Take(AppPresets.MaximumIgnoredMediaApps).ToList();
        DisplayMonitors = (DisplayMonitors ?? []).OfType<ChosenDisplay>().Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .Select(d => d with { Id = d.Id.Trim(), Name = (d.Name ?? "").Trim() }).DistinctBy(d => d.Id).ToList();
        if (Display == DisplayPlacement.Chosen && DisplayMonitors.Count == 0) Display = DisplayPlacement.Primary;
        Core.Profiles.Normalize(this);
    }
}

// appVersion is the running application's version; it defaults to the entry assembly's version.
// settings.json holds the settings of the newest version that saved it, plus "Snapshots": the settings each earlier
// version last saved, keyed by version. Upgrading keeps the older version's settings there; a downgraded DeckIsland reads
// and saves only its own snapshot, so the newer version's settings stay as they were.
public sealed class SettingsStore(string directory, string? appVersion = null)
{
    private const string SnapshotsKey = "Snapshots";
    private const int KeptSnapshots = 5;
    private readonly string _appVersion = ApplicationVersion.Parse(appVersion ?? ApplicationVersion.Current).ToString(3);
    private JsonObject _snapshots = [];
    // The file as saved by a newer version; saving replaces only this version's snapshot in it.
    private JsonObject? _newerFile;
    // Settings this build does not know, from a file saved by the same version (another build of it, such as one with a feature
    // not merged yet). They are written back as they were, so running this build never deletes them. An older version's file
    // goes through SettingsMigrations instead, and its unknown keys are the ones the migrations retire.
    private JsonObject _unknown = [];
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    // Worded when shown, in the language the loaded settings choose.
    private Func<string>? _loadWarning;
    public string? LoadWarning => _loadWarning?.Invoke();
    // True when the last Load found no settings file, or none it could read, and returned defaults.
    public bool IsFirstStart { get; private set; }
    // Without saved settings the interface follows the Windows display language; saved settings keep their language.
    // Normalized as read settings are: the start relies on what Normalize ensures, such as one current profile (Profiles.Keep).
    private DeckSettings FirstStart()
    {
        IsFirstStart = true;
        var settings = new DeckSettings { Language = Loc.SystemLanguage() };
        settings.Normalize();
        return settings;
    }
    public DeckSettings Load()
    {
        _snapshots = []; _newerFile = null; _unknown = []; IsFirstStart = false; _loadWarning = null;
        try
        {
            if (!File.Exists(FilePath)) return FirstStart();
            var text = File.ReadAllText(FilePath);
            var root = SettingsMigrations.Parse(text);
            var running = ApplicationVersion.Parse(_appVersion);
            var saved = SettingsMigrations.VersionOf(root);
            var snapshots = root[SnapshotsKey] as JsonObject ?? [];
            root.Remove(SnapshotsKey);
            if (saved > running)
            {
                _newerFile = SettingsMigrations.Parse(text);
                var own = snapshots.Where(pair => pair.Value is JsonObject && ApplicationVersion.Parse(pair.Key) <= running)
                    .OrderByDescending(pair => ApplicationVersion.Parse(pair.Key)).Select(pair => (JsonObject)pair.Value!.DeepClone()).FirstOrDefault();
                _loadWarning = own is null
                    ? () => Loc.T($"设置文件由 DeckIsland {saved.ToString(3)} 保存。当前版本 {_appVersion} 读取其中能识别的设置，之后的修改只保存给 {_appVersion}，不影响 {saved.ToString(3)} 的设置。",
                        $"The settings file was saved by DeckIsland {saved.ToString(3)}. Version {_appVersion} reads the settings it recognizes; later changes are saved for {_appVersion} only and leave the settings of {saved.ToString(3)} unchanged.")
                    : () => Loc.T($"设置文件由 DeckIsland {saved.ToString(3)} 保存。当前版本 {_appVersion} 使用自己上次保存的设置，修改不影响 {saved.ToString(3)} 的设置。",
                        $"The settings file was saved by DeckIsland {saved.ToString(3)}. Version {_appVersion} uses the settings it saved last; changes leave the settings of {saved.ToString(3)} unchanged.");
                return SettingsMigrations.Read(own ?? root, running, out _);
            }
            _snapshots = snapshots;
            // Keep what an older version saved so that returning to it restores its settings. Files saved before
            // versioning cannot be told apart and are not kept.
            if (saved < running && saved > new Version(0, 0, 0)) _snapshots[saved.ToString(3)] = root.DeepClone();
            if (saved == running)
            {
                var known = JsonSerializer.SerializeToNode(new DeckSettings())!.AsObject();
                _unknown = new JsonObject(root.Where(pair => !known.ContainsKey(pair.Key)).Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
            }
            return SettingsMigrations.Read(root, running, out _);
        }
        // Any exception, since a faulty migration step can throw anything. Defaults replace the file at the next save,
        // so the file is copied aside first and can still be restored by hand.
        catch (Exception)
        {
            var kept = KeepUnreadable();
            _loadWarning = kept is null
                ? () => Loc.T("无法读取已保存的设置，本次使用默认值。", "The saved settings could not be read. Default settings are used this time.")
                : () => Loc.T($"无法读取已保存的设置，本次使用默认值。原文件已另存为 {kept}。", $"The saved settings could not be read. Default settings are used this time. The original file is kept as {kept}.");
            return FirstStart();
        }
    }

    // Copies an unreadable settings file next to it and returns the copy's name, or null when there is nothing to copy.
    private string? KeepUnreadable()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var name = $"settings.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            File.Copy(FilePath, Path.Combine(DirectoryPath, name), true);
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    public void Save(DeckSettings settings) => Write(Serialize(settings));

    // The file's text for these settings. It normalizes them and reads this store's state, so it runs where the settings live;
    // Write may then run on any thread, one call at a time.
    public string Serialize(DeckSettings settings)
    {
        settings.AppVersion = _appVersion;
        settings.Normalize();
        var own = JsonSerializer.SerializeToNode(settings)!.AsObject();
        foreach (var (key, value) in _unknown) if (!own.ContainsKey(key)) own[key] = value?.DeepClone();
        JsonObject file;
        if (_newerFile is not null)
        {
            file = (JsonObject)_newerFile.DeepClone();
            var snapshots = file[SnapshotsKey] as JsonObject ?? [];
            file.Remove(SnapshotsKey);
            snapshots[_appVersion] = own;
            file[SnapshotsKey] = snapshots;
        }
        else
        {
            file = own;
            var running = ApplicationVersion.Parse(_appVersion);
            var kept = _snapshots.Where(pair => pair.Value is JsonObject && ApplicationVersion.Parse(pair.Key) < running)
                .OrderByDescending(pair => ApplicationVersion.Parse(pair.Key)).Take(KeptSnapshots)
                .Select(pair => KeyValuePair.Create(pair.Key, pair.Value!.DeepClone())).ToList();
            if (kept.Count > 0) file[SnapshotsKey] = new JsonObject(kept!);
        }
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public void Write(string text)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, FilePath, true);
    }
}
