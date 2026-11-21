namespace DeckIsland.Core;

public enum WidgetKind { Empty, Weather, Monitor, Focus, Devices, Quotas, Today }

public static class WidgetLayout
{
    public static string Title => Loc.T("小组件", "Widgets");

    public static WidgetKind[] Normalize(IEnumerable<WidgetKind>? slots)
    {
        var result = new WidgetKind[4];
        var seen = new HashSet<WidgetKind>();
        int index = 0;
        foreach (var kind in (slots ?? [WidgetKind.Weather, WidgetKind.Monitor, WidgetKind.Focus, WidgetKind.Devices]).Take(4))
            result[index++] = Enum.IsDefined(kind) && seen.Add(kind) ? kind : WidgetKind.Empty;
        return result;
    }

    public static CompactPart[] MinimalParts(bool focus, bool transfer, bool music, CompactPart? first, CompactPart? second)
    {
        CompactPart[] active = [.. new[] { (CompactPart.Focus, focus), (CompactPart.Transfer, transfer), (CompactPart.Music, music) }
            .Where(item => item.Item2).Select(item => item.Item1).Take(2)];
        return active.Length > 0 ? active : [.. new[] { first, second }.OfType<CompactPart>().Where(part => Enum.IsDefined(part) && part != CompactPart.Focus).Distinct()];
    }

    public static string Name(WidgetKind kind) => kind switch
    {
        WidgetKind.Weather => Loc.T("天气", "Weather"),
        WidgetKind.Monitor => Loc.T("系统监控", "System monitoring"),
        WidgetKind.Focus => Loc.T("专注计时器", "Focus timer"),
        WidgetKind.Devices => Loc.T("显示器与设备", "Display and devices"),
        WidgetKind.Today => Loc.T("今日", "Today"),
        WidgetKind.Quotas => Loc.T("配额与用量", "Quotas and usage"),
        _ => Loc.T("空", "Empty")
    };
}
