using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeckIsland.Core;
using DeckIsland.Native;
using ManagedShell.WindowsTray;
using Path = System.Windows.Shapes.Path;

namespace DeckIsland;

// The Tray widget, after lumabar's: one arrow on the dock that opens the system tray's icons in a grid (App.DockTray). Clicks on an
// icon reach its app as they would on the taskbar (NotifyIcon.IconMouseDown/Up, as RetroBar sends them), its menus included.
internal sealed partial class DockWindow
{
    private const int TrayColumns = 6;
    private Popup? _trayPopup;

    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    private (FrameworkElement, Action) TrayArrow(double tile)
    {
        bool top = _app.DockSettings.Edge == DockEdge.Top;
        var arrow = new Path { Data = Geometry.Parse(top ? "M 0,0 L 5,5 L 10,0" : "M 0,5 L 5,0 L 10,5"), Stroke = UI.Brush(_look.Text), StrokeThickness = 1.6,
            Width = tile * 0.24, Height = tile * 0.12, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var area = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = Loc.T("显示隐藏的图标", "Show hidden icons") };
        area.Children.Add(arrow);
        area.MouseLeftButtonDown += (_, e) => e.Handled = true;
        area.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleTray(area); };
        return (area, () => { });
    }

    private void ToggleTray(FrameworkElement anchor)
    {
        if (_trayPopup is { IsOpen: true }) { _trayPopup.IsOpen = false; return; }
        if (_app.DockTray is not { } tray)
        {
            _app.DockAlert("dock-tray", Loc.T("无法读取托盘图标", "The tray icons could not be read"), null);
            return;
        }
        bool top = _app.DockSettings.Edge == DockEdge.Top;
        bool light = _look.Text == "#FF1D1D1F";
        var grid = new UniformGrid { Columns = TrayColumns };
        foreach (var icon in VisibleTrayIcons(tray.AllIcons.OfType<NotifyIcon>())) grid.Children.Add(TrayIcon(icon));
        if (grid.Children.Count == 0)
            grid.Children.Add(new TextBlock { Text = Loc.T("没有托盘图标", "No tray icons"), Foreground = UI.Brush(_look.Dim), Margin = new Thickness(8) });
        _trayPopup = new Popup
        {
            PlacementTarget = anchor, Placement = top ? PlacementMode.Bottom : PlacementMode.Top, StaysOpen = true, AllowsTransparency = true,
            VerticalOffset = top ? 10 : -10, HorizontalOffset = 0,
            Child = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(8), Child = grid,
                Background = UI.Brush(light ? "#FAF8F8FA" : "#F51E1E21"), BorderBrush = UI.Brush(light ? "#1A000000" : "#1FFFFFFF"), BorderThickness = new Thickness(1) }
        };
        // The grid stays up while an icon's own menu is open (a menu window, class #32768, in front), and closes once anything else
        // comes to the front or the pointer is pressed outside it and outside that menu.
        var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        var popup = _trayPopup;
        watch.Tick += (_, _) => { if (!TrayStillWanted(popup)) popup.IsOpen = false; };
        popup.Opened += (_, _) => { _menuOpen = true; _trayAway = 0; watch.Start(); };
        popup.Closed += (_, _) => { _menuOpen = false; watch.Stop(); };
        DockAppWindows.Focus(_handle);
        popup.IsOpen = true;
    }

    private static IEnumerable<NotifyIcon> VisibleTrayIcons(IEnumerable<NotifyIcon> icons)
    {
        var callbacks = new HashSet<(nint Window, uint Id)>();
        var guids = new HashSet<Guid>();
        bool securityShown = false;
        var visible = new List<NotifyIcon>();
        // Explorer's imports can leave older registrations behind when the live tray host receives a replacement.
        foreach (var icon in icons.Reverse())
        {
            if (icon.Icon is null || GetWindowThreadProcessId(icon.HWnd, out int pid) == 0) continue;
            if (callbacks.Contains((icon.HWnd, icon.UID)) || (icon.GUID != Guid.Empty && guids.Contains(icon.GUID))) continue;
            bool security = string.Equals(System.IO.Path.GetFileName(AppIdentity.ImagePath(pid)), "SecurityHealthSystray.exe", StringComparison.OrdinalIgnoreCase);
            if (security && securityShown) continue;
            securityShown |= security;
            callbacks.Add((icon.HWnd, icon.UID));
            if (icon.GUID != Guid.Empty) guids.Add(icon.GUID);
            visible.Add(icon);
        }
        visible.Reverse();
        return visible;
    }

    private FrameworkElement TrayIcon(NotifyIcon icon)
    {
        var image = new Image { Source = icon.Icon, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var cell = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = UI.Brush("#00000000"), Child = image, ToolTip = icon.Title };
        cell.MouseEnter += (_, _) => { cell.Background = UI.Brush(_look.Text == "#FF1D1D1F" ? "#17000000" : "#24FFFFFF"); Place(icon, cell); icon.IconMouseEnter(CursorParam()); };
        cell.MouseLeave += (_, _) => { cell.Background = UI.Brush("#00000000"); icon.IconMouseLeave(CursorParam()); };
        cell.MouseMove += (_, _) => icon.IconMouseMove(CursorParam());
        cell.MouseDown += (_, e) => { e.Handled = true; Place(icon, cell); icon.IconMouseDown(e.ChangedButton, CursorParam(), (int)GetDoubleClickTime()); };
        cell.MouseUp += (_, e) =>
        {
            e.Handled = true;
            icon.IconMouseUp(e.ChangedButton, CursorParam(), (int)GetDoubleClickTime());
            // A left click opens the app's window or flyout; the grid makes way for it. A right click's menu opens over the grid.
            if (e.ChangedButton == MouseButton.Left && _trayPopup is not null) _trayPopup.IsOpen = false;
        };
        return cell;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(CursorPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }

    private int _trayAway;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    private bool TrayStillWanted(Popup popup)
    {
        // An app opens its tray menu from a hidden window of its own, which takes the foreground; the menu itself (class #32768) is
        // what shows. While any menu is up the grid stays.
        if (IsWindowVisible(FindWindow("#32768", null))) { _trayAway = 0; return true; }
        var front = GetForegroundWindow();
        GetWindowThreadProcessId(front, out var owner);
        // Something else in front for a moment (the app's window opening its menu) is waited out.
        if (owner != Environment.ProcessId && front != IntPtr.Zero) return ++_trayAway < 4;
        _trayAway = 0;
        // A press (left or right button) outside the grid and outside any menu closes it.
        if ((GetAsyncKeyState(0x01) & 0x8000) == 0 && (GetAsyncKeyState(0x02) & 0x8000) == 0) return true;
        GetCursorPos(out var at);
        var under = GetAncestor(WindowFromPoint(new CursorPoint { X = at.X, Y = at.Y }), 2);
        GetWindowThreadProcessId(under, out var underOwner);
        return underOwner == Environment.ProcessId || ClassOf(under) == "#32768";
    }

    private static string ClassOf(IntPtr window) { var name = new System.Text.StringBuilder(32); GetClassName(window, name, name.Capacity); return name.ToString(); }

    // Where the icon is, in screen pixels, so the app opens its menu or flyout beside it.
    private static void Place(NotifyIcon icon, FrameworkElement cell)
    {
        if (PresentationSource.FromVisual(cell) is null) return;
        var corner = cell.PointToScreen(new Point(0, 0));
        var far = cell.PointToScreen(new Point(cell.ActualWidth, cell.ActualHeight));
        icon.Placement = new ManagedShell.Interop.NativeMethods.Rect { Left = (int)corner.X, Top = (int)corner.Y, Right = (int)(far.X - corner.X), Bottom = (int)(far.Y - corner.Y) };
    }

    // The cursor as a window message packs it: x in the low word, y in the high word.
    private static uint CursorParam()
    {
        GetCursorPos(out var point);
        return (uint)((point.Y & 0xFFFF) << 16 | (point.X & 0xFFFF));
    }
}
