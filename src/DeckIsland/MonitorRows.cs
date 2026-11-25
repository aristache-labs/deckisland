using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// The monitor page's readings as rows of one card, after a hardware dashboard: each row its icon and name at the left, what it is
// made of in the middle (the processor's core columns, the memory's bar), and its figure at the right, rows divided by hairlines.
internal sealed class MonitorRows : StackPanel
{
    private PanelLook Look => PanelLook.Of(this);
    private readonly MonitorColors _colors;
    private readonly double _body, _caption;
    private readonly bool _temperatures;
    private readonly int _low;
    private readonly Action<GpuSample> _gpuChanged;
    private readonly List<Row> _rows = [];
    private readonly Row? _cpu, _memory, _gpu, _network, _power, _battery, _frames;
    private readonly ColumnGraph? _cores;
    private readonly ShareBar? _memoryBar, _gpuBar;
    private readonly TextBlock? _memoryNote;
    private readonly Border? _gpuSwitch, _address, _modeChip;
    private readonly TextBlock? _health;
    private readonly List<Border> _chips = [];
    private readonly ShowMenu? _menu;
    private GpuSample? _nextGpu;
    private MonitorSnapshot? _last;
    // The power slider's mode: the battery's reading of it where there is one, else asked once (it can wait on the power service).
    private PowerMode? _mode;
    // The local address, looked up again after AddressLife.
    private static (string Text, DateTime At)? _ip;
    private static readonly TimeSpan AddressLife = TimeSpan.FromSeconds(30);

    // A drop-down under an anchor: its items with the chosen one, and what a pick does (MainWindow.ShowAnchoredMenu).
    public delegate void ShowMenu(FrameworkElement anchor, IReadOnlyList<(string Label, bool Chosen)> items, Action<int> pick);

