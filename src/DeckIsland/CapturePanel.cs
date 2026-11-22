using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeckIsland.Core;
using DeckIsland.Native;
using DockSide = System.Windows.Controls.Dock;
using PathGlyph = System.Windows.Shapes.Path;

namespace DeckIsland;

// The Capture tab: screenshots of every screen, one screen or a dragged region (Pictures\Screenshots, and the clipboard), screen
// recordings to MP4 (Videos\DeckIsland\Recordings), and the past captures. A running recording shows in the collapsed island.
public partial class MainWindow
{
    private const string CameraGlyph = "M4,7 H7.5 L9.5,4.5 H14.5 L16.5,7 H20 A2,2 0 0 1 22,9 V18 A2,2 0 0 1 20,20 H4 A2,2 0 0 1 2,18 V9 A2,2 0 0 1 4,7 Z M12,10 A3.4,3.4 0 1 0 12,16.8 A3.4,3.4 0 1 0 12,10 Z";
    private const string RegionGlyph = "M6,2 V18 H22 M2,6 H18 V22";
    private const string MicGlyph = "M12,3 A3,3 0 0 1 15,6 V11 A3,3 0 0 1 9,11 V6 A3,3 0 0 1 12,3 Z M5.5,10.5 A6.5,6.5 0 0 0 18.5,10.5 M12,17 V21";
    private const string SpeakerGlyph = "M4,9 H8 L13,5 V19 L8,15 H4 Z M16.5,9 A4,4 0 0 1 16.5,15 M19,6.5 A7.5,7.5 0 0 1 19,17.5";
    private const string PauseGlyph = "M5,3 V11 M9,3 V11";
    private const string StopGlyph = "M3.5,3.5 H10.5 V10.5 H3.5 Z";
    private const string DeleteGlyph = "M3,4 H11 M5,4 V2 H9 V4 M4,4 L5,12 H9 L10,4 M7,6 V10";
    private int _captureScreen, _captureDisplay, _captureFilter;
    private (int Generation, string Language, int Version) _captureAppearance;
    private int _capturesVersion;
    private TextBlock? _captureStatus, _captureBadgeTime;
    private string _captureMessage = "";

