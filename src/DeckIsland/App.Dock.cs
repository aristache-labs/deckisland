using System.IO;
using DeckIsland.Core;

namespace DeckIsland;

// DeckIsland Dock, the taskbar replacement. Everything about it stays apart from the island: its settings are dock.json
// (DockSettingsStore) and its own window edits them. The island's settings hold only the on/off switch (DeckSettings.DockEnabled).
public partial class App
{
    private DockSettingsStore? _dockStore;
    private DockSettings? _dockSettings;
    private DockSettingsWindow? _dockSettingsWindow;

    // Read from dock.json the first time the dock or its settings need them.
    internal DockSettings DockSettings
    {
        get
        {
            if (_dockSettings is null)
            {
                _dockStore ??= new DockSettingsStore(Store.DirectoryPath);
                _dockSettings = _dockStore.Load();
            }
            return _dockSettings;
        }
    }

    // Follows DeckSettings.DockEnabled. The dock window itself comes with phase F1; until then switching off closes its settings.
    private void ApplyDock()
    {
        if (!Settings.DockEnabled) _dockSettingsWindow?.Close();
    }

    internal void OpenDockSettings()
    {
        if (_dockSettingsWindow is null)
        {
            _dockSettingsWindow = new DockSettingsWindow(this, Settings.Theme);
            _dockSettingsWindow.Closed += (_, _) => _dockSettingsWindow = null;
            _dockSettingsWindow.Show();
        }
        if (_dockSettingsWindow.WindowState == System.Windows.WindowState.Minimized) _dockSettingsWindow.WindowState = System.Windows.WindowState.Normal;
        _dockSettingsWindow.Activate();
    }

    internal void UpdateDockSettings(DockSettings settings)
    {
        settings.Normalize();
        _dockSettings = settings;
        try { _dockStore!.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _recordCrash?.Invoke(ex); }
    }
}
