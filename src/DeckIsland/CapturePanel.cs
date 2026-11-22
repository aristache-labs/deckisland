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
    private const string Off = "#E5484D";
    private const string DeleteGlyph = "M3,4 H11 M5,4 V2 H9 V4 M4,4 L5,12 H9 L10,4 M7,6 V10";
    // What each section takes: 0 is every screen, then each display. Screenshots and recordings choose apart.
    private int _captureScreen, _captureVideoScreen, _captureFilter;
    private bool _captureVideoMode;
    // Select mode: clicking a capture marks it instead of opening it; Delete sends the marked ones to the Recycle Bin.
    private bool _captureSelecting;
    private readonly HashSet<string> _captureSelected = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? _captureSelectedCount;
    private (int Generation, string Language, int Version, int Width) _captureAppearance;
    private int _capturesVersion;
    private TextBlock? _captureStatus, _captureBadgeTime, _captureRecordLabel;
    private string _captureMessage = "";
    private int _captureMessageVersion;

    private void CapturePage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Capture);

    private void InitializeCapture()
    {
        void Recording(bool rebuild)
        {
            if (_captureBadgeTime is not null && _app.Recording is { } recording) _captureBadgeTime.Text = CaptureTime(recording.Elapsed);
            if (_captureRecordLabel is not null && _app.Recording is { } running) _captureRecordLabel.Text = StopLabel(running);
            if (!rebuild) return;
            RenderCompact();
            if (_expanded && CurrentPage == DeckPage.Capture) { _captureAppearance = default; RenderCapture(); }
        }
        void Files() { _capturesVersion++; if (_expanded && CurrentPage == DeckPage.Capture) RenderCapture(); }
        _app.RecordingChanged += Recording;
        _app.CapturesChanged += Files;
        Closed += (_, _) => { _app.RecordingChanged -= Recording; _app.CapturesChanged -= Files; };
    }

    private static string StopLabel(ScreenRecorder recording) => Loc.T("停止 ", "Stop · ") + CaptureTime(recording.Elapsed);

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
        var appearance = (Look.Generation, Loc.Language, _capturesVersion, (int)ContentCanvas.Width);
        if (_captureAppearance == appearance && CaptureContent.Children.Count > 0) return;
        _captureAppearance = appearance;
        CaptureContent.Children.Clear();
        CaptureContent.Children.Add(BuildCapture());
    }

    private FrameworkElement BuildCapture()
    {
        var displays = Native.Displays.Current();
        _captureScreen = Math.Clamp(_captureScreen, 0, displays.Count);
        _captureVideoScreen = Math.Clamp(_captureVideoScreen, 0, displays.Count);
        var root = new DockPanel();

        // Two rows on one grid: the label, the screen picker (the same width in both, so they line up), the row's own options,
        // then its actions at the right edge. A rule divides them from the list below.
        var screens = new List<string> { Loc.T("所有屏幕", "All screens") };
        // Short names (no resolution), so the picker stays narrow.
        screens.AddRange(displays.Select(d => displays.Count(o => o.Name == d.Name) > 1 ? $"{d.Name} {d.Device.Replace(@"\\.\DISPLAY", "")}" : d.Name));
        var screenChip = CategoryChip(screens[_captureScreen], anchor => ShowAnchoredMenu(anchor,
            [.. screens.Select((label, i) => (label, i == _captureScreen))], index => { _captureScreen = index; _captureAppearance = default; RenderCapture(); }));
        var shot = CaptureAction(CameraGlyph, Loc.T("截图", "Capture"), () => _ = TakeScreenshot(displays));
        var region = CaptureAction(RegionGlyph, Loc.T("区域", "Region"), () => _ = TakeRegion(displays));
        // What the last action did ("Saved and copied", an error), for a few seconds, just left of the row's actions.
        _captureStatus = new TextBlock { Text = _captureMessage, FontSize = 11, Foreground = UI.Brush(Look.Tertiary), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 10, 0) };
        AutomationProperties.SetLiveSetting(_captureStatus, AutomationLiveSetting.Polite);

        var recording = _app.Recording;
        var mic = CaptureToggle(MicGlyph, Loc.T("麦克风", "Microphone"), Settings.CaptureMic, on => _app.ChangeShared(s => s.CaptureMic = on));
        var sound = CaptureToggle(SpeakerGlyph, Loc.T("系统声音", "System audio"), Settings.CaptureSystemAudio, on => _app.ChangeShared(s => s.CaptureSystemAudio = on));
        var videoChip = CategoryChip(screens[_captureVideoScreen], anchor => ShowAnchoredMenu(anchor,
            [.. screens.Select((label, i) => (label, i == _captureVideoScreen))], index => { _captureVideoScreen = index; _captureAppearance = default; RenderCapture(); }));
        foreach (var part in new FrameworkElement[] { videoChip, mic, sound }) part.IsEnabled = recording is null;
        // Idle: Record. While recording: Pause (or Resume) and Stop, which counts the time beside a red dot.
        FrameworkElement[] videoActions;
        if (recording is null)
        {
            videoActions = [CaptureAction("", Loc.T("录制", "Record"), () => _ = ToggleRecording(displays), filled: true, dot: Look.Hex("surface"))];
            _captureRecordLabel = null;
        }
        else
        {
            var pause = CaptureAction(recording.Paused ? "M7,5 L18,12 L7,19 Z" : "M8,5 V19 M16,5 V19", recording.Paused ? Loc.T("继续", "Resume") : Loc.T("暂停", "Pause"), _app.PauseRecording);
            var stop = CaptureAction("", StopLabel(recording), () => _ = ToggleRecording(displays), filled: true, dot: recording.Paused ? Look.Secondary : "#E5484D");
            _captureRecordLabel = (TextBlock)((StackPanel)stop.Content).Children[^1];
            Typography.SetNumeralAlignment(_captureRecordLabel, FontNumeralAlignment.Tabular);
            videoActions = [pause, stop];
        }

        var top = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        // One row: the section switch at the left, the section's actions centred (Record or Pause and Stop, then mic and
        // speaker; or Capture and Region), the screen picker at the right with the status note just before it.
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        // One section at a time, switched at the left: Screenshot or Video. A running recording keeps Video shown.
        if (recording is not null) _captureVideoMode = true;
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (video, name) in new[] { (false, Loc.T("截图", "Screenshot")), (true, Loc.T("视频", "Video")) })
        {
            var button = PanelSegment(name, () => { _captureVideoMode = video; _captureAppearance = default; RenderCapture(); });
            StylePanelSegment(button, _captureVideoMode == video);
            button.IsEnabled = recording is null || video;
            modes.Children.Add(button);
        }
        var modeTrack = PanelSegmentTrack(modes);
        modeTrack.Margin = new Thickness(0, 0, 10, 0);
        FrameworkElement picker = _captureVideoMode ? videoChip : screenChip;
        picker.Width = 170; picker.Margin = new Thickness(0);
        FrameworkElement[] actions = _captureVideoMode ? [.. videoActions, mic, sound] : [shot, region];
        var centre = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < actions.Length; i++) { actions[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0); centre.Children.Add(actions[i]); }
        _captureStatus.HorizontalAlignment = HorizontalAlignment.Right; _captureStatus.Margin = new Thickness(8, 0, 10, 0);
        var cells = new FrameworkElement?[] { modeTrack, null, centre, _captureStatus, picker };
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] is not { } cell) continue;
            cell.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(cell, i); top.Children.Add(cell);
        }
        DockPanel.SetDock(top, DockSide.Top);
        root.Children.Add(top);
        var rule = new Border { Height = 1, Background = UI.Brush(Look.Outline), Margin = new Thickness(0, 6, 0, 10) };
        DockPanel.SetDock(rule, DockSide.Top);
        root.Children.Add(rule);

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
        // The layout of the list, at the right, as one button with a menu (Explorer's View).
        var layoutTrack = CategoryChip(CaptureLayoutName(Settings.CaptureLayout), anchor => ShowAnchoredMenu(anchor,
            [.. Enum.GetValues<CaptureLayout>().Select(layout => (CaptureLayoutName(layout), layout == Settings.CaptureLayout))],
            index => { _app.ChangeShared(s => s.CaptureLayout = (CaptureLayout)index); _captureAppearance = default; RenderCapture(); }));
        layoutTrack.Margin = new Thickness(8, 0, 0, 0);
        var files = CaptureFiles(_captureFilter);
        _captureSelected.IntersectWith(files.Select(f => f.FullName));
        var selectBar = CaptureSelectBar(files);
        DockPanel.SetDock(selectBar, DockSide.Right);
        var filterRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8), Children = { PanelSegmentTrack(filters), layoutTrack, selectBar, new Border() } };
        DockPanel.SetDock(filterRow.Children[0], DockSide.Left);
        DockPanel.SetDock(layoutTrack, DockSide.Right);
        DockPanel.SetDock(filterRow, DockSide.Top);
        root.Children.Add(filterRow);

        // The captures, newest first, in as many columns as the page's width holds, so both sides are filled.
        var mode = Settings.CaptureLayout;
        double room = Math.Max(200, ContentCanvas.Width - CaptureContent.Margin.Left - CaptureContent.Margin.Right - 12);
        int columns = mode switch
        {
            CaptureLayout.Details => 1,
            CaptureLayout.List => Math.Max(1, (int)(room / 190)),
            CaptureLayout.Tiles => Math.Max(1, (int)(room / 250)),
            _ => Math.Max(2, (int)(room / 140))
        };
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns, VerticalAlignment = VerticalAlignment.Top };
        foreach (var file in files) cards.Children.Add(CaptureCard(file, mode));
        // The last row is filled out so its cards keep the others' width.
        while (files.Count > 0 && cards.Children.Count % columns != 0) cards.Children.Add(new FrameworkElement());
        UIElement list = files.Count > 0 ? cards
            : new TextBlock { Text = Loc.T("还没有截图或录屏。", "No captures yet."), FontSize = 12.5, Foreground = UI.Brush(Look.Secondary), Margin = new Thickness(2, 8, 0, 0) };
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return root;
    }

    private static PathGlyph CaptureGlyph(string data, double size, string colour) => new()
    {
        Data = Geometry.Parse(data), Width = size, Height = size, Stretch = Stretch.Uniform, Stroke = UI.Brush(colour), StrokeThickness = 1.5,
        VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
    };

    // A pill with an optional glyph; filled is the light Record button.
    private Button CaptureAction(string glyph, string label, Action action, bool filled = false, string? dot = null)
    {
        string ink = filled ? Look.Hex("surface") : Look.Primary;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot is not null) content.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = UI.Brush(dot), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        else if (glyph.Length > 0) { var path = CaptureGlyph(glyph, 13, ink); path.Margin = new Thickness(0, 0, 7, 0); content.Children.Add(path); }
        content.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = UI.Brush(ink), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Height = 32, MinWidth = 92, Padding = new Thickness(14, 0, 15, 0),
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
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Width = 32, Height = 32,
            Background = UI.Brush(on ? Look.FillSelected : Look.Fill), ToolTip = label
        };
        // Off reads as off, not as disabled: the glyph in red with a slash through it.
        var icon = new Grid { Width = 14, Height = 14, Children = { CaptureGlyph(glyph, 14, on ? Look.Primary : Off) } };
        if (!on) icon.Children.Add(CaptureGlyph("M3,3 L21,21", 14, Off));
        button.Content = icon;
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetHelpText(button, on ? Loc.T("开", "On") : Loc.T("关", "Off"));
        button.Click += (_, _) => { change(!on); _captureAppearance = default; RenderCapture(); };
        return button;
    }

    private void CaptureSays(string text)
    {
        _captureMessage = text;
        if (_captureStatus is not null) _captureStatus.Text = text;
        if (text.Length == 0) return;
        int version = ++_captureMessageVersion;
        _ = Task.Delay(4000).ContinueWith(_ => { if (version == _captureMessageVersion) CaptureSays(""); }, TaskScheduler.FromCurrentSynchronizationContext());
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
        string path = ScreenShots.Save(image, Settings.CaptureImageFormat == CaptureImageFormat.Jpeg);
        if (Settings.CaptureCopy)
        {
            try { Clipboard.SetImage(image); }
            catch (System.Runtime.InteropServices.COMException) { } // the clipboard was busy; the file is saved anyway
        }
        if (Settings.CaptureToShelf) _app.Shelf.AddFiles([path], null);
        CaptureSays((Settings.CaptureCopy ? Loc.T("已保存并复制", "Saved and copied") : Loc.T("已保存", "Saved")) + " · " + System.IO.Path.GetFileName(path));
        _app.CaptureSaved();
    }

    // ---- Recording ----

    private async Task ToggleRecording(IReadOnlyList<DisplayInfo> displays)
    {
        try
        {
            if (_app.Recording is not null) { await _app.StopRecording(); CaptureSays(Loc.T("录屏已保存", "Recording saved")); return; }
            if (displays.Count == 0) return;
            await _app.StartRecording(_captureVideoScreen == 0 || _captureVideoScreen > displays.Count ? displays : [displays[_captureVideoScreen - 1]]);
            CaptureSays("");
        }
        // Any failure to start or finish is shown, not left to end the app later as an unobserved task.
        catch (Exception ex) when (ex is not OutOfMemoryException)
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
        var shots = filter == 2 ? [] : Of(ScreenShots.ShotFolder, "*.png").Concat(Of(ScreenShots.ShotFolder, "*.jpg"));
        var videos = filter == 1 ? [] : Of(ScreenShots.VideoFolder, "*.mp4");
        // shortcut: only the newest 40 are listed; a gallery with paging would be needed for more.
        return [.. shots.Concat(videos).OrderByDescending(f => f.LastWriteTime).Take(40)];
    }

    private static string CaptureLayoutName(CaptureLayout layout) => layout switch
    {
        CaptureLayout.List => Loc.T("列表", "List"),
        CaptureLayout.Details => Loc.T("详细信息", "Details"),
        CaptureLayout.Tiles => Loc.T("平铺", "Tiles"),
        _ => Loc.T("小图标", "Small icons")
    };

    // One capture in the chosen layout. Grid: the picture over its date and size. List: a small picture and the date on one line.
    // Details: one row with date, kind, size and length in columns. Tiles: a picture at the left, its details beside it.
    private Border CaptureCard(FileInfo file, CaptureLayout mode)
    {
        bool video = file.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        bool grid = mode == CaptureLayout.Grid;
        var picture = new Image { Stretch = Stretch.UniformToFill };
        var length = new TextBlock { FontSize = 10.5, Foreground = Brushes.White, Margin = new Thickness(6, 2, 6, 2) };
        var lengthTag = new Border { Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), CornerRadius = new CornerRadius(7), Child = length,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6), Visibility = Visibility.Collapsed };
        var delete = new Button { Style = (Style)FindResource("PanelIconButton"), Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = grid ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(4),
            Background = grid ? new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)) : Brushes.Transparent, Content = CaptureGlyph(DeleteGlyph, 11, grid ? "#FFFFFF" : Look.Secondary),
            ToolTip = Loc.T("移到回收站", "Move to Recycle Bin"), Visibility = Visibility.Hidden };
        AutomationProperties.SetName(delete, Loc.T("移到回收站", "Move to Recycle Bin"));
        delete.Click += (_, e) =>
        {
            e.Handled = true;
            if (RecycleBin.Send(file.FullName)) _app.CaptureSaved();
            else CaptureSays(Loc.T("无法删除", "Not deleted"));
        };
        var (thumbWidth, thumbHeight) = mode switch { CaptureLayout.List => (32.0, 20.0), CaptureLayout.Details => (40.0, 24.0), CaptureLayout.Tiles => (88.0, 50.0), _ => (double.NaN, 70.0) };
        var thumb = new Border { Width = thumbWidth, Height = thumbHeight, Background = UI.Brush(Look.FillSelected), Child = new Grid { Children = { picture } } };
        double round = grid ? 7 : 4;
        thumb.SizeChanged += (_, e) => thumb.Clip = new RectangleGeometry(new Rect(e.NewSize), round, round);
        if (grid) ((Grid)thumb.Child).Children.Add(lengthTag);
        string when = file.LastWriteTime.Date == DateTime.Today ? Loc.T("今天 ", "Today ") + file.LastWriteTime.ToString("HH:mm") : file.LastWriteTime.ToString("d MMM HH:mm");
        TextBlock Line(string text, double size, string colour, bool strong = false) => new()
        {
            Text = text, FontSize = size, Foreground = UI.Brush(colour), FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        string kind = video ? Loc.T("录屏", "Recording") : Loc.T("截图", "Screenshot");
        // Outside the grid the length stands in the text, not in the tag over the picture.
        if (!grid) lengthTag.Child = null;
        if (!grid) { length.Foreground = UI.Brush(Look.Secondary); length.Margin = new Thickness(0); length.FontSize = 11; length.VerticalAlignment = VerticalAlignment.Center; }
        FrameworkElement content;
        switch (mode)
        {
            case CaptureLayout.List:
                thumb.Margin = new Thickness(6, 5, 8, 5);
                content = new DockPanel { Children = { thumb, Line(when, 11.5, Look.Primary, strong: true) }, Margin = new Thickness(0, 0, 26, 0) };
                break;
            case CaptureLayout.Details:
            {
                thumb.Margin = new Thickness(6, 4, 10, 4);
                var row = new Grid { Margin = new Thickness(0, 0, 30, 0) };
                foreach (var width in new[] { GridLength.Auto, new GridLength(2, GridUnitType.Star), new GridLength(1.4, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star) })
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
                var cells = new FrameworkElement[] { thumb, Line(when, 11.5, Look.Primary, strong: true), Line(kind, 11, Look.Secondary), Line(SizeText(file.Length), 11, Look.Secondary), length };
                for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); row.Children.Add(cells[i]); }
                content = row;
                break;
            }
            case CaptureLayout.Tiles:
            {
                thumb.Margin = new Thickness(6, 6, 10, 6);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 26, 0),
                    Children = { Line(when, 12, Look.Primary, strong: true), Line(kind + " · " + SizeText(file.Length), 11, Look.Secondary) } };
                if (video) text.Children.Add(length);
                content = new DockPanel { Children = { thumb, text } };
                break;
            }
            default:
                content = new StackPanel { Children = { thumb, new StackPanel { Margin = new Thickness(8, 6, 8, 7),
                    Children = { Line(when, 11.5, Look.Primary, strong: true), Line(SizeText(file.Length), 10.5, Look.Secondary) } } } };
                break;
        }
        // An outline around each capture, so neighbouring pictures do not run together.
        var body = new Grid { Children = { content, delete } };
        var card = new Border { Margin = grid ? new Thickness(0, 0, 8, 8) : new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(8),
            Background = UI.Brush(Look.Fill), BorderBrush = UI.Brush(Look.Outline), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = body, ToolTip = file.Name };
        bool line = !grid;
        AutomationProperties.SetName(card, file.Name);
        var check = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = line ? VerticalAlignment.Center : VerticalAlignment.Top, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false, Visibility = _captureSelecting ? Visibility.Visible : Visibility.Collapsed,
            Child = new PathGlyph { Data = Geometry.Parse("M1.5,5.2 L4,7.7 L8.5,2.5"), Stroke = Brushes.White, StrokeThickness = 1.6, Width = 9, Height = 9, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round } };
        body.Children.Add(check);
        void ShowMark()
        {
            bool on = _captureSelected.Contains(file.FullName);
            check.Background = on ? UI.Brush(Look.Accent) : new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
            ((PathGlyph)check.Child).Visibility = on ? Visibility.Visible : Visibility.Hidden;
            card.BorderBrush = UI.Brush(on ? Look.Accent : Look.Outline);
        }
        if (_captureSelecting) ShowMark();
        card.MouseEnter += (_, _) => { if (!_captureSelecting) delete.Visibility = Visibility.Visible; };
        card.MouseLeave += (_, _) => delete.Visibility = Visibility.Hidden;
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (_captureSelecting)
            {
                if (!_captureSelected.Add(file.FullName)) _captureSelected.Remove(file.FullName);
                ShowMark();
                UpdateCaptureSelection();
                return;
            }
            try { Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { CaptureSays(ex.Message); }
        };
        _ = LoadCaptureThumb(file.FullName, video, picture, length, lengthTag);
        return card;
    }

    // "Select" when browsing; while selecting, the count, All, Delete and Cancel.
    private StackPanel CaptureSelectBar(IReadOnlyList<FileInfo> files)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Button Small(string label, Action action, bool strong = false)
        {
            var button = CaptureAction("", label, action);
            button.Height = 30; button.MinWidth = 0; button.Padding = new Thickness(12, 0, 12, 0); button.Margin = new Thickness(6, 0, 0, 0);
            if (strong) ((TextBlock)((StackPanel)button.Content).Children[^1]).Foreground = UI.Brush("#E5484D");
            return button;
        }
        void Again() { _captureAppearance = default; RenderCapture(); }
        if (!_captureSelecting)
        {
            var select = Small(Loc.T("选择", "Select"), () => { _captureSelecting = true; _captureSelected.Clear(); Again(); });
            select.IsEnabled = files.Count > 0;
            bar.Children.Add(select);
            return bar;
        }
        _captureSelectedCount = new TextBlock { FontSize = 11.5, Foreground = UI.Brush(Look.Secondary), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(_captureSelectedCount);
        bar.Children.Add(Small(Loc.T("全选", "All"), () =>
        {
            if (_captureSelected.Count == files.Count) _captureSelected.Clear(); else _captureSelected.UnionWith(files.Select(f => f.FullName));
            Again();
        }));
        bar.Children.Add(Small(Loc.T("删除", "Delete"), DeleteSelectedCaptures, strong: true));
        bar.Children.Add(Small(Loc.T("取消", "Cancel"), () => { _captureSelecting = false; _captureSelected.Clear(); Again(); }));
        UpdateCaptureSelection();
        return bar;
    }

    private void UpdateCaptureSelection()
    {
        if (_captureSelectedCount is not null) _captureSelectedCount.Text = Loc.T($"已选 {_captureSelected.Count} 项", $"{_captureSelected.Count} selected");
    }

    private void DeleteSelectedCaptures()
    {
        if (_captureSelected.Count == 0) return;
        int failed = _captureSelected.Count(path => !RecycleBin.Send(path));
        _captureSelecting = false;
        _captureSelected.Clear();
        CaptureSays(failed == 0 ? "" : Loc.T($"{failed} 项无法删除", $"{failed} not deleted"));
        _app.CaptureSaved();
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
