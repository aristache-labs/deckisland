using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeckIsland.Core;
using DeckIsland.Native;
using Windows.Devices.Radios;

namespace DeckIsland;

// The Quick Toggles widget, after lumabar's: a strip of switches (QuickToggle), Wi-Fi, Bluetooth and Sound as pills whose arrow
// opens their panel, the rest round buttons. A lit button is on. Right-click chooses which buttons show (the Quick Toggles list).
// The state is read every two seconds and right after a switch; the system calls are in Native/QuickSystem.cs and AudioMixer.cs.
internal sealed partial class DockWindow
{
    private sealed class QuickState
    {
        public bool? WiFi, Bluetooth;
        public bool Ethernet, SoundOn, Hotspot, Airplane, Microphone, Camera, KeepAwake, DesktopIcons, DisplayMode, EnergySaver, DoNotDisturb;

        public bool On(QuickToggle toggle) => toggle switch
        {
            QuickToggle.WiFi => WiFi == true, QuickToggle.Ethernet => Ethernet, QuickToggle.Bluetooth => Bluetooth == true,
            QuickToggle.Sound => SoundOn, QuickToggle.Hotspot => Hotspot, QuickToggle.Airplane => Airplane,
            QuickToggle.Microphone => Microphone, QuickToggle.Camera => Camera, QuickToggle.KeepAwake => KeepAwake,
            QuickToggle.DesktopIcons => DesktopIcons, QuickToggle.DisplayMode => DisplayMode, QuickToggle.EnergySaver => EnergySaver,
            _ => DoNotDisturb
        };
    }

    private readonly QuickState _quick = new();
    private long _quickReadAt;
    private bool _quickReading;
    private event Action? QuickChanged;

    private static string QuickIcon(QuickToggle toggle, bool on) => toggle switch
    {
        QuickToggle.WiFi => "wifi", QuickToggle.Ethernet => "ethernet", QuickToggle.Bluetooth => "bluetooth",
        QuickToggle.Sound => on ? "volume" : "volume-off", QuickToggle.Hotspot => "hotspot", QuickToggle.Airplane => "airplane",
        QuickToggle.Microphone => "mic", QuickToggle.Camera => "camera", QuickToggle.KeepAwake => "coffee", QuickToggle.DesktopIcons => "layout",
        QuickToggle.DisplayMode => "monitor", QuickToggle.EnergySaver => "battery-saver", _ => "moon"
    };

    // Reads every state; the radios asynchronously. At most every two seconds unless forced.
    private async void ReadQuickState(bool force = false)
    {
        if (_quickReading || !force && Environment.TickCount64 - _quickReadAt < 2000) return;
        _quickReading = true;
        _quickReadAt = Environment.TickCount64;
        try
        {
            _quick.Ethernet = QuickSystem.EthernetUp();
            _quick.SoundOn = !AudioMixer.Muted();
            _quick.Hotspot = QuickSystem.HotspotOn();
            _quick.Airplane = QuickSystem.AirplaneOn();
            _quick.Microphone = !_app.MicMuted;
            _quick.Camera = QuickSystem.CameraAllowed();
            _quick.KeepAwake = QuickSystem.KeepingAwake;
            _quick.DesktopIcons = QuickSystem.DesktopIconsShown();
            _quick.DisplayMode = QuickSystem.SeveralDisplays();
            _quick.EnergySaver = QuickSystem.EnergySaverOn();
            _quick.DoNotDisturb = QuickSystem.DoNotDisturbOn();
            _quick.WiFi = await QuickSystem.RadioOn(RadioKind.WiFi);
            _quick.Bluetooth = await QuickSystem.RadioOn(RadioKind.Bluetooth);
        }
        finally { _quickReading = false; }
        QuickChanged?.Invoke();
    }