    // Which rows: the parts the plan has a place for, in its order. battery: the machine has one (its own row after the power).
    public MonitorRows(IReadOnlyList<ExpandedItem> order, MonitorColors colors, MonitorLayout layout, bool temperatures, PowerOptions? power,
        bool frames, Action<GpuSample> gpuChanged, CoreDisplay coreDisplay, ShowMenu? menu = null)
    {
        _colors = colors; _body = layout.Body; _caption = layout.Caption; _temperatures = temperatures; _gpuChanged = gpuChanged; _menu = menu;
        _low = power?.Low ?? 20;
        foreach (var item in order)
        {
            switch (item)
            {
                case ExpandedItem.Cpu when _cpu is null:
                    _cores = new ColumnGraph(6, _caption, false, gap: 1.5, name: Loc.T("CPU 各核心占用", "CPU usage by core"), display: coreDisplay)
                        { Height = 12, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
                    _cpu = Add("CPU", "cpu", colors.Performance, _cores);
                    break;
                case ExpandedItem.Memory when _memory is null:
                    _memoryBar = new ShareBar { Margin = new Thickness(0, 6, 0, 0) };
                    _memoryNote = new TextBlock { FontSize = _caption, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                    var memory = new StackPanel(); memory.Children.Add(_memoryBar); memory.Children.Add(_memoryNote);
                    _memory = Add("RAM", "memory", colors.Memory, memory);
                    break;
                case ExpandedItem.Gpu when _gpu is null:
                    _gpuBar = new ShareBar { Margin = new Thickness(0, 6, 0, 0) };
                    _gpu = Add("GPU", "gpu", colors.Gpu, _gpuBar);
                    // With more than one adapter, the name and the counter both show the next one.
                    _gpuSwitch = Chip(_gpu, Loc.T("切换显卡", "Switch GPU"), SwitchGpu);
                    _gpuSwitch.Visibility = Visibility.Collapsed;
                    _gpu.Detail.MouseLeftButtonUp += (_, e) => { if (_nextGpu is null) return; e.Handled = true; SwitchGpu(); };
                    break;
                case ExpandedItem.Download or ExpandedItem.Upload when _network is null:
                    _network = Add(Loc.T("网络", "Net"), "network", colors.Download, null);
                    _address = Chip(_network, Loc.T("点击运行 ipconfig", "Click to run ipconfig"), () => Start("cmd.exe", "/k ipconfig"));
                    _address.Visibility = Visibility.Collapsed;
                    break;
                case ExpandedItem.Frames when frames && _frames is null:
                    _frames = Add(Loc.T("帧率", "FPS"), "frames", colors[MonitorRole.Frames], null);
                    break;
                case ExpandedItem.Power when power is not null && _power is null:
                    _power = Add(Loc.T("功耗", "Power"), "power", colors[MonitorRole.Power], null);
                    if (_menu is not null) _modeChip = Chip(_power, Loc.T("电源模式", "Power mode"), PickMode);
                    if (power.Battery)
                    {
                        _battery = Add(Loc.T("电池", "Battery"), "battery", colors[MonitorRole.Battery], null);
                        _health = new TextBlock { FontSize = _caption + .5, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("点击打开电池报告", "Click to open the battery report") };
                        _health.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenBatteryReport(); };
                        _battery.Extras.Children.Add(_health);
                        // The health follows the battery's state right after it ("Plugged in  Health 100 %"), not at the row's right.
                        if (_battery.Extras.Parent is Grid line)
                        {
                            line.ColumnDefinitions[0].Width = GridLength.Auto; line.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                            _battery.Extras.HorizontalAlignment = HorizontalAlignment.Left;
                        }
                    }
                    break;
            }
        }
        Loaded += (_, _) => { Paint(); if (_last is not null) Update(_last, false); };
    }

    public bool ShowsPower => _power is not null;
    public bool ShowsFrames => _frames is not null;

    // A row: the icon, the name, the detail line with what goes under it, the figure.
    private sealed record Row(Grid Grid, Border Line, FrameworkElement Icon, TextBlock Name, StackPanel Extras, TextBlock Detail, TextBlock Value);

    // A small rounded button after a row's detail (the GPU counter, the address, the power mode).
    private Border Chip(Row row, string tip, Action click)
    {
        var chip = new Border { Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { FontSize = _caption - .5 }, ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(chip, tip);
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        row.Extras.Children.Add(chip); _chips.Add(chip);
        return chip;
    }
    private static void SetChip(Border chip, string text) => ((TextBlock)chip.Child).Text = text;

    private void SwitchGpu() { if (_nextGpu is { } next) _gpuChanged(next); }

    private void PickMode()
    {
        var current = _mode ?? PowerMode.Unknown;
        var modes = BatteryModel.SliderModes;
        _menu!(_modeChip!, [.. modes.Select(mode => (BatteryModel.SliderName(mode), mode == current))], index =>
        {
            if (!Native.BatteryProvider.SetMode(modes[index])) return;
            _mode = modes[index];
            if (_last is not null) UpdatePower(_last);
        });
    }

    // Opens a program; nothing happens when Windows cannot start it.
    private static void Start(string file, string arguments)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, arguments) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
    // Windows' battery report (powercfg /batteryreport), written to the temporary folder and opened in the browser.
    private static void OpenBatteryReport() => Task.Run(() =>
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeckIsland battery report.html");
        try
        {
            using var powercfg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powercfg.exe", $"/batteryreport /output \"{path}\"")
                { UseShellExecute = false, CreateNoWindow = true });
            if (powercfg is null || !powercfg.WaitForExit(20_000) || powercfg.ExitCode != 0 || !System.IO.File.Exists(path)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException) { }
    });

    // The IPv4 address of the connected physical Wi-Fi or Ethernet adapter, as ipconfig lists it; looked up again every AddressLife.
    private static string? Address()
    {
        if (_ip is { } known && DateTime.UtcNow - known.At < AddressLife) return known.Text.Length > 0 ? known.Text : null;
        string text = "";
        try
        {
            // The physical Wi-Fi or Ethernet adapter that is connected, not WSL's, a VPN's or another virtual one (PhysicalAdapters).
            var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Select(adapter =>
            {
                var properties = adapter.GetIPProperties();
                bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));
                string? address = properties.UnicastAddresses.Select(u => u.Address).FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
                return new AdapterInfo(adapter.Name, adapter.Description, adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211,
                    adapter.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Ethernet or System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet
                        or System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT or System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx,
                    adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up, gateway, address);
            });
            text = PhysicalAdapters.Choose(adapters)?.Address ?? "";
        }
        catch (System.Net.NetworkInformation.NetworkInformationException) { }
        _ip = (text, DateTime.UtcNow);
        return text.Length > 0 ? text : null;
    }

    private Row Add(string name, string icon, string color, FrameworkElement? under)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Round(_body * 5.4)) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Math.Round(_body * 4.5) });
        var glyph = MonitorIcons.Icon(icon, Math.Round(_body + 3), UI.Brush(color));
        glyph.VerticalAlignment = VerticalAlignment.Top; glyph.HorizontalAlignment = HorizontalAlignment.Left; glyph.Margin = new Thickness(0, 1, 0, 0);
        var label = new TextBlock { Text = name, FontSize = _body + .5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top };
        var detail = new TextBlock { FontSize = _caption + .5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        // The detail takes what the chips at the right leave, cut short rather than pushing them out.
        var extras = new StackPanel { Orientation = Orientation.Horizontal };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(extras, 1);
        top.Children.Add(detail); top.Children.Add(extras);
        var middle = new StackPanel { Margin = new Thickness(0, 1, 8, 0) };
        middle.Children.Add(top);
        if (under is not null) middle.Children.Add(under);
        var value = new TextBlock { FontSize = _body + 1.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(label, 1); Grid.SetColumn(middle, 2); Grid.SetColumn(value, 3);
        grid.Children.Add(glyph); grid.Children.Add(label); grid.Children.Add(middle); grid.Children.Add(value);
        var line = new Border { Child = grid, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, _rows.Count == 0 ? 0 : 1, 0, 0) };
        Children.Add(line);
        var row = new Row(grid, line, glyph, label, extras, detail, value);
        _rows.Add(row);
        return row;
    }

    // The theme's colors, known once the rows are in their panel.
    private void Paint()
    {
        var hairline = UI.Brush(Look.Hairline);
        foreach (var row in _rows) { row.Line.BorderBrush = hairline; row.Name.Foreground = UI.Brush(Look.Primary); row.Detail.Foreground = UI.Brush(Look.Tertiary); }
        if (_memoryNote is not null) _memoryNote.Foreground = UI.Brush(Look.Tertiary);
        foreach (var chip in _chips) { chip.Background = UI.Brush(Look.FillSelected); ((TextBlock)chip.Child).Foreground = UI.Brush(Look.Secondary); }
        if (_health is not null) _health.Foreground = UI.Brush(Look.Primary);
        _memoryBar?.SetTrack(Look.Track); _gpuBar?.SetTrack(Look.Track);
    }

    public void Update(MonitorSnapshot snapshot, bool motion)
    {
        _last = snapshot;
        if (_cpu is not null) UpdateCpu(snapshot, motion);
        if (_memory is not null) UpdateMemory(snapshot, motion);
        if (_gpu is not null) UpdateGpu(snapshot, motion);
        if (_network is not null) UpdateNetwork(snapshot);
        if (_power is not null) UpdatePower(snapshot);
        if (_frames is not null) UpdateFrames(snapshot);
    }

    private void UpdateCpu(MonitorSnapshot snapshot, bool motion)
    {
        if (!snapshot.Cpu.TryGet(out var cpu)) { Empty(_cpu!, snapshot.Cpu.Reason); _cores!.Set([], motion); return; }
        string frequency = cpu.FrequencyMhz.TryGet(out var mhz) ? MonitorFormat.Gigahertz(mhz) + " GHz" : "";
        double? heat = _temperatures ? cpu.Temperature : null;
        Detail(_cpu!, string.Join(" · ", new[] { Loc.T($"{cpu.Cores.Count} 线程", $"{cpu.Cores.Count}T"), frequency }.Where(part => part.Length > 0)),
            heat is { } c ? Temperatures.Detail(c) : null, heat is { } h ? Hot(Temperatures.OfCpu(h), Look.Primary) : null);
        Value(_cpu!, MonitorFormat.Percent(cpu.Usage) + " %", Hot(ReadingLevels.OfShare(cpu.Usage), Look.Primary));
        _cpu!.Grid.ToolTip = "CPU " + MonitorFormat.Percent(cpu.Usage) + "%" + (frequency.Length > 0 ? " · " + frequency : "")
            + Loc.T($" · {cpu.Cores.Count} 个逻辑处理器", $" · {cpu.Cores.Count} logical processors");
        int groups = Math.Max(1, cpu.GroupCount);
        _cores!.Width = cpu.Cores.Count * 7.5;
        _cores.Set(cpu.Cores.Select(core => new GraphColumn(core.Usage, null, _colors.Core(core.Group, groups), core.Group, core.Describe(),
            Hot: _colors.Level(ReadingLevels.OfShare(core.Usage), _colors.Core(core.Group, groups)))).ToList(), motion);
    }

    private void UpdateMemory(MonitorSnapshot snapshot, bool motion)
    {
        if (!snapshot.Memory.TryGet(out var memory)) { Empty(_memory!, snapshot.Memory.Reason); _memoryBar!.Set([]); _memoryNote!.Text = ""; return; }
        Detail(_memory!, MonitorFormat.Gigabytes(memory.Used) + " / " + MonitorFormat.Gigabytes(memory.Total) + " GB");
        Value(_memory!, MonitorFormat.Percent(memory.UsedFraction) + " %", Hot(memory.Level, Look.Primary));
        _memoryBar!.Set([(memory.UsedFraction, _colors.Level(memory.Level, _colors.Memory)), (memory.CachedFraction, MonitorDraw.Faint(_colors.Memory, .4))]);
        _memoryNote!.Text = Loc.T("缓存 ", "Cache ") + MonitorFormat.Gigabytes(memory.Cached) + Loc.T(" · 提交 ", " · Commit ")
            + MonitorFormat.Gigabytes(memory.CommitUsed) + " / " + MonitorFormat.Gigabytes(memory.CommitLimit) + " GB";
    }

    private void UpdateGpu(MonitorSnapshot snapshot, bool motion)
    {
        if (!snapshot.Gpu.TryGet(out var gpu) || gpu.NoReadings)
        {
            Empty(_gpu!, snapshot.Gpu.TryGet(out _) ? Loc.T("暂无读数", "No reading") : snapshot.Gpu.Reason); _gpuBar!.Set([]);
            _nextGpu = null; _gpuSwitch!.Visibility = Visibility.Collapsed; return;
        }
        bool several = snapshot.Gpus.Count > 1;
        _nextGpu = several ? GpuChoice.Next(snapshot.Gpus, gpu) : null;
        if (several)
        {
            int index = Math.Max(0, snapshot.Gpus.ToList().FindIndex(other => other.Key == gpu.Key));
            ((TextBlock)_gpuSwitch!.Child).Text = $"{index + 1}/{snapshot.Gpus.Count}";
        }
        _gpuSwitch!.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
        _gpu!.Detail.Cursor = several ? Cursors.Hand : null;
        double? heat = _temperatures ? gpu.Temperature : null;
        string memory = gpu.DedicatedBytes.TryGet(out var used)
            ? MonitorFormat.Gigabytes(used) + (gpu.DedicatedTotal is { } total ? " / " + MonitorFormat.GigabytesCompact(total) : "") + " GB" : "";
        Detail(_gpu!, string.Join(" · ", new[] { gpu.Name ?? "GPU", memory }.Where(part => part.Length > 0)),
            heat is { } c ? Temperatures.Detail(c) : null, heat is { } h ? Hot(Temperatures.OfGpu(h), Look.Primary) : null);
        Value(_gpu!, MonitorFormat.Percent(gpu.Usage) + " %", Hot(ReadingLevels.OfShare(gpu.Usage), Look.Primary));
        _gpu!.Grid.ToolTip = (gpu.Name ?? "GPU") + " · " + MonitorFormat.Percent(gpu.Usage) + "%" + (memory.Length > 0 ? Loc.T(" · 显存 ", " · VRAM ") + memory : "")
            + (_nextGpu is { } next ? "\n" + Loc.T("点击名称切换到 ", "Click the name to show ") + GpuChoice.Describe(next.Name, next.Kind) : "");
        _gpuBar!.Set(gpu.DedicatedFraction is { } share ? [(share, _colors.Level(ReadingLevels.OfShare(share), _colors.Gpu))] : []);
    }

    private void UpdateNetwork(MonitorSnapshot snapshot)
    {
        if (!snapshot.Network.TryGet(out var network)) { Empty(_network!, snapshot.Network.Reason); return; }
        var down = MonitorFormat.Rate(network.DownBytesPerSecond); var up = MonitorFormat.Rate(network.UpBytesPerSecond);
        var detail = _network!.Detail;
        detail.Inlines.Clear();
        detail.Inlines.Add(new Run("↓ ") { Foreground = UI.Brush(_colors.Download) });
        detail.Inlines.Add(new Run(down.Number + " " + down.Unit + "   "));
        detail.Inlines.Add(new Run("↑ ") { Foreground = UI.Brush(_colors.Upload) });
        detail.Inlines.Add(new Run(up.Number + " " + up.Unit));
        var total = MonitorFormat.CompactRate(network.DownBytesPerSecond + network.UpBytesPerSecond);
        Value(_network, total.Number + total.Unit, null);
        string? address = Address();
        _address!.Visibility = address is null ? Visibility.Collapsed : Visibility.Visible;
        if (address is not null) SetChip(_address, address);
    }

    private void UpdatePower(MonitorSnapshot snapshot)
    {
        var battery = snapshot.Battery.OrDefault;
        // The mode: the battery reads it with every sample; without a battery it is asked once, then kept as picked.
        _mode = battery is not null && battery.Mode != PowerMode.Unknown ? battery.Mode : _mode ?? Native.BatteryProvider.Mode;
        if (_modeChip is not null) SetChip(_modeChip, (_mode is PowerMode.Unknown or null ? Loc.T("电源模式", "Power mode") : BatteryModel.SliderName(_mode.Value)) + " ▾");
        if (snapshot.Power.TryGet(out var power))
        {
            // Where the power goes, as the battery says it: charging, discharging; on mains without a battery, where it is read.
            string state = battery is { Charging: true } ? Loc.T("充电中", "Charging")
                : battery is { AcOnline: false } || power.Source == PowerSource.Battery ? Loc.T("放电", "Discharge")
                : power.Source == PowerSource.SystemMeter ? Loc.T("整机功率计", "System meter") : Loc.T("估算", "Estimate");
            var parts = power.Parts.Where(part => part.Key is "cpu" or "gpu").GroupBy(part => part.Key)
                .Select(group => (group.Key == "cpu" ? "CPU " : "GPU ") + PowerText.Watts(group.Sum(part => part.Watts)));
            Detail(_power!, string.Join(" · ", parts.Prepend(state)));
            _power!.Grid.ToolTip = PowerText.Watts(power.Watts) + (power.Estimated ? Loc.T("（估算）", " (estimated)") : Loc.T("（实测）", " (measured)"));
            Value(_power!, PowerText.Watts(power.Watts), null);
        }
        else Empty(_power!, snapshot.Power.Reason);
        if (_battery is null) return;
        if (battery is null) { Empty(_battery, snapshot.Battery.Reason); _health!.Visibility = Visibility.Collapsed; return; }
        // Plugged in: the time to full while it charges; on battery: the time left.
        string time = battery.AcOnline
            ? battery.Charging ? battery.TimeToFull is { } full ? Loc.T($"{Duration(full)} 后充满", $"{Duration(full)} to full") : Loc.T("充电中", "Charging") : Loc.T("已接通电源", "Plugged in")
            : battery.TimeLeft is { } left ? Loc.T($"剩余 {Duration(left)}", $"{Duration(left)} left") : Loc.T("使用电池", "On battery");
        Detail(_battery, time);
        _health!.Visibility = battery.Health is null ? Visibility.Collapsed : Visibility.Visible;
        if (battery.Health is { } health) _health.Text = Loc.T("健康度 ", "Health ") + MonitorFormat.Percent(health) + " %";
        Value(_battery, PowerText.Percent(battery.Percent).Replace("%", " %"), Hot(BatteryModel.Level(battery, _low), Look.Primary));
    }

    private void UpdateFrames(MonitorSnapshot snapshot)
    {
        if (!snapshot.Frames.TryGet(out var frames)) { Empty(_frames!, snapshot.Frames.Reason); return; }
        Detail(_frames!, frames.Program + (frames.Refresh is { } hz ? $" · {hz:0} Hz" : ""));
        Value(_frames!, $"{frames.PerSecond:0} FPS", null);
    }

    private static string Duration(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{Math.Max(1, time.Minutes)} min";

    // The detail line, with an emphasised part after it (a temperature) in its own color.
    private void Detail(Row row, string text, string? strong = null, string? strongColor = null)
    {
        row.Detail.Inlines.Clear();
        row.Detail.Inlines.Add(new Run(text));
        if (strong is null) return;
        row.Detail.Inlines.Add(new Run((text.Length > 0 ? "  " : "") + strong) { FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(strongColor ?? Look.Primary) });
    }
    private void Value(Row row, string text, string? color)
    {
        row.Value.Text = text;
        row.Value.Foreground = UI.Brush(color ?? Look.Primary);
    }
    private void Empty(Row row, string reason)
    {
        Detail(row, reason); Value(row, "—", null);
    }
    // A level's color, or the given one while normal.
    private string Hot(MemoryLevel level, string normal) => _colors.Level(level, normal);
}

