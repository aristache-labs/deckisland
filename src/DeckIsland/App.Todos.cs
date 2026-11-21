using DeckIsland.Core;

namespace DeckIsland;

public partial class App
{
    // Every change to the To do list goes through here: it is saved with the settings, and the widget redraws on its next tick
    // because the list it drew is no longer the one in the settings.
    internal void UpdateTodos(Func<IReadOnlyList<TodoItem>, List<TodoItem>> change)
    {
        Settings.TodoItems = change(Settings.TodoItems);
        QueueSave();
        TodosChanged?.Invoke();
    }

    internal event Action? TodosChanged;
}
