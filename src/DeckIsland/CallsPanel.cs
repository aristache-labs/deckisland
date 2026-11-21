using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeckIsland.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DeckIsland;

// The Calls tab: the microphone as a big mute button, and the speaker and microphone to test before a call. Testing never changes the
// system's default devices; it plays a short tone on the chosen speaker, or shows the chosen microphone's level for a few seconds.
public partial class MainWindow
{
    private Button? _callMic;
    private TextBlock? _callMicState, _callMicHint;
    private Path? _callMicGlyph;
    private string? _callSpeaker, _callMicrophone;
    private (int Theme, string Language) _callsAppearance;
    private const string MicPath = "M10,2.5 A2.5,2.5 0 0 1 12.5,5 V10 A2.5,2.5 0 0 1 7.5,10 V5 A2.5,2.5 0 0 1 10,2.5 Z M5,9.5 A5,5 0 0 0 15,9.5 M10,14.5 V17.5";

    private void InitializeCalls()
    {
        _app.CallsChanged += CallsChanged;
        Closed += (_, _) => _app.CallsChanged -= CallsChanged;
    }

    private void CallsPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Calls);

    private void CallsChanged()
    {
        if (Settings.CallsPage && CurrentPage == DeckPage.Calls && _callMic is not null) UpdateCallMic();
    }

    private void RenderCalls()
    {
        RefreshStatus();
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility = ShelfContent.Visibility =
            DevicesContent.Visibility = WeatherContent.Visibility = FocusContent.Visibility = RemindersContent.Visibility = NotificationsContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        double height = ContentCanvas.Height;
        if (!double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        CallsContent.Margin = new Thickness(_header.Side, 0, _header.Side, MonitorBottom(height));
        var appearance = (Look.Generation, Loc.Language);
        if (CallsContent.Children.Count > 0 && _callsAppearance == appearance) { UpdateCallMic(); return; }
        _callsAppearance = appearance;
        CallsContent.Children.Clear();

        // The microphone: a round button, "Mic on" under it and what a press does.
        _callMicGlyph = new Path { Data = Geometry.Parse(MicPath), Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.6, Stretch = Stretch.Uniform, Width = 30, Height = 30,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _callMic = new Button
        {
            Content = _callMicGlyph, Width = 84, Height = 84, Style = (Style)FindResource("WidgetSurfaceButton"), Background = UI.Brush(Look.Hex("fillSelected")),
            Clip = new EllipseGeometry(new Point(42, 42), 42, 42), HorizontalAlignment = HorizontalAlignment.Center
        };
        _callMic.Click += (_, _) => _app.ToggleMicMute();
        _callMicState = WeatherText("", 14);
        _callMicState.FontWeight = FontWeights.SemiBold;
        _callMicState.HorizontalAlignment = HorizontalAlignment.Center;
        _callMicState.Margin = new Thickness(0, 8, 0, 0);
        _callMicHint = WeatherText("", 12);
        _callMicHint.Foreground = UI.Brush(Look.Secondary);
        _callMicHint.HorizontalAlignment = HorizontalAlignment.Center;
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 170, Children = { _callMic, _callMicState, _callMicHint } };

        // The tests.
        var title = WeatherText(Loc.T("通话准备就绪", "Ready for calls"), 15);
        title.FontWeight = FontWeights.SemiBold;
        var hint = WeatherText(Loc.T("加入前先测试麦克风和扬声器。", "Test your mic and speaker before you join."), 12);
        hint.Foreground = UI.Brush(Look.Secondary);
        hint.Margin = new Thickness(0, 2, 0, 8);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, hint, DeviceRow(DataFlow.Render), DeviceRow(DataFlow.Capture) } };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        CallsContent.Children.Add(grid);
        UpdateCallMic();
    }

    private void UpdateCallMic()
    {
        if (_callMic is null || _callMicState is null || _callMicHint is null || _callMicGlyph is null) return;
        bool muted = _app.MicMuted;
        _callMicState.Text = muted ? Loc.T("麦克风已静音", "Mic muted") : Loc.T("麦克风已开启", "Mic on");
        _callMicHint.Text = muted ? Loc.T("点击取消静音", "Click to unmute") : Loc.T("点击静音", "Click to mute");
        _callMicGlyph.Stroke = UI.Brush(muted ? Look.Hex("danger") : Look.Primary);
        _callMicGlyph.Data = Geometry.Parse(muted ? MicPath + " M3,3 L17,17" : MicPath);
        string name = $"{_callMicState.Text}. {_callMicHint.Text}";
        AutomationProperties.SetName(_callMic, name);
        _callMic.ToolTip = name;
    }

    // One device row: its icon, a button that opens the list of devices, and Test.
    private FrameworkElement DeviceRow(DataFlow flow)
    {
        bool speaker = flow == DataFlow.Render;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var test = new Button { Content = Loc.T("测试", "Test"), Style = (Style)FindResource("PanelOutlineButton"), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(test, System.Windows.Controls.Dock.Right);
        var choose = new Button { Style = (Style)FindResource("PanelOutlineButton"), HorizontalContentAlignment = HorizontalAlignment.Left };
        var name = WeatherText("", 12.5);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        choose.Content = name;
        var level = new Border { Height = 3, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Background = UI.Brush(Look.Hex("accent")),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 2), IsHitTestVisible = false };
        string Chosen() => (speaker ? _callSpeaker : _callMicrophone) ?? "";
        void Show()
        {
            using var devices = new MMDeviceEnumerator();
            string id = Chosen();
            string label = Loc.T("默认设备", "Default device");
            try
            {
                var device = id.Length > 0 ? devices.GetDevice(id) : devices.GetDefaultAudioEndpoint(flow, Role.Communications);
                label = device.FriendlyName;
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException) { }
            name.Text = label;
            AutomationProperties.SetName(choose, (speaker ? Loc.T("扬声器：", "Speaker: ") : Loc.T("麦克风：", "Microphone: ")) + label);
        }
        choose.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = choose, Placement = PlacementMode.Bottom };
            using var devices = new MMDeviceEnumerator();
            foreach (var device in devices.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                string id = device.ID, label = device.FriendlyName;
                var item = new MenuItem { Header = label, IsChecked = id == Chosen() };
                item.Click += (_, _) => { if (speaker) _callSpeaker = id; else _callMicrophone = id; Show(); };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        test.Click += (_, _) => { if (speaker) PlayTestTone(Chosen(), test); else ShowMicLevel(Chosen(), test, level); };
        var icon = new Path
        {
            Data = Geometry.Parse(speaker ? "M3,11 V9 A7,7 0 0 1 17,9 V11 M3,10 H5 V16 H3 Z M15,10 H17 V16 H15 Z" : MicPath), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.4,
            Stretch = Stretch.Uniform, Width = 15, Height = 15, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
        row.Children.Add(test);
        row.Children.Add(icon);
        var chooseHost = new Grid { Children = { choose, level } };
        row.Children.Add(chooseHost);
        Show();
        level.Tag = chooseHost;
        return row;
    }

    // A short tone on the chosen speaker; the Test button says so meanwhile.
    private void PlayTestTone(string deviceId, Button button)
    {
        string label = (string)button.Content;
        try
        {
            using var devices = new MMDeviceEnumerator();
            var device = deviceId.Length > 0 ? devices.GetDevice(deviceId) : devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
            var tone = new SignalGenerator(44100, 2) { Frequency = 660, Gain = .12, Type = SignalGeneratorType.Sin }.Take(TimeSpan.FromMilliseconds(700));
            var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            button.IsEnabled = false;
            button.Content = "…";
            output.PlaybackStopped += (_, _) => Dispatcher.BeginInvoke(() => { button.Content = label; button.IsEnabled = true; output.Dispose(); });
            output.Init(tone);
            output.Play();
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or NAudio.MmException)
        {
            button.Content = label;
            button.IsEnabled = true;
        }
    }

    // The chosen microphone's level for three seconds, as a bar under its name.
    private void ShowMicLevel(string deviceId, Button button, Border bar)
    {
        string label = (string)button.Content;
        MMDevice device;
        try
        {
            using var devices = new MMDeviceEnumerator();
            device = deviceId.Length > 0 ? devices.GetDevice(deviceId) : devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException) { return; }
        button.IsEnabled = false;
        var end = DateTime.UtcNow.AddSeconds(3);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            double width = (bar.Tag as FrameworkElement)?.ActualWidth ?? 0;
            try { bar.Width = Math.Max(0, (width - 16) * Math.Clamp(device.AudioMeterInformation.MasterPeakValue * 1.6, 0, 1)); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { end = DateTime.MinValue; }
            if (DateTime.UtcNow < end) return;
            timer.Stop();
            bar.Width = 0;
            button.IsEnabled = true;
            button.Content = label;
            device.Dispose();
        };
        timer.Start();
    }
}
