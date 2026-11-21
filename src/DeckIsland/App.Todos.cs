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

    // The note is kept as it is typed; the widget draws it again on its next tick.
    internal void SetNote(string text)
    {
        Settings.NoteText = text.Length <= DeckSettings.MaxNoteLength ? text : text[..DeckSettings.MaxNoteLength];
        QueueSave();
    }

    // The Scratchpad's drafts and language are kept as they change, like the note.
    internal void SetScratchDraft(ScratchLanguage language, string text)
    {
        Scratchpad.SetDraft(Settings, language, text);
        QueueSave();
    }

    // A change to settings every profile shares, made from a panel (the Converter's last choices): kept, and saved shortly after.
    internal void ChangeShared(Action<DeckSettings> change)
    {
        change(Settings);
        QueueSave();
    }

    internal void SetScratchLanguage(ScratchLanguage language)
    {
        Settings.ScratchpadLanguage = language;
        QueueSave();
    }
}
