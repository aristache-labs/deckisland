using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using Microsoft.Win32;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow : Window, IKeepsAppFonts
{
    private SettingsPalette _palette = new(false);
    private string Surface => _palette.Surface;
    private string Sidebar => _palette.Sidebar;
    private string Divider => _palette.Divider;
    private string Inset => _palette.Inset;
    private string TextPrimary => _palette.Primary;
    private string TextSecondary => _palette.Secondary;
    private string TextTertiary => _palette.Tertiary;
    private readonly App _app;
    private readonly StackPanel _navigation = new();
    private readonly StackPanel _content = new();
    private readonly TextBlock _status = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
    private ScrollViewer _scroll = null!;
    private static string[] _pages => [Loc.T("常规", "General"), Loc.T("收起外观", "Collapsed view"), Loc.T("展开外观", "Expanded view"),
        Loc.T("配额与统计", "Quotas and usage"), Loc.T("模型价格", "Model prices"), Loc.T("样式", "Style"), Loc.T("播放器", "Players"), Loc.T("关于 DeckIsland", "About DeckIsland"), Loc.T("面板主题", "Panel theme"), Loc.T("显示与读数", "Display and readings"), Loc.T("增强传感器", "Enhanced sensors"), Loc.T("快捷键", "Shortcuts"), Loc.T("显示与设备", "Display and devices"),
        Loc.T("剪贴板", "Clipboard"), Loc.T("文件中转", "File transfer"), Loc.T("快捷操作", "Quick actions"), Loc.T("显示器与配置", "Displays and profiles"), Loc.T("页面布局", "Page layout"), Loc.T("剪贴板同步", "Clipboard sync"), Loc.T("天气", "Weather"), Loc.T("专注计时器", "Focus timer"), Loc.T("提醒", "Reminders"), Loc.T("小组件与标签页", "Widgets & Tabs"), Loc.T("通知与状态提示", "Notifications and HUDs"), Loc.T("通话", "Calls"), Loc.T("代码草稿", "Scratchpad"), Loc.T("换算", "Converter"), Loc.T("终端", "Terminal")];
    // The order of the pages in the navigation; the theme page (ThemePage) and the shortcuts (HotkeysPage) are listed with the
    // basic pages, and the system monitor's page (MonitorSettingsPage) has its own group after music, then the clipboard's
    // pages (the layout first), before About. The profiles page (ProfilesPage) closes the basic pages.
    private static readonly int[] PageOrder = [0, 1, 2, WidgetsSettingsPage, NotificationsSettingsPage, CallsSettingsPage, ThemePage, HotkeysPage, ProfilesPage, 3, 4, 5, 6, MusicGesturesPage, MonitorSettingsPage, SensorsPage, ShelfLayoutSettingsPage, ClipboardSettingsPage, TransferSettingsPage, ClipboardSyncSettingsPage, DevicesSettingsPage, 7];
    private int _page;
    private bool _preview;
    private StackPanel? _appRows;
    public FrameworkElement RootVisual { get; private set; } = null!;
    // The whole page, including the part scrolled out of view.
    private DeckSettings S => _app.Settings;
    private event Action? PaletteChanged;

    public static string StyleName(CompactStyle style) => style switch { CompactStyle.Notch => Loc.T("刘海", "Notch"), CompactStyle.Capsule => Loc.T("胶囊", "Capsule"), _ => Loc.T("指示条", "Indicator") };
    public static string BehaviorName(WindowBehavior behavior) => behavior switch { WindowBehavior.Normal => Loc.T("保持原有样式", "Keep style"), WindowBehavior.Line => Loc.T("显示为指示条", "As indicator"), _ => Loc.T("隐藏", "Hide") };

    private const double DefaultWidth = 960, DefaultHeight = 780;
    // The minimum size is 840 x 640, reduced where the screen's work area (less a 24 margin) is smaller. Returns the
    // largest size that fits.
    private (double Width, double Height) FitMinimumSize(Rect work, double scale)
    {
        double fitWidth = work.Width / scale - 24, fitHeight = work.Height / scale - 24;
        MinWidth = Math.Min(840, fitWidth); MinHeight = Math.Min(640, fitHeight);
        return (fitWidth, fitHeight);
    }
    // Keeps the size to open with next time: the normal size, also when the window is maximized or minimized.
    internal void RememberSize()
    {
        var size = WindowState == WindowState.Normal ? new Size(ActualWidth, ActualHeight) : RestoreBounds.Size;
        if (size.IsEmpty || size.Width <= 0 || size.Height <= 0) return;
        bool maximized = WindowState == WindowState.Maximized;
        if (S.SettingsWindowWidth == size.Width && S.SettingsWindowHeight == size.Height && S.SettingsWindowMaximized == maximized) return;
        Change(s => { s.SettingsWindowWidth = size.Width; s.SettingsWindowHeight = size.Height; s.SettingsWindowMaximized = maximized; });
    }
    public SettingsWindow(App app)
    {
        _app = app;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DeckIsland;component/SettingsResources.xaml", UriKind.Relative) });
        using (OwnFonts()) Fonts.Publish(Resources);
        Style =(Style)FindResource(typeof(Window));
        // Sizes are DIPs, so the window looks the same at any scale factor. The default fits the page layout (the content
        // column is at most 858 wide); the last closed size replaces it. Either is fitted to the work area of the screen
        // the window opens on, which CenterScreen takes as the one under the mouse pointer.
        var (_, work, scale) = Native.WindowsHost.CursorScreen();
        var (fitWidth, fitHeight) = FitMinimumSize(work, scale);
        Width = Math.Clamp(S.SettingsWindowWidth ?? DefaultWidth, MinWidth, fitWidth);
        Height = Math.Clamp(S.SettingsWindowHeight ?? DefaultHeight, MinHeight, fitHeight);
        // Dragged to a smaller screen, such as a portrait one, the window must still be able to shrink to fit it.
        IntPtr monitor = IntPtr.Zero;
        void ScreenMaybeChanged()
        {
            if (PresentationSource.FromVisual(this) is null) return;
            var screen = Native.WindowsHost.WindowScreen(this);
            if (screen.Monitor == monitor) return;
            monitor = screen.Monitor; FitMinimumSize(screen.Work, screen.Scale);
        }
        SourceInitialized += (_, _) => ScreenMaybeChanged();
        LocationChanged += (_, _) => ScreenMaybeChanged();
        // WPF also raises DpiChanged with equal old and new values after a move; only a real change needs the sizes again.
        DpiChanged += (_, e) => { if (e.OldDpi.DpiScaleX == e.NewDpi.DpiScaleX) return; monitor = IntPtr.Zero; ScreenMaybeChanged(); };
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (S.SettingsWindowMaximized) WindowState = WindowState.Maximized;
        Closing += (_, _) => { if (!_app.Exiting) RememberSize(); };
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Native.SettingsChrome.Apply(this);
        // Its own icon on the taskbar: without one, Windows drew an empty tile for it under the app's own taskbar id (App.OnStartup).
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/DeckIsland;component/Assets/DeckIsland.ico"));
        SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(this, _palette.Dark);
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        Deactivated += (_, _) => { EndAppDrag(false); QueuePreviewDismissal(); };
        // Back from PawnIO's installer or uninstaller: the enhanced sensors page shows what changed.
        Activated += (_, _) => _refreshSensors?.Invoke();
        PreviewMouseDown += (_, e) => DismissPreviewOutsideControl(e.OriginalSource);
        DismissPreviewOnCaptionPress();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _appDrag is not null) { EndAppDrag(false); e.Handled = true; } };
        ReleaseFocusOnEmptyClick(this, () => RootVisual);
        // Moving focus scrolls the control just into view; a little more keeps its focus outline visible at the edges.
        bool widening = false;
        _content.AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, e) =>
        {
            if (widening || !e.TargetRect.IsEmpty || e.TargetObject is not FrameworkElement target || !_content.IsAncestorOf(target)) return;
            e.Handled = true; widening = true;
            try { target.BringIntoView(new Rect(-6, -6, target.ActualWidth + 12, target.ActualHeight + 12)); }
            finally { widening = false; }
        }));
        _app.Prices.Changed += PricesChanged;
        _app.Updates.Changed += UpdatesChanged;
        Native.SensorGuard.Changed += SensorStopsChanged;
        SizeChanged += (_, _) => { if (_priceTable is not null) _priceTable.Height = Math.Max(220, ActualHeight - PriceTableReserve); };
        Closed += (_, _) => { WatchThemeFolder(false); EndAppDrag(false, false); _updateOpenGeneration++; _app.Updates.Changed -= UpdatesChanged; Native.SensorGuard.Changed -= SensorStopsChanged; SystemEvents.UserPreferenceChanged -= SystemThemeChanged; _app.Prices.Changed -= PricesChanged; _exceptionsReservedHeight = 0; _exceptionsOpen = false; _exceptionsHost = null; _scroll.ScrollChanged -= ExceptionsScrollChanged; if (!_app.Exiting) { _app.PreviewDeck.Preview(false); _app.FlushSettings(); } };
        StateChanged += (_, _) =>
        {
            UpdateCaption();
            if (WindowState == WindowState.Minimized) { _preview = false; _app.PreviewDeck.Preview(false); }
        };
        RebuildTheme();
    }

    private Button? _maximize;
    private void UpdateCaption()
    {
        if (_maximize is null) return;
        _maximize.Content = CaptionIcon(WindowState == WindowState.Maximized ? "restore" : "maximize");
        _maximize.ToolTip = WindowState == WindowState.Maximized ? Loc.T("还原", "Restore") : Loc.T("最大化", "Maximize");
        AutomationProperties.SetName(_maximize, (string)_maximize.ToolTip);
    }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() =>
        { if (IsLoaded && S.Theme == SettingsTheme.System && SettingsPalette.IsDark(S.Theme) != _palette.Dark) RebuildTheme(); });
    }
    private void PricesChanged()
    { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (IsLoaded) RefreshPriceRows(); }); }
    // The window keeps the app's own fonts whatever interface font the user chose (Fonts.Plain), unless the user asked for it to follow
    // the chosen one (DeckSettings.SettingsFollowsFont). The font row keeps them either way (AppearanceSection), so a font that is
    // hard to read can be changed back. Fonts.Use leaves this window alone (IKeepsAppFonts); a change of font or of the switch rebuilds it.
    private IDisposable? OwnFonts() => S.SettingsFollowsFont ? null : Fonts.Plain();
    // The family for what this window builds or draws outside a rebuild (a dialog, a popup, a tick label).
    internal FontFamily SettingsFont(UiFontRole role) => S.SettingsFollowsFont && !Fonts.IsPlain ? Fonts.Of(role) : Fonts.PlainOf(role);
    internal void RebuildTheme() { using (OwnFonts()) { Fonts.Publish(Resources); RebuildThemeCore(); } }
    private void RebuildThemeCore()
    {
        RememberFocus();
        EndAppDrag(false, false);
        _keptOffset = _scroll?.VerticalOffset ?? 0;
        // A language change rebuilds the window the same way.
        Title = Loc.T("DeckIsland 设置", "DeckIsland Settings");
        _palette = new(SettingsPalette.IsDark(S.Theme)); _palette.Apply(Resources);
        Background = UI.Brush(Surface); Foreground = UI.Brush(TextPrimary); FontFamily = SettingsFont(UiFontRole.Panel);
        // Detach reused panels before rebuilding the window's theme-specific surfaces.
        _navigation.Children.Clear(); _content.Children.Clear();
        if (_navigation.Parent is ScrollViewer oldNavigation) oldNavigation.Content = null;
        if (_scroll is not null) _scroll.Content = null;
        if (_status.Parent is Panel oldStatus) oldStatus.Children.Remove(_status);
        var outer = new Grid { Background = UI.Brush(Surface) };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); outer.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new Grid { Background = UI.Brush(Sidebar) };
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) }); sidebar.RowDefinitions.Add(new RowDefinition());
        sidebar.Children.Add(Brand(24, new Thickness(24, 0, 22, 0)));
        // The list of pages scrolls once it is taller than the window, so the last pages (About) stay reachable in a short window.
        // Like the page, it always keeps the scroll bar's 16 DIP column, here in place of its right margin, so the buttons and the
        // selected one's background keep their width when the bar appears. The scroll area clips at its edges, so the list keeps a
        // 4 DIP inset at the top, left and right for the keyboard focus outline, which reaches 2 DIP past a button; the scroll
        // area's own margin gives the top and left back, and the buttons stay where they were.
        _navigation.Margin = new Thickness(8, 4, 4, 12);
        var navigationScroll = new ScrollViewer { Style = (Style)FindResource("SettingsPageScrollViewer"), Content = _navigation, Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(4, -4, 0, 0) };
        Grid.SetRow(navigationScroll, 1); sidebar.Children.Add(navigationScroll);
        outer.Children.Add(sidebar); outer.Children.Add(new Border { Width = 1, Background = UI.Brush(Divider), HorizontalAlignment = HorizontalAlignment.Right });
        // The scroll area clips at its edges, so the content keeps a 4 DIP inset inside it for keyboard focus outlines
        // that extend past a control; the outer margins give those 4 DIP back and the page stays where it was.
        var main = new DockPanel { Margin = new Thickness(32, 56, 32, 22), MaxWidth = 858 }; Grid.SetColumn(main, 1); outer.Children.Add(main);
        _status.Margin = new Thickness(4, 4, 4, 14); _status.FontSize = 12; DockPanel.SetDock(_status, Dock.Top); main.Children.Add(_status);
        _content.Margin = new Thickness(4);
        // Only the controls inside take focus; a focusable scroll area would draw the system outline around the whole page.
        // The page's scroll bar always keeps its 16 DIP column, taken from the right margin, so no page changes width when it starts scrolling.
        _scroll = new ScrollViewer { Style = (Style)FindResource("SettingsPageScrollViewer"), Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0,0,4,0), Margin = new Thickness(0,0,-16,0), Focusable = false }; main.Children.Add(_scroll);
        var caption = new DockPanel { Height = 40, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 0, 6, 0), LastChildFill = false }; Grid.SetColumn(caption, 1); outer.Children.Add(caption);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); caption.Children.Add(buttons);
        buttons.Children.Add(WindowButton("minimize", Loc.T("最小化", "Minimize"), () => SystemCommands.MinimizeWindow(this)));
        _maximize = WindowButton("maximize", Loc.T("最大化", "Maximize"), () => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }); buttons.Children.Add(_maximize);
        buttons.Children.Add(WindowButton("close", Loc.T("关闭设置", "Close settings"), Close, true));
        // Focusable only to hold focus after a click on empty space; Tab never stops on it and it draws no outline.
        var frame = new Border { Child = outer, BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1), Focusable = true, FocusVisualStyle = null };
        KeyboardNavigation.SetIsTabStop(frame, false);
        RadioGroups.Use(); RadioGroups.SetEnabled(frame, true);
        Content = frame; RootVisual = frame; UpdateCaption(); Native.SettingsChrome.SetTheme(this, _palette.Dark); ShowPage(_page);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SettingsError"); PaletteChanged?.Invoke();
    }
    // Clicking anything that is not a control takes focus off a text field, as elsewhere in Windows (EmptyClickFocus).
    private static void ReleaseFocusOnEmptyClick(Window window, Func<UIElement> holder) => EmptyClickFocus.Attach(window, holder);
    private StackPanel Brand(double size, Thickness margin)
    {
        var brand = new StackPanel { Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        // The wordmark carries the application icon's status dot at its top right: 7 px at the 24 px size, centred on the cap line,
        // 4 px clear of the final letter. The brim line underneath stays.
        var name = Wordmark(size);
        double dot = Math.Round(size * .3), gap = Math.Round(size * .17);
        name.Margin = new Thickness(0, 0, dot + gap, 0);
        var mark = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        mark.Children.Add(name);
        mark.Children.Add(new Ellipse { Width = dot, Height = dot, Fill = UI.Brush("#E5A385"), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Math.Round(size * .12), 0, 0), IsHitTestVisible = false });
        brand.Children.Add(mark);
        brand.Children.Add(new Border { Width = 30, Height = 1, Margin = new Thickness(1,7,0,0), HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(TextTertiary), Colors.Transparent, 0) });
        return brand;
    }
    // The product name on one line: a heavier "Deck" followed by a light "Island".
    private TextBlock Wordmark(double size)
    {
        var name = new TextBlock { FontFamily = SettingsFont(UiFontRole.Text), FontSize = size, Foreground = UI.Brush(TextPrimary) };
        name.Inlines.Add(new Run("Deck") { FontWeight = FontWeights.SemiBold });
        name.Inlines.Add(new Run("Island") { FontWeight = FontWeights.Light, Foreground = UI.Brush(_palette.Dark ? "#C5C5CB" : "#565661") });
        AutomationProperties.SetName(name, "DeckIsland"); return name;
    }
    private TextBlock TextLine(string text, double size = 13, string color = "#F1F1F4", FontWeight? weight = null)
    {
        var block = UI.Line(text, size, color, weight); block.FontFamily = SettingsFont(UiFontRole.Panel);
        string? resource = color == TextPrimary ? "SettingsTextPrimary" : color == TextSecondary ? "SettingsTextSecondary" :
            color is "#F0A6AA" or "#B12E3A" ? "SettingsError" : null;
        if (resource is not null) block.SetResourceReference(TextBlock.ForegroundProperty, resource);
        return block;
    }
    public void SaveStatus(string message, bool error = false)
    { _status.Text = error ? message : ""; _status.Foreground = UI.Brush(_palette.Dark ? "#F0A6AA" : "#B12E3A"); _status.Visibility = error ? Visibility.Visible : Visibility.Collapsed; }

    private Button ActionButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Style = (Style)FindResource(primary ? "SettingsPrimaryButton" : "SettingsButton") };
        button.Click += (_, _) => action(); return button;
    }

    private Button WindowButton(string glyph, string label, Action action, bool close = false)
    {
        var button = ActionButton("", action); button.Content = CaptionIcon(glyph); button.Width = 42; button.Height = 32;
        button.Padding = new Thickness(0); button.Margin = new Thickness(1, 4, 0, 0); button.ToolTip = label;
        if (close) button.Style = (Style)FindResource("SettingsCloseButton");
        WindowChrome.SetIsHitTestVisibleInChrome(button, true); AutomationProperties.SetName(button, label); return button;
    }
    private Path CaptionIcon(string kind)
    {
        var data = kind switch { "minimize" => "M 1,6 L 11,6", "maximize" => "M 1,1 L 11,1 L 11,11 L 1,11 Z",
            "restore" => "M 4,1 L 11,1 L 11,8 M 1,4 L 8,4 L 8,11 L 1,11 Z", _ => "M 1,1 L 11,11 M 11,1 L 1,11" };
        var path = Stroke(data,12,12,TextSecondary);
        path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        return path;
    }

    // Small vector icons drawn with strokes, independent of icon font availability.
    private static Path Stroke(string data, double width, double height, string color) => new()
    {
        Data = Geometry.Parse(data), Width = width, Height = height, Stroke = UI.Brush(color), StrokeThickness = 1.4,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
    };

    // The drag handle: two columns of three dots.
    internal static UniformGrid Grip(string color)
    {
        var grip = new UniformGrid { Columns = 2, Rows = 3, Width = 9, Height = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 6; i++) grip.Children.Add(new Ellipse { Width = 2.4, Height = 2.4, Fill = UI.Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return grip;
    }

    private void Change(Action<DeckSettings> edit) { var settings = S.Copy(); edit(settings); _app.UpdateSettings(settings); }

    // Many choices rebuild the page. The control that had keyboard focus is replaced by an identical one, so focus
    // moves to it by name (and position among controls of the same name) instead of being dropped.
    private (string Key, int Index)? _focusToRestore;
    private static string FocusKey(DependencyObject element) =>
        AutomationProperties.GetAutomationId(element) is { Length: > 0 } id ? "#" + id : AutomationProperties.GetName(element);
    private IEnumerable<FrameworkElement> FocusCandidates(string key) =>
        new DependencyObject[] { _navigation, _content }.SelectMany(Descendants).OfType<FrameworkElement>().Where(e => e.Focusable && FocusKey(e) == key);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child)) yield return item;
    }
    private void RememberFocus()
    {
        if (_focusToRestore is not null || Keyboard.FocusedElement is not FrameworkElement focused) return;
        if (!_navigation.IsAncestorOf(focused) && !_content.IsAncestorOf(focused)) return;
        var key = FocusKey(focused);
        if (key.Length == 0) return;
        int index = FocusCandidates(key).ToList().IndexOf(focused);
        if (index >= 0) _focusToRestore = (key, index);
    }
    private void RestoreFocus()
    {
        if (_focusToRestore is not { } target) return;
        _focusToRestore = null;
        Dispatcher.BeginInvoke(() =>
        {
            if (Keyboard.FocusedElement is FrameworkElement current && IsAncestorOf(current)) return;
            FocusCandidates(target.Key).Where(e => e.IsVisible && e.IsEnabled).ElementAtOrDefault(target.Index)?.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void ShowPage(int page) { using (OwnFonts()) ShowPageCore(page); }
    private void ShowPageCore(int page)
    {
        if (page < 0 || page >= _pages.Length) return;
        // Weather, Focus timer and Reminders are cards on the Widgets page now; a link to one of them opens that card.
        int? card = page is WeatherSettingsPage or FocusSettingsPage or RemindersSettingsPage ? page : null;
        if (card is not null) page = WidgetsSettingsPage;
        RememberFocus();
        if (page != _page) { _expandedSource = null; _openSizes.Clear(); _phraseEdit = null; }
        if (page != ThemePage) WatchThemeFolder(false);
        _sourceTestCancellation?.Cancel();
        EndAppDrag(false, false);
        _appRowMotionVersions.Clear();
        _sourceRows.Clear();
        _appRows = null;
        if (page != _page && _preview) { _preview = false; _app.PreviewDeck.Preview(false); }
        // Rebuilding the same page after a change keeps the reader where they were; only switching pages starts at the top.
        double keep = page == _page ? Math.Max(_scroll.VerticalOffset, _keptOffset) : 0; _keptOffset = 0;
        // The exception list's reserved height only lasts for this visit of the page.
        _exceptionsReservedHeight = 0; _exceptionsOpen = false; _exceptionsHost = null;
        _ignoredOpen = false; _ignoredHost = null;
        _scroll.ScrollChanged -= ExceptionsScrollChanged;
        _refreshSensors = null;
        _page = page; _cardTitle = null; _cardBody = null; _navigation.Children.Clear(); _content.Children.Clear(); _optionCards.Clear(); _aboutUpdateView = null; _scroll.ScrollToTop();
        foreach (int i in PageOrder)
        {
            if (i is 0 or 3 or 5 or 7 or MonitorSettingsPage or ShelfLayoutSettingsPage or DevicesSettingsPage)
            {
                var group = TextLine(i switch { 0 => Loc.T("基础设置", "Basics"), 3 => Loc.T("AI 用量", "AI usage"), MonitorSettingsPage => Loc.T("系统监控", "System monitor"), ShelfLayoutSettingsPage => Loc.T("中转站", "Shelf"), DevicesSettingsPage => Loc.T("设备电量", "Device batteries"), WeatherSettingsPage => Loc.T("天气", "Weather"), RemindersSettingsPage => Loc.T("效率工具", "Productivity"), 5 => Loc.T("音乐", "Music"), _ => Loc.T("关于", "About") },10.5,TextTertiary);
                group.Margin = new Thickness(12, i == 0 ? 0 : 24, 0, 8); _navigation.Children.Add(group);
            }
            int target=i; bool selected=page==i;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = NavigationIcon(i, selected ? TextPrimary : TextSecondary); icon.Margin = new Thickness(0,0,10,0); row.Children.Add(icon);
            row.Children.Add(TextLine(i == 7 ? "DeckIsland" : _pages[i],12.5,selected ? TextPrimary : TextSecondary,selected ? FontWeights.Medium : FontWeights.Normal));
            if (i == 7) { _updateBadge = UpdateBadge(); row.Children.Add(_updateBadge); }
            var button=new Button { Style=(Style)FindResource("SettingsNav"),Content=row,Margin=new Thickness(0,0,0,4),Tag=selected ? "Selected" : null };
            button.Click+=(_,_)=>ShowPage(target); AutomationProperties.SetName(button,_pages[i]); _navigation.Children.Add(button);
            // The rebuilt list keeps its scroll position; a page opened from elsewhere (the tray, a link) is brought into view.
            if (selected) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => { if (button.IsLoaded) button.BringIntoView(); });
        }
        // The About page is titled by the wordmark itself, so the name does not appear twice.
        var head = new DockPanel { Margin = new Thickness(0,0,0,page == 7 ? 10 : 20) };
        if (page is < 4 or 5 or 6 or ThemePage or MonitorSettingsPage or HotkeysPage or DevicesSettingsPage or ClipboardSettingsPage or ShelfLayoutSettingsPage or MusicGesturesPage)
        {
            // Resets every setting on the page, so it asks first (SW-09, S-03); it keeps the look of the other "restore defaults" links.
            var reset=ActionButton(Loc.T("恢复默认", "Restore defaults"),()=>{ if(Confirm(Loc.T("恢复默认", "Restore defaults"),ResetConfirmText(page),Loc.T("恢复默认", "Restore defaults")))ResetPage(); }); reset.FontSize=11; DockPanel.SetDock(reset,Dock.Right); head.Children.Add(reset);
        }
        head.Children.Add(page == 7 ? Wordmark(28) : TextLine(_pages[page],24,TextPrimary,FontWeights.SemiBold)); _content.Children.Add(head);
        if (page == 0) PageHeader(head, Loc.T("启动、外观以及 DeckIsland 在桌面上的行为。", "Startup, appearance and how DeckIsland behaves on your desktop."));
        if (page == 1) PageHeader(head, Loc.T("面板收起时的样子、显示的内容和位置。", "How the island looks, what it shows and where it sits while closed."));
        if (page == 3) PageHeader(head, Loc.T("灵动岛显示哪些应用的配额和用量，以及如何显示。", "Which apps' quotas and usage the island shows, and how."));
        if (page == 4) PageHeader(head, Loc.T("面板和模型明细中的费用按这里的单价估算。价格每天从 OpenRouter 更新，手动添加的价格优先。",
            "Costs in the panel and model details are estimated with these unit prices. Prices are updated daily from OpenRouter; prices added by hand take precedence."));
        if (page == 5) PageHeader(head, Loc.T("音乐在灵动岛上收起和展开时的样子。", "How music looks on the island, collapsed and open."));
        if (page == 6) PageHeader(head, Loc.T("针对特定音乐播放器的设置。", "Settings for particular music players."));
        if (page == MusicGesturesPage) PageHeader(head, Loc.T("在音乐页面上拖动和滑动时执行的操作。", "What drags and swipes do on the music page."));
        if (page == MonitorSettingsPage) PageHeader(head, Loc.T("系统监控的颜色、读数和数据来源。", "The colors, readings and sources of the system monitor."));
        if (page == ShelfLayoutSettingsPage) PageHeader(head, Loc.T("中转站里剪贴板和文件中转的布局。", "How the shelf lays out the clipboard and the file transfer."));
        if (page == ClipboardSettingsPage) PageHeader(head, Loc.T("剪贴板记录哪些内容、保留多久，以及如何分类。", "What the clipboard keeps, for how long, and how it sorts it."));
        if (page == TransferSettingsPage) PageHeader(head, Loc.T("暂放在灵动岛上、稍后再拖出的文件。", "Files you park on the island to drag out later."));
        if (page == ClipboardSyncSettingsPage) PageHeader(head, Loc.T("在这台电脑和其他设备之间同步复制的内容。", "Sync what you copy between this PC and your other devices."));
        if (page == DevicesSettingsPage) PageHeader(head, Loc.T("已连接设备的电量，收起时和展开时。", "The battery levels of your connected devices, collapsed and open."));
        if (page == ScratchpadSettingsPage) PageHeader(head, Loc.T("带语法着色、按语言分别保存的代码草稿。", "A code pad that keeps a draft per language."));
        if (page == ConverterSettingsPage) PageHeader(head, Loc.T("单位换算，以及按央行参考汇率的货币换算。", "Unit conversion, and currencies at the central banks' reference rates."));
        if (page == TerminalSettingsPage) PageHeader(head, Loc.T("在灵动岛里运行的命令行及其外观。", "The shell that runs inside the notch, and how it looks."));
        if (page == CallsSettingsPage) PageHeader(head, Loc.T("通话时灵动岛显示的计时、静音和设备检查。", "The timer, mute and device check the island offers during calls."));
        if (page == ThemePage) PageHeader(head, Loc.T("刘海、胶囊和展开面板的配色与外观。", "The colors and look of the notch, capsule and open panel."));
        if (page == HotkeysPage) PageHeader(head, Loc.T("在任何地方都能使用的快捷键。", "Keyboard shortcuts that work from anywhere."));
        if (page == ProfilesPage) PageHeader(head, Loc.T("保存多套设置，按显示器或应用自动切换。", "Keep sets of settings and switch between them by display or app."));
        if (page == NotificationsSettingsPage) PageHeader(head, Loc.T("灵动岛显示的通知、状态提示和实时活动。", "What the island tells you: notifications, readouts and things in progress."));
        if (page == WidgetsSettingsPage) PageHeader(head, Loc.T("选择小组件页显示哪些小组件并逐一设置，以及代码草稿、换算和终端标签页。", "Choose what the widget page shows, set each widget up, and the Scratchpad, Converter and Terminal tabs."));
        if (page == 2) PageHeader(head, Loc.T("展开面板显示哪些页面、它们的尺寸以及如何响应。", "Which pages the open island shows, how big they are and how it responds."));
        switch(page) { case WidgetsSettingsPage: WidgetsSettings(); break; case NotificationsSettingsPage: NotificationsSettings(); break; case CallsSettingsPage: CallsSettings(); break; case ScratchpadSettingsPage: ScratchpadSettings(); break; case ConverterSettingsPage: ConverterSettings(); break; case TerminalSettingsPage: TerminalSettings(); break; case 0: General(); break; case 1: Compact(); break; case 2: Expanded(); break; case 3: Pages(); break; case 4: ModelPrices(); break; case 5: MusicStyle(); break; case 6: Players(); break; case 7: About(); break; case ThemePage: ThemesPage(); break; case MonitorSettingsPage: MonitorSettings(); break; case SensorsPage: SensorsSettings(); break; case HotkeysPage: HotkeysSettings(); break; case DevicesSettingsPage: DevicesSettings(); break; case ClipboardSettingsPage: ClipboardSettings(); break; case TransferSettingsPage: TransferSettings(); break; case ClipboardSyncSettingsPage: ClipboardSyncSettings(); break; case ShelfLayoutSettingsPage: ShelfLayoutSettings(); break; case MusicGesturesPage: MusicGesturesSettings(); break; case ProfilesPage: ProfilesSettings(); break; }
        if (_content.Children[^1] is Border lastGroup) lastGroup.Margin = new Thickness(lastGroup.Margin.Left, lastGroup.Margin.Top, lastGroup.Margin.Right, 0);
        // Apply the offset before the frame is drawn, so the rebuilt page never shows its top for a frame.
        if (keep > 0) { _scroll.UpdateLayout(); _scroll.ScrollToVerticalOffset(keep); _scroll.UpdateLayout(); }
        if (card is { } asked && _optionCards.TryGetValue(asked, out var opened))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => { if (opened.IsLoaded) opened.BringIntoView(new Rect(0, 0, opened.ActualWidth, 1)); });
        RestoreFocus();
    }
    // The scroll position of the page being rebuilt by a theme change, whose scroll viewer is replaced.
    private double _keptOffset;
    private static Path NavigationIcon(int page, string color) => Stroke(page switch
    {
        0 => "M 1,4 H 15 M 1,12 H 15 M 5,1 V 7 M 11,9 V 15",
        1 => "M 1,3 H 15 V 13 H 1 Z M 5,3 V 6 H 11 V 3",
        2 => "M 1,6 V 1 H 6 M 10,1 H 15 V 6 M 15,10 V 15 H 10 M 6,15 H 1 V 10",
        3 => "M 1,1 H 6 V 6 H 1 Z M 10,1 H 15 V 6 H 10 Z M 1,10 H 6 V 15 H 1 Z M 10,10 H 15 V 15 H 10 Z",
        4 => "M 1,2 H 15 V 14 H 1 Z M 1,6 H 15 M 1,10 H 15 M 6,2 V 14",
        6 => "M 1,2 H 15 V 14 H 1 Z M 6.5,5.5 L 11,8 L 6.5,10.5 Z",
        MonitorSettingsPage => "M 1,9 H 4 L 6,4 L 9,13 L 11,7 L 12,9 H 15",
        WeatherSettingsPage => "M 8,4 A 4,4 0 1 0 8,12 A 4,4 0 1 0 8,4 M 8,0.5 V 2 M 8,14 V 15.5 M 0.5,8 H 2 M 14,8 H 15.5 M 2.5,2.5 L 3.75,3.75 M 12.25,12.25 L 13.5,13.5 M 13.5,2.5 L 12.25,3.75 M 3.75,12.25 L 2.5,13.5",
        ScratchpadSettingsPage => "M 5,4 L 1,8 L 5,12 M 11,4 L 15,8 L 11,12 M 9.5,2 L 6.5,14",
        ConverterSettingsPage => "M 2,5 H 13 M 10,2 L 13,5 L 10,8 M 14,11 H 3 M 6,8 L 3,11 L 6,14",
        TerminalSettingsPage => "M 2,2.5 H 14 A 1,1 0 0 1 15,3.5 V 12.5 A 1,1 0 0 1 14,13.5 H 2 A 1,1 0 0 1 1,12.5 V 3.5 A 1,1 0 0 1 2,2.5 Z M 4,6 L 6,8 L 4,10 M 8,10 H 11",
        CallsSettingsPage => "M 8,2 A2,2 0 0 1 10,4 V8 A2,2 0 0 1 6,8 V4 A2,2 0 0 1 8,2 Z M3.5,7.5 A4.5,4.5 0 0 0 12.5,7.5 M8,12 V14.5",
        NotificationsSettingsPage => "M 3,3 H13 A1,1 0 0 1 14,4 V10 A1,1 0 0 1 13,11 H7 L4,14 V11 H3 A1,1 0 0 1 2,10 V4 A1,1 0 0 1 3,3 Z",
        RemindersSettingsPage => "M 2,11 C 4,9 4,8 4,6 A 4,4 0 0 1 12,6 C 12,8 12,9 14,11 Z M 6,14 A 2,2 0 0 0 10,14",
        FocusSettingsPage => "M 8,4 A 5.5,5.5 0 1 0 8,15 A 5.5,5.5 0 1 0 8,4 M 6,1 H 10 M 8,6.5 V 9.5 L 10,11",
        DevicesSettingsPage => "M 3,4.5 H 11.5 A 1.5,1.5 0 0 1 13,6 V 10 A 1.5,1.5 0 0 1 11.5,11.5 H 3 A 1.5,1.5 0 0 1 1.5,10 V 6 A 1.5,1.5 0 0 1 3,4.5 Z M 14.8,7 V 9 M 4.5,7 V 9 M 7,7 V 9",
        SensorsPage => "M 4,4 H 12 V 12 H 4 Z M 6,1 V 4 M 10,1 V 4 M 6,12 V 15 M 10,12 V 15 M 1,6 H 4 M 1,10 H 4 M 12,6 H 15 M 12,10 H 15",
        MusicGesturesPage => "M 1,8 H 15 M 4,5 L 1,8 L 4,11 M 12,5 L 15,8 L 12,11",
        HotkeysPage => "M 1,3 H 15 V 13 H 1 Z M 4,6 V 6.1 M 7,6 V 6.1 M 10,6 V 6.1 M 13,6 V 6.1 M 4,10 H 12",
        // Three stacked layers.
        ProfilesPage => "M 8,1.5 L 15,5 L 8,8.5 L 1,5 Z M 1,8 L 8,11.5 L 15,8 M 1,11 L 8,14.5 L 15,11",
        ClipboardSyncSettingsPage => "M 2,5 H 14 M 10,1 L 14,5 L 10,9 M 14,11 H 2 M 6,7 L 2,11 L 6,15",
        ClipboardSettingsPage => "M 3,2.5 H 13 V 15 H 3 Z M 6,1 H 10 V 4 H 6 Z M 5.5,8 H 10.5 M 5.5,11 H 10.5",
        // Two columns, the right one narrower.
        ShelfLayoutSettingsPage => "M 1,2.5 H 15 V 13.5 H 1 Z M 10,2.5 V 13.5",
        TransferSettingsPage => "M 1.5,9 L 3.5,3 H 12.5 L 14.5,9 V 13 A 1,1 0 0 1 13.5,14 H 2.5 A 1,1 0 0 1 1.5,13 Z M 1.5,9 H 5.5 L 6.5,10.5 H 9.5 L 10.5,9 H 14.5",
        ThemePage => "M 8,1 C 4,1 1,4 1,8 C 1,12 4,15 8,15 C 9.4,15 9.6,13.6 8.8,12.8 C 8,12 8.6,10.5 10,10.5 H 12 C 13.7,10.5 15,9.2 15,7.5 C 15,4 12,1 8,1 Z M 4.6,8 V 8.1 M 6,4.8 V 4.9 M 9.6,4.4 V 4.5 M 12,6.8 V 6.9",
        5 => "M 6,12.5 V 3 L 14,1.5 V 11 M 6,12.5 A 2,2 0 1 1 2,12.5 A 2,2 0 1 1 6,12.5 M 14,11 A 2,2 0 1 1 10,11 A 2,2 0 1 1 14,11",
        _ => "M 8,1 A 7,7 0 1 1 7.99,1 M 8,7 V 12 M 8,4 V 4.2"
    },16,16,color);
    private void General()
    {
        var startup = Describe(Toggle(Loc.T("开机自启动", "Start with Windows"),S.LaunchAtStartup,value =>
        {
            try { Native.StartupRegistration.SetEnabled(value); Change(s=>s.LaunchAtStartup=value); }
            catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { SaveStatus(Loc.T("无法设置自启动：", "Could not change the startup setting: ")+ex.Message,true); ShowPage(0); }
        }), Loc.T("登录 Windows 时自动打开 DeckIsland。", "Open DeckIsland when you sign in."));
        var tray = Describe(Toggle(Loc.T("显示托盘图标", "Show tray icon"), S.ShowTrayIcon, value => Change(s => s.ShowTrayIcon = value),
            help: Loc.T("关闭后，再次打开 DeckIsland 即可显示隐藏的面板并打开设置。",
                "When off, open DeckIsland again to show hidden panels and open the settings.")), Loc.T("在通知区域显示它的图标。", "Keep its icon in the notification area."));
        var memory = Describe(Row(Loc.T("资源占用", "Resource use"), SegmentBorder(MemoryModes()), help: () => Loc.T(
            "省资源：收起后很快回收暂时不用的内存，并在空闲期间持续回收，占用最少；再次使用时需要重新读取，部分操作可能稍有卡顿。\n平衡：几分钟没有使用后回收不用的内存，兼顾占用与流畅。\n流畅：收起后不回收内存，常用的数据保留更久，展开和切换页面更流畅，占用较多。\n在设置中关闭的页面，其占用的资源会在一段时间后释放。",
            "Saver: frees memory not in use soon after the panel closes and keeps freeing it while idle, using the least. It is read again when needed, so some actions may stutter slightly.\nBalanced: frees unused memory after a few minutes without use, balancing resource use and smoothness.\nSmooth: frees no memory after the panel closes and keeps frequently used data longer, so opening the panel and switching pages feel smoother, using more resources.\nThe resources of a page switched off in the settings are released a while later.")),
            Loc.T("在内存占用和流畅之间取舍。", "Trade memory for smoothness."));
        GeneralCard(Loc.T("启动与资源占用", "Startup and resources"), Loc.T("DeckIsland 如何启动，以及保留多少内存。", "How DeckIsland starts and how much memory it keeps."), startup, tray, memory);

        // Each language is named in itself. Choosing one rebuilds the window in it, as a theme change does.
        (string Value, string Name)[] languages = [(Loc.Chinese, "简体中文"), (Loc.English, "English")];
        var language = new ComboBox { Width=GeneralCombo,Style=(Style)FindResource("SettingsCombo"),ItemsSource=languages.Select(l=>l.Name).ToList(),
            SelectedIndex=Math.Max(0,Array.FindIndex(languages,l=>l.Value==S.Language)) };
        AutomationProperties.SetName(language,Loc.T("语言", "Language")); AutomationProperties.SetAutomationId(language,"language"); PopupMotion.Attach(language);
        language.SelectionChanged+=(_,_)=>
        {
            if(language.SelectedIndex<0||languages[language.SelectedIndex].Value==S.Language) return;
            var value=languages[language.SelectedIndex].Value; Change(s=>s.Language=value); RebuildTheme();
        };
        // The scale DeckIsland is drawn at, collapsed and expanded alike (PanelScales); sizes set elsewhere are before it.
        var scales = PanelScales.Choices;
        var scale = new ComboBox { Width=GeneralCombo,Style=(Style)FindResource("SettingsCombo"),ItemsSource=scales.Select(value=>value+"%").ToList(),
            SelectedIndex=Math.Max(0,scales.ToList().IndexOf(PanelScales.Fit(S.PanelScale))) };
        AutomationProperties.SetName(scale,Loc.T("缩放", "Scale")); PopupMotion.Attach(scale);
        scale.SelectionChanged+=(_,_)=> { if(scale.SelectedIndex>=0) { var value=scales[scale.SelectedIndex]; Change(s=>s.PanelScale=value); } };
        GeneralCard(Loc.T("外观", "Appearance"), Loc.T("主题、语言、大小和形状。", "Theme, language, size and shape."),
            Describe(Row(Loc.T("主题", "Theme"), ThemeCards()), Loc.T("跟随 Windows，或选择一种。", "Follow Windows or pick one.")),
            Describe(Row(Loc.T("语言", "Language"), language), Loc.T("DeckIsland 各处使用的语言。", "Used everywhere in DeckIsland.")),
            Describe(Row(Loc.T("缩放", "Scale"), scale), Loc.T("面板收起和展开时的大小。", "The island's size, collapsed and expanded.")),
            Describe(InlineSlider(Loc.T("顶部菜单大小", "Tab bar size"), S.TabBarScale, TabBarScales.Min, TabBarScales.Max, 5, "%", v => Change(s => s.TabBarScale = (int)v), keepPreview: true),
                Loc.T("展开面板顶部页面切换的大小。", "The size of the page switch at the top of the open panel.")),
            Describe(InlineSlider(Loc.T("圆角", "Corner roundness"), S.CornerPercent, 0, 100, 5, "%", v => Change(s => s.CornerPercent = (int)v), keepPreview: true),
                Loc.T("面板圆角的程度。", "How round the island's corners are.")),
            // The surface's transparency (DeckSettings.SurfaceTransparency).
            Describe(InlineSlider(Loc.T("背景透明度", "Background transparency"), S.SurfaceTransparency, 0, DeckSettings.MaxSurfaceTransparency, 5, "%",
                value => Change(s => s.SurfaceTransparency = (int)value)), Loc.T("面板背后透出多少。", "How much shows through the panel.")),
            Describe(Toggle(Loc.T("在屏幕共享和录屏中隐藏 DeckIsland", "Hide DeckIsland from screen sharing and recordings"), S.HideFromCapture,
                value => { Change(s => s.HideFromCapture = value); _app.ApplyCaptureExclusion(); }),
                Loc.T("截图、录屏和共享屏幕时不显示它的窗口（Windows 10 2004 及以上）。", "Its windows are left out of screenshots, recordings and shared screens (Windows 10 2004 or later).")));

        // The interface font (UiFonts, Fonts): "Default" keeps the app's own fonts. The installed families are listed when the list is
        // opened and not before, and the choice reaches every window at once (Fonts.Use) without a restart; this window is built again,
        // in the chosen font only when the switch below asks for it (DeckSettings.SettingsFollowsFont). The font row and the switch are
        // always in the app's own fonts (Fonts.Plain), so a font that is hard to read can be changed back from here.
        var fontDefault = Loc.T("默认", "Default");
        List<string> fontNames = S.UiFont.Length > 0 ? [fontDefault, S.UiFont] : [fontDefault];
        bool fontListed = false, fontFilling = false;
        var font = new ComboBox { Width=GeneralCombo,Style=(Style)FindResource("SettingsCombo"),ItemsSource=fontNames,SelectedIndex=S.UiFont.Length > 0 ? 1 : 0,MaxDropDownHeight=320,IsTextSearchEnabled=true,
            FontFamily=Fonts.PlainOf(UiFontRole.Panel) };
        AutomationProperties.SetName(font,Loc.T("界面字体", "Interface font")); AutomationProperties.SetAutomationId(font,"interface-font"); PopupMotion.Attach(font);
        font.DropDownOpened+=(_,_)=>
        {
            if(fontListed) return;
            fontListed=true; fontFilling=true;
            var installed=Fonts.Installed(); var current=S.UiFont;
            List<string> all=[fontDefault]; if(current.Length>0&&!installed.Contains(current,StringComparer.OrdinalIgnoreCase)) all.Add(current);
            all.AddRange(installed);
            font.ItemsSource=all; font.SelectedIndex=current.Length==0?0:Math.Max(0,all.FindIndex(n=>string.Equals(n,current,StringComparison.OrdinalIgnoreCase)));
            fontFilling=false;
        };
        font.SelectionChanged+=(_,_)=>
        {
            if(fontFilling||font.SelectedItem is not string chosen) return;
            var value=font.SelectedIndex==0?"":chosen; if(value==S.UiFont) return;
            Change(s=>s.UiFont=value); RebuildTheme();
        };
        var fontSample = new TextBlock { Text=Loc.T("字体预览 Aa Bb 0123 中英文混排", "Font preview Aa Bb 0123 mixed text"),
            FontFamily=Fonts.Sample(S.UiFont),FontSize=13,Foreground=UI.Brush(TextPrimary),VerticalAlignment=VerticalAlignment.Center };
        FrameworkElement fontRow, followRow;
        using (Fonts.Plain())
        {
            fontRow = Describe(Row(Loc.T("界面字体", "Interface font"),font,help: () => Loc.T("选择界面文字使用的字体，立即生效。图标、等宽的脚本编辑器和时钟的自带字体样式不受影响；字体缺失时会回到默认字体。",
                "Choose the font the interface text is drawn in; it applies at once. Icons, the monospaced script editor and the clock styles that ship their own fonts are not affected; a missing font falls back to the default one.")),
                Loc.T("立即生效。", "Applies at once."));
            followRow = Describe(Toggle(Loc.T("字体应用到设置窗口", "Apply font to settings window"), S.SettingsFollowsFont,
                on => { Change(s => s.SettingsFollowsFont = on); RebuildTheme(); }), Loc.T("这个窗口也使用它。", "Use it in this window too."));
        }
        GeneralCard(Loc.T("字体", "Typography"), Loc.T("DeckIsland 文字使用的字体。", "The font DeckIsland's text is drawn in."), fontRow, followRow, FontPreview(fontSample));

        Interaction(); WindowRules();

        // Quitting is an action, not a setting: last, on its own, as a quiet outline button.
        var exit = ActionButton(Loc.T("退出", "Exit"), _app.ExitApplication); exit.Style = (Style)FindResource("SettingsOutlineButton"); exit.MinWidth = 84;
        GeneralCard(Loc.T("应用", "App"), "", Describe(Row(Loc.T("退出 DeckIsland", "Exit DeckIsland"), exit), Loc.T("关闭 DeckIsland 和它的托盘图标。", "Close DeckIsland and its tray icon.")));
    }
    // Monitors were connected, removed or rearranged; the list of displays is built again.
    // A taskbar that appeared, moved or went away also shows or hides the collapsed group's anchor row.
    internal void DisplaysChanged() { if (!IsLoaded) return; if (_page is 0 or 1 or ProfilesPage) ShowPage(_page); }
    // The style hotkey changed the collapsed style; the page's cards and options follow it.
    internal void CompactStyleChanged()
    {
        if (IsLoaded && _page == 1 && _styleCards.TryGetValue(S.Style, out var card)) card.IsChecked = true;
    }
    private void Compact()
    {
        _cardBody = null;
        SectionTitle(Loc.T("快捷操作", "Shortcuts"), description: Loc.T("收起面板两端的快捷操作。", "Quick actions at the ends of the collapsed island."));
        Group(Describe(GripDoubleClickRow(), Loc.T("双击面板两端的把手时执行的操作。", "What a double click on a handle at either end does.")));
        // The grid clips at its own bounds, so each card keeps 3 DIP or more of its cell free on every side of the
        // keyboard focus ring: 6 DIP left and right (the grid overhangs the card's padding by the same amount) and 3 DIP above.
        var styles=new UniformGrid { Columns=3,Margin=new Thickness(-6,13,-6,18) };
        _styleCards.Clear();
        foreach(var style in Enum.GetValues<CompactStyle>())
        {
            var button=new RadioButton { Style=(Style)FindResource("SettingsChoice"),Content=CompactStylePreview.Create(style,_palette.Dark,S.ShowsClock(style)),Margin=new Thickness(6,3,6,0),Tag=StyleName(style),GroupName="CompactStyle",IsChecked=S.Style==style };
            button.Checked+=(_,_)=>{ var previous=S.Style; if(previous!=style) Change(s=>s.Style=style); ShowStyleOptions(style,previous); }; AutomationProperties.SetName(button,StyleName(style)); styles.Children.Add(button);
            _styleCards[style]=button;
        }
        _styleOptions=new Grid();
        Card(Loc.T("收起样式", "Collapsed style"), Loc.T("面板收起时的形状。下面的选项属于所选样式。", "The island's shape while closed. The options below belong to the chosen style."), null, null, styles);
        _content.Children.Add(_styleOptions); ShowStyleOptions(S.Style,null);
    }
    // One row per expanded page: its switch at the right, and its sizes under the row while the row is open, set in
    // from the row's title. A row opens and closes only by its own arrow or a click on it, whether or not its page is shown.
    private readonly HashSet<DeckPage> _openSizes = [];
    private void Expanded()
    {
        // The last viewed page is always offered, followed by each page that is shown. A choice whose page is switched off
        // reads as the last viewed page, which is what the panel does then, and returns when the page is shown again.
        // Switching a page on or off refills the list in place, so the page is not rebuilt and the switch keeps its animation.
        _cardBody = null;
        var start = new ComboBox { Width = GeneralCombo, Style = (Style)FindResource("SettingsCombo") };
        AutomationProperties.SetName(start, Loc.T("每次打开时显示", "Page shown on opening")); PopupMotion.Attach(start);
        List<(DefaultDeckPage Value, string Label)> starts = [];
        bool filling = false;
        void FillStarts()
        {
            starts = [(DefaultDeckPage.Last, Loc.T("上次停留的页面", "Last viewed page"))];
            foreach (var page in DeckPages.Visible(S))
                starts.Add(page switch
                {
                    DeckPage.Music => (DefaultDeckPage.Music, Loc.T("音乐", "Music")),
                    DeckPage.Monitor => (DefaultDeckPage.Monitor, Loc.T("系统监控", "System monitor")),
                    DeckPage.Clipboard => (DefaultDeckPage.Clipboard, Loc.T("剪贴板与文件中转", "Clipboard and file transfer")),
                    DeckPage.Widgets => (DefaultDeckPage.Widgets, WidgetLayout.Title),
                    DeckPage.Reminders => (DefaultDeckPage.Reminders, Loc.T("提醒", "Reminders")),
                    DeckPage.Notifications => (DefaultDeckPage.Notifications, Loc.T("通知", "Notifications")),
                    DeckPage.Calls => (DefaultDeckPage.Calls, Loc.T("通话", "Calls")),
                    DeckPage.Scratchpad => (DefaultDeckPage.Scratchpad, Loc.T("代码草稿", "Scratchpad")),
                    DeckPage.Converter => (DefaultDeckPage.Converter, Loc.T("换算", "Converter")),
                    DeckPage.Terminal => (DefaultDeckPage.Terminal, Loc.T("终端", "Terminal")),
                    DeckPage.Colour => (DefaultDeckPage.Colour, Loc.T("颜色", "Colour")),
                    DeckPage.Capture => (DefaultDeckPage.Capture, Loc.T("截图与录屏", "Capture")),
                    DeckPage.Focus => (DefaultDeckPage.Focus, Loc.T("专注计时器", "Focus timer")),
                    DeckPage.Weather => (DefaultDeckPage.Weather, Loc.T("天气", "Weather")),
                    DeckPage.Devices => (DefaultDeckPage.Devices, Loc.T("设备电量", "Device batteries")),
                    _ => (DefaultDeckPage.Usage, Loc.T("AI 用量", "AI usage"))
                });
            filling = true;
            start.ItemsSource = starts.Select(option => option.Label).ToList();
            start.SelectedIndex = Math.Max(0, starts.FindIndex(option => option.Value == S.DefaultPage));
            filling = false;
        }
        FillStarts();
        start.SelectionChanged += (_, _) => { if (!filling && start.SelectedIndex >= 0) Change(s => s.DefaultPage = starts[start.SelectedIndex].Value); };
        SectionTitle(Loc.T("显示的页面", "Pages shown"), help: () => Loc.T("除了在下面设置宽度与高度，也可以把鼠标移到展开面板的左右两边、底边或底角，直接拖动调整大小。",
            "Besides setting the width and height below, you can point at the expanded panel's sides, bottom edge or bottom corners and drag them to resize it."),
            description: Loc.T("打开或关闭页面，拖动把手排序，展开一行可调整它的尺寸。", "Turn pages on or off, drag a handle to reorder, open a row for its size."));
        // The rows are in the order of the page switch; dragging one by its handle moves its page there at once.
        var pages = new StackPanel();
        _pageRows.Clear();
        foreach (var page in S.PageOrder)
            pages.Children.Add(page switch
            {
                DeckPage.Music => SizeRow(Loc.T("显示音乐", "Show music"), Loc.T("音乐", "music"), page, S.MusicPage, value => { Change(s => s.MusicPage = value); FillStarts(); }, Collect(MusicSizeControls), pages, FillStarts),
                DeckPage.Monitor => SizeRow(Loc.T("显示系统监控", "Show system monitor"), Loc.T("系统监控", "system monitor"), page, S.MonitorPage, value => { Change(s => s.MonitorPage = value); FillStarts(); }, Collect(MonitorSizeControls), pages, FillStarts),
                DeckPage.Clipboard => SizeRow(Loc.T("显示剪贴板与文件中转", "Show clipboard and file transfer"), Loc.T("剪贴板与文件中转", "clipboard and file transfer"), page, S.ClipboardPage, value => { Change(s => s.ClipboardPage = value); FillStarts(); }, Collect(ShelfSizeControls), pages, FillStarts),
                DeckPage.Widgets => SizeRow(WidgetLayout.Title, WidgetLayout.Title, page, S.WidgetsPage, value => { Change(s => s.WidgetsPage = value); FillStarts(); }, Collect(WidgetSizeControls), pages, FillStarts),
                DeckPage.Reminders => SizeRow(Loc.T("显示提醒", "Show Reminders"), Loc.T("提醒", "Reminders"), page, S.RemindersPage, value => { Change(s => s.RemindersPage = value); FillStarts(); }, Collect(ReminderSizeControls), pages, FillStarts),
                DeckPage.Notifications => SizeRow(Loc.T("显示通知", "Show Notifications"), Loc.T("通知", "Notifications"), page, S.NotificationsPage, value => { Change(s => s.NotificationsPage = value); FillStarts(); }, Collect(NotificationSizeControls), pages, FillStarts),
                DeckPage.Calls => SizeRow(Loc.T("显示通话", "Show Calls"), Loc.T("通话", "Calls"), page, S.CallsPage, value => { Change(s => s.CallsPage = value); FillStarts(); }, Collect(CallSizeControls), pages, FillStarts),
                DeckPage.Scratchpad => SizeRow(Loc.T("显示代码草稿", "Show Scratchpad"), Loc.T("代码草稿", "Scratchpad"), page, S.ScratchpadPage, value => { Change(s => s.ScratchpadPage = value); FillStarts(); }, Collect(ScratchpadSizeControls), pages, FillStarts),
                DeckPage.Converter => SizeRow(Loc.T("显示换算", "Show Converter"), Loc.T("换算", "Converter"), page, S.ConverterPage, value => { Change(s => s.ConverterPage = value); FillStarts(); }, Collect(ConverterSizeControls), pages, FillStarts),
                DeckPage.Colour => SizeRow(Loc.T("显示颜色", "Show Colour"), Loc.T("颜色", "Colour"), page, S.ColourTool,
                    value => { Change(s => s.ColourTool = value); FillStarts(); }, Collect(() => PageSizeControls(DeckPage.Colour, Loc.T("颜色", "Colour"))), pages, FillStarts),
                DeckPage.Capture => SizeRow(Loc.T("显示截图与录屏", "Show Capture"), Loc.T("截图与录屏", "Capture"), page, S.CapturePage,
                    value => { Change(s => s.CapturePage = value); FillStarts(); }, Collect(() => PageSizeControls(DeckPage.Capture, Loc.T("截图与录屏", "Capture"))), pages, FillStarts),
                DeckPage.Terminal => SizeRow(Loc.T("显示终端", "Show Terminal"), Loc.T("终端", "Terminal"), page, S.TerminalPage, value => { Change(s => s.TerminalPage = value); FillStarts(); }, Collect(TerminalSizeControls), pages, FillStarts),
                DeckPage.Focus => SizeRow(Loc.T("显示专注计时器", "Show Focus timer"), Loc.T("专注计时器", "Focus timer"), page, S.FocusPage, value => { Change(s => s.FocusPage = value); FillStarts(); }, Collect(FocusSizeControls), pages, FillStarts),
                DeckPage.Weather => SizeRow(Loc.T("显示天气", "Show weather"), Loc.T("天气", "weather"), page, S.WeatherPage, value => { Change(s => s.WeatherPage = value); FillStarts(); }, Collect(WeatherSizeControls), pages, FillStarts),
                DeckPage.Devices => SizeRow(Loc.T("显示设备电量", "Show device batteries"), Loc.T("设备电量", "device batteries"), page, S.DevicesPage, value => { Change(s => s.DevicesPage = value); FillStarts(); }, Collect(DeviceSizeControls), pages, FillStarts),
                _ => SizeRow(Loc.T("显示 AI 用量", "Show AI usage"), Loc.T("AI 用量", "AI usage"), page, S.UsagePage, value => { Change(s => s.UsagePage = value); FillStarts(); }, Collect(UsageSizes), pages, FillStarts)
            });
        UpdateDividers(pages);
        Group(pages);
        // Settings that apply across the expanded pages rather than to one of them.
        SectionTitle(Loc.T("通用", "General"), description: Loc.T("展开面板如何打开以及如何响应操作。", "How the open island starts and answers the mouse."));
        Group(Describe(Row(Loc.T("每次打开时显示", "Page shown on opening"), start), Loc.T("展开后首先看到的页面。", "The page you see first.")),
            Describe(Toggle(Loc.T("右键点击打开设置", "Right-click opens settings"), S.RightClickSettings, value => Change(s => s.RightClickSettings = value),
                help: Loc.T("面板展开时，在面板内任意位置单击右键即可打开设置。", "When the panel is expanded, right-click anywhere inside it to open settings.")), Loc.T("在面板内单击右键。", "Right-click inside the panel.")),
            Describe(MouseWheelRow(), Loc.T("在展开的面板上滚动滚轮时。", "Rolling the wheel over the open island.")),
            Describe(ExpandPlaceRow(), Loc.T("刘海比面板更宽时，面板展开的位置。", "Where it opens when the notch is wider.")));
    }
    private const double PageRowHeight = 44;
    private readonly List<(Border Row, StackPanel Sizes, Button Arrow)> _pageRows = [];
    // A page's row: the handle, then the switch with its sizes underneath. A closed row is one line high, as the drag needs.
    private Border SizeRow(string title, string name, DeckPage page, bool shown, Action<bool> show, StackPanel sizes, StackPanel rows, Action reordered)
    {
        bool open = _openSizes.Contains(page);
        Button arrow = null!;
        Border item = null!;
        void Flip()
        {
            bool now = sizes.Visibility != Visibility.Visible;
            if (now) _openSizes.Add(page); else _openSizes.Remove(page);
            sizes.Visibility = now ? Visibility.Visible : Visibility.Collapsed; PointArrow(arrow, now);
            item.Height = now ? double.NaN : PageRowHeight;
        }
        var toggle = new CheckBox { Style = (Style)FindResource("SettingsToggle"), IsChecked = shown, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, title);
        toggle.Checked += (_, _) => show(true);
        toggle.Unchecked += (_, _) => show(false);
        arrow = ChevronButton(Loc.T("展开" + (name.Length > 0 && name[0] < 128 ? " " : "") + name + "尺寸", "Show " + name + " sizes"), () => Flip());
        arrow.Margin = new Thickness(12, 0, 0, 0);
        PointArrow(arrow, open);
        var controls = new StackPanel { Orientation = Orientation.Horizontal }; controls.Children.Add(toggle); controls.Children.Add(arrow);
        var header = Row(title, controls); header.Background = Brushes.Transparent;
        // Clicking anywhere else on the row opens or closes it, as the application rows do.
        header.MouseLeftButtonDown += (_, e) => { Flip(); e.Handled = true; };
        sizes.Visibility = open ? Visibility.Visible : Visibility.Collapsed; sizes.Margin = new Thickness(20, 4, 0, 8);
        var body = new StackPanel(); body.Children.Add(header); body.Children.Add(sizes); Grid.SetColumn(body, 1);
        var handle = new GripHandle { Width = 22, Height = PageRowHeight, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Cursor = Cursors.SizeAll,
            HorizontalAlignment = HorizontalAlignment.Left, Child = Grip(TextTertiary) };
        var row = new Grid { Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PartHandleWidth) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(handle); row.Children.Add(body);
        item = new Border { Child = row, Height = open ? double.NaN : PageRowHeight, BorderBrush = UI.Brush(_palette.RowDivider), BorderThickness = new Thickness(0), Tag = page, RenderTransform = new TranslateTransform() };
        _pageRows.Add((item, sizes, arrow));
        // The sizes close when a drag begins, so every row is one line high while they move.
        AttachDrag(handle, title, () => rows, PageRowHeight, list =>
        {
            var order = list.Children.OfType<Border>().Where(b => b.Tag is DeckPage).Select(b => (DeckPage)b.Tag).ToArray();
            Change(s => s.PageOrder = [.. order]);
            reordered();
        }, ClosePageSizes);
        return item;
    }
    private void ClosePageSizes()
    {
        _openSizes.Clear();
        foreach (var (row, sizes, arrow) in _pageRows) { sizes.Visibility = Visibility.Collapsed; PointArrow(arrow, false); row.Height = PageRowHeight; }
    }
    // A page's width and height side by side, then its quick sizes (DeckPages.QuickSize); a quick size sets both and rebuilds
    // the page, so the sliders show it. The one matching the page's size is shown as chosen.
    private void PageSizeControls(DeckPage page, string name)
    {
        var (width, height) = DeckPages.Size(S, page);
        var (minWidth, maxWidth, minHeight, maxHeight) = DeckPages.Limits(S, page);
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) }; dimensions.ColumnDefinitions.Add(new()); dimensions.ColumnDefinitions.Add(new());
        var wide = Dimension(Loc.T("宽度", "Width"), width, minWidth, maxWidth, value => Resize(s => DeckPages.SetSize(s, page, value, DeckPages.Size(s, page).Height), page));
        var tall = Dimension(Loc.T("高度", "Height"), height, minHeight, maxHeight, value => Resize(s => DeckPages.SetSize(s, page, DeckPages.Size(s, page).Width, value), page));
        wide.Margin = new Thickness(0, 0, 20, 0); tall.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(tall, 1);
        dimensions.Children.Add(wide); dimensions.Children.Add(tall);
        Target.Children.Add(dimensions);
        var sizes = KeepPreview(new UniformGrid { Columns = 3, Width = 292 });
        foreach (var size in Enum.GetValues<PanelSize>())
        {
            var (presetWidth, presetHeight) = Quick(page, size);
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = PanelSizes.Name(size), GroupName = "QuickSize" + page, IsChecked = (width, height) == (presetWidth, presetHeight) };
            AutomationProperties.SetName(choice, Loc.T(name + "快捷尺寸 ", name + " quick size ") + PanelSizes.Name(size));
            choice.Checked += (_, _) => { Resize(s => DeckPages.SetSize(s, page, presetWidth, presetHeight), page); ShowPage(2); };
            sizes.Children.Add(choice);
        }
        QuickSizeRow(sizes);
    }
    // A page's quick size on the monitor the panel is on, at the panel's scale.
    private (double Width, double Height) Quick(DeckPage page, PanelSize size)
    {
        var work = _app.PreviewDeck.WorkDip;
        return DeckPages.QuickSize(S, page, size, work.Width, work.Height, PanelScales.Factor(S));
    }
    private void QuickSizeRow(UniformGrid sizes)
    {
        Target.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider), Margin = new Thickness(0, 20, 0, 0) });
        Target.Children.Add(QuickRow(Loc.T("快捷尺寸", "Quick sizes"), sizes));
    }
    // A row of quick choices, as "快捷尺寸": labels inside an open row are set lighter than the row's own title.
    private Grid QuickRow(string title, UniformGrid choices)
    {
        var row = Row(title, SegmentBorder(choices));
        if (row.Children[0] is StackPanel { Children: [TextBlock label, ..] }) label.FontWeight = FontWeights.Normal;
        return row;
    }
    private void UsageSizes()
    {
        var dimensions=new Grid { Margin=new Thickness(0,8,0,0) }; dimensions.ColumnDefinitions.Add(new()); dimensions.ColumnDefinitions.Add(new());
        var width=Dimension(Loc.T("宽度", "Width"),S.Width,S.MinimumWidth,1200,value=>Resize(s=>{s.Width=value;s.QuickSize=null;})); width.Margin=new Thickness(0,0,20,0); dimensions.Children.Add(width);
        var height=Dimension(Loc.T("高度", "Height"),S.Height,140,400,value=>Resize(s=>{s.Height=value;s.QuickSize=null;})); height.Margin=new Thickness(20,0,0,0); Grid.SetColumn(height,1); dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
        // Quick sizes are computed from the enabled application count so each lands on one of the panel's typography tiers;
        // the segment that matches the current width and height is shown as selected.
        var sizes = KeepPreview(new UniformGrid { Columns = 3, Width = 292 });
        foreach (var size in Enum.GetValues<PanelSize>())
        {
            var (presetWidth, presetHeight) = Quick(DeckPage.Usage, size);
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = PanelSizes.Name(size), GroupName = "PanelSize",
                IsChecked = S.QuickSize is { } quick ? quick == size : (S.Width, S.Height) == (presetWidth, presetHeight) };
            AutomationProperties.SetName(choice, Loc.T("快捷尺寸 ", "Quick size ") + PanelSizes.Name(size));
            // The chosen size is written once; removing or adding applications later leaves it untouched.
            choice.Checked += (_, _) => { Resize(s => { s.Width = presetWidth; s.Height = presetHeight; s.QuickSize = size; }); ShowPage(2); };
            sizes.Children.Add(choice);
        }
        QuickSizeRow(sizes);
    }
    // What the "restore defaults" confirmation says. The quota page also puts the application list back (ResetPage, case 3), which removes the applications
    // the user added and their quota sources, the largest loss of any page, so it says so.
    private string ResetConfirmText(int page) => Loc.T($"将把“{_pages[page]}”页的全部设置恢复为默认值。", $"This puts every setting on the {_pages[page]} page back to its default.")
        + (page == 3 ? Loc.T("\n\n应用列表也会换回默认：你添加的应用、各应用的额度来源与自定义设置都会消失。", "\n\nThe application list also goes back to the default: the applications you added, and each one's quota source and custom settings, are removed.") : "");
    private void ResetPage()
    {
        var defaults=new DeckSettings();
        if(_page==0)
        {
            try { Native.StartupRegistration.SetEnabled(false); }
            catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {SaveStatus(Loc.T("无法恢复自启动设置：", "Could not restore the startup setting: ")+ex.Message,true);return;}
        }
        Change(s=>
        {
            switch(_page)
            {
                case 0:s.LaunchAtStartup=false;s.ShowTrayIcon=defaults.ShowTrayIcon;s.MemoryMode=defaults.MemoryMode;s.Theme=defaults.Theme;s.TimeFormat=defaults.TimeFormat;s.Language=Loc.SystemLanguage();s.OpenDelay=defaults.OpenDelay;s.ClickToExpand=defaults.ClickToExpand;s.CloseDelay=defaults.CloseDelay;s.Animations=defaults.Animations;s.AnimationDuration=defaults.AnimationDuration;s.AlwaysOnTop=defaults.AlwaysOnTop;s.ForceTopmost=defaults.ForceTopmost;s.Maximized=defaults.Maximized;s.Borderless=defaults.Borderless;s.Exclusive=defaults.Exclusive;s.Occupied=defaults.Occupied;s.FullscreenRules=[];s.PanelScale=defaults.PanelScale;s.TabBarScale=defaults.TabBarScale;s.SurfaceTransparency=defaults.SurfaceTransparency;s.UiFont=defaults.UiFont;s.SettingsFollowsFont=defaults.SettingsFollowsFont;
                    s.ClickThrough=defaults.ClickThrough;s.ClickThroughPanel=defaults.ClickThroughPanel;s.ClickThroughKeyboard=defaults.ClickThroughKeyboard;s.WindowCapture=defaults.WindowCapture;break;
                case HotkeysPage:foreach(var kind in Enum.GetValues<HotkeyKind>())s.SetHotkey(kind,defaults.Hotkey(kind));break;
                case 1:s.Style=defaults.Style;s.NotchSummary=defaults.NotchSummary;s.NotchMusic=defaults.NotchMusic;s.CapsuleSummary=defaults.CapsuleSummary;s.CapsuleMusic=defaults.CapsuleMusic;s.MusicIndicatorProgress=defaults.MusicIndicatorProgress;
                    s.NotchFocus=defaults.NotchFocus;s.CapsuleFocus=defaults.CapsuleFocus;
                    s.NotchWeather=defaults.NotchWeather;s.CapsuleWeather=defaults.CapsuleWeather;
                    s.NotchMinimal=defaults.NotchMinimal;s.CapsuleMinimal=defaults.CapsuleMinimal;s.NotchMinimalFirst=defaults.NotchMinimalFirst;s.NotchMinimalSecond=defaults.NotchMinimalSecond;s.CapsuleMinimalFirst=defaults.CapsuleMinimalFirst;s.CapsuleMinimalSecond=defaults.CapsuleMinimalSecond;s.NotchEqualizer=defaults.NotchEqualizer;s.CapsuleEqualizer=defaults.CapsuleEqualizer;s.NotchClock=defaults.NotchClock;s.CapsuleClock=defaults.CapsuleClock;s.ClockStyle=defaults.ClockStyle;s.ClockColor=defaults.ClockColor;s.ClockCustomColor=defaults.ClockCustomColor;s.ClockGradient=[.. PanelTheme.Current.ClockGradients[0].Stops];s.ClockSeconds=defaults.ClockSeconds;CopyCollapsedSize(s,defaults);s.GripDoubleClick=defaults.GripDoubleClick;
                    s.NotchMonitor=defaults.NotchMonitor;s.CapsuleMonitor=defaults.CapsuleMonitor;s.NotchDevices=defaults.NotchDevices;s.CapsuleDevices=defaults.CapsuleDevices;s.NotchOrder=defaults.NotchOrder;s.CapsuleOrder=defaults.CapsuleOrder;s.NotchTransfer=defaults.NotchTransfer;s.CapsuleTransfer=defaults.CapsuleTransfer;
                    // The clock goes back to the one the panel theme suggests, as choosing the theme gives.
                    PanelThemes.ApplyClock(PanelTheme.Current,s);break;
                case 2:s.UsagePage=true;s.MusicPage=defaults.MusicPage;s.DefaultPage=defaults.DefaultPage;s.RightClickSettings=defaults.RightClickSettings;s.Width=defaults.Width;s.Height=defaults.Height;s.QuickSize=null;s.MusicWidth=defaults.MusicWidth;s.MusicHeight=defaults.MusicHeight;s.MouseWheel=defaults.MouseWheel;s.ExpandAtPointer=defaults.ExpandAtPointer;
                    s.MonitorPage=defaults.MonitorPage;s.MonitorWidth=defaults.MonitorWidth;s.MonitorHeight=defaults.MonitorHeight;s.PageOrder=defaults.PageOrder;
                    s.ClipboardPage=defaults.ClipboardPage;s.ClipboardWidth=defaults.ClipboardWidth;s.ClipboardHeight=defaults.ClipboardHeight;
                    s.WidgetsPage=defaults.WidgetsPage;s.WidgetsWidth=defaults.WidgetsWidth;s.WidgetsHeight=defaults.WidgetsHeight;s.RemindersPage=defaults.RemindersPage;s.NotificationsPage=defaults.NotificationsPage;s.CallsPage=defaults.CallsPage;s.CallsWidth=defaults.CallsWidth;s.CallsHeight=defaults.CallsHeight;s.ScratchpadPage=defaults.ScratchpadPage;s.ScratchpadWidth=defaults.ScratchpadWidth;s.ScratchpadHeight=defaults.ScratchpadHeight;s.ConverterPage=defaults.ConverterPage;s.ConverterWidth=defaults.ConverterWidth;s.ConverterHeight=defaults.ConverterHeight;s.ColourTool=defaults.ColourTool;s.ColourWidth=defaults.ColourWidth;s.ColourHeight=defaults.ColourHeight;s.CapturePage=defaults.CapturePage;s.CaptureWidth=defaults.CaptureWidth;s.CaptureHeight=defaults.CaptureHeight;s.TerminalPage=defaults.TerminalPage;s.TerminalWidth=defaults.TerminalWidth;s.TerminalHeight=defaults.TerminalHeight;s.NotificationsWidth=defaults.NotificationsWidth;s.NotificationsHeight=defaults.NotificationsHeight;s.RemindersWidth=defaults.RemindersWidth;s.RemindersHeight=defaults.RemindersHeight;
                    s.FocusPage=defaults.FocusPage;s.FocusWidth=defaults.FocusWidth;s.FocusHeight=defaults.FocusHeight;
                    s.WeatherPage=defaults.WeatherPage;s.WeatherWidth=defaults.WeatherWidth;s.WeatherHeight=defaults.WeatherHeight;
                    s.DevicesPage=defaults.DevicesPage;s.DevicesWidth=defaults.DevicesWidth;s.DevicesHeight=defaults.DevicesHeight;break;
                case MonitorSettingsPage:s.MonitorPalette=defaults.MonitorPalette;s.MonitorCustomColors=defaults.MonitorCustomColors;s.CompactWarningColor=null;s.CompactCriticalColor=null;s.CompactSmoothing=defaults.CompactSmoothing;s.CoreDisplay=defaults.CoreDisplay;s.MonitorInterval=defaults.MonitorInterval;s.MonitorProcesses=defaults.MonitorProcesses;s.MonitorSort=defaults.MonitorSort;s.MonitorMain=defaults.MonitorMain;s.MonitorProcessView=defaults.MonitorProcessView;
                    s.CompactMonitor=defaults.CompactMonitor;s.CompactMonitorItems=defaults.CompactMonitorItems;s.CompactReadings=defaults.CompactReadings;s.NotchAutoWidth=defaults.NotchAutoWidth;s.CapsuleAutoWidth=defaults.CapsuleAutoWidth;s.MonitorTemperatures=defaults.MonitorTemperatures;s.MonitorStorage=defaults.MonitorStorage;s.MonitorPower=defaults.MonitorPower;s.MonitorFrames=defaults.MonitorFrames;s.ExpandedTable=null;s.MonitorGpuUsage=defaults.MonitorGpuUsage;s.MonitorTrafficPeriod=defaults.MonitorTrafficPeriod;break;
                // The devices' order, names and switches stay; the page and its size are Expanded view's (case 2).
                case DevicesSettingsPage:s.DeviceLowPercent=defaults.DeviceLowPercent;s.DeviceWarningPercent=defaults.DeviceWarningPercent;s.DeviceFigures=defaults.DeviceFigures;break;
                case ShelfLayoutSettingsPage:s.ShelfShown=defaults.ShelfShown;s.ShelfArrangement=defaults.ShelfArrangement;s.ShelfSameSideStack=defaults.ShelfSameSideStack;s.ShelfFileShare=defaults.ShelfFileShare;s.ShelfHoverSwitch=defaults.ShelfHoverSwitch;s.ShelfMiddleSwitch=defaults.ShelfMiddleSwitch;s.ShelfSwipeSwitch=defaults.ShelfSwipeSwitch;break;
                // The categories and phrases are the user's own data, not settings; the other page has nothing to restore.
                case ClipboardSettingsPage:s.ClipboardDecode=defaults.ClipboardDecode;s.ClipboardPresentationMode=defaults.ClipboardPresentationMode;s.ClipboardRecord=defaults.ClipboardRecord;s.ClipboardExcluded=[];s.ClipboardKeepHistory=defaults.ClipboardKeepHistory;s.ClipboardKeepFor=defaults.ClipboardKeepFor;break;
                // The current theme's application colors go back to its own; the other themes keep what was saved for them.
                case 3:{var previous=s.Apps;s.QuotaAlerts=defaults.QuotaAlerts;s.IncludeWsl=defaults.IncludeWsl;s.QuotaRingStyle=defaults.QuotaRingStyle;s.QuotaRingRemaining=defaults.QuotaRingRemaining;s.QuotaRingLetters=defaults.QuotaRingLetters;s.Apps=defaults.Apps;PanelThemes.CarryAppChoices(s,previous);break;}
                case 5:s.EqualizerAlways=defaults.EqualizerAlways;s.EqualizerBars=defaults.EqualizerBars;s.EqualizerMode=defaults.EqualizerMode;s.EqualizerColor=defaults.EqualizerColor;s.EqualizerCustomColor=defaults.EqualizerCustomColor;
                    s.MusicText=defaults.MusicText;s.MusicEqualizerBars=defaults.MusicEqualizerBars;s.MusicTrackNotice=defaults.MusicTrackNotice;s.MusicPausedHide=defaults.MusicPausedHide;s.MusicInstrumentalTitle=defaults.MusicInstrumentalTitle;s.MusicLyricAutoWidth=defaults.MusicLyricAutoWidth;s.MusicTitleAutoWidth=defaults.MusicTitleAutoWidth;s.MusicCompactCover=defaults.MusicCompactCover;s.MusicPageCover=defaults.MusicPageCover;s.LyricsEnabled=defaults.LyricsEnabled;s.MusicLyricsLayout=defaults.MusicLyricsLayout;s.MusicLyricsAlign=defaults.MusicLyricsAlign;
                    s.MusicLyricColor=defaults.MusicLyricColor;s.MusicLyricCustomColor=defaults.MusicLyricCustomColor;break;
                case MusicGesturesPage:s.MusicDragLeft=defaults.MusicDragLeft;s.MusicDragRight=defaults.MusicDragRight;s.MusicDragUp=defaults.MusicDragUp;s.MusicDragDown=defaults.MusicDragDown;
                    s.MusicSwipeLeft=defaults.MusicSwipeLeft;s.MusicSwipeRight=defaults.MusicSwipeRight;break;
                case 6:s.NeteaseFullControlEntry=defaults.NeteaseFullControlEntry;s.NeteaseAutoFullControl=defaults.NeteaseAutoFullControl;s.AppleMusicFullControl=defaults.AppleMusicFullControl;break;
                // The default theme, with its own colors and suggested clock, the clock reset as the collapsed page resets it; the
                // other themes keep what was saved for them.
                case ThemePage:{ChooseTheme(s,defaults.PanelTheme);var palette=PanelThemes.Resolve(PanelTheme.Find(defaults.PanelTheme));
                    s.ClockStyle=defaults.ClockStyle;s.ClockColor=defaults.ClockColor;s.ClockCustomColor=defaults.ClockCustomColor;s.ClockGradient=[.. palette.ClockGradients[0].Stops];
                    PanelThemes.ApplyMatching(palette,s);break;}
            }
        });
        if(_page==0)RebuildTheme();else ShowPage(_page);
    }

    // A page that is not shown cannot be previewed, so its sizes are only saved.
    private void Resize(Action<DeckSettings> edit, DeckPage page = DeckPage.Usage)
    { bool shown = DeckPages.Shown(S, page); Change(edit); if (!shown) return; _preview = true; _app.PreviewDeck.Preview(true); _app.PreviewDeck.SelectPage(page, remember: false); }

    private void Interaction()
    {
        // motion-review F20: the length covers the panel's changes of shape: expanding and collapsing, and the collapsed surface's
        // width, style and edge, the swell on start and the shrink on exit. Page switches, hover and press feedback keep their own pace.
        var duration = InlineSlider(Loc.T("动画时长", "Animation duration"), S.AnimationDuration, 100, 1000, 20, Loc.T("毫秒", "ms"), value => Change(s => s.AnimationDuration = (int)value),
            Loc.T("展开、收起以及收起时外形变化的时长。",
                "How long expanding, collapsing and changes of the collapsed shape take."));
        var openDelay = InlineSlider(Loc.T("展开延迟", "Expand delay"), S.OpenDelay, 0, 2000, 50, Loc.T("毫秒", "ms"), value => Change(s => s.OpenDelay = (int)value),
            Loc.T("鼠标悬停在 DeckIsland 的收起面板或指示条上多久后，开始展开。0 毫秒表示立即开始展开。",
                "How long the pointer rests on DeckIsland's collapsed panel or indicator before it starts to expand. 0 ms expands immediately."));
        EnableRow(duration, S.Animations);
        EnableRow(openDelay, !S.ClickToExpand);
        Describe(duration, Loc.T("展开和收起所用的时间。", "How long opening and closing take."));
        Describe(openDelay, Loc.T("悬停多久后展开。", "Wait before opening on hover."));
        GeneralCard(Loc.T("悬停与动画", "Hover and animation"), Loc.T("面板何时展开和收起，以及如何移动。", "When the island opens and closes, and how it moves."),
            Describe(ExpandWayRow(click => EnableRow(openDelay, !click)), Loc.T("悬停展开，或只在点击时展开。", "Open on hover or only on click.")), openDelay,
            Describe(InlineSlider(Loc.T("收起延迟", "Collapse delay"), S.CloseDelay, 0, 2000, 50, Loc.T("毫秒", "ms"), value => Change(s => s.CloseDelay = (int)value),
                Loc.T("鼠标离开 DeckIsland 的展开面板范围多久后，开始收起。0 毫秒表示立即开始收起。",
                    "How long after the pointer leaves DeckIsland's expanded panel it starts to collapse. 0 ms collapses immediately.")),
                Loc.T("鼠标离开后多久收起。", "Wait before closing once the pointer leaves.")),
            Describe(Toggle(Loc.T("启用动画", "Animations"), S.Animations, value => { Change(s => s.Animations = value); EnableRow(duration, value); }, help:
                Loc.T("控制面板展开收起、页面切换、悬停与按压反馈等全部动画。关闭，或 Windows 关闭了动画效果、使用高对比度时，界面不再有动画。",
                    "Switches all animation on or off: the panel expanding and collapsing, page changes, hover and press feedback. With it off, or when Windows has animation effects off or uses high contrast, nothing animates.")),
                Loc.T("DeckIsland 中的全部动画。", "All motion in DeckIsland.")), duration);
    }

    private void WindowRules()
    {
        // The window rules apply whether or not the panel is kept above other windows; Keep on top only concerns a panel above
        // them, so it leaves with the switch.
        var rules = new StackPanel();
        foreach (var row in new[]
        {
            BehaviorRow(Loc.T("有窗口最大化时", "When a window is maximized"), S.Maximized, value => Change(s => s.Maximized = value)),
            BehaviorRow(Loc.T("有应用处于无边框全屏时", "When an app is in borderless full screen"), S.Borderless, value => Change(s => s.Borderless = value)),
            BehaviorRow(Loc.T("有应用处于独占全屏时", "When an app is in exclusive full screen"), S.Exclusive, value => Change(s => s.Exclusive = value)),
            BehaviorRow(Loc.T("有窗口与 DeckIsland 重叠时", "When a window overlaps DeckIsland"), S.Occupied, value => Change(s => s.Occupied = value),
                Loc.T("DeckIsland 收起时，有窗口移到它所在的位置就生效，移开后恢复。最大化和全屏窗口按上面的规则处理。",
                    "Applies when a window is moved onto the collapsed DeckIsland, and ends when it is moved away. Maximized and full-screen windows follow the rules above.")),
            Describe(Toggle(Loc.T("同时检测后方窗口", "Also check windows behind"), S.BackgroundWindows, value => Change(s => s.BackgroundWindows = value),
                help: Loc.T("开启后，正在使用的窗口后方有窗口最大化、无边框全屏或与收起的 DeckIsland 重叠时，也按上面对应的规则处理。关闭后只检测正在使用的窗口。",
                    "When on, a window behind the window you are using that is maximized, in borderless full screen or overlapping the collapsed DeckIsland also follows the matching rule above. When off, only the window you are using is checked.")),
                Loc.T("规则也用于当前窗口后方的窗口。", "Apply the rules to windows behind the active one.")),
            ExceptionsRow()
        })
        {
            rules.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider) });
            rules.Children.Add(row);
        }
        var keep = new StackPanel { Visibility = S.AlwaysOnTop ? Visibility.Visible : Visibility.Collapsed };
        var onTop = Describe(Toggle(Loc.T("显示在其他窗口上方", "Keep above other windows"), S.AlwaysOnTop, value =>
        {
            if (S.AlwaysOnTop != value) Change(s => s.AlwaysOnTop = value);
            keep.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }, help: Loc.T("关闭后，面板像普通窗口一样会被其他窗口遮挡，展开时显示在最前面，显示桌面时仍然可见。下面的规则仍然生效：选择“保持原有样式”时面板被遮挡，选择“显示为指示条”时指示条显示在该窗口上方。",
            "When off, other windows can cover the panel like any window. It comes to the front when it expands and stays visible when you show the desktop. The rules below still apply: with Keep style the panel is covered, with As indicator the indicator shows above that window.")),
            Loc.T("保持在其他窗口之上。", "Stay over other windows."));
        var behavior = new StackPanel();
        behavior.Children.Add(onTop); behavior.Children.Add(rules);
        GeneralCard(Loc.T("窗口行为", "Window behavior"), Loc.T("其他窗口需要屏幕时，面板如何处理。", "What the island does when other windows need the screen."), behavior);
        var raise = ActionButton(Loc.T("立即置顶", "Raise now"), () =>
        {
            foreach (var deck in _app.Decks.ToList()) Native.WindowsHost.RaiseOnce(deck, S.AlwaysOnTop);
        });
        raise.BorderBrush = UI.Brush(_palette.Border);
        raise.Background = UI.Brush(_palette.Input);
        keep.Children.Add(Toggle(Loc.T("持续置顶", "Keep on top"), S.ForceTopmost, value => Change(s => s.ForceTopmost = value), help:
            Loc.T("开启后，面板被其他置顶窗口遮住时仍显示在最上方，可以正常操作。其他应用的菜单除外。",
                "When on, the panel stays above other always-on-top windows and works as usual. Menus from other apps are excepted.")));
        keep.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider) });
        var topRows = new StackPanel();
        topRows.Children.Add(keep);
        topRows.Children.Add(Describe(Row(Loc.T("单次置顶", "Raise once"), raise, help: () => Loc.T("每次点击仅置顶一次。若面板再次被其他窗口遮挡，需要重新点击。",
            "Each click raises the panel once. If another window covers it again, click again.")), Loc.T("把面板置顶一次。", "Bring the panel up once.")));
        GeneralCard(Loc.T("兼容性", "Compatibility"), Loc.T("适用于争抢屏幕最上层的应用。", "For apps that compete for the top of the screen."), topRows,
            ClickThroughRows(),
            Describe(Toggle(Loc.T("允许录制软件捕获窗口", "Allow window capture"), S.WindowCapture, value => Change(s => s.WindowCapture = value), help:
                Loc.T("开启后，OBS 等录制软件可以用“窗口采集”单独捕获面板，同时面板会出现在 Alt + Tab 的窗口列表中。",
                    "When on, OBS and other recording apps can capture the panel on its own with window capture. The panel also appears in the Alt+Tab list.")),
                Loc.T("让录制软件可以捕获面板。", "Let recording apps capture the panel.")));
    }

    // Each row independently chooses its quota source, statistics source and dashboard name.
    private void Pages()
    {
        SectionTitle(Loc.T("提示", "Notifications"), description: Loc.T("额度用到预警或告警线时提醒你。", "A heads-up when a quota reaches its warning or critical level."));
        Group(Toggle(Loc.T("预警和告警提示", "Warning and critical notices"), S.QuotaAlerts, v => Change(s => s.QuotaAlerts = v), help:
            Loc.T($"开启后，某个应用的已用比例升到预警（{AppPresets.WarningPercent:0}%）或告警（{AppPresets.CriticalPercent:0}%）时，收起的刘海或胶囊会短暂显示该应用的名称、比例和重置时间，约 5 秒后恢复。比例按“配额显示”中的设置显示已用或剩余。",
                $"When on, and an app's used share rises to the warning ({AppPresets.WarningPercent:0}%) or critical ({AppPresets.CriticalPercent:0}%) level, the collapsed notch or capsule briefly shows the app's name, used share and reset time, then returns after about 5 seconds.")));
        SectionTitle(Loc.T("数据来源", "Data sources"), description: Loc.T("用量统计从哪里读取。", "Where usage is read from."));
        Group(Toggle(Loc.T("同步 WSL 中的用量统计", "Include usage from WSL"), S.IncludeWsl, v => Change(s => s.IncludeWsl = v), help:
            Loc.T("开启后，会读取已安装的 WSL 子系统中 Claude Code 和 Codex CLI 的会话记录，并计入 Token 用量与费用统计。读取时会唤醒尚未运行的 WSL 子系统，默认关闭。",
                "When on, the Claude Code and Codex CLI session records inside installed WSL distributions are read and counted in token usage and cost. Reading wakes a distribution that is not running, so this is off by default.")));
        QuotaRingSection();
        SectionTitle(Loc.T("应用", "Apps"), description: Loc.T("看板上的每一列：配额来源、统计应用、名称和颜色。", "Each column of the panel: its quota source, usage app, name and colors."));
        _appRows = new StackPanel();
        foreach (var entry in S.Apps) _appRows.Children.Add(AppRow(entry));
        UpdateDividers(); UpdateShowLimit();
        var list = new StackPanel();
        var headings = AppColumns(); headings.Height = 34;
        AddHeadings(headings, new[]
        {
            (Loc.T("配额来源", "Quota source"), 1, Loc.T("决定本列额度、余额、进度条和重置时间的数据来源。", "Where this column's quota, balance, progress bar and reset time come from.")),
            (Loc.T("统计应用", "Usage app"), 2, Loc.T("决定本列 Token 用量、费用统计及模型明细来自哪个应用。", "The app whose token usage, cost and model details this column shows.")),
            (Loc.T("显示名称", "Display name"), 3, Loc.T("显示在看板上的名称。默认使用配额来源名称。", "The name shown on the panel. By default, the quota source's name.")),
            (Loc.T("主题", "Theme"), 4, Loc.T("本列进度条和配额环的颜色。配额环始终使用这个颜色。", "The color of this column's progress bars and quota ring. The quota ring always uses this color.")),
            (Loc.T("预警", "Warning"), 5, Loc.T("已用达到 70%（即剩余 30%）时，进度条和数字改用这个颜色。", "At 70% used (30% left), progress bars and numbers change to this color.")),
            (Loc.T("告警", "Critical"), 6, Loc.T("已用达到 90%（即剩余 10%）时，进度条和数字改用这个颜色。", "At 90% used (10% left), progress bars and numbers change to this color.")),
            (Loc.T("显示", "Show"), 7, Loc.T($"最多同时显示 {AppPresets.MaximumShownApps} 个应用。关闭的应用保留其设置。", $"Up to {AppPresets.MaximumShownApps} apps are shown at once. Apps switched off keep their settings."))
        });
        list.Children.Add(headings);
        list.Children.Add(new Border { Child = _appRows, BorderBrush = UI.Brush(Divider), BorderThickness = new Thickness(0, 1, 0, 1) });
        var add = ActionButton("", () =>
        {
            // Past the display limit a new app is added switched off.
            Change(s => s.Apps.Add(new AppEntry { QuotaSource = ProviderId.Codex, UsageSource = ProviderId.Codex, Enabled = !s.ShownAppsFull }));
            ShowPage(3);
        });
        var addContent = new StackPanel { Orientation = Orientation.Horizontal };
        addContent.Children.Add(Stroke("M 5,0 L 5,10 M 0,5 L 10,5", 10, 10, TextSecondary));
        var addLabel = TextLine(Loc.T("添加应用", "Add app"), 12.5, TextSecondary); addLabel.Margin = new Thickness(8, 0, 0, 0); addLabel.VerticalAlignment = VerticalAlignment.Center; addContent.Children.Add(addLabel);
        add.Content = addContent; add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(10, 6, 0, 6); add.Padding = new Thickness(10, 6, 12, 6);
        add.ToolTip = Loc.T($"同一配额来源或统计应用可以重复选择。已显示 {AppPresets.MaximumShownApps} 个应用时，新添加的应用默认不显示。",
            $"The same quota source or usage app can be chosen more than once. With {AppPresets.MaximumShownApps} apps shown, a new app is added switched off.");
        AutomationProperties.SetName(add, Loc.T("添加应用", "Add app"));
        list.Children.Add(add);
        // Into the Apps card (Card), as the sections above; the table reaches nearer the card's edges than rows do, so its seven
        // columns keep their width at the narrowest window.
        list.Margin = new Thickness(-12, 0, -12, 0);
        Group(list);
    }

    // Column headings, each with its help mark when it has a note, as the app list has them.
    private void AddHeadings(Grid headings, IEnumerable<(string Title, int Column, string Help)> columns)
    {
        foreach (var (title, column, help) in columns)
        {
            var label = TextLine(title, 11, TextSecondary);
            FrameworkElement heading = label;
            if (help.Length > 0)
            {
                // The mark sits close to its own label so that, in the narrow color columns, each label and mark read as one pair.
                // The 24 DIP pointer box of SmallHelp keeps its negative margin, so the mark sits 2.5 DIP after the label.
                var info = SmallHelp(title, () => help); info.Margin = new Thickness(2.5 - 6, -6, -6, -6);
                heading = new HelpHeading(label, info);
            }
            heading.VerticalAlignment = VerticalAlignment.Center; heading.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(heading, column); headings.Children.Add(heading);
        }
    }

    // The color columns fit their headings and help marks: "Warning" and "Critical" need more room than "预警" and "告警".
    private static GridLength SwatchColumn => new(Loc.IsEnglish ? 60 : 50);

    private static Grid AppColumns()
    {
        var grid = new Grid { Background = Brushes.Transparent };
        var swatch = SwatchColumn;
        foreach (var width in new[] { new GridLength(40), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star),
            new GridLength(1.12, GridUnitType.Star), swatch, swatch, swatch, new GridLength(50), new GridLength(32) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        return grid;
    }

    private Border AppRow(AppEntry entry)
    {
        var row = AppColumns();
        row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var handle = new GripHandle { Width = 22, Background = Brushes.Transparent, Cursor = Cursors.SizeAll,
            HorizontalAlignment = HorizontalAlignment.Left, Child = Grip(TextTertiary) };
        row.Children.Add(handle);
        string name = entry.Name;
        var quota = SourceSelector(entry, true); Grid.SetColumn(quota, 1); row.Children.Add(quota);
        var usage = SourceSelector(entry, false); Grid.SetColumn(usage, 2); row.Children.Add(usage);
        var nameField = AppNameField(entry);
        Grid.SetColumn(nameField, 3); row.Children.Add(nameField);

        // An unchosen color shows the panel theme's default for this column, which is what the panel draws.
        var theme = ColorSwatch(name, Loc.T("主题", "theme"), entry.ThemeColor.Length > 0 ? entry.ThemeColor : PanelTheme.AppDefault(entry.QuotaSource), entry.ThemeColor.Length == 0, value => Change(s => s.Entry(entry.InstanceId)!.ThemeColor = value), owner: entry.InstanceId);
        var warning = ColorSwatch(name, Loc.T("预警", "warning"), entry.WarningColor.Length > 0 ? entry.WarningColor : PanelTheme.WarningDefault, entry.WarningColor.Length == 0, value => Change(s => s.Entry(entry.InstanceId)!.WarningColor = value), AppPresets.WarningPercent, entry.InstanceId);
        var critical = ColorSwatch(name, Loc.T("告警", "critical"), entry.CriticalColor.Length > 0 ? entry.CriticalColor : PanelTheme.CriticalDefault, entry.CriticalColor.Length == 0, value => Change(s => s.Entry(entry.InstanceId)!.CriticalColor = value), AppPresets.CriticalPercent, entry.InstanceId);
        Grid.SetColumn(theme, 4); row.Children.Add(theme); Grid.SetColumn(warning, 5); row.Children.Add(warning); Grid.SetColumn(critical, 6); row.Children.Add(critical);
        var toggle = new CheckBox { Style = (Style)FindResource("SettingsToggle"), IsChecked = entry.Enabled, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, Loc.T("显示 ", "Show ") + name); AutomationProperties.SetAutomationId(toggle, "enabled-" + entry.InstanceId.ToString("N"));
        toggle.Tag = ShowToggleTag; ToolTipService.SetShowOnDisabled(toggle, true);
        toggle.Checked += (_, _) => { Change(s => s.Entry(entry.InstanceId)!.Enabled = true); UpdateShowLimit(); };
        toggle.Unchecked += (_, _) => { Change(s => s.Entry(entry.InstanceId)!.Enabled = false); UpdateShowLimit(); };
        Grid.SetColumn(toggle, 7); row.Children.Add(toggle);
        AttachDrag(handle, name);
        // Removing drops the app's colors and sources with it, so it asks first (SW-09).
        var remove = ActionButton("", () =>
        {
            if (!Confirm(Loc.T("移除应用", "Remove app"), Loc.T($"将从看板移除“{name}”，连同它的颜色和来源设置。", $"This removes {name} from the panel, with its colors and sources."), Loc.T("移除", "Remove"))) return;
            Change(s => s.Apps.RemoveAll(app => app.InstanceId == entry.InstanceId));
            ShowPage(3);
        });
        var cross = Stroke("M 0,0 L 8,8 M 8,0 L 0,8", 8, 8, TextSecondary);
        cross.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        remove.Style = (Style)FindResource("SettingsGlyphButton"); remove.Content = cross; remove.Width = 24; remove.Height = 26; remove.Padding = new Thickness(0);
        AutomationProperties.SetName(remove, Loc.T("移除 ", "Remove ") + name); AutomationProperties.SetAutomationId(remove, "remove-" + entry.InstanceId.ToString("N"));
        // At the column's right edge, at least 12 DIP from the switch beside it.
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(remove, 8); row.Children.Add(remove);
        var border = new Border { Child = row, Height = AppRowHeight, BorderBrush = UI.Brush(Divider), BorderThickness = new Thickness(0), Tag = entry,
            RenderTransform = new TranslateTransform() };
        AttachSourceDetails(entry, row, border);
        return border;
    }

    private Button SourceSelector(AppEntry entry, bool quota)
    {
        string role = quota ? Loc.T("配额来源", "Quota source") : Loc.T("统计应用", "Usage app");
        ProviderId selected = quota ? entry.QuotaSource : entry.UsageSource;
        string Label(ProviderId id) => quota ? AppPresets.Name(id) : ProviderCatalog.UsageName(id);
        var select = new DropDownButton { Style = (Style)FindResource("SettingsButton"), Value = () => Label(selected) };
        var selectContent = new DockPanel();
        var chevron = Chevron(); chevron.Foreground = UI.Brush(TextSecondary); chevron.Margin = new Thickness(8, ChevronDrop.Top, 0, ChevronDrop.Bottom); DockPanel.SetDock(chevron, Dock.Right); selectContent.Children.Add(chevron);
        var selectLabel = TextLine(Label(selected), 12.5, TextPrimary, FontWeights.SemiBold);
        selectLabel.VerticalAlignment = VerticalAlignment.Center; selectLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        selectContent.Children.Add(selectLabel);
        select.Content = selectContent; select.Height = 34;
        select.ToolTip = quota ? ProviderCatalog.QuotaTip(selected) : ProviderCatalog.UsageTip(selected);
        select.HorizontalContentAlignment = HorizontalAlignment.Stretch; select.VerticalAlignment = VerticalAlignment.Center;
        select.Padding = new Thickness(9, 0, 9, 0); select.Margin = new Thickness(6, 0, 6, 0);
        select.Background = UI.Brush(_palette.Input); select.BorderBrush = UI.Brush(_palette.InputBorder);
        AutomationProperties.SetName(select, role + " " + entry.Name);
        AutomationProperties.SetAutomationId(select, (quota ? "quota-" : "usage-") + entry.InstanceId.ToString("N"));
        // The application menu is a styled popup list: dark surface, rounded corners, no icon column.
        var menu = new Popup { PlacementTarget = select, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        var items = new StackPanel { Width = 208 };
        Button? current = null;
        string? group = null;
        foreach (var id in quota ? ProviderCatalog.Sources : ProviderCatalog.UsageSources)
        {
            if (quota && ProviderCatalog.Group(id) != group)
            {
                group = ProviderCatalog.Group(id);
                if (items.Children.Count > 0) items.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider), Margin = new Thickness(4, 5, 4, 3) });
                var header = TextLine(group, 10.5, TextTertiary); header.Margin = new Thickness(10, 4, 10, 3); items.Children.Add(header);
            }
            var item = MenuEntry(Label(id), "", true, id == selected);
            // Each option carries the same note as the selector shows once it is chosen.
            item.ToolTip = quota ? ProviderCatalog.QuotaTip(id) : ProviderCatalog.UsageTip(id);
            if (id == selected) current = item;
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                PopupMotion.AfterClose(Choose);
            };
            void Choose()
            {
                if (id != selected) Change(s =>
                {
                    var current = s.Entry(entry.InstanceId)!;
                    if (quota)
                    {
                        current.QuotaSource = id; current.Site = ""; current.Script = id == ProviderId.Custom ? ProviderScripts.Example : ""; current.SecretRevision = Guid.Empty;
                    }
                    else current.UsageSource = id;
                });
                if (quota && !ProviderCatalog.IsBuiltIn(id)) { CollapseSourceRows(); _expandedSource = entry.InstanceId; }
                RefreshAppRow(entry.InstanceId);
            }
            AutomationProperties.SetName(item, Loc.T("选择 ", "Select ") + Label(id)); items.Children.Add(item);
        }
        // The list grows as sources are added; past the room on screen it scrolls instead of running off the screen.
        var list = new ScrollViewer
        {
            Style = (Style)FindResource("SettingsScrollViewer"), Content = items, Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var surface = new Border
        {
            Child = list, Background = UI.Brush(_palette.Popup), BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(5)
        };
        menu.Child = surface;
        // A long list opens at the selected source.
        menu.Opened += (_, _) =>
        {
            list.ScrollToTop();
            if (current is { } chosen) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => chosen.BringIntoView());
        };
        select.Tag = menu;
        select.Menu = menu;
        // At most MenuRows sources show at once, with the next one cut through the middle so the list reads as one that scrolls.
        // Headers and the divider above them count toward the height but not the rows. A shorter list shows whole.
        double MenuLimit()
        {
            items.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double height = surface.Padding.Top + surface.Padding.Bottom + surface.BorderThickness.Top + surface.BorderThickness.Bottom;
            int shown = 0;
            foreach (UIElement row in items.Children)
            {
                bool source = row is Button;
                if (source && shown == MenuRows) return height + row.DesiredSize.Height / 2;
                height += row.DesiredSize.Height;
                if (source) shown++;
            }
            return double.PositiveInfinity;
        }
        // The list sits right against its button, as the other drop-downs of the settings do.
        select.Click += (_, _) => { if (!menu.IsOpen) { FitMenuToScreen(select, menu, surface, MenuLimit()); menu.VerticalOffset = 0; } };
        TogglePopup(select, menu);
        return select;
    }
    private const int MenuRows = 10;

    // Opens the menu on the side of the button that has room for it inside the monitor's work area, so it never covers
    // the taskbar, and limits its height to that room. The room depends on the screen resolution, the Windows scale and
    // where the button is, so it is measured again each time the menu opens. limit caps the height even where there is more room.
    private static void FitMenuToScreen(FrameworkElement anchor, Popup menu, FrameworkElement surface, double limit = double.PositiveInfinity)
    {
        const double Gap = 4, Edge = 8;
        if (PresentationSource.FromVisual(anchor)?.CompositionTarget is not { } target) return;
        var fromDevice = target.TransformFromDevice;
        var top = anchor.PointToScreen(new Point(0, 0));
        var bottom = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
        var work = Native.WindowsHost.WorkAreaAt(new Point((top.X + bottom.X) / 2, (top.Y + bottom.Y) / 2));
        double below = fromDevice.Transform(new Vector(0, work.Bottom - bottom.Y)).Y - Gap - Edge;
        double above = fromDevice.Transform(new Vector(0, top.Y - work.Top)).Y - Gap - Edge;
        surface.MaxHeight = double.PositiveInfinity;
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        bool down = Math.Min(surface.DesiredSize.Height, limit) <= below || below >= above;
        menu.Placement = down ? PlacementMode.Bottom : PlacementMode.Top;
        menu.VerticalOffset = down ? Gap : -Gap;
        surface.MaxHeight = Math.Max(0, Math.Min(limit, down ? below : above));
    }

    // A popup that closes when the mouse goes down outside it also closes when its own button is pressed.
    // While the popup is open the button ignores the mouse, so that press only closes the popup and does not open it again.
    // WPF then passes that press on to the window: the popup is already shut (IsOpen is false) but Closed comes only after the
    // press has been routed, so the popup stays in _shownPopups until then and the app rows let the press only close it.
    private void TogglePopup(ButtonBase button, Popup popup)
    {
        popup.Opened += (_, _) => { button.IsHitTestVisible = false; _shownPopups.Add(popup); };
        PopupMotion.Attach(popup);
        popup.Closed += (_, _) =>
        {
            button.IsHitTestVisible = true; _shownPopups.Remove(popup);
            // A reading that came while the list was open left the rows as they were (QuotasChanged); they are brought up to date now.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, QuotasChanged);
        };
        button.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
    }
    private readonly HashSet<Popup> _shownPopups = [];

    // One line of the application menu: the name at the left, an optional note at the right.
    private Button MenuEntry(string name, string note, bool enabled, bool current)
    {
        var content = new DockPanel();
        if (note.Length > 0)
        {
            var hint = TextLine(note, 10.5, TextTertiary); hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(hint, Dock.Right); content.Children.Add(hint);
        }
        var label = TextLine(name, 12.5, enabled ? TextPrimary : TextSecondary, current ? FontWeights.SemiBold : FontWeights.Normal); label.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(label);
        var item = ActionButton("", () => { }); item.Content = content; item.IsEnabled = enabled;
        item.HorizontalContentAlignment = HorizontalAlignment.Stretch; item.Padding = new Thickness(9, 6, 9, 6); item.Margin = new Thickness(0, 1, 0, 1);
        if (current) item.Background = UI.Brush(Inset);
        return item;
    }

    private Button ColorSwatch(string app, string label, string color, bool isDefault, Action<string> apply, double? threshold = null, Guid? owner = null, Action? applied = null)
    {
        // The swatch shows the color as the panel draws it (see PanelTheme.Readable); the picker starts from the color as chosen.
        var button = new Button { Style = (Style)FindResource("SettingsColorButton"), Foreground = UI.Brush(PanelTheme.Readable(color)) };
        // The column heading explains each color; the swatch itself carries no hover note.
        AutomationProperties.SetName(button, Loc.T($"{app} {label}颜色", $"{app} {label} color")); AutomationProperties.SetHelpText(button, color + (isDefault ? Loc.T("（默认）", " (default)") : ""));
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        popup.Child = ColorPicker(color, isDefault, value => { popup.IsOpen = false; PopupMotion.AfterClose(() => { apply(value); if (applied is not null) applied(); else if (owner is { } id) RefreshAppRow(id); else ShowPage(3); }); });
        TogglePopup(button, popup);
        return button;
    }

    // A saturation/value square with a hue strip, preset swatches and a hexadecimal field. Dragging or typing changes one pending
    // color shown by every part without closing the popup; "确定" or Enter applies it. A swatch applies its color at once, "默认"
    // restores the preset value, and a click outside closes the popup without changes.
    // choose receives the new color, or "" for the default; the caller closes the popup.
    private Border ColorPicker(string current, bool isDefault, Action<string> choose, string? resetLabel = null)
    {
        const double width = 224;
        static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
        static string Hex(Color color) => ColorMath.ToHex(color.R, color.G, color.B);
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        string pending = AppPresets.NormalizeColor(current) is { Length: > 0 } valid ? valid : "#FFFFFF";
        var panel = new StackPanel { Width = width };
        var area = new HsvColorPicker(width, 132, _palette.Border) { Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(area, Loc.T("颜色选择器", "Color picker"));
        panel.Children.Add(area);
        var palette = new UniformGrid { Columns = 6 };
        panel.Children.Add(palette);

        var entry = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var ok = ActionButton(Loc.T("确定", "OK"), () => { }, primary: true); ok.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(ok, Dock.Right);
        var reset = ActionButton(resetLabel ?? Loc.T("默认", "Default"), () => choose("")); reset.IsEnabled = !isDefault; reset.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(reset, Dock.Right);
        entry.Children.Add(ok); entry.Children.Add(reset);
        var dot = new Ellipse { Width = 20, Height = 20, Stroke = UI.Brush(_palette.Border), StrokeThickness = 1, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dot, Dock.Left); entry.Children.Add(dot);
        var field = new StackPanel { Orientation = Orientation.Horizontal };
        var hash = TextLine("#", 12, TextSecondary); hash.VerticalAlignment = VerticalAlignment.Center; hash.Margin = new Thickness(0, 0, 4, 0); field.Children.Add(hash);
        var input = new TextBox { Style = (Style)FindResource("SettingsNumber"), Width = 60, TextAlignment = TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center, MaxLength = 6 };
        AutomationProperties.SetName(input, Loc.T("颜色值", "Color value")); field.Children.Add(input);
        var chip = new Border { Child = field, Height = 28, CornerRadius = new CornerRadius(6), Background = UI.Brush(Inset), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), Padding = new Thickness(9, 0, 9, 0) };
        FieldFocus(chip, input, () => Brushes.Transparent);
        entry.Children.Add(chip); panel.Children.Add(entry);

        var rings = new List<(Ellipse Ring, string Color)>();
        bool typing = false;
        void Select(string color, object? source)
        {
            pending = color;
            if (source != area) area.SetColor(Parse(color));
            if (source != input) { typing = true; input.Text = color.TrimStart('#'); typing = false; }
            dot.Fill = UI.Brush(color);
            foreach (var (ring, preset) in rings) { bool selected = Same(preset, color); ring.Stroke = UI.Brush(selected ? TextPrimary : _palette.Border); ring.StrokeThickness = selected ? 2 : 1; }
            ok.IsEnabled = !Same(pending, current);
        }
        void Commit() { if (!Same(pending, current)) choose(pending); }
        // The current panel theme's colors, so a pick from here suits the panel it is shown on.
        foreach (var color in PanelTheme.Current.Swatches.Select(swatch => swatch.ToString()))
        {
            var swatch = ActionButton("", () => choose(color)); swatch.Padding = new Thickness(0); swatch.Width = 31; swatch.Height = 31;
            var ring = new Ellipse { Width = 20, Height = 20, Fill = UI.Brush(color) }; swatch.Content = ring; rings.Add((ring, color));
            swatch.ToolTip = color; AutomationProperties.SetName(swatch, Loc.T("颜色 ", "Color ") + color); palette.Children.Add(swatch);
        }
        area.Changed += color => Select(Hex(color), area);
        input.TextChanged += (_, _) => { if (!typing && input.Text.Length == 6 && AppPresets.NormalizeColor(input.Text) is { Length: > 0 } value) Select(value, input); };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (AppPresets.NormalizeColor(input.Text) is { Length: > 0 } value) { Select(value, input); Commit(); }
            e.Handled = true;
        };
        ok.Click += (_, _) => Commit();
        Select(pending, null);
        var surface = new Border
        {
            Child = panel, Background = UI.Brush(_palette.Popup), BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(12)
        };
        return surface;
    }

    // Replaces one application row in place, so the rest of the page neither rebuilds nor flickers.
    private void RefreshAppRow(Guid id)
    {
        var old = _appRows?.Children.OfType<Border>().FirstOrDefault(b => b.Tag is AppEntry e && e.InstanceId == id);
        if (_appRows is null || old is null || S.Entry(id) is not { } entry) { ShowPage(3); return; }
        int index = _appRows.Children.IndexOf(old);
        _sourceRows.Remove(id);
        _appRows.Children.RemoveAt(index); _appRows.Children.Insert(index, AppRow(entry));
        UpdateDividers(); UpdateShowLimit();
    }

    private const string ShowToggleTag = "show";
    // With the most apps shown, the switches of the others are disabled until one is switched off.
    private void UpdateShowLimit()
    {
        if (_appRows is null) return;
        bool full = S.ShownAppsFull;
        foreach (var toggle in _appRows.Children.OfType<Border>().Select(border => border.Child).OfType<Grid>()
            .SelectMany(row => row.Children.OfType<CheckBox>()).Where(box => ShowToggleTag.Equals(box.Tag)))
        {
            bool blocked = full && toggle.IsChecked != true;
            toggle.IsEnabled = !blocked;
            toggle.ToolTip = blocked ? Loc.T($"最多同时显示 {AppPresets.MaximumShownApps} 个应用，请先关闭一个", $"Up to {AppPresets.MaximumShownApps} apps can be shown. Switch one off first.") : null;
        }
    }

    private void UpdateDividers() { if (_appRows is not null) UpdateDividers(_appRows); }
    private static void UpdateDividers(Panel rows)
    {
        for (int i = 0; i < rows.Children.Count; i++)
            if (rows.Children[i] is Border border) border.BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0);
    }

    private void About()
    {
        var description=TextLine(Loc.T("Windows 顶部悬浮面板。", "A floating panel at the top of the Windows screen."),13,TextSecondary);description.Margin=new Thickness(0,0,0,30);_content.Children.Add(description);
        SectionTitle(Loc.T("软件更新", "Software update"), description: Loc.T("检查并安装新版本。", "Check for and install new versions."));
        _aboutUpdateView = CreateUpdateView(true); Group(_aboutUpdateView.Root);
        SectionTitle(Loc.T("项目信息", "Project"), description: Loc.T("版本、许可证和联系方式。", "Version, license and where to reach us."));
        // What's new sits beside the version number, where someone looking for this version's changes looks first.
        var version = new StackPanel { Orientation = Orientation.Horizontal };
        var current = TextLine(_app.Updates.CurrentVersion,12,TextSecondary); current.VerticalAlignment = VerticalAlignment.Center; version.Children.Add(current);
        var notes = ActionButton(Loc.T("更新内容", "What's new"), ShowReleaseNotes); notes.Style = (Style)FindResource("SettingsLink"); notes.Margin = new Thickness(14, 0, 0, 0);
        version.Children.Add(notes);
        Group(Row(Loc.T("当前版本", "Current version"),version),
            Row(Loc.T("开源许可证", "License"),TextLine("Apache License 2.0",12,TextSecondary)),
            Row(Loc.T("项目主页", "Homepage"),Link("GitHub ↗","https://github.com/aristache-labs/deckisland")),
            Row(Loc.T("问题与建议", "Issues and suggestions"),Link(Loc.T("提交反馈 ↗", "Send feedback ↗"),"https://github.com/aristache-labs/deckisland/issues")),
            // The README's community section in the interface language, so the ways to reach us can change without a new version.
            Row(Loc.T("交流", "Community"),Link(Loc.T("查看交流方式 ↗", "Join the community ↗"),Loc.T(
                "https://github.com/aristache-labs/deckisland/blob/main/README.zh-CN.md#%E4%BA%A4%E6%B5%81",
                "https://github.com/aristache-labs/deckisland/blob/main/README.md#community"))));
    }

    // External links are plain text, so their right edge lines up with the values in the rows above.
    private Button Link(string text, string url)
    {
        var link = ActionButton(text, () => UI.OpenUrl(url)); link.Style = (Style)FindResource("SettingsLink"); link.ToolTip = Uri.UnescapeDataString(url); return link;
    }

    // Sections and groups are added to the page, or to the panel being collected while one is.
    private Panel? _target;
    // On a card page, what a section is given goes into its card, made when the first thing is put under its title (OpenCard);
    // the cards themselves go where sections go (Outer).
    private Panel Target => OpenCard() ?? Outer;
    private Panel Outer => _target ?? _content;
    private StackPanel? OpenCard()
    {
        if (!CardPage) return null;
        if (_cardTitle is { } heading) { _cardTitle = null; _cardBody = CardFrame(heading.Title, heading.Description, heading.Action, heading.Help, body: true); }
        return _cardBody;
    }
    // What is collected is built apart (a page row's sizes, the chosen style's options), outside any card open on the page.
    private StackPanel Collect(Action build)
    {
        var panel = new StackPanel(); var previous = _target; _target = panel;
        var (title, open) = (_cardTitle, _cardBody); _cardTitle = null; _cardBody = null;
        try { build(); } finally { _target = previous; _cardTitle = title; _cardBody = open; }
        return panel;
    }

    // The Collapsed view page is laid out in General's cards (Card): there a section title heads the card its next group
    // opens, and a group with no title of its own joins the card above it.
    private bool CardPage => _page is 1 or 2 or 3 or 5 or 6 or 7 or MusicGesturesPage or MonitorSettingsPage or SensorsPage or ShelfLayoutSettingsPage or ClipboardSettingsPage
        or TransferSettingsPage or ClipboardSyncSettingsPage or DevicesSettingsPage or WidgetsSettingsPage or NotificationsSettingsPage or CallsSettingsPage or ScratchpadSettingsPage or ConverterSettingsPage or TerminalSettingsPage or ThemePage or HotkeysPage or ProfilesPage;
    private (string Title, string Description, FrameworkElement? Action, Func<string>? Help)? _cardTitle;
    private StackPanel? _cardBody;

    private void SectionTitle(string title, FrameworkElement? action = null, Func<string>? help = null, string description = "")
    {
        if (CardPage) { _cardTitle = (title, description, action, help); _cardBody = null; return; }
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (action is not null) { DockPanel.SetDock(action, Dock.Right); action.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(action); }
        var label = TextLine(title, 11, TextSecondary); label.VerticalAlignment = VerticalAlignment.Center;
        if (help is null) row.Children.Add(label);
        else
        {
            // An info icon right after the title, as the settings rows carry theirs.
            row.Children.Add(new HelpHeading(label, HelpIcon(title, help, 13)) { HorizontalAlignment = HorizontalAlignment.Left });
        }
        Target.Children.Add(row);
    }

    private void Group(params FrameworkElement[] rows)
    {
        if (CardPage)
        {
            if (OpenCard() is { } body) CardRows(body, rows);
            else _cardBody = Card("", "", null, null, rows);
            return;
        }
        var panel = new StackPanel();
        foreach (var row in rows)
        {
            if (panel.Children.Count > 0) panel.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider) });
            panel.Children.Add(row);
        }
        Target.Children.Add(new Border { Child = panel, Background = Brushes.Transparent, BorderBrush = UI.Brush(Divider), BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0, 0, 0, 28) });
    }

    // One line per setting: the label at the left, the control at the right, an optional description underneath the label.
    // The three memory modes (DeckSettings.MemoryMode), from the least kept to the most.
    private UniformGrid MemoryModes()
    {
        var modes = new UniformGrid { Columns = 3, Width = 240 };
        foreach (var mode in new[] { MemoryMode.Saver, MemoryMode.Balanced, MemoryMode.Smooth })
        {
            string name = mode switch { MemoryMode.Saver => Loc.T("省资源", "Saver"), MemoryMode.Smooth => Loc.T("流畅", "Smooth"), _ => Loc.T("平衡", "Balanced") };
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = name, IsChecked = S.MemoryMode == mode, GroupName = "MemoryMode" };
            AutomationProperties.SetName(choice, name); choice.Checked += (_, _) => Change(s => s.MemoryMode = mode); modes.Children.Add(choice);
        }
        return modes;
    }

    private Grid Row(string title, FrameworkElement control, TextBlock? subtitle = null, Func<string>? help = null)
    {
        var row = new Grid { MinHeight = 44, Margin = new Thickness(0) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, subtitle is null ? 0 : 10, 12, subtitle is null ? 0 : 10) };
        labels.Children.Add(SettingLabel(title, help));
        if (subtitle is not null) labels.Children.Add(subtitle);
        row.Children.Add(labels);
        control.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(control, 1); row.Children.Add(control);
        return row;
    }

    // keepPreview marks the switch alone as a preview control, so a press on the rest of the row still ends the preview.
    private FrameworkElement Toggle(string title, bool value, Action<bool> changed, bool enabled = true, string? help = null, bool keepPreview = false)
    {
        var toggle = new CheckBox { Content = TextLine(title, 13, TextPrimary, FontWeights.SemiBold), Style = (Style)FindResource("SettingsToggle"), IsChecked = value, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        if (keepPreview) KeepPreview(toggle);
        AutomationProperties.SetName(toggle, title); toggle.Checked += (_, _) => changed(true); toggle.Unchecked += (_, _) => changed(false);
        if (help is not null)
        {
            toggle.Content = null; toggle.Padding = new Thickness(0);
            return Row(title, toggle, help: () => help);
        }
        return new Border { Child = toggle, Padding = new Thickness(0), Height = 44 };
    }

    private Grid SliderRow(string title, double value, double min, double max, double tick, string unit, Action<double> changed, string? help = null)
    {
        // A card page's slider is General's, its slider and number box together on the control column.
        if (CardPage) return InlineSlider(title, value, min, max, tick, unit, changed, help);
        var row=new Grid { Height=44 };
        row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(174)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        row.Children.Add(SettingLabel(title, help is null ? null : () => help));
        var controls=NumericControls(title,value,min,max,tick,unit,changed);controls.Slider.Margin=new Thickness(0,0,18,0);Grid.SetColumn(controls.Slider,1);row.Children.Add(controls.Slider);Grid.SetColumn(controls.Number,2);controls.Number.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(controls.Number);
        return row;
    }

    private FrameworkElement MouseWheelRow()
    {
        var title = Loc.T("鼠标滚轮", "Mouse wheel");
        var options = new UniformGrid { Columns = 3, Width = 324 };
        foreach (var (action, label) in new[] { (MouseWheelAction.None, Loc.T("不做任何事", "Nothing")),
            (MouseWheelAction.Pages, Loc.T("切换页面", "Switch pages")), (MouseWheelAction.Volume, Loc.T("调整音量", "Volume")) })
        {
            var option = new RadioButton { Content = label, Style = (Style)FindResource("SettingsSegment"), GroupName = "MouseWheel", IsChecked = S.MouseWheel == action };
            AutomationProperties.SetName(option, title + " · " + label); option.Checked += (_, _) => Change(s => s.MouseWheel = action); options.Children.Add(option);
        }
        return Row(title, SegmentBorder(options), help: () => Loc.T("仅在展开的面板区域内滚动时生效；歌词、明细等会滚动的区域不受影响。",
            "Works only when the wheel is rolled over the expanded panel; areas that scroll, such as the lyrics and the details, are not affected."));
    }

    // Hover or click opens the collapsed panel (DeckSettings.ClickToExpand); chosen tells the expand delay's row whether it applies.
    private FrameworkElement ExpandWayRow(Action<bool> chosen)
    {
        var title = Loc.T("展开方式", "Expand on");
        var options = new UniformGrid { Columns = 2, Width = 240 };
        foreach (var (click, label) in new[] { (false, Loc.T("悬停展开", "Hover")), (true, Loc.T("点击展开", "Click")) })
        {
            var option = new RadioButton { Content = label, Style = (Style)FindResource("SettingsSegment"), GroupName = "ExpandWay", IsChecked = S.ClickToExpand == click };
            AutomationProperties.SetName(option, title + " · " + label);
            option.Checked += (_, _) => { Change(s => s.ClickToExpand = click); chosen(click); };
            options.Children.Add(option);
        }
        return Row(title, SegmentBorder(options));
    }

    private FrameworkElement ExpandPlaceRow()
    {
        var title = Loc.T("展开位置", "Expand at");
        var options = new UniformGrid { Columns = 2, Width = 216 };
        foreach (var (atPointer, label) in new[] { (false, Loc.T("正中间", "Middle")), (true, Loc.T("跟随鼠标", "Pointer")) })
        {
            var option = new RadioButton { Content = label, Style = (Style)FindResource("SettingsSegment"), GroupName = "ExpandPlace", IsChecked = S.ExpandAtPointer == atPointer };
            AutomationProperties.SetName(option, title + " · " + label); option.Checked += (_, _) => Change(s => s.ExpandAtPointer = atPointer); options.Children.Add(option);
        }
        return Row(title, SegmentBorder(options), help: () => Loc.T("刘海或胶囊比展开面板更宽时，悬停展开的面板所在的位置。",
            "Where the panel opens when hovered, if the notch or capsule is wider than the expanded panel."));
    }

    private FrameworkElement BehaviorRow(string title, WindowBehavior selected, Action<WindowBehavior> changed, string? help = null)
    {
        // 270 leaves the long rule names room in the General card at the window's narrowest.
        var options = new UniformGrid { Columns = 3, Width = 270 };
        foreach (var behavior in Enum.GetValues<WindowBehavior>())
        {
            var option = new RadioButton { Content = BehaviorName(behavior), Style = (Style)FindResource("SettingsSegment"), GroupName = "Behavior" + title, IsChecked = selected == behavior };
            AutomationProperties.SetName(option, title + " · " + BehaviorName(behavior)); option.Checked += (_, _) => changed(behavior); options.Children.Add(option);
        }
        return help is null ? Row(title, SegmentBorder(options)) : Row(title, SegmentBorder(options), help: () => help);
    }
}