    // Switches a toggle; one that cannot be switched here opens its Settings page.
    private async void FlipQuick(QuickToggle toggle)
    {
        bool on = _quick.On(toggle);
        bool done = toggle switch
        {
            QuickToggle.WiFi => await QuickSystem.SetRadio(RadioKind.WiFi, !on),
            QuickToggle.Bluetooth => await QuickSystem.SetRadio(RadioKind.Bluetooth, !on),
            QuickToggle.Sound => Do(() => AudioMixer.SetMuted(on)),
            QuickToggle.Hotspot => await QuickSystem.SetHotspot(!on),
            QuickToggle.Airplane => QuickSystem.SetAirplane(!on),
            QuickToggle.Microphone => Do(_app.ToggleMicMute),
            QuickToggle.Camera => QuickSystem.SetCameraAllowed(!on),
            QuickToggle.KeepAwake => Do(() => QuickSystem.SetKeepAwake(!on)),
            QuickToggle.DesktopIcons => Do(QuickSystem.ToggleDesktopIconsShown),
            QuickToggle.DisplayMode => Do(QuickSystem.OpenProjection),
            QuickToggle.EnergySaver => await QuickSystem.SetEnergySaver(!on),
            QuickToggle.DoNotDisturb => QuickSystem.SetDoNotDisturb(!on),
            _ => false
        };
        if (!done) QuickSystem.OpenSettings(QuickSettingsPage(toggle));
        await Task.Delay(300);
        ReadQuickState(force: true);

        static bool Do(Action action) { action(); return true; }
    }

    private static string QuickSettingsPage(QuickToggle toggle) => toggle switch
    {
        QuickToggle.WiFi => "network-wifi", QuickToggle.Ethernet => "network-ethernet", QuickToggle.Bluetooth => "bluetooth",
        QuickToggle.Sound => "sound", QuickToggle.Hotspot => "network-mobilehotspot", QuickToggle.Airplane => "network-airplanemode",
        QuickToggle.Microphone => "privacy-microphone", QuickToggle.Camera => "privacy-webcam", QuickToggle.DisplayMode => "display",
        QuickToggle.EnergySaver => "batterysaver", QuickToggle.DoNotDisturb => "notifications", _ => "home"
    };

    private string QuickAccentFill => ThemeColor.TryParse(Accent, out var c) ? $"#33{c.R:X2}{c.G:X2}{c.B:X2}" : "#335E5CE6";

    // --- The strip ---

    // The strip's width in tiles: each button is 0.56 of a tile with a 0.1 gap, pills two buttons wide.
    private static double QuickStripTiles(IReadOnlyCollection<QuickToggle> shown) => QuickToggles.Units(shown) * 0.56 + shown.Count * 0.1 + 0.3;

    private (FrameworkElement, Action) QuickTogglesTile(double tile)
    {
        double size = Math.Round(tile * 0.56), gap = Math.Round(tile * 0.1);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var paints = new List<Action>();
        foreach (var toggle in _app.DockSettings.ShownToggles)
        {
            var (element, paint) = QuickToggles.IsPill(toggle) ? QuickPill(toggle, size) : QuickRound(toggle, size);
            element.Margin = new Thickness(gap / 2, 0, gap / 2, 0);
            row.Children.Add(element);
            paints.Add(paint);
        }
        void Paint() { foreach (var paint in paints) paint(); }
        QuickChanged += Paint;
        row.Unloaded += (_, _) => QuickChanged -= Paint;
        Paint();
        return (row, () => ReadQuickState());
    }

