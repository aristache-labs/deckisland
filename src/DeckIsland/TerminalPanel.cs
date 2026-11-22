using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using DeckIsland.Core;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;

namespace DeckIsland;

// The Terminal tab: PowerShell 7, Windows PowerShell or Command Prompt in the Windows Terminal control. That control is a native window
// (an HwndHost), which a transparent WPF window such as the panel cannot draw, so it lives in a small window of its own owned by the panel
// and laid exactly over the tab's frame while the tab is on screen. Hiding that window (the notch closed, another tab) leaves the session
// running; it ends only when the tab is switched off, restarted or the app exits.
public partial class MainWindow
{
    private Window? _terminalHost;
    private EasyTerminalControl? _terminal;
    private Border? _terminalFrame;
    private Grid? _terminalBody;
    private (int Generation, string Language) _terminalAppearance;
    private int _terminalFontSize;
    private const string TerminalBackground = "#0C0C0C";
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool SetStdHandle(int which, IntPtr handle);

    private void InitializeTerminal()
    {
        TerminalContent.IsVisibleChanged += (_, _) => PlaceTerminal();
        // Layout passes follow the open and close animations and edge drags; one handler for the window's life.
        LayoutUpdated += (_, _) => PlaceTerminal();
        Closed += (_, _) => CloseTerminal();
    }

