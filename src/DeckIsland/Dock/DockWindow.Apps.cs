using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// The dock's tiles: the Start button, pinned apps, running apps, then Trash and Show desktop. A dot sits under each open app, a
// wider one under the app in front. Left click starts, brings forward, cycles or minimizes (DockApps.Click); middle click opens
// another window; right click opens the app's menu; dragging reorders, dragging off the bar unpins, and files dropped on the bar
// are pinned (on Trash, recycled).
// shortcut: the windows are read once a second rather than from window events; upgrade to a WinEvent hook when the delay shows.
internal sealed partial class DockWindow
{
    private const double DragThreshold = 6;
    private readonly StackPanel _apps = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private List<DockApp> _shown = [];
    private List<string> _order = [];
    private string _signature = "";
    private (Border Tile, Point Start)? _press;
    private bool _dragging, _menuOpen;
    private double _tileSize, _spacing;

    private void RefreshApps()
    {
        // Not while a tile is held: rebuilding would take its mouse capture away.
        // Nor while the tray grid is open: it hangs from its arrow, which a rebuild would take away.
        if (_dragging || _press is not null || _trayPopup is { IsOpen: true }) return;
        var settings = _app.DockSettings;
        if (!settings.ImportedTaskbarPins)
        {
            ImportTaskbarPins();
            settings = _app.DockSettings;
        }
        var pins = PinKeys(settings);
        long foreground = DockAppWindows.Foreground;
        (var apps, _order) = DockApps.Build(pins, DockAppWindows.Collect(), _order, foreground, DockAppWindows.AppName);
        if (!settings.GroupWindows) apps = DockApps.Ungroup(apps, foreground);
        bool trashFull = settings.ShowTrash && DockShell.RecycleBinHasItems();
        var signature = $"{settings.Style}{settings.ClearCards}{Accent}{settings.CleanWidgets}{settings.Corner}|{settings.Size}|{settings.Spacing}|{settings.IconSize}|{settings.AppCards}{settings.NamesOnHover}{settings.Indicator}{settings.WindowPreviews}|"
            + $"{settings.ShowStart}{settings.ShowTrash}{settings.ShowDesktopButton}{trashFull}|{_app.DockPickerOpen}|{string.Join(",", settings.Widgets.Select(w => w.Id + w.Option))}|"
            + string.Join(";", apps.Select(a => $"{a.Key},{a.Pinned},{a.Windows.Count},{a.Active}"));
        _shown = apps;
        if (signature == _signature) return;
        _signature = signature;
        _apps.Children.Clear();
        double tile = _tileSize = settings.Size - 10;
        _spacing = settings.Spacing;
        if (settings.ShowStart) _apps.Children.Add(Button(StartGlyph(tile), Loc.T("开始", "Start"), tile, DockShell.OpenStart));
        foreach (var app in apps) _apps.Children.Add(Tile(app, tile));
        AddWidgets(settings, tile);
        if (settings.ShowTrash || settings.ShowDesktopButton)
            _apps.Children.Add(new Rectangle { Width = 1, Height = tile * 0.6, Fill = UI.Brush(_look.Divider), Margin = new Thickness(5, 0, 5, 0) });
        if (settings.ShowTrash) _apps.Children.Add(Trash(tile));
        if (settings.ShowDesktopButton) _apps.Children.Add(Button(DesktopGlyph(tile), Loc.T("显示桌面", "Show desktop"), tile, DockShell.ToggleDesktop));
        if (_handle != IntPtr.Zero && Math.Abs(NaturalWidth() - _barWidth) >= 1) Place();
    }

    private static List<(DockPin Pin, string Key)> PinKeys(DockSettings settings) => (settings.Pins ?? []).Select(pin => (pin, DockAppWindows.PinKey(pin))).ToList();

