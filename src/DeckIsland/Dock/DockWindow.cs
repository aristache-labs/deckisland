using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// DeckIsland Dock's bar on one screen: a borderless window on the top or bottom edge, registered with the shell as an AppBar so
// maximized windows stop at its strip (after lumabar's dock.rs), unless it hides itself. It never takes focus and stays out of
// Alt+Tab. Dragging the bar by an empty spot into the other half of the screen moves it to that edge. Ctrl+Alt+Shift+Q switches
// the dock off from anywhere, which gives the taskbar back.
internal sealed partial class DockWindow : Window
{
    private const int GwlExStyle = -20, ExNoActivate = 0x08000000, ExToolWindow = 0x80;
    private const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3, AbeTop = 1, AbeBottom = 3;
    private const int AbnPosChanged = 1, AbnFullscreenApp = 2;
    private const int WmEndSession = 0x16, WmDisplayChange = 0x7E, WmDpiChanged = 0x2E0, WmHotkey = 0x312;
    private const int PanicHotkey = 0x4442;
    private const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModNoRepeat = 0x4000, VkQ = 0x51;
    private const uint SwpNoMove = 0x2, SwpNoSize = 0x1, SwpNoActivate = 0x10, SwpShowWindow = 0x40, SwpHideWindow = 0x80;
    private const double SnapDistance = 30;
    private static readonly IntPtr HwndTopmost = new(-1), HwndBottom = new(1);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(700), Slide = TimeSpan.FromMilliseconds(160);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size; public IntPtr Window; public uint CallbackMessage; public uint Edge;
        public int Left, Top, Right, Bottom; public IntPtr Param;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point32 { public int X, Y; }
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point32 point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int capacity);

    private readonly App _app;
    private readonly Grid _root = new();
    private readonly Border _bar;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _edgeWatch = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DockPreview _preview;
    private IntPtr _handle;
    private uint _callbackMessage, _taskbarCreated;
    private bool _registered, _fullscreen;
    private double _barWidth;
    private PixelRect _window, _screen;
    // Auto-hide: how far the bar is shown (1 in place, 0 away), where it is sliding to, and since when nothing has kept it up.
    private double _reveal = 1, _target = 1, _slideStart;
    private DateTime _unneededSince = DateTime.MaxValue, _slideFrom;
    private Point? _snapPress;
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private readonly DockRow _dockRow = new();

    // The screen this dock is on (an HMONITOR), and its GDI name, by which App.SyncDocks knows it.
    public IntPtr Monitor { get; }
    public string Device { get; }

    public DockWindow(App app, IntPtr monitor, string device, bool previewOnly = false)
    {
        _app = app; Monitor = monitor; Device = device;
        _preview = new DockPreview(() => _app.DockSettings.Edge);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false;
        Title = "DeckIsland Dock"; FontFamily = UI.PanelFont; Foreground = UI.Brush("#F2F2F4");
        _glass.Tick += (_, _) => GlassTick();
        _dockRow.Children.Add(_apps);
        _dockRow.Children.Add(_trailing);
        // The bar shows the screen behind it (glass styles) or its solid colour; the tint over it carries the style's colour.
        _tint.Child = _dockRow;
        _bar = new Border { BorderThickness = new Thickness(1), MinWidth = DockGeometry.MinBarWidth, Child = _tint };
        _bar.ContextMenu = BarMenu();
        _bar.SizeChanged += (_, _) => UpdateGlassRim();
        _bar.ContextMenuOpening += (_, e) =>
        {
            // A tile's own menu opens over a tile (WPF opens the nearest menu); the bar's only needs focus on its empty parts.
            if (e.OriginalSource is DependencyObject source && FindTile(source)) return;
            _preview.HideNow(); _menuOpen = true; DockAppWindows.Focus(_handle);
        };
        _bar.ContextMenuClosing += (_, _) => _menuOpen = false;
        _root.Children.Add(_bar);
        _root.Children.Add(_dropMarker);
        Content = _root;
        PreviewMouseMove += (_, e) => MoveWidgetDrag(e);
        _bar.AllowDrop = true;
        _bar.Drop += (_, e) => { if (!_app.DockSettings.Locked) DropFiles(e); };
        _bar.MouseLeftButtonDown += (_, e) =>
        {
            if (_app.DockSettings.Locked || _press is not null) return;
            _snapPress = e.GetPosition(this);
            _bar.CaptureMouse();
        };
        _bar.MouseLeftButtonUp += (_, _) => Snap();
        _bar.LostMouseCapture += (_, _) => _snapPress = null;
        _apps.MouseMove += (_, e) => Magnify(e.GetPosition(this));
        _apps.MouseLeave += (_, _) => Magnify(null);
        _tick.Tick += (_, _) => Tick();
        _edgeWatch.Tick += (_, _) => WatchEdge();
        SourceInitialized += (_, _) => Start();
        // Closing, not Closed: the AppBar must be removed while its window still exists.
        Closing += (_, _) => Stop();
        ApplyStyle();
        ApplyLook();
        if (!previewOnly) Tick();
    }

    private void Tick()
    {
        var settings = _app.DockSettings;
        UpdateWidgets();
        _widgetPopupUpdate?.Invoke();
        // Windows' energy saver: the dock reads its windows less often, unless the clock shows seconds.
        _tick.Interval = TimeSpan.FromSeconds(!settings.ClockSeconds && EnergySaverOn() ? 3 : 1);
        RefreshApps();
    }

    private static bool EnergySaverOn()
    {
        try { return Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On; }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return false; }
    }

    private double NaturalWidth()
    {
        _bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _bar.DesiredSize.Width;
    }

    private sealed class DockRow : Panel
    {
        public bool FullWidth { get; set; }

        protected override Size MeasureOverride(Size available)
        {
            foreach (UIElement child in Children) child.Measure(new Size(double.PositiveInfinity, available.Height));
            var main = Children[0].DesiredSize;
            var end = Children[1].DesiredSize;
            double width = main.Width + end.Width;
            return new Size(FullWidth ? Math.Min(width, available.Width) : width, Math.Max(main.Height, end.Height));
        }

        protected override Size ArrangeOverride(Size bounds)
        {
            double main = Children[0].DesiredSize.Width;
            double end = Children[1].DesiredSize.Width;
            double endX = FullWidth ? Math.Max(0, bounds.Width - end) : main;
            double available = Math.Min(main, endX);
            double x = FullWidth ? Math.Clamp((bounds.Width - main) / 2, 0, endX - available) : 0;
            Children[0].Arrange(new Rect(x, 0, available, bounds.Height));
            Children[1].Arrange(new Rect(endX, 0, end, bounds.Height));
            return bounds;
        }
    }

    private void Start()
    {
        _handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(_handle, GwlExStyle, GetWindowLong(_handle, GwlExStyle) | ExNoActivate | ExToolWindow);
        HwndSource.FromHwnd(_handle)?.AddHook(Hook);
        _callbackMessage = RegisterWindowMessage("DeckIsland.Dock.AppBarCallback");
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        ClaimPanicKey();
        Place();
        ApplyLook();
        _tick.Start(); _edgeWatch.Start();
    }

    // One dock holds the key; on the others the registration fails and is not needed. App.SyncDocks asks every dock again after
    // closing one, so the key is never left without a holder.
    public void ClaimPanicKey()
    {
        if (_handle != IntPtr.Zero) RegisterHotKey(_handle, PanicHotkey, ModControl | ModAlt | ModShift | ModNoRepeat, VkQ);
    }

    private void Stop()
    {
        CloseWidgetDetails();
        _tick.Stop(); _edgeWatch.Stop(); _glass.Stop();
        _volume?.Dispose(); _volume = null;
        _preview.Close();
        if (_handle == IntPtr.Zero) return;
        UnregisterHotKey(_handle, PanicHotkey);
        Unregister();
    }

    private AppBarData Data() => new() { Size = Marshal.SizeOf<AppBarData>(), Window = _handle, CallbackMessage = _callbackMessage };

    private void Register()
    {
        if (_registered) return;
        var data = Data(); _registered = SHAppBarMessage(AbmNew, ref data) != UIntPtr.Zero;
    }

    private void Unregister()
    {
        if (!_registered) return;
        var data = Data(); SHAppBarMessage(AbmRemove, ref data); _registered = false;
    }

    // After the dock's settings change: its look, its place and its tiles follow.
    // keepPanels: a change made from inside a panel (the Quick Toggles list) leaves it open.
    public void Apply(bool keepPanels = false)
    {
        if (!keepPanels) CloseWidgetDetails();
        ApplyStyle();
        ApplyLook();
        if (!_app.DockSettings.AutoHide) _target = _reveal = 1;
        Place();
        RefreshSoon();
    }

    // The bar's height with its rim: the rim is drawn inside the bar, so it is added on top of the padded row instead of eating into it.
    private double OuterBarHeight => _app.DockSettings.BarHeight + 2 * (_bar.BorderThickness.Top + _tint.BorderThickness.Top);

    // The bar's shape from the settings: corners, the space inside its ends, and which side of the window it sits on.
    private void ApplyStyle()
    {
        var settings = _app.DockSettings;
        _dockRow.FullWidth = settings.FullWidth;
        _dockRow.InvalidateArrange();
        _bar.Height = OuterBarHeight;
        _bar.CornerRadius = _tint.CornerRadius = new CornerRadius(settings.FullWidth ? 0 : DockGeometry.CornerRadius(settings.Corner, settings.BarHeight));
        _tint.Padding = new Thickness(settings.Padding);
        // Each tile carries half the spacing on both sides; pulled back here so the spacing falls only between tiles, never at the ends.
        _dockRow.Margin = new Thickness(-settings.Spacing / 2.0, 0, -settings.Spacing / 2.0, 0);
        _bar.VerticalAlignment = settings.Edge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    }

    // Asks the shell for the strip on the chosen edge of this screen (none while the dock hides itself), then puts the bar in it.
    public void Place()
    {
        if (_handle == IntPtr.Zero) return;
        var settings = _app.DockSettings;
        var (bounds, _, scale) = WindowsHost.MonitorScreen(Monitor) ?? WindowsHost.PrimaryScreen();
        _screen = new PixelRect((int)bounds.Left, (int)bounds.Top, (int)bounds.Right, (int)bounds.Bottom);
        int distance = settings.FullWidth ? 0 : settings.Distance;
        int stripHeight = (int)Math.Round((settings.FullWidth ? OuterBarHeight : DockGeometry.StripHeight((int)Math.Ceiling(OuterBarHeight)) + distance) * scale);
        var strip = DockGeometry.Strip(_screen, settings.Edge, stripHeight);
        if (settings.AutoHide) Unregister();
        else
        {
            Register();
            if (_registered)
            {
                var data = Data();
                data.Edge = settings.Edge == DockEdge.Top ? AbeTop : AbeBottom;
                (data.Left, data.Top, data.Right, data.Bottom) = (strip.Left, strip.Top, strip.Right, strip.Bottom);
                SHAppBarMessage(AbmQueryPos, ref data);
                strip = DockGeometry.KeepHeight(new PixelRect(data.Left, data.Top, data.Right, data.Bottom), settings.Edge, stripHeight);
                (data.Left, data.Top, data.Right, data.Bottom) = (strip.Left, strip.Top, strip.Right, strip.Bottom);
                SHAppBarMessage(AbmSetPos, ref data);
                strip = new PixelRect(data.Left, data.Top, data.Right, data.Bottom);
            }
        }
        _barWidth = NaturalWidth();
        // The width is laid out at the window's own DPI, which may still be another monitor's until WM_DPICHANGED.
        var bar = DockGeometry.Bar(strip, settings.Edge, (int)Math.Ceiling(_barWidth * VisualTreeHelper.GetDpi(this).DpiScaleX),
            (int)Math.Round(OuterBarHeight * scale), (int)Math.Round(distance * scale), settings.FullWidth);
        int extra = settings.Magnification ? (int)Math.Ceiling(settings.Size * settings.MagnificationSize / 100.0 * scale) + 4 : 0;
        _window = DockGeometry.Window(bar, settings.Edge, extra);
        Move();
    }

    // Puts the window where it is, or part of the way out of the screen while it slides away; hidden once all the way out.
    private void Move()
    {
        if (_reveal <= 0) { SetWindowPos(_handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpHideWindow); GlassTick(); return; }
        bool top = _app.DockSettings.Edge == DockEdge.Top;
        int away = (int)Math.Round((1 - _reveal) * (top ? _window.Bottom - _screen.Top : _screen.Bottom - _window.Top));
        SetWindowPos(_handle, _fullscreen ? HwndBottom : HwndTopmost, _window.Left, _window.Top + (top ? -away : away), _window.Width, _window.Height, SwpNoActivate | SwpShowWindow);
        GlassTick();
    }

    // Auto-hide: shown while the pointer is on it or at the screen edge, while a menu, preview or drag is in use, and (Show on
    // desktop) while nothing but the desktop is in front; slides away once none of these has held for a moment.
    private void WatchEdge()
    {
        var settings = _app.DockSettings;
        if (!settings.AutoHide || _handle == IntPtr.Zero) return;
        GetCursorPos(out var cursor);
        bool inScreen = cursor.X >= _screen.Left && cursor.X < _screen.Right;
        bool atEdge = inScreen && (settings.Edge == DockEdge.Top ? cursor.Y <= _screen.Top + 1 : cursor.Y >= _screen.Bottom - 2);
        bool over = _reveal > 0 && cursor.X >= _window.Left && cursor.X < _window.Right && cursor.Y >= _window.Top - 6 && cursor.Y < _window.Bottom + 6;
        bool busy = _dragging || _press is not null || _snapPress is not null || _preview.IsVisible || _menuOpen || _widgetPopup is { IsOpen: true };
        bool needed = !_fullscreen && (atEdge || over || busy || (settings.ShowOnDesktop && DesktopInFront()));
        var now = DateTime.Now;
        if (needed) _unneededSince = DateTime.MaxValue;
        else if (_unneededSince == DateTime.MaxValue) _unneededSince = now;
        double target = needed || now - _unneededSince < HideDelay ? 1 : 0;
        if (target != _target) { _target = target; _slideFrom = now; _slideStart = _reveal; }
        if (_reveal == _target) return;
        double progress = Math.Clamp((now - _slideFrom) / Slide, 0, 1);
        _reveal = progress >= 1 ? _target : _slideStart + (_target - _slideStart) * (1 - Math.Pow(1 - progress, 3));
        Move();
    }

    private static bool DesktopInFront()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;
        var name = new System.Text.StringBuilder(32); GetClassName(foreground, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW";
    }

    // The bar dragged by an empty spot and let go in the other half of the screen moves to that edge.
    private void Snap()
    {
        if (_snapPress is not { } press) return;
        _snapPress = null;
        _bar.ReleaseMouseCapture();
        GetCursorPos(out var cursor);
        var at = PointFromScreen(new Point(cursor.X, cursor.Y));
        if (Math.Abs(at.Y - press.Y) < SnapDistance) return;
        var edge = cursor.Y < (_screen.Top + _screen.Bottom) / 2 ? DockEdge.Top : DockEdge.Bottom;
        if (edge == _app.DockSettings.Edge) return;
        var copy = _app.DockSettings.Copy(); copy.Edge = edge; _app.UpdateDockSettings(copy);
    }

    private IntPtr Hook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_callbackMessage != 0 && message == _callbackMessage)
        {
            switch ((int)wParam)
            {
                case AbnPosChanged: Place(); break;
                // A fullscreen app covers the screen: the dock drops behind it and comes back after.
                case AbnFullscreenApp:
                    _fullscreen = lParam != IntPtr.Zero; Topmost = !_fullscreen;
                    SetWindowPos(_handle, _fullscreen ? HwndBottom : HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                    break;
            }
            handled = true;
        }
        else if (_taskbarCreated != 0 && message == _taskbarCreated)
        {
            // Explorer restarted and forgot every AppBar.
            DockTaskbar.TaskbarRecreated();
            _registered = false; Place();
        }
        else if (message == WmEndSession && wParam != IntPtr.Zero) DockTaskbar.Restore();
        else if (message == WmHotkey && (int)wParam == PanicHotkey) { _app.TurnOffDock(); handled = true; }
        else if (message == WmDisplayChange) Dispatcher.BeginInvoke(_app.SyncDocks, DispatcherPriority.Background);
        else if (message == WmDpiChanged) Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
        return IntPtr.Zero;
    }
}
