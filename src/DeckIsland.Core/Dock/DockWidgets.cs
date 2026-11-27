namespace DeckIsland.Core;

// The dock's widgets, after lumabar's: live tiles that sit on the bar beside the apps, chosen in the widget picker by category.
// The catalog grows with each wave of widgets; a kind this build does not know is dropped from the settings when they are read.
public enum DockWidgetKind { Cpu, Memory, Battery, AiUsage, NextEvent, Network, Storage, Volume, Weather, AnalogClock, NowPlaying, Calendar, AiRing }
public enum DockWidgetCategory { Ai, Productivity, System, MediaTime }

// Width is in tiles: 1 a square tile, more a wide card.
public sealed record DockWidgetInfo(DockWidgetKind Kind, DockWidgetCategory Category, string Name, string Description, int Width);

// One widget on the dock. Id tells two of the same kind apart; Option is what the widget shows (an AI provider's id), empty for
// its default.
public sealed record DockWidget(string Id, [property: System.Text.Json.Serialization.JsonConverter(typeof(DockWidgetKindConverter))] DockWidgetKind Kind, string Option = "");

// A set of widgets the dock can switch between, as lumabar's dock profiles.
public sealed record DockProfile(string Id, string Name, List<DockWidget> Widgets)
{
    public DockProfile Copy() => this with { Widgets = [.. Widgets] };
}

public static class DockWidgets
{
    public const int MaxProfiles = 12, MaxWidgets = 24, MaxProfileName = 32;

    public static IReadOnlyList<DockWidgetInfo> Catalog => [
        new(DockWidgetKind.AiUsage, DockWidgetCategory.Ai, Loc.T("AI 用量", "AI Limits"), Loc.T("一个 AI 工具的会话和每周限额。", "An AI tool's session and weekly limits."), 4),
        new(DockWidgetKind.AiRing, DockWidgetCategory.Ai, Loc.T("AI 圆环", "AI Ring"), Loc.T("一个 AI 工具的限额，以圆环显示。", "One AI tool's limit as a ring."), 1),
        new(DockWidgetKind.Calendar, DockWidgetCategory.Productivity, Loc.T("日历", "Calendar"), Loc.T("今天的日期和今天的日程数。", "Today's date and how many events today holds."), 1),
        new(DockWidgetKind.NextEvent, DockWidgetCategory.Productivity, Loc.T("下一个日程", "Next event"), Loc.T("日历中的下一个日程，以及还有多久。", "The next event on your calendar and how soon it starts."), 4),
        new(DockWidgetKind.Cpu, DockWidgetCategory.System, "CPU", Loc.T("处理器的使用率。", "How busy the processor is."), 1),
        new(DockWidgetKind.Memory, DockWidgetCategory.System, "RAM", Loc.T("正在使用的内存。", "How much memory is in use."), 1),
        new(DockWidgetKind.Battery, DockWidgetCategory.System, Loc.T("电池", "Battery"), Loc.T("电量，以及是否在充电。", "The charge, and whether it is charging."), 1),
        new(DockWidgetKind.Network, DockWidgetCategory.System, Loc.T("网络", "Network"), Loc.T("下载和上传速度。", "Download and upload speed."), 2),
        new(DockWidgetKind.Storage, DockWidgetCategory.System, Loc.T("存储", "Storage"), Loc.T("系统盘的剩余空间。", "Free space on the system drive."), 1),
        new(DockWidgetKind.Volume, DockWidgetCategory.MediaTime, Loc.T("音频输出", "Audio Output"), Loc.T("音量；在上面滚动可调节。", "The volume; scroll on it to change it."), 1),
        new(DockWidgetKind.NowPlaying, DockWidgetCategory.MediaTime, Loc.T("正在播放", "Now Playing"), Loc.T("正在播放的歌曲、封面和播放控制。", "The song playing, its cover and the controls."), 4),
        new(DockWidgetKind.Weather, DockWidgetCategory.MediaTime, Loc.T("天气", "Weather"), Loc.T("当前的温度和天气。", "The temperature and conditions now."), 2),
        new(DockWidgetKind.AnalogClock, DockWidgetCategory.MediaTime, Loc.T("指针时钟", "Analog Minimal"), Loc.T("简洁的指针时钟。", "A minimal clock with hands."), 1)];

