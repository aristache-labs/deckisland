using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

// The listening ports, over the system monitor page: each port with its addresses, its process and PID, buttons to copy its address
// and open it in the browser, and to end its process (a second click confirms). Read again every few seconds while it is shown.
internal sealed class PortsView : Border
{
    private const double RowHeight = 32, FontSize_ = 12.5;
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(3), ConfirmFor = TimeSpan.FromSeconds(3);
    private PanelLook Look => PanelLook.Of(this);
    private readonly StackPanel _rows = new();
    private readonly TextBlock _count = new() { FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _portHead = new();
    private readonly DispatcherTimer _timer = new() { Interval = Every };
    private IReadOnlyList<(ListeningPort Port, string Name, bool CanEnd)> _ports = [];
    private bool _descending, _reading;
    // The process whose End button waits for its second click, and until when.
    private (int Pid, DateTime Until)? _confirm;

    public PortsView(Action close)
    {
        Padding = new Thickness(12, 8, 12, 8);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var x = new TextBlock { Text = "✕", FontSize = 13, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("关闭", "Close") };
        AutomationProperties.SetName(x, Loc.T("关闭", "Close"));
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; close(); };
        DockPanel.SetDock(x, Dock.Right); title.Children.Add(x);
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(new TextBlock { Text = Loc.T("监听端口", "LISTENING PORTS"), FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        heading.Children.Add(_count);
        title.Children.Add(heading);
        root.Children.Add(title);

        var head = Line();
        head.Margin = new Thickness(0, 0, 0, 2);
        _portHead.Cursor = Cursors.Hand;
        _portHead.MouseLeftButtonUp += (_, e) => { e.Handled = true; _descending = !_descending; Show(); };
        Put(head, _portHead, 1); Put(head, Caption(Loc.T("地址", "Address")), 2); Put(head, Caption(Loc.T("进程", "Process")), 3);
        var pid = Caption("PID"); pid.HorizontalAlignment = HorizontalAlignment.Right; pid.Margin = new Thickness(0, 0, 10, 0); Put(head, pid, 4);
        var headLine = new Border { Child = head, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(headLine, 1); root.Children.Add(headLine);

        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 2, 0, 0) };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        Child = root;

        _timer.Tick += (_, _) => Read();
        Loaded += (_, _) =>
        {
            Background = UI.Brush(Look.Fill); CornerRadius = new CornerRadius(Look.Corner(10));
            headLine.BorderBrush = UI.Brush(Look.Hairline);
            foreach (var text in new[] { (TextBlock)heading.Children[0], _count, x }) text.Foreground = UI.Brush(Look.Tertiary);
            Read(); _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Read(); _timer.Start(); } else _timer.Stop(); };
    }

