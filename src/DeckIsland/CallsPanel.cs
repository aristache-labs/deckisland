using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;
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
    private Path? _callMuteGlyph, _callRowMic;
    private TextBlock? _callTitle, _callHint, _callTimer, _callWhere;
    private StackPanel? _callLiveBlock;
    private bool _callTesting, _cameraStarting;
    private CallMicResponse _callMicResponse;
    private int _cameraRequest;
    private MediaCapture? _camera;
    private MediaFrameReader? _cameraReader;
    private System.Windows.Media.Imaging.WriteableBitmap? _cameraBitmap;
    private int _cameraBusy;
    private Action? _stopMicTest;
    internal bool CameraPreviewActive => _camera is not null;
    internal bool MicrophoneTestActive => _stopMicTest is not null;
    private StackPanel? _callControls;
    private Grid? _callDevicePicker;
    private Button? _callDevicePickerAnchor;
    private string? _callMicResult;
    private string? _callSpeaker, _callMicrophone;
    private CallDeviceLevel? _callOutputLevel, _callInputLevel;
    private DispatcherTimer? _callLevelsTimer;
    private MMDeviceEnumerator? _callLevelDevices;
    private long _callLevelCheckedAt;
    private (int Theme, string Language) _callsAppearance;
    private static readonly Brush CallGreen = UI.Brush("#30D158");
    private static readonly Geometry MicOnGlyph = Geometry.Parse(MicPath);
    private static readonly Geometry MicOffGlyph = Geometry.Parse(MicPath + " M3,3 L17,17");
    private const string MicPath = "M10,2.5 A2.5,2.5 0 0 1 12.5,5 V10 A2.5,2.5 0 0 1 7.5,10 V5 A2.5,2.5 0 0 1 10,2.5 Z M5,9.5 A5,5 0 0 0 15,9.5 M10,14.5 V17.5";

    private void InitializeCalls()
    {
        IslandContent.SizeChanged += (_, _) => PlaceSensorIndicator();
        _app.CallsChanged += CallsChanged;
        CallsContent.IsVisibleChanged += (_, _) =>
        {
            if (CallsContent.IsVisible) UpdateCallMic();
            else StopCallPreview();
        };
        Closed += (_, _) => { _app.CallsChanged -= CallsChanged; StopCallPreview(); };
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
            DevicesContent.Visibility = WeatherContent.Visibility = FocusContent.Visibility = RemindersContent.Visibility = NotificationsContent.Visibility = ScratchpadContent.Visibility = ConverterContent.Visibility = TerminalContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        double height = ContentCanvas.Height;
        if (!double.IsFinite(height)) return;
        var header = PanelHeader.For(30, 18);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        CallsContent.Margin = new Thickness(header.Side, 0, header.Side, 12);
        double width = Math.Max(0, ContentCanvas.Width - header.Side * 2);
        double bodyHeight = Math.Max(0, height - header.Row - 12);
        var appearance = (Look.Generation, Loc.Language);
        if (_callsAppearance == appearance && CallsContent.Children.Count > 0 &&
            CallsContent.Children[0] is Viewbox { Child: Grid body })
        {
            body.Width = width;
            body.MinHeight = bodyHeight;
            if (_callDevicePicker is { } picker) picker.Height = bodyHeight;
            UpdateCallMic();
            return;
        }
        CloseCallDevicePicker();
        _stopMicTest?.Invoke();
        StopCallLevels();
        _callsAppearance = appearance;
        CallsContent.Children.Clear();

        // The microphone: a round button, "Mic on" under it and what a press does. While the camera is being checked the button gives
        // its place to the picture and the mute switch becomes a small pill under it.
        _callMicGlyph = new Path { Data = MicOnGlyph, Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.6, Stretch = Stretch.Uniform, Width = 23, Height = 26,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _callMicRing = new Ellipse { Width = 72, Height = 72, StrokeThickness = 2, Stroke = CallGreen, Fill = UI.Brush("#1430D158"), Opacity = 0, IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Color.FromRgb(0x30, 0xD1, 0x58), ShadowDepth = 0, BlurRadius = 11 } };
        _callMic = new Button
        {
            Content = _callMicGlyph, Width = 72, Height = 72, Style = (Style)FindResource("WidgetSurfaceButton"), Background = UI.Brush(Look.Hex("fill")),
            Clip = new EllipseGeometry(new Point(36, 36), 36, 36), HorizontalAlignment = HorizontalAlignment.Center
        };
        _callMic.Click += (_, _) => _app.ToggleMicMute();
        var micHost = new Grid { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center, Children = { _callMic, _callMicRing } };
        _callMicState = WeatherText("", 12);
        _callMicState.FontWeight = FontWeights.SemiBold;
        _callMicState.HorizontalAlignment = HorizontalAlignment.Center;
        _callMicState.Margin = new Thickness(0, 8, 0, 0);
        _callMicHint = WeatherText("", 12);
        _callMicHint.Foreground = UI.Brush(Look.Secondary);
        _callMicHint.HorizontalAlignment = HorizontalAlignment.Center;
        _callMicBlock = new StackPanel { Children = { micHost, _callMicState, _callMicHint } };

        _callPreview = new Image { Source = _cameraBitmap, Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(-1, 1) };
        var previewBox = new Border { Width = 144, Height = 81, CornerRadius = new CornerRadius(12), Clip = new RectangleGeometry(new Rect(0, 0, 144, 81), 12, 12),
            Background = UI.Brush(Look.Hex("fill")), Child = _callPreview, HorizontalAlignment = HorizontalAlignment.Center };
        var muteGlyph = _callMuteGlyph = new Path { Data = MicOnGlyph, Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        _callMuteLabel = WeatherText("", 12);
        _callMuteChip = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Background = UI.Brush(Look.Hex("fillSelected")),
            Padding = new Thickness(12, 0, 12, 0), Height = 30, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0), Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { muteGlyph, _callMuteLabel } }
        };
        _callMuteChip.Click += (_, _) => _app.ToggleMicMute();
        _callCameraBlock = new StackPanel { Children = { previewBox, _callMuteChip }, Visibility = Visibility.Collapsed };
        var left = new Grid { VerticalAlignment = VerticalAlignment.Center, Width = 144, Margin = new Thickness(0, 0, 15, 0), Children = { _callMicBlock, _callCameraBlock } };

        // The tests.
        var title = WeatherText(Loc.T("通话准备就绪", "Ready for calls"), 15);
        title.FontWeight = FontWeights.SemiBold;
        _callTitle = title;
        var hint = WeatherText(Loc.T("加入前先测试麦克风和扬声器。", "Test your mic and speaker before you join."), 12);
        hint.Foreground = UI.Brush(Look.Secondary);
        hint.Margin = new Thickness(0, 2, 0, 8);
        _callHint = hint;

        // In a call: the running time, a Live pill and the app that has the microphone, in place of the title.
        _callTimer = WeatherText("", 27);
        _callTimer.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _callTimer.FontWeight = FontWeights.Bold;
        var liveDot = new Ellipse { Width = 7, Height = 7, Fill = CallGreen, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        var liveText = WeatherText(Loc.T("通话中", "Live"), 12);
        liveText.Foreground = CallGreen;
        liveText.FontWeight = FontWeights.SemiBold;
        var livePill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(7, 2, 8, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x30, 0xD1, 0x58)), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { liveDot, liveText } } };
        _callWhere = WeatherText("", 12);
        _callWhere.Foreground = UI.Brush(Look.Secondary);
        _callWhere.Margin = new Thickness(0, 0, 0, 5);
        _callLiveBlock = new StackPanel { Visibility = Visibility.Collapsed, Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { _callTimer, livePill } }, _callWhere } };

        _callCameraGlyph = new Path { Data = Geometry.Parse("M2,6 H5 L6.5,4 H11.5 L13,6 H16 V15 H2 Z M9,8.2 A2.6,2.6 0 1 1 9,13.4 A2.6,2.6 0 1 1 9,8.2 Z"), Stroke = UI.Brush(Look.Primary),
            StrokeThickness = 1.3, Stretch = Stretch.Uniform, Width = 14, Height = 14, Margin = new Thickness(0, 0, 7, 0), StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center };
        _callCameraLabel = WeatherText("", 12);
        _callCameraLabel.FontWeight = FontWeights.SemiBold;
        _callCameraButton = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Background = UI.Brush(Look.Hex("fill")),
            Padding = new Thickness(12, 0, 12, 0), Height = 30, HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _callCameraGlyph, _callCameraLabel } }
        };
        _callCameraButton.Click += (_, _) => ToggleCamera();
        _callSay = WeatherText(Loc.T("说点什么", "Say something"), 12);
        _callSay.Foreground = UI.Brush(Look.Secondary);
        _callSay.VerticalAlignment = VerticalAlignment.Center;
        _callSay.HorizontalAlignment = HorizontalAlignment.Right;
        _callSay.Visibility = Visibility.Collapsed;
        var cameraRow = new DockPanel { LastChildFill = true, Children = { _callCameraButton, _callSay } };
        _callSay.Margin = new Thickness(8, 0, 0, 0);

        _callControls = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, hint, _callLiveBlock, DeviceRow(DataFlow.Render), DeviceRow(DataFlow.Capture), cameraRow } };
        _callDevicePicker = new Grid { Height = bodyHeight, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        var right = new Grid { Children = { _callControls, _callDevicePicker } };

        var grid = new Grid { Width = width, MinHeight = bodyHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        CallsContent.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = grid });
        UpdateCallMic();
    }

    private void UpdateCallMic()
    {
        if (_callMic is null || _callMicState is null || _callMicHint is null || _callMicGlyph is null || _callMicRing is null || _callCameraBlock is null || _callMicBlock is null
            || _callMuteLabel is null || _callCameraLabel is null || _callCameraButton is null || _callSay is null || _callCameraGlyph is null
            || _callTitle is null || _callHint is null || _callLiveBlock is null || _callTimer is null || _callWhere is null) return;
        bool muted = _app.MicMuted, camera = _camera is not null;
        var elapsed = _app.MicLiveFor;
        bool inCall = elapsed is not null;
        _callTitle.Visibility = _callHint.Visibility = inCall ? Visibility.Collapsed : Visibility.Visible;
        _callLiveBlock.Visibility = inCall ? Visibility.Visible : Visibility.Collapsed;
        if (elapsed is { } span)
        {
            _callTimer.Text = HudFormat.Duration(span);
            _callWhere.Text = Loc.T("在 ", "In ") + _app.MicLiveApp;
        }
        UpdateCallLevels();
        _callMicBlock.Visibility = camera ? Visibility.Collapsed : Visibility.Visible;
        _callCameraBlock.Visibility = camera ? Visibility.Visible : Visibility.Collapsed;
        UpdateCallSpeech();
        _callMic.Background = muted ? UI.Brush(Look.Hex("danger")) : UI.Brush(Look.Hex("fill"));
        _callMicGlyph.Stroke = muted ? Brushes.White : UI.Brush(Look.Primary);
        UpdateCallRing();
        _callMicGlyph.Data = muted ? MicOffGlyph : MicOnGlyph;
        _callMuteLabel.Text = muted ? Loc.T("已静音", "Muted") : Loc.T("静音", "Mute");
        _callMuteLabel.Foreground = muted ? Brushes.White : UI.Brush(Look.Primary);
        if (_callMuteChip is not null) _callMuteChip.Background = muted ? UI.Brush(Look.Hex("danger")) : UI.Brush(Look.Hex("fillSelected"));
        if (_callMuteGlyph is not null) { _callMuteGlyph.Stroke = muted ? Brushes.White : UI.Brush(Look.Primary); _callMuteGlyph.Data = muted ? MicOffGlyph : MicOnGlyph; }
        if (_callRowMic is not null) _callRowMic.Data = muted ? MicOffGlyph : MicOnGlyph;
        _callCameraLabel.Text = camera ? Loc.T("摄像头已开启", "Camera on") : Loc.T("检查摄像头", "Check camera");
        _callCameraButton.Background = camera ? UI.Brush(Look.Primary) : UI.Brush(Look.Hex("fill"));
        _callCameraLabel.Foreground = camera ? UI.Brush(Look.Hex("surface")) : UI.Brush(Look.Primary);
        _callCameraGlyph.Stroke = camera ? UI.Brush(Look.Hex("surface")) : UI.Brush(Look.Primary);
        _callSay.Text = _callMicResult ?? Loc.T("说点什么", "Say something");
        _callSay.Visibility = _callTesting || _callMicResult is not null ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(_callCameraButton, _callCameraLabel.Text);
    }

    private void UpdateCallSpeech()
    {
        if (_callMic is null || _callMicState is null || _callMicHint is null) return;
        bool muted = _app.MicMuted, hearing = _callTesting && _callMicResponse.Hearing && !muted;
        _callMicState.Text = muted ? Loc.T("已静音", "Muted") : hearing ? Loc.T("正在听到你", "Hearing you") : Loc.T("麦克风已开启", "Mic on");
        _callMicHint.Text = muted ? Loc.T("点击开口说话", "Click to talk") : Loc.T("点击静音", "Click to mute");
        _callMicState.Foreground = muted ? UI.Brush(Look.Hex("danger")) : hearing ? CallGreen : UI.Brush(Look.Primary);
        string name = $"{_callMicState.Text}. {_callMicHint.Text}";
        AutomationProperties.SetName(_callMic, name);
        _callMic.ToolTip = name;
        if (_callMuteChip is not null) AutomationProperties.SetName(_callMuteChip, name);
    }

    // A pill with no hover layer of its own, so its colour (red while muted) is the same hovered or not; hovering only dims it a little.
    private static ControlTemplate FlatPill()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .85));
        template.Triggers.Add(hover);
        return template;
    }

    // The camera, shown mirrored as in a call. Nothing is recorded or kept: frames go straight to the picture, and the camera is released as
    // soon as the button is pressed again, the tab is left or the island closes.
    private void ToggleCamera()
    {
        if (_cameraStarting || _camera is not null) { StopCamera(); return; }
        _ = StartCameraAsync();
    }

    private async Task StartCameraAsync()
    {
        int request = ++_cameraRequest;
        _cameraStarting = true;
        MediaCapture? capture = null;
        MediaFrameReader? reader = null;
        try
        {
            capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video, SharingMode = MediaCaptureSharingMode.SharedReadOnly, MemoryPreference = MediaCaptureMemoryPreference.Cpu
            });
            if (request != _cameraRequest) return;
            var source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source is null) return;
            reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            if (request != _cameraRequest || await reader.StartAsync() != MediaFrameReaderStartStatus.Success) return;
            if (request != _cameraRequest) return;
            _camera = capture;
            _cameraReader = reader;
            capture.Failed += CameraFailed;
            reader.FrameArrived += CameraFrame;
            capture = null;
            reader = null;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // A missing or unavailable camera leaves the microphone controls visible.
        }
        finally
        {
            reader?.Dispose();
            capture?.Dispose();
            if (request == _cameraRequest)
            {
                _cameraStarting = false;
                _app.UpdateSensorIndicator();
                UpdateCallMic();
            }
        }
    }

    private void CameraFailed(MediaCapture sender, MediaCaptureFailedEventArgs args) => Dispatcher.BeginInvoke(() =>
    {
        if (ReferenceEquals(_camera, sender)) StopCamera();
    });

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
                    if (!ReferenceEquals(reader, _cameraReader)) return;
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
        if (!_cameraStarting && _camera is null) return;
        ++_cameraRequest;
        _cameraStarting = false;
        var reader = _cameraReader; var capture = _camera;
        _cameraReader = null; _camera = null; _cameraBitmap = null;
        if (_callPreview is not null) _callPreview.Source = null;
        if (reader is not null) reader.FrameArrived -= CameraFrame;
        if (capture is not null) capture.Failed -= CameraFailed;
        reader?.Dispose();
        capture?.Dispose();
        _app.UpdateSensorIndicator();
        UpdateCallMic();
    }

    private void StopCallPreview()
    {
        StopCallLevels();
        CloseCallDevicePicker();
        _stopMicTest?.Invoke();
        _callMicResult = null;
        StopCamera();
        UpdateCallMic();
    }

    private void UpdateCallLevels()
    {
        if (_app.MicLiveFor is null || !_expanded || CurrentPage != DeckPage.Calls || !CallsContent.IsVisible)
        {
            StopCallLevels();
            return;
        }

        long now = Environment.TickCount64;
        if (_callLevelsTimer?.IsEnabled != true || now - _callLevelCheckedAt >= 1000)
        {
            _callLevelCheckedAt = now;
            try
            {
                var devices = _callLevelDevices ??= new MMDeviceEnumerator();
                _callOutputLevel?.Attach(devices, _callSpeaker);
                _callInputLevel?.Attach(devices, _callMicrophone);
            }
            catch (Exception error) when (CallAudio.IsUnavailable(error)) { }
        }

        if (_callLevelsTimer is null)
        {
            _callLevelsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
            _callLevelsTimer.Tick += (_, _) =>
            {
                _callOutputLevel?.Refresh();
                _callInputLevel?.Refresh(_callTesting);
                UpdateCallRing();
            };
        }
        _callLevelsTimer.Start();
    }

    private void StopCallLevels()
    {
        if (_callLevelsTimer?.IsEnabled != true) return;
        _callLevelsTimer.Stop();
        _callOutputLevel?.Detach();
        _callInputLevel?.Detach(_callTesting);
        _callLevelDevices?.Dispose();
        _callLevelDevices = null;
    }

    private void UpdateCallRing()
    {
        if (_callMicRing is null) return;
        double level = _callTesting ? _callMicResponse.Level : _callInputLevel?.Level ?? 0;
        _callMicRing.Opacity = _app.MicMuted || level < .06 ? 0 : Math.Min(.86, .08 + level * 1.75);
    }

    // Reads the endpoint's existing meter while Windows has an active input session. Test buttons keep control of their own bars.
    private sealed class CallDeviceLevel(DataFlow flow, Grid card, Border bar, Button test)
    {
        private MMDevice? _device;
        private string? _id;
        internal double Level { get; private set; }

        internal void Attach(MMDeviceEnumerator devices, string? selected)
        {
            try
            {
                using var candidate = string.IsNullOrEmpty(selected)
                    ? devices.GetDefaultAudioEndpoint(flow, Role.Communications)
                    : devices.GetDevice(selected);
                string id = candidate.ID;
                if (_id == id && _device is not null) return;
                Release();
                _device = devices.GetDevice(id);
                _id = id;
            }
            catch (Exception error) when (CallAudio.IsUnavailable(error)) { Release(); }
        }

        internal void Refresh(bool testing = false)
        {
            if (testing || !test.IsEnabled) { Level = 0; return; }
            double peak = 0;
            try
            {
                if (_device is { } device && !device.AudioEndpointVolume.Mute)
                    peak = device.AudioMeterInformation.MasterPeakValue;
            }
            catch (Exception error) when (CallAudio.IsUnavailable(error)) { Release(); }
            Level = Math.Max(Level * (flow == DataFlow.Capture ? .58 : .87), Math.Min(1, Math.Sqrt(Math.Max(0, peak)) * 1.5));
            if (Level < .01) Level = 0;
            double width = card.ActualWidth * Level;
            if (Math.Abs(bar.Width - width) >= .5 || width == 0 && bar.Width != 0) bar.Width = width;
        }

        internal void Detach(bool testing = false)
        {
            Release();
            Level = 0;
            if (!testing && test.IsEnabled) bar.Width = 0;
        }

        private void Release()
        {
            _device?.Dispose();
            _device = null;
            _id = null;
        }
    }

    private bool CloseCallDevicePicker(bool restoreFocus = false)
    {
        if (_callDevicePicker is not { Visibility: Visibility.Visible } picker || _callControls is null) return false;
        picker.Visibility = Visibility.Collapsed;
        picker.Children.Clear();
        _callControls.Visibility = Visibility.Visible;
        var anchor = _callDevicePickerAnchor;
        _callDevicePickerAnchor = null;
        if (restoreFocus && anchor?.IsVisible == true) anchor.Focus();
        return true;
    }

    private void OpenCallDevicePicker(DataFlow flow, Button anchor, Action refresh)
    {
        if (_callDevicePicker is not { } picker || _callControls is null) return;
        _stopMicTest?.Invoke();
        bool speaker = flow == DataFlow.Render;
        var title = WeatherText(speaker ? Loc.T("播放声音到", "Play sound on") : Loc.T("录音设备", "Record from"), 12);
        title.FontWeight = FontWeights.SemiBold;
        var done = new Button
        {
            Content = Loc.T("完成", "Done"), Style = (Style)FindResource("PanelTextButton"), Foreground = UI.Brush(Look.Hex("accent")),
            FontWeight = FontWeights.SemiBold, FontSize = 12, Padding = new Thickness(8, 0, 0, 0)
        };
        done.Click += (_, _) => CloseCallDevicePicker(restoreFocus: true);
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4), Children = { done, title } };
        DockPanel.SetDock(done, System.Windows.Controls.Dock.Right);
        var choices = new StackPanel();
        var scroll = new ScrollViewer { Content = choices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        picker.Children.Clear();
        picker.RowDefinitions.Clear();
        picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        picker.RowDefinitions.Add(new RowDefinition());
        picker.Children.Add(header);
        Grid.SetRow(scroll, 1);
        picker.Children.Add(scroll);

        var devices = new List<(string Id, string Label)>();
        string? defaultId = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device) devices.Add((device.ID, device.FriendlyName));
            }
            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
                defaultId = device.ID;
            }
            catch (Exception error) when (CallAudio.IsUnavailable(error)) { }
        }
        catch (Exception error) when (CallAudio.IsUnavailable(error)) { }

        string? selected = speaker ? _callSpeaker : _callMicrophone;
        if (selected is not null && !devices.Any(device => device.Id == selected))
        {
            if (speaker) _callSpeaker = null;
            else _callMicrophone = null;
            selected = null;
            refresh();
        }
        selected ??= defaultId;
        var options = new List<(string Id, Button Button, Path Check)>();
        foreach (var (id, label) in devices.OrderBy(device => device.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = WeatherText(label, 12);
            name.FontWeight = FontWeights.SemiBold;
            var check = new Path
            {
                Data = Geometry.Parse("M1,5 L4.5,8 L10,2"), Stroke = UI.Brush(Look.Hex("accent")),
                StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = 11, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            };
            var row = new DockPanel { Children = { check, name } };
            check.Margin = new Thickness(0, 0, 7, 0);
            var option = new Button
            {
                Style = (Style)FindResource("WidgetSurfaceButton"), Height = 23, Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(7, 0, 9, 0), Content = row, ToolTip = label
            };
            AutomationProperties.SetName(option, label);
            option.Click += (_, _) =>
            {
                selected = id;
                if (speaker) _callSpeaker = id == defaultId ? null : id;
                else
                {
                    _callMicrophone = id == defaultId ? null : id;
                    _callMicResult = null;
                }
                refresh();
                _callLevelCheckedAt = 0;
                UpdateCallMic();
                PaintSelection();
            };
            choices.Children.Add(option);
            options.Add((id, option, check));
        }
        if (options.Count == 0) choices.Children.Add(WeatherText(Loc.T("没有可用的音频设备", "No audio devices available"), 12));
        PaintSelection();
        _callDevicePickerAnchor = anchor;
        _callControls.Visibility = Visibility.Collapsed;
        picker.Visibility = Visibility.Visible;
        (options.FirstOrDefault(option => option.Id == selected).Button ?? done).Focus();

        void PaintSelection()
        {
            foreach (var (id, option, check) in options)
            {
                bool chosen = id == selected;
                check.Visibility = chosen ? Visibility.Visible : Visibility.Hidden;
                option.Background = UI.Brush(Look.Hex(chosen ? "fillSelected" : "fill"));
                AutomationProperties.SetItemStatus(option, chosen ? Loc.T("已选中", "Selected") : "");
            }
        }
    }

    // One device row: a card with its icon, a button that opens the list of devices, and Test. While a test runs a level fills the card.
    private FrameworkElement DeviceRow(DataFlow flow)
    {
        bool speaker = flow == DataFlow.Render;
        var card = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 7) };
        card.ColumnDefinitions.Add(new ColumnDefinition());
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fillCard = new Border { CornerRadius = new CornerRadius(11), Background = UI.Brush(Look.Hex("fill")) };
        Brush meterBrush = speaker ? UI.Brush("#64D2FF") : CallGreen;
        var level = new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
            BorderBrush = meterBrush, BorderThickness = new Thickness(0, 0, 0, 2),
            Background = new SolidColorBrush(((SolidColorBrush)meterBrush).Color) { Opacity = .18 } };
        var meter = new Grid { IsHitTestVisible = false, Children = { level } };
        meter.SizeChanged += (_, _) => meter.Clip = new RectangleGeometry(new Rect(meter.RenderSize), 11, 11);
        var test = new Button { Content = Loc.T("测试", "Test"), Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(),
            Background = UI.Brush(Look.Hex("fillSelected")), Foreground = UI.Brush(Look.Primary), FontSize = 12,
            Padding = new Thickness(11, 0, 11, 0), MinWidth = 44, Margin = new Thickness(0, 4, 4, 4) };
        var liveLevel = new CallDeviceLevel(flow, card, level, test);
        if (speaker) _callOutputLevel = liveLevel;
        else _callInputLevel = liveLevel;
        var choose = new Button { Style = (Style)FindResource("WidgetSurfaceButton"), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(9, 0, 6, 0) };
        var name = WeatherText("", 12);
        name.FontWeight = FontWeights.SemiBold;
        var chevron = new Path { Data = Geometry.Parse("M1,1 L4,4 L1,7"), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.1, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var icon = new Path
        {
            Data = speaker ? Geometry.Parse("M3,11 V9 A7,7 0 0 1 17,9 V11 M3,10 H5 V16 H3 Z M15,10 H17 V16 H15 Z") : MicOnGlyph, Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.4,
            Stretch = Stretch.Uniform, Width = 13, Height = 13, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        if (!speaker) _callRowMic = icon;
        var deviceLabel = new Grid { Children = { icon, name, chevron } };
        deviceLabel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        deviceLabel.ColumnDefinitions.Add(new ColumnDefinition());
        deviceLabel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(chevron, 2);
        choose.Content = deviceLabel;
        string Chosen() => (speaker ? _callSpeaker : _callMicrophone) ?? "";
        void Show()
        {
            string id = Chosen();
            string label = Loc.T("默认设备", "Default device");
            try
            {
                using var devices = new MMDeviceEnumerator();
                using var device = id.Length > 0 ? devices.GetDevice(id) : devices.GetDefaultAudioEndpoint(flow, Role.Communications);
                label = device.FriendlyName;
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException) { }
            int details = label.IndexOf(" (", StringComparison.Ordinal);
            name.Text = !speaker && details > 0 ? label[..details] : label;
            choose.ToolTip = label;
            AutomationProperties.SetName(choose, (speaker ? Loc.T("扬声器：", "Speaker: ") : Loc.T("麦克风：", "Microphone: ")) + label);
        }
        choose.Click += (_, _) => OpenCallDevicePicker(flow, choose, Show);
        test.Click += (_, _) => { if (speaker) PlayTestTone(Chosen(), test, level, card); else ShowMicLevel(Chosen(), test, level, card); };
        Grid.SetColumnSpan(fillCard, 2);
        Grid.SetColumnSpan(meter, 2);
        Grid.SetColumn(test, 1);
        card.Children.Add(fillCard); card.Children.Add(meter); card.Children.Add(choose); card.Children.Add(test);
        Show();
        return card;
    }

    // A short tone on the chosen speaker; the Test button says so meanwhile.
    private void PlayTestTone(string deviceId, Button button, Border bar, FrameworkElement card)
    {
        string label = (string)button.Content;
        MMDevice? device = null;
        WasapiOut? output = null;
        try
        {
            using var devices = new MMDeviceEnumerator();
            device = deviceId.Length > 0 ? devices.GetDevice(deviceId) : devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
            var tone = new SignalGenerator(44100, 2) { Frequency = 660, Gain = .12, Type = SignalGeneratorType.Sin }.Take(TimeSpan.FromMilliseconds(700));
            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            button.IsEnabled = false;
            button.Content = "…";
            output.PlaybackStopped += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                bar.BeginAnimation(FrameworkElement.WidthProperty, null);
                bar.Width = 0;
                button.Content = label;
                button.IsEnabled = true;
                output.Dispose();
                device.Dispose();
            });
            output.Init(tone);
            output.Play();
            bar.BeginAnimation(FrameworkElement.WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(0, card.ActualWidth, TimeSpan.FromMilliseconds(700)));
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or NAudio.MmException)
        {
            output?.Dispose();
            device?.Dispose();
            button.Content = label;
            button.IsEnabled = true;
        }
    }

    // Measure the captured samples from the selected microphone. The endpoint's meter can be zero even while data is arriving.
    private void ShowMicLevel(string deviceId, Button button, Border bar, FrameworkElement card)
    {
        if (_stopMicTest is { } stop) { stop(); return; }
        _callMicResult = null;
        string label = (string)button.Content;
        var foreground = button.Foreground;
        MMDevice? device = null;
        WasapiCapture? capture = null;
        CallMicMeter meter;
        try
        {
            using var devices = new MMDeviceEnumerator();
            device = deviceId.Length > 0 ? devices.GetDevice(deviceId) : devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            if (device.AudioEndpointVolume.Mute)
            {
                device.Dispose();
                _callMicResult = Loc.T("麦克风已静音", "Mic is muted");
                UpdateCallMic();
                return;
            }
            capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 20);
            meter = new CallMicMeter(capture.WaveFormat);
            capture.DataAvailable += (_, args) => meter.AddSamples(args.Buffer, args.BytesRecorded);
            capture.StartRecording();
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or NAudio.MmException or NotSupportedException or UnauthorizedAccessException)
        {
            capture?.Dispose();
            device?.Dispose();
            _callMicResult = Loc.T("麦克风不可用", "Mic unavailable");
            UpdateCallMic();
            return;
        }
        button.Content = Loc.T("正在聆听", "Listening");
        button.ToolTip = Loc.T("再次点击以停止测试", "Click again to stop the test");
        button.Foreground = Brushes.Black;
        button.Background = CallGreen;
        _callTesting = true; UpdateCallMic();
        long lastPacket = Environment.TickCount64, end = lastPacket + 6000;
        bool hadSignal = false;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        void Stop()
        {
            if (_stopMicTest != Stop) return;
            _stopMicTest = null;
            timer.Stop();
            bar.Width = 0;
            _callMicResponse = default;
            _callTesting = false;
            button.Content = label;
            button.ToolTip = null;
            button.Foreground = foreground;
            button.Background = UI.Brush(Look.Hex("fillSelected"));
            capture.Dispose();
            device.Dispose();
            _app.UpdateSensorIndicator();
            UpdateCallMic();
        }
        capture.RecordingStopped += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_stopMicTest != Stop) return;
            _callMicResult = Loc.T("麦克风不可用", "Mic unavailable");
            Stop();
        });
        _stopMicTest = Stop;
        _app.UpdateSensorIndicator();
        timer.Tick += (_, _) =>
        {
            long now = Environment.TickCount64;
            if (now >= end)
            {
                if (!hadSignal) _callMicResult = Loc.T("没有检测到声音", "No mic signal");
                Stop();
                return;
            }
            // A render tick can land between WASAPI packets. That gap is different from a packet of silence.
            if (meter.TryTakePeak(out float peak)) lastPacket = now;
            else if (now - lastPacket < 32) return;

            bool wasHearing = _callMicResponse.Hearing;
            _callMicResponse.Update(peak);
            bar.Width = card.ActualWidth * _callMicResponse.Level;
            UpdateCallRing();
            hadSignal |= _callMicResponse.Hearing;
            if (_callMicResponse.Hearing != wasHearing) UpdateCallSpeech();
        };
        timer.Start();
    }
}
