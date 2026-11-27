using System.IO;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// DeckIsland Dock, the taskbar replacement. Everything about it stays apart from the island: its settings are dock.json
// (DockSettingsStore) and its page in the settings window (SettingsDock) edits them. The island's settings hold only the on/off switch (DeckSettings.DockEnabled).
public partial class App
{
    private DockSettingsStore? _dockStore;
    private DockSettings? _dockSettings;
    private DockWindow? _dock;
    private bool _dockCrashGuard;

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

    // Follows DeckSettings.DockEnabled. Switched off (and at every start with it off), the taskbar is given back, a state file
    // left by a run that never restored included.
    private void ApplyDock()
    {
        if (!Settings.DockEnabled) { StopDock(); return; }
        if (_dock is not null) return;
        if (!_dockCrashGuard)
        {
            _dockCrashGuard = true;
            AppDomain.CurrentDomain.UnhandledException += (_, _) => DockTaskbar.Restore();
        }
        DockTaskbar.Apply(DockSettings.TaskbarMode);
        _dock = new DockWindow(this);
        _dock.Show();
    }

    private void StopDock()
    {
        _dock?.Close(); _dock = null;
        DockTaskbar.Stop();
    }

    // The dock's own panic key (DockWindow): switches it off as the island's settings would.
    internal void TurnOffDock()
    {
        if (!Settings.DockEnabled) return;
        var copy = Settings.Copy(); copy.DockEnabled = false; UpdateSettings(copy);
    }

    internal void UpdateDockSettings(DockSettings settings)
    {
        settings.Normalize();
        bool taskbarChanged = settings.TaskbarMode != DockSettings.TaskbarMode;
        _dockSettings = settings;
        try { _dockStore!.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _recordCrash?.Invoke(ex); }
        if (_dock is null) return;
        if (taskbarChanged) DockTaskbar.Apply(settings.TaskbarMode);
        _dock.Place();
    }
}