    // The table and the process names are read off the interface thread; only the rows are built on it.
    private void Read()
    {
        if (_reading) return;
        _reading = true;
        Task.Run(() =>
        {
            var ports = ListeningPorts.Group(Native.TcpListeners.ReadAll().Select(l => (l.Address, l.Port, l.ProcessId)));
            var names = new Dictionary<int, (string, bool)>();
            return ports.Select(port =>
            {
                if (!names.TryGetValue(port.ProcessId, out var known)) names[port.ProcessId] = known = (ProcessName(port.ProcessId), CanEnd(port.ProcessId));
                return (port, known.Item1, known.Item2);
            }).ToList();
        }).ContinueWith(task =>
        {
            _reading = false;
            if (task.IsCompletedSuccessfully) { _ports = task.Result; Show(); }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static string ProcessName(int pid)
    {
        if (pid == 0) return Loc.T("系统空闲进程", "System Idle Process");
        if (pid == 4) return "System";
        try { using var process = Process.GetProcessById(pid); return process.ProcessName + ".exe"; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return Loc.T("未知", "Unknown"); }
    }

    // Whether this app may end the process: not the system's own, not DeckIsland, and Windows grants the right to terminate it.
    private static bool CanEnd(int pid)
    {
        if (pid <= 4 || pid == Environment.ProcessId) return false;
        nint handle = OpenProcess(ProcessTerminate, false, pid);
        if (handle == 0) return false;
        CloseHandle(handle);
        return true;
    }

    private void Show()
    {
        _count.Text = _ports.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _portHead.Text = Loc.T("端口", "Port") + (_descending ? " ˅" : " ˄");
        _portHead.FontSize = 11.5; _portHead.Foreground = UI.Brush(Look.Secondary);
        if (_confirm is { } c && c.Until < DateTime.UtcNow) _confirm = null;
        _rows.Children.Clear();
        var ordered = _descending ? _ports.OrderByDescending(p => p.Port.Port) : _ports.OrderBy(p => p.Port.Port);
        foreach (var (port, name, canEnd) in ordered) _rows.Children.Add(RowFor(port, name, canEnd));
    }

    private FrameworkElement RowFor(ListeningPort port, string name, bool canEnd)
    {
        var line = Line();
        var icon = MonitorIcons.Icon(port.Public ? "globe" : "lock", 13, UI.Brush(port.Public ? Look.WarningDefault : Look.Tertiary));
        icon.ToolTip = port.Public ? Loc.T("其他设备可访问", "Reachable from other devices") : Loc.T("仅本机", "This PC only");
        icon.HorizontalAlignment = HorizontalAlignment.Left; icon.VerticalAlignment = VerticalAlignment.Center;
        Put(line, icon, 0);
        var number = Text(port.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), Look.Primary); number.FontWeight = FontWeights.SemiBold; number.FontSize = FontSize_ + 1;
        Put(line, number, 1);
        var address = Text(port.AddressText, Look.Tertiary); address.ToolTip = port.AddressText; Put(line, address, 2);
        Put(line, Text(name, Look.Secondary), 3);
        var pid = Text(port.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), Look.Tertiary); pid.HorizontalAlignment = HorizontalAlignment.Right; Put(line, pid, 4);
        Put(line, Glyph("copy", Loc.T("复制 ", "Copy ") + port.Endpoint, () => Copy(port.Endpoint)), 5);
        Put(line, Glyph("open", Loc.T("在浏览器中打开 ", "Open ") + port.Url, () => Open(port.Url)), 6);
        Put(line, EndButton(port.ProcessId, name, canEnd), 7);
        var row = new Border { Child = line, Height = RowHeight, CornerRadius = new CornerRadius(Look.Corner(6)), Background = Brushes.Transparent };
        row.MouseEnter += (_, _) => row.Background = UI.Brush(Look.FillSelected);
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    private Border EndButton(int pid, string name, bool canEnd)
    {
        bool confirming = _confirm?.Pid == pid;
        var label = Text(confirming ? Loc.T("确认结束？", "Confirm?") : Loc.T("结束进程", "End process"), confirming ? Look.CriticalDefault : Look.Primary);
        label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0); label.FontWeight = FontWeights.SemiBold;
        var button = new Border { Child = label, Height = 24, Margin = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(Look.Corner(12)),
            Background = UI.Brush(Look.FillSelected), Opacity = canEnd ? 1 : .4, Cursor = canEnd ? Cursors.Hand : null,
            ToolTip = canEnd ? Loc.T("结束 ", "End ") + name : Loc.T("无法结束这个进程", "This process cannot be ended from here") };
        AutomationProperties.SetName(button, Loc.T("结束进程 ", "End process ") + name);
        if (!canEnd) return button;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_confirm?.Pid != pid || _confirm.Value.Until < DateTime.UtcNow) { _confirm = (pid, DateTime.UtcNow + ConfirmFor); Show(); return; }
            _confirm = null;
            try { using var process = Process.GetProcessById(pid); process.Kill(); process.WaitForExit(2000); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            Read();
        };
        return button;
    }

    private Border Glyph(string icon, string tip, Action click)
    {
        var glyph = MonitorIcons.Icon(icon, 13, UI.Brush(Look.Secondary));
        var button = new Border { Child = glyph, Width = 24, Height = 24, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = tip, CornerRadius = new CornerRadius(Look.Corner(5)) };
        glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(button, tip);
        button.MouseEnter += (_, _) => button.Background = UI.Brush(Look.Fill);
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch (COMException) { }
    }
    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // A row's columns: icon, port, address, process, PID, copy, open, end.
    private static Grid Line()
    {
        var grid = new Grid();
        foreach (var width in new[] { new GridLength(22), new GridLength(60), new GridLength(1.3, GridUnitType.Star), new GridLength(1, GridUnitType.Star),
            new GridLength(60), new GridLength(34), new GridLength(28), new GridLength(110) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        return grid;
    }
    private static void Put(Grid grid, UIElement element, int column) { Grid.SetColumn(element, column); grid.Children.Add(element); }
    private TextBlock Caption(string text) => new() { Text = text, FontSize = 11.5, Foreground = UI.Brush(Look.Tertiary), VerticalAlignment = VerticalAlignment.Center };
    private TextBlock Text(string text, string color) => new()
    {
        Text = text, FontSize = FontSize_, Foreground = UI.Brush(color), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0)
    };

    private const uint ProcessTerminate = 0x0001;
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