// A thin rounded bar: shares side by side over a track, each in its color.
internal sealed class ShareBar : FrameworkElement
{
    private IReadOnlyList<(double Share, string Color)> _parts = [];
    private Brush _track = Brushes.Transparent;

    public ShareBar() { Height = 4; SnapsToDevicePixels = true; }
    public void SetTrack(string color) { _track = UI.Brush(color); InvalidateVisual(); }
    public void Set(IReadOnlyList<(double Share, string Color)> parts) { _parts = parts; InvalidateVisual(); }
    protected override Size MeasureOverride(Size availableSize) => new(0, Height);

    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, height = ActualHeight, radius = height / 2;
        if (width <= 0) return;
        drawing.DrawRoundedRectangle(_track, null, new Rect(0, 0, width, height), radius, radius);
        double x = 0;
        foreach (var (share, color) in _parts)
        {
            double w = Math.Clamp(share, 0, 1) * width;
            if (w <= 0) continue;
            w = Math.Min(w, width - x);
            drawing.DrawRoundedRectangle(UI.Brush(color), null, new Rect(x, 0, w, height), radius, radius);
            x += w;
        }
    }
}

// Line icons for the rows and the ports page, from Lucide (ISC): cpu, memory-stick, gpu, arrow-up-down, zap, battery-medium, globe, lock, copy,
// external-link, gauge; on a 24 unit grid.
internal static class MonitorIcons
{
    public static FrameworkElement Icon(string name, double size, Brush brush)
    {
        string data = name switch
        {
            "cpu" => "M6,4 H18 A2,2 0 0 1 20,6 V18 A2,2 0 0 1 18,20 H6 A2,2 0 0 1 4,18 V6 A2,2 0 0 1 6,4 Z M10,9 H14 A1,1 0 0 1 15,10 V14 A1,1 0 0 1 14,15 H10 A1,1 0 0 1 9,14 V10 A1,1 0 0 1 10,9 Z"
                + " M15,2 V4 M15,20 V22 M2,15 H4 M2,9 H4 M20,15 H22 M20,9 H22 M9,2 V4 M9,20 V22",
            "memory" => "M6,19 V16 M10,19 V16 M14,19 V16 M18,19 V16 M8,11 V9 M16,11 V9 M12,11 V9 M2,15 H22"
                + " M4,5 H20 A2,2 0 0 1 22,7 V8.1 A2,2 0 0 0 22,11.9 V17 A2,2 0 0 1 20,19 H4 A2,2 0 0 1 2,17 V11.9 A2,2 0 0 0 2,8.1 V7 A2,2 0 0 1 4,5 Z",
            "gpu" => "M2,21 V3 M2,5 H20 A2,2 0 0 1 22,7 V15 A2,2 0 0 1 20,17 H2.26 M7,17 V20 A1,1 0 0 0 8,21 H13 A1,1 0 0 0 14,20 V17"
                + " M18,11 A2,2 0 1 1 14,11 A2,2 0 1 1 18,11 Z M10,11 A2,2 0 1 1 6,11 A2,2 0 1 1 10,11 Z",
            "network" => "M21,16 L17,20 L13,16 M17,20 V4 M3,8 L7,4 L11,8 M7,4 V20",
            "power" => "M4,14 A1,1 0 0 1 3.22,12.37 L13.12,2.17 A0.5,0.5 0 0 1 13.98,2.63 L12.06,8.65 A1,1 0 0 0 13,10 H20 A1,1 0 0 1 20.78,11.63"
                + " L10.88,21.83 A0.5,0.5 0 0 1 10.02,21.37 L11.94,15.35 A1,1 0 0 0 11,14 Z",
            "battery" => "M4,6 H16 A2,2 0 0 1 18,8 V16 A2,2 0 0 1 16,18 H4 A2,2 0 0 1 2,16 V8 A2,2 0 0 1 4,6 Z M22,14 V10 M6,10 V14 M10,10 V14",
            "hourglass" => "M5,22 H19 M5,2 H19 M17,22 V17.828 A2,2 0 0 0 16.414,16.414 L12,12 L7.586,16.414 A2,2 0 0 0 7,17.828 V22 M7,2 V6.172 A2,2 0 0 0 7.586,7.586 L12,12 L16.414,7.586 A2,2 0 0 0 17,6.172 V2",
            "globe" => "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z M12,2 A14.5,14.5 0 0 0 12,22 A14.5,14.5 0 0 0 12,2 Z M2,12 H22",
            "lock" => "M5,11 H19 A2,2 0 0 1 21,13 V20 A2,2 0 0 1 19,22 H5 A2,2 0 0 1 3,20 V13 A2,2 0 0 1 5,11 Z M7,11 V7 A5,5 0 0 1 17,7 V11",
            "copy" => "M10,8 H20 A2,2 0 0 1 22,10 V20 A2,2 0 0 1 20,22 H10 A2,2 0 0 1 8,20 V10 A2,2 0 0 1 10,8 Z M4,16 C2.9,16 2,15.1 2,14 V4 C2,2.9 2.9,2 4,2 H14 C15.1,2 16,2.9 16,4",
            "open" => "M15,3 H21 V9 M10,14 L21,3 M18,13 V19 A2,2 0 0 1 16,21 H5 A2,2 0 0 1 3,19 V8 A2,2 0 0 1 5,6 H11",
            _ => "M12,14 L16,10 M3.34,19 A10,10 0 1 1 20.66,19"
        };
        var path = new Path { Data = Geometry.Parse(data), Stroke = brush, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
