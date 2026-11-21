namespace DeckIsland.Core;

public sealed record TodoItem(Guid Id, string Text, bool Done);

// The To do widget's list, kept in the order it is shown: the open items first, highest priority at the top, then the done
// ones, the most recently ticked first. Every change returns a new list; only open items can be moved.
public static class Todos
{
    public const int MaxLength = 200, MaxItems = 100;

    public static List<TodoItem> Normalize(IEnumerable<TodoItem>? items)
    {
        var valid = (items ?? []).Where(item => item is not null && item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.Id).Select(group => group.First())
            .Select(item => item with { Text = Clip(item.Text) }).Take(MaxItems).ToList();
        return [.. valid.Where(item => !item.Done), .. valid.Where(item => item.Done)];
    }

    public static int OpenCount(IEnumerable<TodoItem> items) => items.Count(item => !item.Done);

    // A new item is the last of the open ones; an empty text or a full list changes nothing.
    public static List<TodoItem> Add(IReadOnlyList<TodoItem> items, string text, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(text) || items.Count >= MaxItems) return [.. items];
        int open = OpenCount(items);
        var list = items.ToList();
        list.Insert(open, new TodoItem(id ?? Guid.NewGuid(), Clip(text), false));
        return list;
    }

    // Ticking moves an item to the top of the done ones; unticking puts it back as the last open one.
    public static List<TodoItem> Toggle(IReadOnlyList<TodoItem> items, Guid id)
    {
        int index = Index(items, id);
        if (index < 0) return [.. items];
        var list = items.ToList();
        var item = list[index] with { Done = !list[index].Done };
        list.RemoveAt(index);
        list.Insert(OpenCount(list), item);
        return list;
    }

    // Moves an open item up (negative) or down (positive) among the open items, stopping at either end.
    public static List<TodoItem> Move(IReadOnlyList<TodoItem> items, Guid id, int by)
    {
        int index = Index(items, id), open = OpenCount(items);
        if (index < 0 || items[index].Done) return [.. items];
        int target = Math.Clamp(index + by, 0, open - 1);
        var list = items.ToList();
        var item = list[index];
        list.RemoveAt(index);
        list.Insert(target, item);
        return list;
    }

    public static List<TodoItem> Remove(IReadOnlyList<TodoItem> items, Guid id) => [.. items.Where(item => item.Id != id)];

    public static List<TodoItem> ClearDone(IReadOnlyList<TodoItem> items) => [.. items.Where(item => !item.Done)];

    private static int Index(IReadOnlyList<TodoItem> items, Guid id)
    {
        for (int i = 0; i < items.Count; i++) if (items[i].Id == id) return i;
        return -1;
    }

    private static string Clip(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
    }
}
