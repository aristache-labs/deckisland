using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// The dock's app tiles: pinned apps, then running apps; a dot under each open app, a wider one under the app in front.
// Left click starts, brings forward, cycles or minimizes (DockApps.Click); middle click opens another window; right click opens
// the app's menu.
// shortcut: the windows are read once a second rather than from window events; upgrade to a WinEvent hook when the delay shows.
internal sealed partial class DockWindow
{
    private readonly StackPanel _apps = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
    private List<DockApp> _shown = [];
    private List<string> _order = [];
    private string _signature = "";

    private void RefreshApps()
    {
        var settings = _app.DockSettings;
        if (settings.Pins is null)
        {
            var seeded = settings.Copy(); seeded.Pins = DockAppWindows.TaskbarPins();
            _app.UpdateDockSettings(seeded);
            settings = _app.DockSettings;
        }
        var pins = settings.Pins!.Select(pin => (pin, DockAppWindows.PinKey(pin))).ToList();
        (var apps, _order) = DockApps.Build(pins, DockAppWindows.Collect(), _order, DockAppWindows.Foreground, DockAppWindows.AppName);
        var signature = settings.Size + "|" + string.Join(";", apps.Select(a => $"{a.Key},{a.Pinned},{a.Windows.Count},{a.Active}"));
        _shown = apps;
        if (signature == _signature) return;
        _signature = signature;
        _apps.Children.Clear();
        foreach (var app in apps) _apps.Children.Add(Tile(app, settings.Size));
        if (_handle != IntPtr.Zero && Math.Abs(NaturalWidth() - _barWidth) >= 1) Place();
    }

    // Read again soon after an action, so the dot follows without waiting for the next second.
    private void RefreshSoon() => Dispatcher.BeginInvoke(() => { _signature = ""; RefreshApps(); }, System.Windows.Threading.DispatcherPriority.Background);

    private FrameworkElement Tile(DockApp app, int barHeight)
    {
        double tile = barHeight - 10, iconSize = Math.Round(tile * 0.7);
        var icon = new Image { Width = iconSize, Height = iconSize, Source = DockAppWindows.Icon(app.Pinned ? app.Launch : app.Windows[0].Exe, 64) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var dot = new Rectangle { Height = 3, Width = app.Active ? 12 : 4, RadiusX = 1.5, RadiusY = 1.5, Fill = UI.Brush(app.Active ? "#F2F2F4" : "#99F2F2F4"),
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2),
            Visibility = app.Open ? Visibility.Visible : Visibility.Hidden };
        var grid = new Grid(); grid.Children.Add(icon); grid.Children.Add(dot);
        var frame = new Border { Width = tile, Height = tile, CornerRadius = new CornerRadius(7), Margin = new Thickness(2, 0, 2, 0), Child = grid,
            Background = UI.Brush(app.Active ? "#1FFFFFFF" : "#00FFFFFF"), ToolTip = app.Name, Cursor = Cursors.Hand };
        frame.MouseEnter += (_, _) => { if (!app.Active) frame.Background = UI.Brush("#14FFFFFF"); };
        frame.MouseLeave += (_, _) => frame.Background = UI.Brush(app.Active ? "#1FFFFFFF" : "#00FFFFFF");
        frame.MouseUp += (_, e) =>
        {
            var current = _shown.FirstOrDefault(a => a.Key == app.Key) ?? app;
            if (e.ChangedButton == MouseButton.Left) DockAppWindows.Act(current, DockApps.Click(current, DockAppWindows.Foreground));
            else if (e.ChangedButton == MouseButton.Middle) DockAppWindows.Launch(current.Launch);
            else return;
            e.Handled = true; RefreshSoon();
        };
        frame.ContextMenu = AppMenu(app);
        frame.ContextMenuOpening += (_, _) =>
        {
            // The dock never takes focus, so its menu could not see a click elsewhere and would stay open; the dock takes the
            // foreground while its menu opens, as the taskbar does, and the menu closes when the click goes elsewhere.
            DockAppWindows.Focus(_handle);
            frame.ContextMenu = AppMenu(_shown.FirstOrDefault(a => a.Key == app.Key) ?? app);
        };
        return frame;
    }

    private ContextMenu AppMenu(DockApp app)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => { action(); RefreshSoon(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuItem { Header = app.Name, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        Add(app.Open ? Loc.T("新窗口", "New window") : Loc.T("打开", "Open"), () => DockAppWindows.Launch(app.Launch));
        if (app.Pinned) Add(Loc.T("从 Dock 取消固定", "Unpin from dock"), () => ChangePins(pins => DockApps.Unpin(pins, app.Key)));
        else Add(Loc.T("固定到 Dock", "Pin to dock"), () => ChangePins(pins => DockApps.Pin(pins.Select(p => p.Pin).ToList(), new DockPin(app.Launch, app.Name))));
        var file = app.Open ? app.Windows[0].Exe : app.Launch;
        Add(Loc.T("打开文件位置", "Open file location"), () => DockAppWindows.OpenFileLocation(file));
        if (app.Open)
        {
            menu.Items.Add(new Separator());
            Add(app.Windows.Count > 1 ? Loc.T("关闭所有窗口", "Close all windows") : Loc.T("关闭窗口", "Close window"),
                () => { foreach (var window in app.Windows) DockAppWindows.Close((nint)window.Handle); });
            Add(Loc.T("结束任务", "End task"), () => DockAppWindows.EndTask(app));
        }
        return menu;
    }

    private void ChangePins(Func<List<(DockPin Pin, string Key)>, List<DockPin>> edit)
    {
        var copy = _app.DockSettings.Copy();
        copy.Pins = edit((copy.Pins ?? []).Select(pin => (pin, DockAppWindows.PinKey(pin))).ToList());
        _app.UpdateDockSettings(copy);
    }
}
