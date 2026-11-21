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
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace DeckIsland;

// The Calls tab: the microphone as a big mute button, and the speaker and microphone to test before a call. Testing never changes the
// system's default devices; it plays a short tone on the chosen speaker, or shows the chosen microphone's level for a few seconds.
public partial class MainWindow
{
    private Button? _callMic;
    private TextBlock? _callMicState, _callMicHint;
    private Path? _callMicGlyph, _callCameraGlyph;
    private Ellipse? _callMicRing;
    private StackPanel? _callMicBlock, _callCameraBlock;
    private Button? _callMuteChip, _callCameraButton;
    private TextBlock? _callMuteLabel, _callCameraLabel, _callSay;
    private Image? _callPreview;
    private TextBlock? _callTitle, _callHint, _callTimer, _callWhere;
    private StackPanel? _callLiveBlock;
    private bool _callHearing, _callTesting;
    private MediaCapture? _camera;
    private MediaFrameReader? _cameraReader;
    private System.Windows.Media.Imaging.WriteableBitmap? _cameraBitmap;
    private int _cameraBusy;
    private string? _callSpeaker, _callMicrophone;
    private (int Theme, string Language) _callsAppearance;
    private const string MicPath = "M10,2.5 A2.5,2.5 0 0 1 12.5,5 V10 A2.5,2.5 0 0 1 7.5,10 V5 A2.5,2.5 0 0 1 10,2.5 Z M5,9.5 A5,5 0 0 0 15,9.5 M10,14.5 V17.5";

