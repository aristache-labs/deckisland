using DeckIsland.Core;
using ManagedShell.WindowsTray;

namespace DeckIsland;

// The system tray's icons for DeckIsland Dock's Tray widget, gathered by ManagedShell (as RetroBar does): a tray host of its own
// takes the icons apps add with Shell_NotifyIcon, reads the ones Explorer already holds, and forwards everything else to Explorer.
// It runs only while a dock shows the Tray widget, since it puts a Shell_TrayWnd of its own in this process.
public partial class App
{
    private TrayService? _trayService;
    private NotificationArea? _dockTray;

    // Null while no Tray widget is on the dock, or when the tray host could not start.
    internal NotificationArea? DockTray => _dockTray is { IsFailed: false } tray ? tray : null;

    private bool DockWantsTray => _docks.Count > 0 && DockSettings.Widgets.Any(w => w.Kind == DockWidgetKind.Tray);

    private void ApplyDockTray()
    {
        if (DockWantsTray && _dockTray is null)
        {
            _trayService = new TrayService();
            _dockTray = new NotificationArea(NotificationArea.DEFAULT_PINNED, _trayService, new ExplorerTrayService());
            _dockTray.Initialize();
            if (_dockTray.IsFailed) _recordCrash?.Invoke(new InvalidOperationException("The dock's tray host could not start."));
        }
        else if (!DockWantsTray && _dockTray is not null)
        {
            _dockTray.Dispose();
            _dockTray = null; _trayService = null;
        }
    }
}