    private void ImportTaskbarPins()
    {
        var copy = _app.DockSettings.Copy();
        var keys = PinKeys(copy).Select(entry => entry.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        copy.Pins ??= [];
        copy.Pins.AddRange(DockAppWindows.TaskbarPins().Where(pin => keys.Add(DockAppWindows.PinKey(pin))));
        copy.ImportedTaskbarPins = true;
        _app.UpdateDockSettings(copy);
    }

    // Read again soon after an action, so the dot follows without waiting for the next second.
    private void RefreshSoon() => Dispatcher.BeginInvoke(() => { _signature = ""; RefreshApps(); }, System.Windows.Threading.DispatcherPriority.Background);

    private Border Frame(UIElement content, double tile, string name) => new()
    {
        Width = tile, Height = tile, CornerRadius = new CornerRadius(TileRadius()),
        BorderBrush = UI.Brush(Cards ? _look.CardRim : "#00000000"), BorderThickness = new Thickness(Cards ? 1 : 0),
        Margin = new Thickness(_spacing / 2, 0, _spacing / 2, 0), Child = content,
        Background = UI.Brush(Rest(false)), ToolTip = _app.DockSettings.NamesOnHover ? name : null, Cursor = Cursors.Hand,
        RenderTransformOrigin = new Point(0.5, _app.DockSettings.Edge == DockEdge.Top ? 0 : 1), RenderTransform = new ScaleTransform(1, 1)
    };

    // Clear always puts the tiles on cards; the other styles when App cards is on.
    private bool Cards => _app.DockSettings.AppCards || _app.DockSettings.Style == DockStyle.Clear;
    private string Rest(bool active) => active ? _look.CardActive : Cards ? _look.Card : "#00000000";

    private void Hover(Border frame, string rest)
    {
        frame.MouseEnter += (_, _) => frame.Background = UI.Brush(_look.CardHover);
        frame.MouseLeave += (_, _) => frame.Background = UI.Brush(rest);
    }

    private Border Button(UIElement glyph, string name, double tile, Action action)
    {
        var frame = Frame(glyph, tile, name);
        Hover(frame, Rest(false));
        // Taken here, so the bar does not start a drag to the other edge and catch the release.
        frame.MouseLeftButtonDown += (_, e) => e.Handled = true;
        frame.MouseLeftButtonUp += (_, e) => { action(); e.Handled = true; RefreshSoon(); };
        return frame;
    }

    private Border Trash(double tile)
    {
        double size = Math.Round(tile * DockGeometry.IconShare(_app.DockSettings.IconSize));
        var icon = new Image { Width = size, Height = size, Source = DockAppWindows.Icon(DockShell.RecycleBin, 64, cache: false) };
        var frame = Button(icon, Loc.T("回收站", "Recycle Bin"), tile, DockShell.OpenRecycleBin);
        frame.AllowDrop = true;
        frame.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        frame.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) DockShell.Recycle(files);
            e.Handled = true; RefreshSoon();
        };
        return frame;
    }

    private System.Windows.Shapes.Path StartGlyph(double tile)
    {
        double s = Math.Round(tile * 0.42);
        return new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,0 H 7 V 7 H 0 Z M 9,0 H 16 V 7 H 9 Z M 0,9 H 7 V 16 H 0 Z M 9,9 H 16 V 16 H 9 Z"),
            Fill = UI.Brush(_look.Text), Opacity = 0.85, Width = s, Height = s, Stretch = Stretch.Uniform };
    }

    private System.Windows.Shapes.Path DesktopGlyph(double tile)
    {
        double s = Math.Round(tile * 0.42);
        return new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1,2 H 15 V 12 H 1 Z M 5,15 H 11 M 8,12 V 15"), Stroke = UI.Brush(_look.Dim),
            StrokeThickness = 1.4, Width = s, Height = s, Stretch = Stretch.Uniform };
    }

    private Border Tile(DockApp app, double tile)
    {
        var settings = _app.DockSettings;
        double iconSize = Math.Round(tile * DockGeometry.IconShare(settings.IconSize));
        var icon = new Image { Width = iconSize, Height = iconSize, Source = DockAppWindows.Icon(app, 64) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        // The open marker (DockIndicator): a dot, a larger dot, or a line; wider under the app in front.
        var (dotWidth, dotHeight) = settings.Indicator switch
        {
            DockIndicator.LargeDot => (app.Active ? 14.0 : 6.0, 6.0),
            DockIndicator.Line => (app.Active ? tile * 0.6 : tile * 0.35, 2.5),
            _ => (app.Active ? 12.0 : 4.0, 3.0)
        };
        var dot = new Rectangle { Height = dotHeight, Width = dotWidth, RadiusX = dotHeight / 2, RadiusY = dotHeight / 2, Fill = UI.Brush(Accent), Opacity = app.Active ? 1 : 0.8,
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2),
            Visibility = app.Open ? Visibility.Visible : Visibility.Hidden };
        var grid = new Grid(); grid.Children.Add(icon); grid.Children.Add(dot);
        // App cards: every app on a card in the dock's shade; otherwise only the app in front is shaded.
        string rest = Rest(app.Active);
        var frame = Frame(grid, tile, app.Name);
        frame.Tag = app; frame.Background = UI.Brush(rest);
        // The preview shows the window titles, so the tooltip would only repeat them.
        if (app.Open && _app.DockSettings.WindowPreviews) frame.ToolTip = null;
        Hover(frame, rest);
        frame.MouseEnter += (_, _) => { if (!_dragging && _app.DockSettings.WindowPreviews) _preview.Show(Current(app), frame); };
        frame.MouseLeave += (_, _) => _preview.HideSoon();
        frame.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _press = (frame, e.GetPosition(this)); frame.CaptureMouse();
        };
        frame.MouseMove += (_, e) => Drag(frame, e.GetPosition(this));
        // A menu, Alt+Tab or a secure prompt can take the mouse mid-drag; the drag is then abandoned, never left half done.
        frame.LostMouseCapture += (_, _) =>
        {
            if (_press?.Tile != frame) return;
            _press = null; _dragging = false; frame.Opacity = 1; RefreshSoon();
        };
        frame.MouseLeftButtonUp += (_, e) => { Release(frame, Current(app), e.GetPosition(this)); e.Handled = true; };
        frame.MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            DockAppWindows.Launch(Current(app).Launch); e.Handled = true; RefreshSoon();
        };
        frame.ContextMenu = AppMenu(app);
        frame.ContextMenuClosing += (_, _) => _menuOpen = false;
        frame.ContextMenuOpening += (_, _) =>
        {
            _preview.HideNow(); _menuOpen = true;
            // The dock never takes focus, so its menu could not see a click elsewhere and would stay open; the dock takes the
            // foreground while its menu opens, as the taskbar does, and the menu closes when the click goes elsewhere.
            DockAppWindows.Focus(_handle);
            frame.ContextMenu = AppMenu(Current(app));
        };
        return frame;
    }

    private DockApp Current(DockApp app) => _shown.FirstOrDefault(a => a.Key == app.Key) ?? app;

    // While the button is held, a move past the threshold starts a drag: the tile follows the pointer along the apps.
    private void Drag(Border frame, Point at)
    {
        if (_press is not { } press || press.Tile != frame || _app.DockSettings.Locked) return;
        if (!_dragging)
        {
            if (Math.Abs(at.X - press.Start.X) < DragThreshold && Math.Abs(at.Y - press.Start.Y) < DragThreshold) return;
            _dragging = true; _preview.HideNow(); frame.Opacity = 0.6;
        }
        int index = _apps.Children.IndexOf(frame);
        // Apps change places with apps, widgets with widgets.
        foreach (var other in _apps.Children.OfType<Border>().Where(child => child != frame && child.Tag is not null && child.Tag.GetType() == frame.Tag?.GetType()).ToList())
        {
            double centre = other.TranslatePoint(new Point(other.ActualWidth / 2, 0), this).X;
            int otherIndex = _apps.Children.IndexOf(other);
            if ((otherIndex > index && at.X > centre) || (otherIndex < index && at.X < centre))
            {
                _apps.Children.Remove(frame); _apps.Children.Insert(otherIndex, frame);
                // Laid out now, so the next move compares against where the tiles are.
                _apps.UpdateLayout();
                return;
            }
        }
    }

    private void Release(Border frame, DockApp app, Point at)
    {
        // Only the release of a press that began on this tile counts (a menu may have taken the press).
        if (_press?.Tile != frame) return;
        bool dragged = _dragging;
        _press = null; _dragging = false; frame.Opacity = 1;
        frame.ReleaseMouseCapture();
        if (!dragged)
        {
            DockAppWindows.Act(app, DockApps.Click(app, DockAppWindows.Foreground));
            RefreshSoon();
            return;
        }
        // Dropped well away from the bar: a pin is removed, as dragging an icon off the dock does.
        bool off = at.Y < -ActualHeight || at.Y > ActualHeight * 2;
        var pins = PinKeys(_app.DockSettings);
        var copy = _app.DockSettings.Copy();
        if (off) { if (app.Pinned) copy.Pins = DockApps.Unpin(pins, app.Key); }
        else copy.Pins = DockApps.Arrange(pins, _apps.Children.OfType<Border>().Select(child => child.Tag).OfType<DockApp>().ToList());
        _app.UpdateDockSettings(copy);
        RefreshSoon();
    }

    // Files, folders and shortcuts dropped on the bar are pinned at the end.
    private void DropFiles(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var copy = _app.DockSettings.Copy();
        foreach (var path in files)
        {
            string name = Directory.Exists(path) ? new DirectoryInfo(path).Name : System.IO.Path.GetFileNameWithoutExtension(path);
            copy.Pins = DockApps.Pin(copy.Pins ?? [], new DockPin(path, name));
        }
        _app.UpdateDockSettings(copy);
        RefreshSoon();
    }

    // Win+1 to Win+0: the n-th app, as a click on it would.
    // Win+T: the open apps in the switcher.
    // Win+T again while it is open (Windows still held) moves to the next app.
    private DockSwitcher? _switcher;
    public void ShowSwitcher()
    {
        if (_switcher is not null) { _switcher.Next(); return; }
        _switcher = new DockSwitcher(_shown, _look, Accent);
        _switcher.Closed += (_, _) => _switcher = null;
        _switcher.Show();
    }

    public void ActivateApp(int index)
    {
        if (index >= _shown.Count) return;
        var app = _shown[index];
        DockAppWindows.Act(app, DockApps.Click(app, DockAppWindows.Foreground));
        RefreshSoon();
    }

    private ContextMenu AppMenu(DockApp app)
    {
        var menu = NewMenu();
        void Add(string header, Action action, ImageSource? icon = null)
        {
            var item = new MenuItem { Header = header };
            if (icon is not null) item.Icon = new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) };
            item.Click += (_, _) => { action(); RefreshSoon(); };
            menu.Items.Add(item);
        }
        // The app's jump list first, its sections under their own headings, as on the taskbar.
        foreach (var section in DockJumpLists.For(app))
        {
            menu.Items.Add(new MenuItem { Header = section.Title, IsEnabled = false, FontSize = 11.5 });
            foreach (var item in section.Items) Add(item.Title, item.Open, ShellIcons.ForItem(item.Target, 16));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(new MenuItem { Header = app.Name, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        Add(app.Open ? Loc.T("新窗口", "New window") : Loc.T("打开", "Open"), () => DockAppWindows.Launch(app.Launch));
        if (_app.DockSettings.Locked) { }
        else if (app.Pinned) Add(Loc.T("从 Dock 取消固定", "Unpin from dock"), () => ChangePins(pins => DockApps.Unpin(pins, app.Key)));
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
        copy.Pins = edit(PinKeys(copy));
        _app.UpdateDockSettings(copy);
    }

    // Magnification: tiles near the pointer grow towards the screen's middle, the nearest by MagnificationSize percent, easing at
    // the chosen speed. They grow over the bar rather than pushing it wider, so the bar keeps still.
    private void Magnify(Point? at)
    {
        var settings = _app.DockSettings;
        if (!settings.Magnification && at is not null) return;
        var duration = DockGeometry.Duration(settings.MagnificationSpeed);
        foreach (var tile in _apps.Children.OfType<Border>())
        {
            double scale = 1;
            if (at is { } point && settings.Magnification && !_dragging)
            {
                double centre = tile.TranslatePoint(new Point(tile.ActualWidth / 2, 0), this).X;
                scale = DockGeometry.Magnify(point.X - centre, (_tileSize + _spacing) * 2.5, settings.MagnificationSize);
            }
            if (tile.RenderTransform is not ScaleTransform transform) continue;
            var animation = new System.Windows.Media.Animation.DoubleAnimation(scale, duration) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase() };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }
}