    private void InitializeCalls()
    {
        _app.CallsChanged += CallsChanged;
        Closed += (_, _) => { _app.CallsChanged -= CallsChanged; StopCamera(); };
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

        // The microphone: a round button, "Mic on" under it and what a press does. While the camera is being checked the button gives
        // its place to the picture and the mute switch becomes a small pill under it.
        _callMicGlyph = new Path { Data = Geometry.Parse(MicPath), Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.6, Stretch = Stretch.Uniform, Width = 30, Height = 30,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _callMicRing = new Ellipse { Width = 100, Height = 100, StrokeThickness = 2, Stroke = Brushes.Transparent, IsHitTestVisible = false };
        _callMic = new Button
        {
            Content = _callMicGlyph, Width = 100, Height = 100, Style = (Style)FindResource("WidgetSurfaceButton"), Background = UI.Brush(Look.Hex("fill")),
            Clip = new EllipseGeometry(new Point(50, 50), 50, 50), HorizontalAlignment = HorizontalAlignment.Center
        };
        _callMic.Click += (_, _) => _app.ToggleMicMute();
        var micHost = new Grid { Width = 100, Height = 100, HorizontalAlignment = HorizontalAlignment.Center, Children = { _callMic, _callMicRing } };
        _callMicState = WeatherText("", 14);
        _callMicState.FontWeight = FontWeights.SemiBold;
        _callMicState.HorizontalAlignment = HorizontalAlignment.Center;
        _callMicState.Margin = new Thickness(0, 8, 0, 0);
        _callMicHint = WeatherText("", 12);
        _callMicHint.Foreground = UI.Brush(Look.Secondary);
        _callMicHint.HorizontalAlignment = HorizontalAlignment.Center;
        _callMicBlock = new StackPanel { Children = { micHost, _callMicState, _callMicHint } };

        _callPreview = new Image { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(-1, 1) };
        var previewBox = new Border { Width = 176, Height = 99, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = UI.Brush(Look.Hex("fill")), Child = _callPreview, HorizontalAlignment = HorizontalAlignment.Center };
        var muteGlyph = new Path { Data = Geometry.Parse(MicPath), Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 13, Height = 13, Margin = new Thickness(0, 0, 6, 0) };
        _callMuteLabel = WeatherText("", 13);
        _callMuteChip = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Background = UI.Brush(Look.Hex("fill")), Padding = new Thickness(16, 7, 16, 7), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0), Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { muteGlyph, _callMuteLabel } }
        };
        _callMuteChip.Click += (_, _) => _app.ToggleMicMute();
        _callCameraBlock = new StackPanel { Children = { previewBox, _callMuteChip }, Visibility = Visibility.Collapsed };
        var left = new Grid { VerticalAlignment = VerticalAlignment.Center, Width = 196, Children = { _callMicBlock, _callCameraBlock } };

        // The tests.
        var title = WeatherText(Loc.T("通话准备就绪", "Ready for calls"), 15);
        title.FontWeight = FontWeights.SemiBold;
        _callTitle = title;
        var hint = WeatherText(Loc.T("加入前先测试麦克风和扬声器。", "Test your mic and speaker before you join."), 12);
        hint.Foreground = UI.Brush(Look.Secondary);
        hint.Margin = new Thickness(0, 2, 0, 8);
        _callHint = hint;

        // In a call: the running time, a Live pill and the app that has the microphone, in place of the title.
        _callTimer = WeatherText("", 30);
        _callTimer.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _callTimer.FontWeight = FontWeights.Bold;
        var liveDot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var liveText = WeatherText(Loc.T("通话中", "Live"), 12);
        liveText.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        liveText.FontWeight = FontWeights.SemiBold;
        var livePill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 12, 3), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x30, 0xD1, 0x58)), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { liveDot, liveText } } };
        _callWhere = WeatherText("", 12);
        _callWhere.Foreground = UI.Brush(Look.Secondary);
        _callWhere.Margin = new Thickness(0, 0, 0, 8);
        _callLiveBlock = new StackPanel { Visibility = Visibility.Collapsed, Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { _callTimer, livePill } }, _callWhere } };

        _callCameraGlyph = new Path { Data = Geometry.Parse("M2,6 H5 L6.5,4 H11.5 L13,6 H16 V15 H2 Z M9,8.2 A2.6,2.6 0 1 1 9,13.4 A2.6,2.6 0 1 1 9,8.2 Z"), Stroke = UI.Brush(Look.Primary),
            StrokeThickness = 1.3, Stretch = Stretch.Uniform, Width = 15, Height = 15, Margin = new Thickness(0, 0, 8, 0), StrokeLineJoin = PenLineJoin.Round };
        _callCameraLabel = WeatherText("", 13);
        _callCameraButton = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Background = UI.Brush(Look.Hex("fill")), Padding = new Thickness(14, 7, 16, 7), HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _callCameraGlyph, _callCameraLabel } }
        };
        _callCameraButton.Click += (_, _) => ToggleCamera();
        _callSay = WeatherText(Loc.T("说点什么", "Say something"), 12);
        _callSay.Foreground = UI.Brush(Look.Secondary);
        _callSay.VerticalAlignment = VerticalAlignment.Center;
        _callSay.HorizontalAlignment = HorizontalAlignment.Right; _callSay.Margin = new Thickness(0, 0, 4, 0);
        _callSay.Visibility = Visibility.Collapsed;
        var cameraRow = new Grid { Margin = new Thickness(0, 2, 0, 0), Children = { _callCameraButton, _callSay } };

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, hint, _callLiveBlock, DeviceRow(DataFlow.Render), DeviceRow(DataFlow.Capture), cameraRow } };

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
        if (_callMic is null || _callMicState is null || _callMicHint is null || _callMicGlyph is null || _callMicRing is null || _callCameraBlock is null || _callMicBlock is null
            || _callMuteLabel is null || _callCameraLabel is null || _callCameraButton is null || _callSay is null || _callCameraGlyph is null
            || _callTitle is null || _callHint is null || _callLiveBlock is null || _callTimer is null || _callWhere is null) return;
        bool muted = _app.MicMuted, camera = _camera is not null, hearing = _callHearing && !muted;
        var green = Color.FromRgb(0x30, 0xD1, 0x58);
        bool inCall = _app.MicLive && _app.MicLiveFor is not null;
        _callTitle.Visibility = _callHint.Visibility = inCall ? Visibility.Collapsed : Visibility.Visible;
        _callLiveBlock.Visibility = inCall ? Visibility.Visible : Visibility.Collapsed;
        if (inCall)
        {
            var span = _app.MicLiveFor ?? TimeSpan.Zero;
            _callTimer.Text = span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
            _callWhere.Text = Loc.T("在 ", "In ") + _app.MicLiveApp;
        }
        _callMicBlock.Visibility = camera ? Visibility.Collapsed : Visibility.Visible;
        _callCameraBlock.Visibility = camera ? Visibility.Visible : Visibility.Collapsed;
        _callMicState.Text = muted ? Loc.T("已静音", "Muted") : hearing ? Loc.T("正在听到你", "Hearing you") : Loc.T("麦克风已开启", "Mic on");
        _callMicHint.Text = muted ? Loc.T("点击开口说话", "Click to talk") : Loc.T("点击静音", "Click to mute");
        _callMicState.Foreground = muted ? UI.Brush(Look.Hex("danger")) : hearing ? new SolidColorBrush(green) : UI.Brush(Look.Primary);
        _callMic.Background = muted ? UI.Brush(Look.Hex("danger")) : UI.Brush(Look.Hex("fill"));
        _callMicGlyph.Stroke = muted ? Brushes.White : UI.Brush(Look.Primary);
        _callMicRing.Stroke = hearing ? new SolidColorBrush(green) : Brushes.Transparent;
        _callMicGlyph.Data = Geometry.Parse(muted ? MicPath + " M3,3 L17,17" : MicPath);
        _callMuteLabel.Text = muted ? Loc.T("取消静音", "Unmute") : Loc.T("静音", "Mute");
        _callCameraLabel.Text = camera ? Loc.T("摄像头已开启", "Camera on") : Loc.T("检查摄像头", "Check camera");
        _callCameraButton.Background = camera ? UI.Brush(Look.Primary) : UI.Brush(Look.Hex("fill"));
        _callCameraLabel.Foreground = camera ? UI.Brush(Look.Hex("surface")) : UI.Brush(Look.Primary);
        _callCameraGlyph.Stroke = camera ? UI.Brush(Look.Hex("surface")) : UI.Brush(Look.Primary);
        _callSay.Visibility = _callTesting && !hearing ? Visibility.Visible : Visibility.Collapsed;
        string name = $"{_callMicState.Text}. {_callMicHint.Text}";
        AutomationProperties.SetName(_callMic, name);
        _callMic.ToolTip = name;
    }

    // The camera, shown mirrored as in a call. Nothing is recorded or kept: frames go straight to the picture, and the camera is released as
    // soon as the button is pressed again, the tab is left or the island closes.
    private void ToggleCamera()
    {
        if (_camera is not null) { StopCamera(); return; }
        _ = StartCameraAsync();
    }

    private async Task StartCameraAsync()
    {
        MediaCapture? capture = null;
        try
        {
            capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video, SharingMode = MediaCaptureSharingMode.SharedReadOnly, MemoryPreference = MediaCaptureMemoryPreference.Cpu
            });
            var source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source is null) { capture.Dispose(); return; }
            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.FrameArrived += CameraFrame;
            _camera = capture; _cameraReader = reader;
            await reader.StartAsync();
            UpdateCallMic();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // No camera, or access to it is off: the button stays as it was.
            capture?.Dispose(); _camera = null; _cameraReader = null;
            UpdateCallMic();
        }
    }

    private void CameraFrame(MediaFrameReader reader, MediaFrameArrivedEventArgs args)
    {
        using var frame = reader.TryAcquireLatestFrame();
        var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null) return;
        if (Interlocked.Exchange(ref _cameraBusy, 1) == 1) return;
        SoftwareBitmap? converted = null;
        try
        {
            converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && bitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied ? SoftwareBitmap.Copy(bitmap)
                : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            int width = converted.PixelWidth, height = converted.PixelHeight;
            var buffer = new Windows.Storage.Streams.Buffer((uint)(width * height * 4));
            converted.CopyToBuffer(buffer);
            var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_camera is null || CurrentPage != DeckPage.Calls || !_expanded) { StopCamera(); return; }
                    if (_cameraBitmap is null || _cameraBitmap.PixelWidth != width || _cameraBitmap.PixelHeight != height)
                        _cameraBitmap = new System.Windows.Media.Imaging.WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
                    _cameraBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                    if (_callPreview is not null && !ReferenceEquals(_callPreview.Source, _cameraBitmap)) _callPreview.Source = _cameraBitmap;
                }
                finally { Interlocked.Exchange(ref _cameraBusy, 0); }
            });
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            Interlocked.Exchange(ref _cameraBusy, 0);
        }
        finally { converted?.Dispose(); }
    }

    private void StopCamera()
    {
        var reader = _cameraReader; var capture = _camera;
        _cameraReader = null; _camera = null; _cameraBitmap = null;
        if (_callPreview is not null) _callPreview.Source = null;
        Interlocked.Exchange(ref _cameraBusy, 0);
        if (reader is not null) reader.FrameArrived -= CameraFrame;
        try { _ = reader?.StopAsync(); } catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        capture?.Dispose();
        UpdateCallMic();
    }

    // One device row: a card with its icon, a button that opens the list of devices, and Test. While a test runs a level fills the card.
    private FrameworkElement DeviceRow(DataFlow flow)
    {
        bool speaker = flow == DataFlow.Render;
        var card = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        var fillCard = new Border { CornerRadius = new CornerRadius(15), Background = UI.Brush(Look.Hex("fill")) };
        var level = new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(15), IsHitTestVisible = false, Opacity = .45,
            Background = speaker ? UI.Brush(Look.Hex("accent")) : new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)) };
        var test = new Button { Content = Loc.T("测试", "Test"), Style = (Style)FindResource("WidgetSurfaceButton"), Background = UI.Brush(Look.Hex("fillSelected")), Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 4, 4, 4), HorizontalAlignment = HorizontalAlignment.Right };
        var choose = new Button { Style = (Style)FindResource("WidgetSurfaceButton"), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 9, 84, 9) };
        var name = WeatherText("", 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var chevron = new Path { Data = Geometry.Parse("M1,1 L4,4 L7,1"), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.3, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var icon = new Path
        {
            Data = Geometry.Parse(speaker ? "M3,11 V9 A7,7 0 0 1 17,9 V11 M3,10 H5 V16 H3 Z M15,10 H17 V16 H15 Z" : MicPath), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.4,
            Stretch = Stretch.Uniform, Width = 15, Height = 15, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        choose.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, name, chevron } };
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
        test.Click += (_, _) => { if (speaker) PlayTestTone(Chosen(), test, level, card); else ShowMicLevel(Chosen(), test, level, card); };
        card.Children.Add(fillCard); card.Children.Add(level); card.Children.Add(choose); card.Children.Add(test);
        Show();
        return card;
    }

    // A short tone on the chosen speaker; the Test button says so meanwhile.
    private void PlayTestTone(string deviceId, Button button, Border bar, FrameworkElement card)
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
            output.PlaybackStopped += (_, _) => Dispatcher.BeginInvoke(() => { bar.BeginAnimation(FrameworkElement.WidthProperty, null); bar.Width = 0; button.Content = label; button.IsEnabled = true; output.Dispose(); });
            output.Init(tone);
            output.Play();
            bar.BeginAnimation(FrameworkElement.WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(0, card.ActualWidth, TimeSpan.FromMilliseconds(700)));
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or NAudio.MmException)
        {
            button.Content = label;
            button.IsEnabled = true;
        }
    }

    // The chosen microphone's level for four seconds, filling the card; the button says it is listening, and the big button shows when you are heard.
    private void ShowMicLevel(string deviceId, Button button, Border bar, FrameworkElement card)
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
        var green = Color.FromRgb(0x30, 0xD1, 0x58);
        button.Content = Loc.T("正在聆听", "Listening");
        button.Background = new SolidColorBrush(green);
        _callTesting = true; UpdateCallMic();
        var end = DateTime.UtcNow.AddSeconds(4);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            bool heard = false;
            try
            {
                double peak = Math.Clamp(device.AudioMeterInformation.MasterPeakValue * 1.6, 0, 1);
                bar.Width = card.ActualWidth * peak; heard = peak > .08;
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { end = DateTime.MinValue; }
            if (heard != _callHearing) { _callHearing = heard; UpdateCallMic(); }
            if (DateTime.UtcNow < end) return;
            timer.Stop();
            bar.Width = 0; _callHearing = false; _callTesting = false;
            button.IsEnabled = true;
            button.Content = label;
            button.Background = UI.Brush(Look.Hex("fillSelected"));
            device.Dispose();
            UpdateCallMic();
        };
        timer.Start();
    }
}