    private Border QuickSurface(double width, double size) => new()
    {
        Width = width, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1), Cursor = Cursors.Hand
    };

    private void PaintQuick(Border surface, bool on)
    {
        surface.Background = UI.Brush(on ? QuickAccentFill : _look.CardHover);
        surface.BorderBrush = UI.Brush(on ? Accent : "#00000000");
    }

    private (FrameworkElement, Action) QuickRound(QuickToggle toggle, double size)
    {
        var surface = QuickSurface(size, size);
        surface.ToolTip = QuickToggles.Name(toggle);
        System.Windows.Automation.AutomationProperties.SetName(surface, QuickToggles.Name(toggle));
        surface.MouseLeftButtonDown += (_, e) => e.Handled = true;
        surface.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (toggle == QuickToggle.Ethernet) QuickSystem.OpenSettings("network-ethernet"); else FlipQuick(toggle); };
        return (surface, () =>
        {
            bool on = _quick.On(toggle);
            PaintQuick(surface, on);
            surface.Child = MonitorIcons.Icon(QuickIcon(toggle, on), size * 0.42, UI.Brush(on ? Accent : _look.Dim));
        });
    }

    private (FrameworkElement, Action) QuickPill(QuickToggle toggle, double size)
    {
        var surface = QuickSurface(size * 2, size);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var main = new Border { Background = Brushes.Transparent, ToolTip = QuickToggles.Name(toggle) };
        var divider = new Border { Width = 1, Margin = new Thickness(0, size * 0.28, 0, size * 0.28) };
        var more = new Border { Background = Brushes.Transparent, ToolTip = Loc.T("更多", "More") };
        Grid.SetColumn(divider, 1); Grid.SetColumn(more, 2);
        grid.Children.Add(main); grid.Children.Add(divider); grid.Children.Add(more);
        surface.Child = grid;
        System.Windows.Automation.AutomationProperties.SetName(surface, QuickToggles.Name(toggle));
        main.MouseLeftButtonDown += (_, e) => e.Handled = true;
        main.MouseLeftButtonUp += (_, e) => { e.Handled = true; FlipQuick(toggle); };
        more.MouseLeftButtonDown += (_, e) => e.Handled = true;
        more.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenQuickPanel(toggle, surface); };
        return (surface, () =>
        {
            bool on = _quick.On(toggle);
            PaintQuick(surface, on);
            var ink = UI.Brush(on ? Accent : _look.Dim);
            divider.Background = UI.Brush(on ? Accent : _look.Divider);
            divider.Opacity = 0.5;
            main.Child = MonitorIcons.Icon(QuickIcon(toggle, on), size * 0.42, ink);
            more.Child = MonitorIcons.Icon("chevron-right", size * 0.34, ink);
        });
    }

    // --- Panels ---

    private void OpenQuickPanel(QuickToggle toggle, FrameworkElement anchor)
    {
        if (!BeginDockPanel(anchor)) return;
        var (body, update) = toggle switch
        {
            QuickToggle.WiFi => WifiPanel(),
            QuickToggle.Bluetooth => BluetoothPanel(),
            _ => SoundPanel()
        };
        ShowDockPanel(anchor, body, update, 400, 16, CalendarRadius(14, 30));
    }

    private TextBlock QuickCaption(string text)
    {
        var caption = Label(text.ToUpperInvariant(), 11.5, bold: true, dim: true);
        caption.Margin = new Thickness(4, 12, 0, 8);
        return caption;
    }

    // An iOS-style switch.
    private Border QuickSwitch(Func<bool> read, Action flip)
    {
        var knob = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brushes.White, Margin = new Thickness(2) };
        var track = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(13), Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        void Paint()
        {
            bool on = read();
            track.Background = UI.Brush(on ? "#FF30D158" : _look.CardActive);
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        track.Tag = (Action)Paint;
        track.MouseLeftButtonDown += (_, e) => e.Handled = true;
        track.MouseLeftButtonUp += (_, e) => { e.Handled = true; flip(); };
        Paint();
        return track;
    }

    private static void RepaintSwitch(Border track) => (track.Tag as Action)?.Invoke();

    private Border QuickBadge(string icon, bool on, double size = 32) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Margin = new Thickness(0, 0, 10, 0),
        Background = UI.Brush(on ? Accent : _look.CardHover), VerticalAlignment = VerticalAlignment.Center,
        Child = MonitorIcons.Icon(icon, size * 0.5, on ? Brushes.White : UI.Brush(_look.Dim))
    };

    private DockPanel QuickHeader(QuickToggle toggle, Border toggleSwitch)
    {
        var header = new DockPanel();
        DockPanel.SetDock(toggleSwitch, System.Windows.Controls.Dock.Right);
        header.Children.Add(toggleSwitch);
        header.Children.Add(QuickBadge(QuickIcon(toggle, true), true));
        var title = Label(QuickToggles.Name(toggle), 17, bold: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        return header;
    }

    private Border QuickFooter(string text, Action click)
    {
        var row = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
        var chevron = MonitorIcons.Icon("chevron-right", 15, UI.Brush(_look.Dim));
        DockPanel.SetDock(chevron, System.Windows.Controls.Dock.Right);
        row.Children.Add(chevron);
        row.Children.Add(Label(text, 13.5, bold: true));
        var footer = new Border { BorderBrush = UI.Brush(_look.Divider), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 14, 0, 2),
            Margin = new Thickness(-16, 10, -16, 0), Child = new Border { Padding = new Thickness(16, 0, 16, 0), Child = row },
            Background = Brushes.Transparent, Cursor = Cursors.Hand };
        footer.MouseLeftButtonUp += (_, _) => { CloseWidgetDetails(); click(); };
        return footer;
    }

    private Border QuickRow(UIElement content, Action? click = null)
    {
        var row = new Border { Background = UI.Brush(_look.CardHover), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 4), Child = content };
        if (click is null) return row;
        row.Cursor = Cursors.Hand;
        row.MouseEnter += (_, _) => row.Background = UI.Brush(_look.CardActive);
        row.MouseLeave += (_, _) => row.Background = UI.Brush(_look.CardHover);
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    private (FrameworkElement, Action) QuickPanelLayout(DockPanel header, FrameworkElement list, Border footer)
    {
        var panel = new DockPanel { Height = 460 };
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        DockPanel.SetDock(footer, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(header);
        panel.Children.Add(footer);
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        return (panel, () => { });
    }

    private (FrameworkElement, Action) WifiPanel()
    {
        var toggleSwitch = QuickSwitch(() => _quick.WiFi == true, () => FlipQuick(QuickToggle.WiFi));
        var list = new StackPanel();
        var refresh = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(6, 12, 0, 8), VerticalAlignment = VerticalAlignment.Bottom,
            Child = MonitorIcons.Icon("refresh", 13, UI.Brush(_look.Dim)), ToolTip = Loc.T("刷新", "Refresh") };
        var caption = new StackPanel { Orientation = Orientation.Horizontal, Children = { QuickCaption(Loc.T("网络", "Networks")), refresh } };
        var content = new StackPanel { Children = { caption, list } };
        async void Load(bool scan)
        {
            var networks = await QuickSystem.WifiNetworks(scan);
            list.Children.Clear();
            if (networks.Count == 0) list.Children.Add(Label(_quick.WiFi == false ? Loc.T("Wi-Fi 已关闭", "Wi-Fi is off") : Loc.T("没有找到网络", "No networks found"), 13, dim: true));
            foreach (var network in networks)
            {
                var line = new DockPanel();
                var marks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (network.Secured) marks.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 0), Child = MonitorIcons.Icon("lock", 14, UI.Brush(_look.Dim)) });
                marks.Children.Add(MonitorIcons.Icon("wifi", 16, UI.Brush(network.Bars >= 3 ? _look.Text : _look.Dim)));
                DockPanel.SetDock(marks, System.Windows.Controls.Dock.Right);
                line.Children.Add(marks);
                var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Label(network.Ssid, 13.5, bold: true) } };
                if (network.Connected) name.Children.Add(Label(Loc.T("已连接", "Connected"), 11.5, dim: true));
                line.Children.Add(name);
                list.Children.Add(QuickRow(line, network.Connected ? null : async () =>
                {
                    if (!await QuickSystem.ConnectWifi(network.Ssid)) { CloseWidgetDetails(); QuickSystem.OpenSettings("network-wifi"); }
                    else Load(scan: false);
                }));
            }
        }
        refresh.MouseLeftButtonUp += (_, _) => Load(scan: true);
        Load(scan: true);
        var (panel, _) = QuickPanelLayout(QuickHeader(QuickToggle.WiFi, toggleSwitch), content,
            QuickFooter(Loc.T("Wi-Fi 设置…", "Wi-Fi Settings…"), () => QuickSystem.OpenSettings("network-wifi")));
        bool? shown = _quick.WiFi;
        return (panel, () =>
        {
            RepaintSwitch(toggleSwitch);
            if (shown != _quick.WiFi) { shown = _quick.WiFi; Load(scan: true); }
        });
    }

    private (FrameworkElement, Action) BluetoothPanel()
    {
        var toggleSwitch = QuickSwitch(() => _quick.Bluetooth == true, () => FlipQuick(QuickToggle.Bluetooth));
        var list = new StackPanel();
        var content = new StackPanel { Children = { QuickCaption(Loc.T("我的设备", "My Devices")), list } };
        async void Load()
        {
            var devices = await QuickSystem.BluetoothDevices();
            list.Children.Clear();
            if (devices.Count == 0) list.Children.Add(Label(Loc.T("没有配对的设备", "No paired devices"), 13, dim: true));
            foreach (var device in devices)
            {
                var line = new DockPanel();
                if (!device.Connected && device.Audio)
                {
                    // Windows has no public call to connect a paired device; Connect opens Bluetooth settings, where it is one click.
                    var connect = new Border { Background = UI.Brush(_look.CardActive), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5),
                        VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Child = Label(Loc.T("连接", "Connect"), 12.5, bold: true) };
                    connect.MouseLeftButtonUp += (_, _) => { CloseWidgetDetails(); QuickSystem.OpenSettings("bluetooth"); };
                    DockPanel.SetDock(connect, System.Windows.Controls.Dock.Right);
                    line.Children.Add(connect);
                }
                line.Children.Add(QuickBadge(device.Audio ? "headphones" : device.Phone ? "phone" : "bluetooth", device.Connected, 34));
                line.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = {
                    Label(device.Name, 13.5, bold: true),
                    Label(device.Connected ? Loc.T("已连接", "Connected") : Loc.T("未连接", "Not connected"), 11.5, dim: true) } });
                list.Children.Add(QuickRow(line));
            }
        }
        Load();
        var (panel, _) = QuickPanelLayout(QuickHeader(QuickToggle.Bluetooth, toggleSwitch), content,
            QuickFooter(Loc.T("蓝牙设置…", "Bluetooth Settings…"), () => QuickSystem.OpenSettings("bluetooth")));
        bool? shown = _quick.Bluetooth;
        return (panel, () =>
        {
            RepaintSwitch(toggleSwitch);
            if (shown != _quick.Bluetooth) { shown = _quick.Bluetooth; Load(); }
        });
    }

    // A slider: a track, an accent fill and a white knob; drag, click or wheel.
    private FrameworkElement QuickSlider(double level, Action<double> changed, double height = 6, bool large = false, string? icon = null)
    {
        double value = Math.Clamp(level, 0, 1);
        var track = new Border { Height = large ? 46 : height, CornerRadius = new CornerRadius(large ? 23 : height / 2), Background = UI.Brush(large ? _look.CardActive : _look.CardHover),
            VerticalAlignment = VerticalAlignment.Center };
        var fill = new Border { CornerRadius = track.CornerRadius, Background = UI.Brush(large ? "#FFF2F2F4" : Accent), HorizontalAlignment = HorizontalAlignment.Left, Height = track.Height,
            VerticalAlignment = VerticalAlignment.Center };
        var knob = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Visibility = large ? Visibility.Collapsed : Visibility.Visible };
        var number = Label("", 13, bold: true, dim: true);
        number.HorizontalAlignment = HorizontalAlignment.Right; number.VerticalAlignment = VerticalAlignment.Center; number.Margin = new Thickness(0, 0, 18, 0);
        number.Visibility = large ? Visibility.Visible : Visibility.Collapsed;
        var area = new Grid { Height = large ? 46 : 22, Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { track, fill, knob, number } };
        if (large && icon is not null)
            area.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
                IsHitTestVisible = false, Child = MonitorIcons.Icon(icon, 18, UI.Brush("#FF6E6E73")) });
        void Paint()
        {
            double width = area.ActualWidth;
            if (width <= 0) return;
            fill.Width = Math.Max(large ? 46 : height, width * value);
            knob.Margin = new Thickness(Math.Clamp(width * value - 10, 0, width - 20), 0, 0, 0);
            number.Text = $"{Math.Round(value * 100)}";
        }
        void Set(double next) { value = Math.Clamp(next, 0, 1); Paint(); changed(value); }
        area.SizeChanged += (_, _) => Paint();
        area.MouseLeftButtonDown += (_, e) => { area.CaptureMouse(); Set(e.GetPosition(area).X / Math.Max(1, area.ActualWidth)); e.Handled = true; };
        area.MouseMove += (_, e) => { if (area.IsMouseCaptured) Set(e.GetPosition(area).X / Math.Max(1, area.ActualWidth)); };
        area.MouseLeftButtonUp += (_, _) => area.ReleaseMouseCapture();
        area.MouseWheel += (_, e) => { Set(value + Math.Sign(e.Delta) * 0.02); e.Handled = true; };
        return area;
    }

    private (FrameworkElement, Action) SoundPanel()
    {
        var header = new DockPanel();
        var close = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Child = MonitorIcons.Icon("close", 18, UI.Brush(_look.Dim)) };
        close.MouseLeftButtonUp += (_, _) => CloseWidgetDetails();
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = UI.Brush(Accent), Margin = new Thickness(0, 0, 10, 0),
            Child = MonitorIcons.Icon("volume", 17, Brushes.White) });
        var title = Label(QuickToggles.Name(QuickToggle.Sound), 17, bold: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        var device = new Border { Background = UI.Brush(_look.CardActive), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3),
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Label(AudioMixer.DefaultName() ?? "", 11.5, bold: true, dim: true) };
        header.Children.Add(device);

        var master = QuickSlider(AudioMixer.Level(), level => AudioMixer.SetLevel((float)level), large: true, icon: "volume");
        master.Margin = new Thickness(0, 16, 0, 0);
        var apps = new StackPanel();
        foreach (var app in AudioMixer.Apps())
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            FrameworkElement icon = app.Path is { } path && DockAppWindows.Icon(path, 32) is { } image
                ? new Image { Source = image, Width = 22, Height = 22 } : MonitorIcons.Icon("speaker", 18, UI.Brush(_look.Dim));
            icon.VerticalAlignment = VerticalAlignment.Center;
            var name = Label(app.Key == "system" ? Loc.T("系统声音", "System sounds") : app.Name, 13.5, bold: true);
            name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(8, 0, 8, 0);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var slider = QuickSlider(app.Level, level => AudioMixer.SetAppLevel(app.Key, (float)level));
            Grid.SetColumn(name, 1); Grid.SetColumn(slider, 2);
            line.Children.Add(icon); line.Children.Add(name); line.Children.Add(slider);
            apps.Children.Add(QuickRow(line));
        }
        var outputs = new StackPanel();
        void LoadOutputs()
        {
            outputs.Children.Clear();
            foreach (var output in AudioMixer.Outputs())
            {
                var line = new DockPanel();
                if (output.Default)
                {
                    var check = MonitorIcons.Icon("check", 16, UI.Brush(Accent));
                    DockPanel.SetDock(check, System.Windows.Controls.Dock.Right);
                    line.Children.Add(check);
                }
                line.Children.Add(new Border { Margin = new Thickness(0, 0, 10, 0), Child = MonitorIcons.Icon("speaker", 17, UI.Brush(_look.Dim)) });
                line.Children.Add(Label(output.Name, 13.5, bold: true));
                outputs.Children.Add(QuickRow(line, output.Default ? null : () =>
                {
                    AudioMixer.SetDefaultOutput(output.Id);
                    ((TextBlock)device.Child).Text = AudioMixer.DefaultName() ?? "";
                    LoadOutputs();
                }));
            }
        }
        LoadOutputs();
        var settings = new Border { Background = UI.Brush(_look.CardActive), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 8, 16, 9), Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Child = Label(Loc.T("声音设置", "Sound Settings"), 13, bold: true) };
        settings.MouseLeftButtonUp += (_, _) => { CloseWidgetDetails(); QuickSystem.OpenSettings("sound"); };
        var content = new StackPanel { Children = { header, master } };
        if (apps.Children.Count > 0) { content.Children.Add(QuickCaption(Loc.T("应用", "Apps"))); content.Children.Add(apps); }
        content.Children.Add(QuickCaption(Loc.T("输出", "Output")));
        content.Children.Add(outputs);
        content.Children.Add(settings);
        return (new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560, Content = content }, () => { });
    }

    // The Quick Toggles list: every toggle with its state and a switch for whether it shows on the strip, the shown ones first in
    // their strip order. Dragging a row by its grip moves it; the strip follows the list's order.
    private void OpenQuickToggleList()
    {
        if (!BeginDockPanel(_bar)) return;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = MonitorIcons.Icon("close", 18, UI.Brush(_look.Dim)) };
        close.MouseLeftButtonUp += (_, _) => CloseWidgetDetails();
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
        header.Children.Add(close);
        header.Children.Add(Label(Loc.T("快捷开关", "Quick Toggles"), 15, bold: true));
        var list = new StackPanel();
        var repaint = new List<Action>();
        void Save(ICollection<QuickToggle> shown)
        {
            var settings = _app.DockSettings.Copy();
            settings.QuickToggles = list.Children.OfType<Border>().Select(row => (QuickToggle)row.Tag).Where(shown.Contains).Select(t => t.ToString()).ToList();
            _app.UpdateDockSettings(settings, keepPanels: true);
            foreach (var paint in repaint) paint();
        }
        var current = _app.DockSettings.ShownToggles;
        foreach (var toggle in current.Concat(Enum.GetValues<QuickToggle>().Except(current)))
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
            var toggleSwitch = QuickSwitch(() => _app.DockSettings.ShownToggles.Contains(toggle), () =>
            {
                var shown = _app.DockSettings.ShownToggles;
                if (!shown.Remove(toggle)) shown.Add(toggle);
                Save(shown);
            });
            DockPanel.SetDock(toggleSwitch, System.Windows.Controls.Dock.Right);
            line.Children.Add(toggleSwitch);
            var grip = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeNS, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = MonitorIcons.Icon("grip", 16, UI.Brush(_look.Dim)), ToolTip = Loc.T("拖动以排序", "Drag to reorder") };
            line.Children.Add(grip);
            var badge = new Border();
            line.Children.Add(badge);
            var name = Label(QuickToggles.Name(toggle), 14, bold: true);
            name.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(name);
            repaint.Add(() => { RepaintSwitch(toggleSwitch); badge.Child = QuickBadge(QuickIcon(toggle, _quick.On(toggle)), _quick.On(toggle), 30); });
            var row = new Border { BorderBrush = UI.Brush(_look.Divider), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 9, 4, 9), Child = line,
                Tag = toggle, Background = Brushes.Transparent };
            // Drag by the grip: the row moves to the slot under the pointer as it goes; the order is saved on release.
            grip.MouseLeftButtonDown += (_, e) => { grip.CaptureMouse(); row.Background = UI.Brush(_look.CardHover); e.Handled = true; };
            grip.MouseMove += (_, e) =>
            {
                if (!grip.IsMouseCaptured || row.ActualHeight <= 0) return;
                int target = Math.Clamp((int)(e.GetPosition(list).Y / row.ActualHeight), 0, list.Children.Count - 1);
                int index = list.Children.IndexOf(row);
                if (target == index) return;
                list.Children.RemoveAt(index);
                list.Children.Insert(target, row);
            };
            grip.MouseLeftButtonUp += (_, e) =>
            {
                if (!grip.IsMouseCaptured) return;
                grip.ReleaseMouseCapture();
                row.Background = Brushes.Transparent;
                Save(_app.DockSettings.ShownToggles);
                e.Handled = true;
            };
            list.Children.Add(row);
        }
        foreach (var paint in repaint) paint();
        var body = new DockPanel();
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        body.Children.Add(header);
        body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560, Content = list });
        ShowDockPanel(_bar, body, () => { foreach (var paint in repaint) paint(); }, 420, 16, CalendarRadius(14, 30));
    }
}
