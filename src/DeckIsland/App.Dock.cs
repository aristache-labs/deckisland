using System.IO;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// DeckIsland Dock, the taskbar replacement. Everything about it stays apart from the island: its settings are dock.json
// (DockSettingsStore) and its page in the settings window (SettingsDock) edits them. The island's settings hold only the on/off
// switch (DeckSettings.DockEnabled). There is one DockWindow on the main screen, or one on each screen (DockSettings.Displays).
public partial class App
{
    private static readonly TimeSpan DockSaveDelay = TimeSpan.FromMilliseconds(400);
    private DockSettingsStore? _dockStore;
    private DockSettings? _dockSettings;
    private readonly List<DockWindow> _docks = [];
    private DockKeys? _dockKeys;
    private bool _dockCrashGuard;
    private System.Windows.Threading.DispatcherTimer? _dockSave;

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
        if (_docks.Count > 0) return;
        if (!_dockCrashGuard)
        {
            _dockCrashGuard = true;
            AppDomain.CurrentDomain.UnhandledException += (_, _) => DockTaskbar.Restore();
        }
        DockTaskbar.Apply(DockSettings.TaskbarMode);
        SyncDocks();
        ApplyDockKeys();
    }

    private void StopDock()
    {
        foreach (var dock in _docks.ToList()) dock.Close();
        _docks.Clear();
        _dockKeys?.Dispose(); _dockKeys = null;
        FlushDockSettings();
        DockTaskbar.Stop();
    }

    // One dock per wanted screen: the main one, or every one; docks on screens no longer wanted or connected are closed.
    internal void SyncDocks()
    {
        if (!Settings.DockEnabled) return;
        var displays = Native.Displays.Current();
        var wanted = DockSettings.Displays == DockDisplays.All ? displays : displays.Where(d => d.Primary).Take(1).ToList();
        // No screen reported (mid-change): keep the docks there are rather than close them all with nothing to bring them back.
        if (wanted.Count == 0) return;
        foreach (var dock in _docks.Where(d => wanted.All(w => w.Device != d.Device || w.Handle != d.Monitor)).ToList()) { dock.Close(); _docks.Remove(dock); }
        foreach (var display in wanted.Where(w => _docks.All(d => d.Device != w.Device)))
        {
            var dock = new DockWindow(this, display.Handle, display.Device);
            _docks.Add(dock);
            dock.Show();
        }
        foreach (var dock in _docks) { dock.Place(); dock.ClaimPanicKey(); }
    }

    private void ApplyDockKeys()
    {
        if (DockSettings.AppShortcuts) _dockKeys ??= new DockKeys(index => _docks.FirstOrDefault()?.ActivateApp(index));
        else { _dockKeys?.Dispose(); _dockKeys = null; }
    }

    // The dock's own panic key (DockWindow): switches it off as the island's settings would.
    internal void TurnOffDock()
    {
        if (!Settings.DockEnabled) return;
        var copy = Settings.Copy(); copy.DockEnabled = false; UpdateSettings(copy);
    }

    // Takes effect at once; written to dock.json a moment after the last change, so a dragged slider does not write on every step.
    internal void UpdateDockSettings(DockSettings settings)
    {
        settings.Normalize();
        var previous = DockSettings;
        _dockSettings = settings;
        if (_dockSave is null)
        {
            _dockSave = new System.Windows.Threading.DispatcherTimer { Interval = DockSaveDelay };
            _dockSave.Tick += (_, _) => FlushDockSettings();
        }
        _dockSave.Stop(); _dockSave.Start();
        if (_docks.Count == 0) return;
        if (settings.TaskbarMode != previous.TaskbarMode) DockTaskbar.Apply(settings.TaskbarMode);
        if (settings.Displays != previous.Displays) SyncDocks();
        ApplyDockKeys();
        foreach (var dock in _docks) dock.Apply();
    }

    private void FlushDockSettings()
    {
        if (_dockSave is not { IsEnabled: true }) return;
        _dockSave.Stop();
        try { _dockStore!.Save(DockSettings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _recordCrash?.Invoke(ex); }
    }
}