    private void TerminalPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Terminal);

    private void RenderTerminal()
    {
        RefreshStatus();
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility = ShelfContent.Visibility = DevicesContent.Visibility = WeatherContent.Visibility =
            FocusContent.Visibility = RemindersContent.Visibility = NotificationsContent.Visibility = CallsContent.Visibility = ScratchpadContent.Visibility = ConverterContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        if (!double.IsFinite(ContentCanvas.Height)) return;
        var header = PanelHeader.For(32, 18);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        ShowHeaderPin(header);
        // Equal insets on the sides and bottom let the frame's lower corners share the panel's curve.
        TerminalContent.Margin = new Thickness(header.Side, 0, header.Side, header.Side);
        var appearance = (Look.Generation, Loc.Language);
        if (_terminalAppearance != appearance || _terminalBody is null || !TerminalContent.Children.Contains(_terminalBody))
        {
            _terminalAppearance = appearance;
            TerminalContent.Children.Clear();
            BuildTerminalPage();
        }
        FitTerminalFrame(header.Side);
        EnsureTerminal();
        ApplyTerminalFont();
        PlaceTerminal();
    }

    private void BuildTerminalPage()
    {
        var title = WeatherText(Loc.T("终端", "Terminal"), 14);
        title.FontWeight = FontWeights.SemiBold;
        var restart = new Button { Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Background = UI.Brush(Look.Hex("fill")), Width = 26, Height = 26,
            HorizontalAlignment = HorizontalAlignment.Right,
            Content = new Path { Data = Geometry.Parse("M 20,12 A 8,8 0 1 1 17.7,6.3 M 20,3 V 7.5 H 15.5"), Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.7, Width = 13, Height = 13,
                Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round } };
        AutomationProperties.SetName(restart, Loc.T("重新启动", "Restart"));
        restart.ToolTip = Loc.T("用当前设置重新启动终端", "Restart the terminal with the current settings");
        restart.Click += (_, _) => { CloseTerminal(); RenderTerminal(); };
        restart.HorizontalAlignment = HorizontalAlignment.Left;
        title.VerticalAlignment = VerticalAlignment.Center;
        restart.Margin = new Thickness(10, 0, 0, 0);
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { title, restart } };
        // The frame the terminal's window is laid over; its colour is the terminal's, so the rounded corners show around it.
        _terminalFrame = new Border { Background = UI.Brush(TerminalBackground) };
        _terminalFrame.SizeChanged += (_, _) => PlaceTerminal();
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_terminalFrame, 1);
        body.Children.Add(top); body.Children.Add(_terminalFrame);
        _terminalBody = body;
        TerminalContent.Children.Add(body);
    }

    private void FitTerminalFrame(double gutter)
    {
        if (_terminalFrame is not { } frame) return;
        double top = Math.Min(Look.Corner(10), PanelRadius / _scale);
        double bottom = Math.Max(0, PanelRadius / _scale - gutter);
        const double side = 8, minimumVertical = 6;
        // The terminal is a rectangular native window. Keep its corners just inside the WPF frame
        // so the renderer never paints over the curve and its text stays inside the usable viewport.
        double SafeInset(double radius) => Math.Max(minimumVertical, Math.Ceiling(PanelHeader.SquareCornerInset(radius, side)) + 1);
        var corners = new CornerRadius(top, top, bottom, bottom);
        var padding = new Thickness(side, SafeInset(top), side, SafeInset(bottom));
        if (frame.CornerRadius != corners) frame.CornerRadius = corners;
        if (frame.Padding != padding) frame.Padding = padding;
    }

    private void EnsureTerminal()
    {
        if (_terminal is not null) return;
        // A shell inherits the app's standard handles when DeckIsland itself was started from a console (dotnet run), and then
        // writes there instead of into the terminal. The app writes nothing to them, so they are let go first.
        SetStdHandle(-10, IntPtr.Zero); SetStdHandle(-11, IntPtr.Zero); SetStdHandle(-12, IntPtr.Zero);
        var shell = Settings.TerminalShell;
        string folder = TerminalShells.StartFolder(Settings.TerminalStartFolder, Directory.Exists, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _terminal = new EasyTerminalControl
        {
            StartupCommandLine = TerminalShells.Program(shell, TerminalShells.OnPath), WorkingDirectory = folder,
            FontFamilyWhenSettingTheme = new FontFamily("Cascadia Mono, Consolas"), FontSizeWhenSettingTheme = Settings.TerminalFontSize
        };
        _terminalFontSize = 0;
        AutomationProperties.SetName(_terminal, Loc.T("终端", "Terminal"));
        _terminalHost = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Background = UI.Brush(TerminalBackground), Content = _terminal, Owner = this, Width = 1, Height = 1, Left = -32000, Top = -32000,
            Title = Loc.T("DeckIsland 终端", "DeckIsland terminal")
        };
    }

    private void ApplyTerminalFont()
    {
        if (_terminal is null) return;
        int size = (int)Math.Round(Settings.TerminalFontSize * _scale);
        if (size == _terminalFontSize) return;
        _terminalFontSize = size;
        _terminal.FontSizeWhenSettingTheme = size;
        _terminal.Theme = TerminalColors();
    }

    // Campbell, the Windows Terminal's default scheme.
    private static TerminalTheme TerminalColors()
    {
        static uint Rgb(string hex) => EasyTerminalControl.ColorToVal((Color)ColorConverter.ConvertFromString(hex));
        return new TerminalTheme
        {
            DefaultBackground = Rgb(TerminalBackground), DefaultForeground = Rgb("#CCCCCC"), DefaultSelectionBackground = Rgb("#404040"),
            CursorStyle = CursorStyle.BlinkingBar,
            ColorTable = [.. new[] { "#0C0C0C", "#C50F1F", "#13A10E", "#C19C00", "#0037DA", "#881798", "#3A96DD", "#CCCCCC",
                "#767676", "#E74856", "#16C60C", "#F9F1A5", "#3B78FF", "#B4009E", "#61D6D6", "#F2F2F2" }.Select(Rgb)]
        };
    }

    // The terminal's window over the frame while the tab is on screen in the open panel, hidden otherwise.
    private void PlaceTerminal()
    {
        _escapeDismissal?.SetEnabled(HoldsEscape);
        if (_terminalHost is null) return;
        bool shown = _expanded && IsVisible && CurrentPage == DeckPage.Terminal && TerminalContent.IsVisible && _terminalFrame is { IsVisible: true, ActualWidth: > 20, ActualHeight: > 20 }
            && PresentationSource.FromVisual(_terminalFrame) is not null;
        if (!shown) { if (_terminalHost.IsVisible) _terminalHost.Hide(); return; }
        var frame = _terminalFrame!;
        var inset = frame.Padding;
        var topLeft = frame.PointToScreen(new Point(inset.Left, inset.Top));
        var bottomRight = frame.PointToScreen(new Point(frame.ActualWidth - inset.Right, frame.ActualHeight - inset.Bottom));
        // Screen points are device pixels; the window is placed in its own DIPs.
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var a = fromDevice.Transform(topLeft); var b = fromDevice.Transform(bottomRight);
        if (Math.Abs(_terminalHost.Left - a.X) > .5) _terminalHost.Left = a.X;
        if (Math.Abs(_terminalHost.Top - a.Y) > .5) _terminalHost.Top = a.Y;
        if (Math.Abs(_terminalHost.Width - (b.X - a.X)) > .5) _terminalHost.Width = Math.Max(1, b.X - a.X);
        if (Math.Abs(_terminalHost.Height - (b.Y - a.Y)) > .5) _terminalHost.Height = Math.Max(1, b.Y - a.Y);
        if (!_terminalHost.IsVisible) _terminalHost.Show();
    }

    // Ends the session: the shell and what it started, then the terminal's window.
    private void CloseTerminal()
    {
        var host = _terminalHost; var terminal = _terminal;
        _terminalHost = null; _terminal = null;
        try
        {
            if (terminal is not null) terminal.DisconnectConPTYTerm()?.Process?.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException) { }
        // The window goes whatever happened to the shell: nothing could reach it again.
        finally { host?.Close(); }
    }
}