    public static DockWidgetInfo Info(DockWidgetKind kind) => Catalog.First(info => info.Kind == kind);

    public static string CategoryName(DockWidgetCategory category) => category switch
    {
        DockWidgetCategory.Ai => "AI", DockWidgetCategory.Productivity => Loc.T("效率", "Productivity"),
        DockWidgetCategory.MediaTime => Loc.T("音乐、天气与时间", "Music, weather & time"), _ => Loc.T("系统", "System")
    };

    // The catalog filtered by category (null for all) and by words in a widget's name or description.
    public static List<DockWidgetInfo> Find(DockWidgetCategory? category, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Catalog.Where(info => (category is null || info.Category == category)
            && words.All(word => info.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase) || info.Description.Contains(word, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    public static DockProfile DefaultProfile() => new("main", Loc.T("主要", "Main"), []);

    public static List<DockWidget> Add(IReadOnlyList<DockWidget> widgets, DockWidgetKind kind) =>
        widgets.Count >= MaxWidgets ? [.. widgets] : [.. widgets, new DockWidget(Guid.NewGuid().ToString("N")[..8], kind)];

    public static List<DockWidget> Remove(IReadOnlyList<DockWidget> widgets, string id) => widgets.Where(w => w.Id != id).ToList();

    // The widgets with one moved to a new place among them.
    public static List<DockWidget> Move(IReadOnlyList<DockWidget> widgets, string id, int index)
    {
        var list = widgets.ToList();
        int from = list.FindIndex(w => w.Id == id);
        if (from < 0) return list;
        var widget = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Clamp(index, 0, list.Count), widget);
        return list;
    }

    // Profiles as the settings may hold them: at least one, distinct ids, names kept short, widgets of known kinds with distinct
    // ids; the current profile one of them.
    public static (List<DockProfile> Profiles, string Current) Normalize(IReadOnlyList<DockProfile>? profiles, string? current)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var kept = (profiles ?? []).Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id) && ids.Add(p.Id)).Take(MaxProfiles).Select(p =>
        {
            var widgetIds = new HashSet<string>(StringComparer.Ordinal);
            var widgets = (p.Widgets ?? []).Where(w => w is not null && Enum.IsDefined(w.Kind) && !string.IsNullOrWhiteSpace(w.Id) && widgetIds.Add(w.Id))
                .Take(MaxWidgets).Select(w => w with { Option = w.Option ?? "" }).ToList();
            string name = string.IsNullOrWhiteSpace(p.Name) ? Loc.T("配置", "Profile") : p.Name.Trim();
            return new DockProfile(p.Id, name.Length > MaxProfileName ? name[..MaxProfileName] : name, widgets);
        }).ToList();
        if (kept.Count == 0) kept.Add(DefaultProfile());
        return (kept, kept.Any(p => p.Id == current) ? current! : kept[0].Id);
    }
}

// Widget kinds are written by name. A name this build does not know (a widget from a newer version) reads as an undefined kind, which
// DockWidgets.Normalize drops, so one unknown widget never makes the whole of dock.json unreadable.
public sealed class DockWidgetKindConverter : System.Text.Json.Serialization.JsonConverter<DockWidgetKind>
{
    public override DockWidgetKind Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options) => reader.TokenType switch
    {
        System.Text.Json.JsonTokenType.String when Enum.TryParse<DockWidgetKind>(reader.GetString(), true, out var kind) => kind,
        System.Text.Json.JsonTokenType.Number when reader.TryGetInt32(out var number) => (DockWidgetKind)number,
        _ => (DockWidgetKind)(-1)
    };

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DockWidgetKind value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