    private void CapturePage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Capture);

    private void InitializeCapture()
    {
        void Recording(bool rebuild)
        {
            if (_captureBadgeTime is not null && _app.Recording is { } recording) _captureBadgeTime.Text = CaptureTime(recording.Elapsed);
            if (!rebuild) return;
            RenderCompact();
            if (_expanded && CurrentPage == DeckPage.Capture) { _captureAppearance = default; RenderCapture(); }
        }
        void Files() { _capturesVersion++; if (_expanded && CurrentPage == DeckPage.Capture) RenderCapture(); }
        _app.RecordingChanged += Recording;
        _app.CapturesChanged += Files;
        Closed += (_, _) => { _app.RecordingChanged -= Recording; _app.CapturesChanged -= Files; };
    }

    private static string CaptureTime(TimeSpan time) => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");

    private void RenderCapture()
    {
        RefreshStatus();
        foreach (var page in DeckPages.Order)
            if (page != DeckPage.Capture) PageContent(page).Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        if (!double.IsFinite(ContentCanvas.Height)) return;
        var header = PanelHeader.For(30, 18);
        if (Settings.UsagePage) header = header.WithControlsOf(CachedUsageHeader());
        ApplyHeader(header with { Row = Math.Max(header.Row, header.Height) });
        CaptureContent.Margin = new Thickness(header.Side, 14, header.Side, 12);
        ShowHeaderPin(header);
        var appearance = (Look.Generation, Loc.Language, _capturesVersion);
        if (_captureAppearance == appearance && CaptureContent.Children.Count > 0) return;
        _captureAppearance = appearance;
        CaptureContent.Children.Clear();
        CaptureContent.Children.Add(BuildCapture());
    }

    private FrameworkElement BuildCapture()
    {
        var displays = Native.Displays.Current();
        _captureScreen = Math.Clamp(_captureScreen, 0, displays.Count);
        _captureDisplay = Math.Clamp(_captureDisplay, 0, Math.Max(0, displays.Count - 1));
        var root = new DockPanel();

        // Screenshot: which screens, Capture, Region.
        var screens = new List<string> { Loc.T("所有屏幕", "All screens") };
        screens.AddRange(displays.Select(d => Native.Displays.Label(d, displays)));
        var screenChip = CategoryChip(screens[_captureScreen], anchor => ShowAnchoredMenu(anchor,
            [.. screens.Select((label, i) => (label, i == _captureScreen))], index => { _captureScreen = index; _captureAppearance = default; RenderCapture(); }));
        var shot = CaptureAction(CameraGlyph, Loc.T("截图", "Capture"), () => _ = TakeScreenshot(displays));
        var region = CaptureAction(RegionGlyph, Loc.T("区域", "Region"), () => _ = TakeRegion(displays));
        AddCaptureRow(root, Loc.T("截图", "Screenshot"), screenChip, shot, region);

        // Video: display, 30/60, microphone, system sound, Record.
        var recording = _app.Recording;
        var displayChip = CategoryChip(displays.Count > 0 ? Native.Displays.Label(displays[_captureDisplay], displays) : "—", anchor => ShowAnchoredMenu(anchor,
            [.. displays.Select((d, i) => (Native.Displays.Label(d, displays), i == _captureDisplay))], index => { _captureDisplay = index; _captureAppearance = default; RenderCapture(); }));
        var rates = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (int fps in new[] { 30, 60 })
        {
            var button = PanelSegment(fps.ToString(), () => { _app.ChangeShared(s => s.CaptureFps = fps); _captureAppearance = default; RenderCapture(); });
            StylePanelSegment(button, Settings.CaptureFps == fps);
            rates.Children.Add(button);
        }
        var mic = CaptureToggle(MicGlyph, Loc.T("麦克风", "Microphone"), Settings.CaptureMic, on => _app.ChangeShared(s => s.CaptureMic = on));
        var sound = CaptureToggle(SpeakerGlyph, Loc.T("系统声音", "System audio"), Settings.CaptureSystemAudio, on => _app.ChangeShared(s => s.CaptureSystemAudio = on));
        var record = CaptureAction("", recording is null ? Loc.T("录制", "Record") : Loc.T("停止", "Stop"), () => _ = ToggleRecording(displays), filled: true);
        foreach (var part in new FrameworkElement[] { displayChip, rates, mic, sound }) part.IsEnabled = recording is null;
        AddCaptureRow(root, Loc.T("视频", "Video"), displayChip, PanelSegmentTrack(rates), mic, sound, record);

        // The filter and the status.
        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        string[] names = [Loc.T("全部", "All"), Loc.T("截图", "Screenshots"), Loc.T("录屏", "Recordings")];
        for (int i = 0; i < names.Length; i++)
        {
            int filter = i;
            var button = PanelSegment(names[i], () => { _captureFilter = filter; _captureAppearance = default; RenderCapture(); });
            StylePanelSegment(button, i == _captureFilter);
            filters.Children.Add(button);
        }
        _captureStatus = new TextBlock { Text = _captureMessage, FontSize = 11, Foreground = UI.Brush(Look.Secondary), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0) };
        AutomationProperties.SetLiveSetting(_captureStatus, AutomationLiveSetting.Polite);
        var filterRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8), Children = { PanelSegmentTrack(filters), _captureStatus } };
        DockPanel.SetDock(filterRow.Children[0], DockSide.Left);
        DockPanel.SetDock(filterRow, DockSide.Top);
        root.Children.Add(filterRow);

        // The captures, newest first.
        var cards = new WrapPanel();
        var files = CaptureFiles(_captureFilter);
        foreach (var file in files) cards.Children.Add(CaptureCard(file));
        if (files.Count == 0)
            cards.Children.Add(new TextBlock { Text = Loc.T("还没有截图或录屏。", "No captures yet."), FontSize = 12.5, Foreground = UI.Brush(Look.Secondary), Margin = new Thickness(2, 8, 0, 0) });
        root.Children.Add(new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return root;
    }

    private void AddCaptureRow(DockPanel root, string label, params FrameworkElement[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 78, FontSize = 12.5, Foreground = UI.Brush(Look.Secondary), VerticalAlignment = VerticalAlignment.Center });
        foreach (var part in parts) { part.Margin = new Thickness(0, 0, 8, 0); part.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(part); }
        DockPanel.SetDock(row, DockSide.Top);
        root.Children.Add(row);
    }

    private static PathGlyph CaptureGlyph(string data, double size, string colour) => new()
    {
        Data = Geometry.Parse(data), Width = size, Height = size, Stretch = Stretch.Uniform, Stroke = UI.Brush(colour), StrokeThickness = 1.5,
        VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
    };

    // A pill with an optional glyph; filled is the light Record button.
    private Button CaptureAction(string glyph, string label, Action action, bool filled = false)
    {
        string ink = filled ? Look.Hex("surface") : Look.Primary;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (filled) content.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = UI.Brush(_app.Recording is null ? "#E5484D" : ink), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        else if (glyph.Length > 0) { var path = CaptureGlyph(glyph, 13, ink); path.Margin = new Thickness(0, 0, 7, 0); content.Children.Add(path); }
        content.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = UI.Brush(ink), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Height = 28, Padding = new Thickness(13, 0, 14, 0),
            Background = UI.Brush(filled ? Look.Primary : Look.Fill), Content = content
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private Button CaptureToggle(string glyph, string label, bool on, Action<bool> change)
    {
        var button = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Width = 28, Height = 28,
            Background = UI.Brush(on ? Look.FillSelected : Look.Fill), Content = CaptureGlyph(glyph, 13, on ? Look.Primary : Look.Tertiary), ToolTip = label
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetHelpText(button, on ? Loc.T("开", "On") : Loc.T("关", "Off"));
        button.Click += (_, _) => { change(!on); _captureAppearance = default; RenderCapture(); };
        return button;
    }

    private void CaptureSays(string text)
    {
        _captureMessage = text;
        if (_captureStatus is not null) _captureStatus.Text = text;
    }

    // ---- Screenshots ----

    private async Task TakeScreenshot(IReadOnlyList<DisplayInfo> displays)
    {
        try
        {
            BitmapSource image;
            using (_app.HoldCaptureExclusion())
            {
                await Task.Delay(60); // the exclusion reaches the compositor
                var area = _captureScreen == 0 || _captureScreen > displays.Count
                    ? displays.Select(d => d.Bounds).Aggregate(Rect.Union) : displays[_captureScreen - 1].Bounds;
                image = ScreenShots.Grab(area);
            }
            KeepShot(image);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CaptureSays(Loc.T("截图失败：", "Screenshot failed: ") + ex.Message);
        }
    }

    private async Task TakeRegion(IReadOnlyList<DisplayInfo> displays)
    {
        try
        {
            BitmapSource? image;
            using (_app.HoldCaptureExclusion())
            {
                await Task.Delay(60);
                image = await ScreenShots.PickRegion(displays);
            }
            if (image is not null) KeepShot(image);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CaptureSays(Loc.T("截图失败：", "Screenshot failed: ") + ex.Message);
        }
    }

    private void KeepShot(BitmapSource image)
    {
        string path = ScreenShots.Save(image);
        try { Clipboard.SetImage(image); }
        catch (System.Runtime.InteropServices.COMException) { } // the clipboard was busy; the file is saved anyway
        if (Settings.CaptureToShelf) _app.Shelf.AddFiles([path], null);
        CaptureSays(Loc.T("已保存并复制", "Saved and copied") + " · " + System.IO.Path.GetFileName(path));
        _app.CaptureSaved();
    }

    // ---- Recording ----

    private async Task ToggleRecording(IReadOnlyList<DisplayInfo> displays)
    {
        try
        {
            if (_app.Recording is not null) { await _app.StopRecording(); CaptureSays(Loc.T("录屏已保存", "Recording saved")); return; }
            if (displays.Count == 0) return;
            await _app.StartRecording(displays[_captureDisplay]);
            CaptureSays("");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            CaptureSays(Loc.T("录屏失败：", "Recording failed: ") + ex.Message);
        }
    }

    // The collapsed island's part while recording: a red dot, the time, pause and stop. It takes the focus timer's place.
    private FrameworkElement? CaptureBadge(double scale)
    {
        _captureBadgeTime = null;
        if (_app.Recording is not { } recording) return null;
        var time = WeatherText(CaptureTime(recording.Elapsed), CompactMusicLayout.CollapsedTextSize(Settings.QuotaRingStyle, scale, 11));
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        time.Text = "00:00:00";
        time.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        time.MinWidth = Math.Ceiling(time.DesiredSize.Width);
        time.Text = CaptureTime(recording.Elapsed);
        _captureBadgeTime = time;
        var dot = new System.Windows.Shapes.Ellipse { Width = 7 * scale, Height = 7 * scale, Fill = UI.Brush(recording.Paused ? Look.Secondary : "#E5484D"),
            Margin = new Thickness(0, 0, 5 * scale, 0), VerticalAlignment = VerticalAlignment.Center };
        Button Small(string glyph, string label, Action action)
        {
            var button = new Button { Style = (Style)FindResource("PanelIconButton"), Width = 18 * scale, Height = 18 * scale, Margin = new Thickness(3 * scale, 0, 0, 0),
                Content = CaptureGlyph(glyph, 8 * scale, Look.Primary), ToolTip = label };
            AutomationProperties.SetName(button, label);
            button.Click += (_, e) => { e.Handled = true; action(); };
            return button;
        }
        var pause = Small(recording.Paused ? "M4,2.5 L11,7 L4,11.5 Z" : PauseGlyph, recording.Paused ? Loc.T("继续录制", "Resume recording") : Loc.T("暂停录制", "Pause recording"), _app.PauseRecording);
        var stop = Small(StopGlyph, Loc.T("停止录制", "Stop recording"), () => _ = _app.StopRecording());
        var host = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Children = { dot, time, pause, stop } };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Width = Math.Ceiling(host.DesiredSize.Width + 4 * scale);
        host.Height = Math.Ceiling(host.DesiredSize.Height);
        return host;
    }

    // ---- Past captures ----

    private static List<FileInfo> CaptureFiles(int filter)
    {
        IEnumerable<FileInfo> Of(string folder, string pattern)
        {
            try { return Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles(pattern) : []; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        }
        var shots = filter == 2 ? [] : Of(ScreenShots.ShotFolder, "*.png");
        var videos = filter == 1 ? [] : Of(ScreenShots.VideoFolder, "*.mp4");
        // shortcut: only the newest 40 are listed; a gallery with paging would be needed for more.
        return [.. shots.Concat(videos).OrderByDescending(f => f.LastWriteTime).Take(40)];
    }

    private Border CaptureCard(FileInfo file)
    {
        bool video = file.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        var picture = new Image { Stretch = Stretch.UniformToFill };
        var length = new TextBlock { FontSize = 10.5, Foreground = Brushes.White, Margin = new Thickness(6, 2, 6, 2) };
        var lengthTag = new Border { Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), CornerRadius = new CornerRadius(7), Child = length,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6), Visibility = Visibility.Collapsed };
        var delete = new Button { Style = (Style)FindResource("PanelIconButton"), Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4), Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), Content = CaptureGlyph(DeleteGlyph, 11, "#FFFFFF"),
            ToolTip = Loc.T("移到回收站", "Move to Recycle Bin"), Visibility = Visibility.Hidden };
        AutomationProperties.SetName(delete, Loc.T("移到回收站", "Move to Recycle Bin"));
        delete.Click += (_, e) =>
        {
            e.Handled = true;
            if (RecycleBin.Send(file.FullName)) _app.CaptureSaved();
            else CaptureSays(Loc.T("无法删除", "Not deleted"));
        };
        var thumb = new Border { Height = 70, CornerRadius = new CornerRadius(8, 8, 0, 0), ClipToBounds = true, Background = UI.Brush(Look.FillSelected),
            Child = new Grid { Children = { picture, lengthTag, delete } } };
        thumb.Clip = new RectangleGeometry(new Rect(0, 0, 124, 70), 8, 8);
        string when = file.LastWriteTime.Date == DateTime.Today ? Loc.T("今天 ", "Today ") + file.LastWriteTime.ToString("HH:mm") : file.LastWriteTime.ToString("d MMM HH:mm");
        var info = new StackPanel { Margin = new Thickness(8, 6, 8, 7), Children =
        {
            new TextBlock { Text = when, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(Look.Primary) },
            new TextBlock { Text = SizeText(file.Length), FontSize = 10.5, Foreground = UI.Brush(Look.Secondary) }
        } };
        var card = new Border { Width = 124, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8), Background = UI.Brush(Look.Fill), Cursor = Cursors.Hand,
            Child = new StackPanel { Children = { thumb, info } }, ToolTip = file.Name };
        AutomationProperties.SetName(card, file.Name);
        card.MouseEnter += (_, _) => delete.Visibility = Visibility.Visible;
        card.MouseLeave += (_, _) => delete.Visibility = Visibility.Hidden;
        card.MouseLeftButtonUp += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { CaptureSays(ex.Message); }
        };
        _ = LoadCaptureThumb(file.FullName, video, picture, length, lengthTag);
        return card;
    }

    private static string SizeText(long bytes) => bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private static async Task LoadCaptureThumb(string path, bool video, Image picture, TextBlock length, Border lengthTag)
    {
        try
        {
            if (!video)
            {
                picture.Source = await Task.Run(() =>
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.UriSource = new Uri(path); bitmap.DecodePixelWidth = 200; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                });
                return;
            }
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.GetVideoPropertiesAsync();
            length.Text = CaptureTime(properties.Duration);
            lengthTag.Visibility = Visibility.Visible;
            using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 200);
            if (thumbnail is null) return;
            using var stream = thumbnail.AsStreamForRead();
            var image = new BitmapImage();
            image.BeginInit(); image.StreamSource = stream; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit();
            picture.Source = image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException) { }
    }
}
