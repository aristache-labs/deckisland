using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// DeckIsland Dock's bar: a borderless window on the top or bottom edge of the main screen, registered with the shell as an AppBar
// so maximized windows stop at its strip (after lumabar's dock.rs). It never takes focus and stays out of Alt+Tab.
// Ctrl+Alt+Shift+Q switches the dock off from anywhere, which gives the taskbar back.
internal sealed partial class DockWindow : Window
{
    private const int GwlExStyle = -20, ExNoActivate = 0x08000000, ExToolWindow = 0x80;
    private const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3, AbeTop = 1, AbeBottom = 3;
    private const int AbnPosChanged = 1, AbnFullscreenApp = 2;
    private const int WmEndSession = 0x16, WmDisplayChange = 0x7E, WmDpiChanged = 0x2E0, WmHotkey = 0x312;
    private const int PanicHotkey = 0x4442;
    private const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModNoRepeat = 0x4000, VkQ = 0x51;
    private const uint SwpNoMove = 0x2, SwpNoSize = 0x1, SwpNoActivate = 0x10, SwpShowWindow = 0x40;
    private static readonly IntPtr HwndTopmost = new(-1), HwndBottom = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size; public IntPtr Window; public uint CallbackMessage; public uint Edge;
        public int Left, Top, Right, Bottom; public IntPtr Param;
    }
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

    private readonly App _app;
    private readonly Border _bar;
    private readonly TextBlock _time = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _date = new() { FontSize = 11, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private IntPtr _handle;
    private uint _callbackMessage, _taskbarCreated;
    private bool _registered, _fullscreen;
    private double _barWidth;

    public DockWindow(App app)
    {
        _app = app;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false;
        Title = "DeckIsland Dock"; FontFamily = UI.PanelFont; Foreground = UI.Brush("#F2F2F4");
        var clock = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 14, 0) };
        clock.Children.Add(_time); clock.Children.Add(_date);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_apps); row.Children.Add(clock);
        // The bar's look until the styles of phase F4: lumabar's solid dark surface (rgba 22, 22, 26, 0.92), 8 px corners.
        _bar = new Border { CornerRadius = new CornerRadius(8), Background = UI.Brush("#EB16161A"), BorderBrush = UI.Brush("#22FFFFFF"),
            BorderThickness = new Thickness(1), MinWidth = DockGeometry.MinBarWidth, Child = row, HorizontalAlignment = HorizontalAlignment.Stretch };
        Content = _bar;
        _clock.Tick += (_, _) => Tick();
        SourceInitialized += (_, _) => Start();
        // Closing, not Closed: the AppBar must be removed while its window still exists.
        Closing += (_, _) => Stop();
        Tick();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        _time.Text = now.ToString("t"); _date.Text = now.ToString("d");
        RefreshApps();
        // The clock's width changes with its text; the bar follows.
        if (_handle != IntPtr.Zero && Math.Abs(NaturalWidth() - _barWidth) >= 1) Place();
    }

    private double NaturalWidth()
    {
        _bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _bar.DesiredSize.Width;
    }

    private void Start()
    {
        _handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(_handle, GwlExStyle, GetWindowLong(_handle, GwlExStyle) | ExNoActivate | ExToolWindow);
        HwndSource.FromHwnd(_handle)?.AddHook(Hook);
        _callbackMessage = RegisterWindowMessage("DeckIsland.Dock.AppBarCallback");
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        Register();
        RegisterHotKey(_handle, PanicHotkey, ModControl | ModAlt | ModShift | ModNoRepeat, VkQ);
        Place();
        _clock.Start();
    }

    private void Stop()
    {
        _clock.Stop();
        if (_handle == IntPtr.Zero) return;
        UnregisterHotKey(_handle, PanicHotkey);
        if (_registered) { var data = Data(); SHAppBarMessage(AbmRemove, ref data); _registered = false; }
    }

    private AppBarData Data() => new() { Size = Marshal.SizeOf<AppBarData>(), Window = _handle, CallbackMessage = _callbackMessage };

    private void Register() { var data = Data(); _registered = SHAppBarMessage(AbmNew, ref data) != UIntPtr.Zero; }

    // Asks the shell for the strip on the chosen edge of the main screen, then puts the bar in the middle of it.
    public void Place()
    {
        if (_handle == IntPtr.Zero) return;
        var settings = _app.DockSettings;
        var (bounds, _, scale) = WindowsHost.PrimaryScreen();
        var screen = new PixelRect((int)bounds.Left, (int)bounds.Top, (int)bounds.Right, (int)bounds.Bottom);
        int stripHeight = (int)Math.Round(DockGeometry.StripHeight(settings.Size) * scale);
        var strip = DockGeometry.Strip(screen, settings.Edge, stripHeight);
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
        _barWidth = NaturalWidth();
        // The width is laid out at the window's own DPI, which may still be another monitor's until WM_DPICHANGED.
        var bar = DockGeometry.Bar(strip, (int)Math.Ceiling(_barWidth * VisualTreeHelper.GetDpi(this).DpiScaleX), (int)Math.Round(settings.Size * scale));
        SetWindowPos(_handle, _fullscreen ? HwndBottom : HwndTopmost, bar.Left, bar.Top, bar.Width, bar.Height, SwpNoActivate | SwpShowWindow);
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
            Register(); Place();
        }
        else if (message == WmEndSession && wParam != IntPtr.Zero) DockTaskbar.Restore();
        else if (message == WmHotkey && (int)wParam == PanicHotkey) { _app.TurnOffDock(); handled = true; }
        else if (message is WmDisplayChange or WmDpiChanged) Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
        return IntPtr.Zero;
    }
}
