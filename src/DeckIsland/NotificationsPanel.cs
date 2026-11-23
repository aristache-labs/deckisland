using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DeckIsland.Core;

namespace DeckIsland;

// The Notifications tab: the last 24 things the island said (Windows notifications and due reminders), newest first. Notices of one
// app are stacked on one row, the newest on top with "+N" under its age; the row opens the stack downward, and a notice opens its
// app. Each is dated by the time since or the time of day (DeckSettings.NotificationClockTimes). The count and Clear all stand in
// the middle under the list.
public partial class MainWindow
{
    private StackPanel? _noticeRows;
    private StackPanel? _noticeEmpty;
    private ScrollViewer? _noticeScroll;
    private TextBlock? _noticeCount;
    private Button? _noticeClear;
    private readonly HashSet<string> _noticeOpen = [];
    // The notices whose whole message is shown (the others show it on one line, cut with …).
    private readonly HashSet<string> _noticeExpanded = [];
    // Which notice of a closed stack is on top, by stack (the arrows under the pointer step it).
    private readonly Dictionary<string, int> _noticeTop = [];
    private readonly List<(FrameworkElement Row, int First, int Last)> _noticeRanges = [];
    private (int Theme, string Language, string TimePattern, bool Clock) _noticeAppearance;

    private void InitializeNotifications()
    {
        _app.NoticesChanged += NoticesChanged;
        Closed += (_, _) => _app.NoticesChanged -= NoticesChanged;
    }

    private void NotificationsPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Notifications);

    private void NoticesChanged()
    {
        if (Settings.NotificationsPage && CurrentPage == DeckPage.Notifications && _noticeRows is not null) FillNoticeRows();
    }

    private void RenderNotifications()
    {
        RefreshStatus();
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility = ShelfContent.Visibility =
            DevicesContent.Visibility = WeatherContent.Visibility = FocusContent.Visibility = RemindersContent.Visibility = CallsContent.Visibility = ScratchpadContent.Visibility = ConverterContent.Visibility = TerminalContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        double height = ContentCanvas.Height;
        if (!double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        NotificationsContent.Margin = new Thickness(_header.Side, 0, _header.Side, MonitorBottom(height));
        var appearance = (Look.Generation, Loc.Language, Settings.TimePattern, Settings.NotificationClockTimes);
        if (NotificationsContent.Children.Count > 0 && _noticeAppearance == appearance) { FillNoticeRows(); return; }
        _noticeAppearance = appearance;
        NotificationsContent.Children.Clear();
        var body = new DockPanel();
        // The count and Clear all, side by side in the middle.
        _noticeClear = new Button { Content = Loc.T("全部清除", "Clear all"), Style = (Style)FindResource("PanelOutlineButton"), Margin = new Thickness(14, 0, 0, 0) };
        _noticeClear.Click += (_, _) => { _noticeOpen.Clear(); _noticeTop.Clear(); _noticeExpanded.Clear(); _app.ClearNotices(); };
        _noticeCount = WeatherText("", 12);
        _noticeCount.Foreground = UI.Brush(Look.Secondary);
        _noticeCount.VerticalAlignment = VerticalAlignment.Center;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
            Children = { _noticeCount, _noticeClear } };
        DockPanel.SetDock(footer, System.Windows.Controls.Dock.Bottom);
        body.Children.Add(footer);
        _noticeRows = new StackPanel();
        _noticeEmpty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), IsHitTestVisible = false };
        var emptyTitle = WeatherText(Loc.T("还没有通知。", "No notifications yet."), 15);
        emptyTitle.FontWeight = FontWeights.SemiBold;
        var emptyHint = WeatherText(Loc.T("灵动岛显示过的通知会保留在这里。", "What the island shows is kept here."), 13);
        emptyHint.Foreground = UI.Brush(Look.Secondary);
        emptyHint.Margin = new Thickness(0, 4, 0, 0);
        foreach (var line in new[] { emptyTitle, emptyHint })
        {
            line.TextWrapping = TextWrapping.Wrap;
            line.TextAlignment = TextAlignment.Center;
            _noticeEmpty.Children.Add(line);
        }
        _noticeScroll = new ScrollViewer { Content = _noticeRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _noticeScroll.ScrollChanged += (_, _) => UpdateNoticeCount();
        body.Children.Add(new Grid { Children = { _noticeScroll, _noticeEmpty } });
        NotificationsContent.Children.Add(body);
        FillNoticeRows();
    }

    private string NoticeAge(DateTimeOffset at)
    {
        if (Settings.NotificationClockTimes) return at.ToLocalTime().ToString(Settings.TimePattern, Loc.Culture);
        var ago = DateTimeOffset.Now - at;
        return ago.TotalMinutes < 1 ? Loc.T("刚刚", "now") : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes}m"
            : ago.TotalDays < 1 ? $"{(int)ago.TotalHours}h" : $"{(int)ago.TotalDays}d";
    }

    // One app's notices share a stack: its app id, or its name for what has none (the reminders).
    private static string NoticeGroup(NoticeItem item) => item.AppId is { Length: > 0 } id ? id : item.App;

    private void FillNoticeRows()
    {
        if (_noticeRows is not { } rows || _noticeEmpty is not { } empty) return;
        rows.Children.Clear();
        _noticeRanges.Clear();
        int index = 1;
        var items = _app.Notices.Items;
        var groups = items.GroupBy(NoticeGroup).ToList();
        _noticeOpen.RemoveWhere(key => groups.All(group => group.Key != key || group.Count() < 2));
        foreach (var group in groups)
        {
            var notices = group.ToList();
            if (notices.Count == 1) { AddRow(NoticeRow(notices[0])); continue; }
            bool open = _noticeOpen.Contains(group.Key);
            string app = notices[0].App;
            if (open)
            {
                var head = new DockPanel { Margin = new Thickness(6, 2, 6, 4) };
                var less = new Button { Content = Loc.T("收起 ˄", "Show less ˄"), Style = (Style)FindResource("PanelTextButton") };
                less.Click += (_, _) => { _noticeOpen.Remove(group.Key); FillNoticeRows(); };
                AutomationProperties.SetName(less, Loc.T($"收起 {app}", $"Show less of {app}"));
                DockPanel.SetDock(less, System.Windows.Controls.Dock.Right);
                var title = WeatherText($"{app} · {notices.Count}", 12.5);
                title.Foreground = UI.Brush(Look.Secondary);
                title.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(less);
                head.Children.Add(title);
                rows.Children.Add(head);
                foreach (var notice in notices) AddRow(NoticeRow(notice));
                continue;
            }
            // Closed: the newest on top of two edges of the ones under it; pressing it opens the stack.
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            stack.Children.Add(NoticeStack(group.Key, notices));
            foreach (var (inset, opacity) in new[] { (10d, .6), (20d, .35) })
                stack.Children.Add(new Border
                {
                    Height = 5, Margin = new Thickness(inset, -1, inset, 0), Opacity = opacity, Background = UI.Brush(Look.Hex("fill")),
                    CornerRadius = new CornerRadius(0, 0, Corner(6), Corner(6)), IsHitTestVisible = false
                });
            AddRow(stack, notices.Count);
        }
        bool none = rows.Children.Count == 0;
        empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        if (_noticeClear is not null) _noticeClear.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        UpdateNoticeCount();
        rows.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, UpdateNoticeCount);

        void AddRow(FrameworkElement row, int count = 1)
        {
            rows.Children.Add(row);
            _noticeRanges.Add((row, index, index + count - 1));
            index += count;
        }
    }

    private void UpdateNoticeCount()
    {
        if (_noticeCount is not { } count || _noticeScroll is not { } scroll) return;
        int total = _app.Notices.Items.Count;
        count.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (total == 0) return;
        int first = 0, last = 0;
        if (_noticeOpen.Count > 0 && scroll.ScrollableHeight > 0)
        {
            foreach (var (row, from, to) in _noticeRanges)
            {
                if (row.ActualHeight <= 0 || !row.IsDescendantOf(scroll)) continue;
                double middle = row.TranslatePoint(new Point(0, row.ActualHeight / 2), scroll).Y;
                if (middle < 0 || middle >= scroll.ViewportHeight) continue;
                if (first == 0) first = from;
                last = to;
            }
        }
        count.Text = first > 0 ? Loc.T($"第 {first}-{last} 条，共 {total} 条，可滚动", $"{first}-{last} of {total}, scroll for more")
            : total == 1 ? Loc.T("1 条通知", "1 notice") : Loc.T($"{total} 条通知", $"{total} notices");
    }

    private FrameworkElement NoticeStack(string key, IReadOnlyList<NoticeItem> items)
    {
        int shown = Math.Clamp(_noticeTop.GetValueOrDefault(key), 0, items.Count - 1);
        var mark = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Left };
        var body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, ClipToBounds = true };
        var age = NoticeAgeText();
        var badge = WeatherText($"+{items.Count - 1}", 12);
        badge.FontWeight = FontWeights.SemiBold;
        badge.Foreground = UI.Brush(Look.Readable(Look.Hex("accent"), 4.5));
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.IsHitTestVisible = false;
        var position = WeatherText("", 12);
        position.Foreground = UI.Brush(Look.Secondary);
        position.IsHitTestVisible = false;
        var expand = NoticeAction("⌄", 14, Loc.T("展开此应用的通知", "Expand notification stack"));
        var previous = NoticeAction("‹", 15, Loc.T("上一条", "Previous notice"));
        var next = NoticeAction("›", 15, Loc.T("下一条", "Next notice"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { expand, previous, position, next } };

        var card = NoticeCard(mark, body, age, actions, badge, OpenStack, out var surface);
        expand.Click += (_, _) => OpenStack();
        previous.Click += (_, _) => Step(-1);
        next.Click += (_, _) => Step(1);
        Show(0);
        return card;

        void OpenStack()
        {
            _noticeOpen.Add(key);
            FillNoticeRows();
        }

        void Step(int direction)
        {
            shown = (shown + direction + items.Count) % items.Count;
            _noticeTop[key] = shown;
            Show(direction);
        }

        void Show(int direction)
        {
            var item = items[shown];
            mark.Content = NoticeAppMark(item);
            string detail = NoticeDetail(item);
            var content = NoticeBody(item, detail, wrap: false, out var line);
            body.Content = content;
            WatchNoticeOverflow(line, detail, expand);
            age.Text = NoticeAge(item.At);
            position.Text = $"{shown + 1}/{items.Count}";
            AutomationProperties.SetName(surface, $"{NoticeLabel(item)} · {position.Text}");
            if (!Animates || direction == 0) return;

            // Only the incoming content moves. The click targets and the count keep their positions.
            var travel = new TranslateTransform();
            content.RenderTransform = travel;
            var settle = new Duration(TimeSpan.FromMilliseconds(160));
            travel.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction * 24, 0, settle)
            { EasingFunction = Soft.Out, FillBehavior = FillBehavior.Stop });
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, settle)
            { EasingFunction = Soft.Out, FillBehavior = FillBehavior.Stop });
        }
    }

    private FrameworkElement NoticeRow(NoticeItem item)
    {
        string key = $"{NoticeGroup(item)}|{item.Title}|{item.At.Ticks}";
        bool canOpenApp = CanOpenNoticeApp(item);
        bool expanded = _noticeExpanded.Contains(key);
        string detail = NoticeDetail(item);
        var body = NoticeBody(item, detail, expanded, out var line);
        var age = NoticeAgeText();
        age.Text = NoticeAge(item.At);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (!expanded && detail.Length > 0)
        {
            var expand = NoticeAction("⌄", 14, Loc.T("展开通知正文", "Expand notification description"));
            expand.Click += (_, _) => Toggle();
            WatchNoticeOverflow(line, detail, expand);
            actions.Children.Add(expand);
        }

        var dismiss = NoticeAction("×", 16, Loc.T("清除通知", "Dismiss the notice"));
        dismiss.Click += (_, _) =>
        {
            _noticeExpanded.Remove(key);
            _app.DismissNotice(item);
        };
        actions.Children.Add(dismiss);

        FrameworkElement? footer = null;
        if (expanded)
        {
            var collapse = NoticeAction("⌃", 14, Loc.T("收起通知正文", "Collapse notification description"));
            collapse.Click += (_, _) => Toggle();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { collapse } };
            if (canOpenApp)
            {
                var open = WidgetSurface(Loc.T("打开", "Open"), Loc.T("打开 ", "Open ") + item.App, () => OpenNoticeApp(item));
                open.Background = UI.Brush(Look.Hex("fillSelected"));
                open.Padding = new Thickness(12, 2, 12, 2);
                open.Height = 22;
                open.Margin = new Thickness(4, 0, 0, 0);
                buttons.Children.Add(open);
            }
            var app = WeatherText(item.App, 11.5);
            app.Foreground = UI.Brush(Look.Secondary);
            app.IsHitTestVisible = false;
            var bar = new DockPanel { Margin = new Thickness(48, 0, 8, 6), Children = { buttons, app } };
            DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
            footer = bar;
        }

        var card = NoticeCard(NoticeAppMark(item), body, age, actions, null, Activate, out var surface, footer);
        AutomationProperties.SetName(surface, NoticeLabel(item));
        card.Margin = new Thickness(0, 0, 0, 4);
        return card;

        void Toggle()
        {
            if (!_noticeExpanded.Remove(key)) _noticeExpanded.Add(key);
            FillNoticeRows();
        }

        void Activate()
        {
            if (expanded || NoticeOverflows(line, detail)) Toggle();
            else OpenNoticeApp(item);
        }
    }

    private static void WatchNoticeOverflow(TextBlock line, string detail, Button expand)
    {
        // A reserved slot keeps the text width stable when the arrow appears.
        expand.Visibility = Visibility.Hidden;
        void Update()
        {
            if (line.IsLoaded)
                expand.Visibility = NoticeOverflows(line, detail) ? Visibility.Visible : Visibility.Hidden;
        }
        line.Loaded += (_, _) => Update();
        line.SizeChanged += (_, _) => Update();
    }

    private static bool NoticeOverflows(TextBlock line, string detail)
    {
        if (detail != line.Text) return true;
        var face = new Typeface(line.FontFamily, line.FontStyle, line.FontWeight, line.FontStretch);
        var measured = new FormattedText(detail, System.Globalization.CultureInfo.CurrentUICulture, line.FlowDirection,
            face, line.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(line).PixelsPerDip);
        return measured.Width > line.ActualWidth + .5;
    }

    // The card button covers the row. Its age, content and secondary buttons sit above it as siblings.
    private Grid NoticeCard(FrameworkElement mark, FrameworkElement body, TextBlock age, StackPanel actions, TextBlock? badge, Action activate, out Button surface,
        FrameworkElement? footer = null)
    {
        var card = new Grid();
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.ColumnDefinitions.Add(new ColumnDefinition());
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var background = new Border { Background = UI.Brush(Look.Hex("fill")), CornerRadius = new CornerRadius(Corner(10)), IsHitTestVisible = false };
        Grid.SetColumnSpan(background, 3);
        card.Children.Add(background);
        surface = WidgetSurface("", "", activate);
        surface.Background = Brushes.Transparent;
        Grid.SetColumnSpan(surface, 3);
        card.Children.Add(surface);

        mark.Margin = new Thickness(8, 6, 10, 6);
        mark.VerticalAlignment = footer is null ? VerticalAlignment.Center : VerticalAlignment.Top;
        card.Children.Add(mark);
        body.IsHitTestVisible = false;
        body.Margin = new Thickness(0, 6, 0, 6);
        Grid.SetColumn(body, 1);
        card.Children.Add(body);

        var tools = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        if (badge is not null) tools.Children.Add(badge);
        tools.Children.Add(actions);
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 5, 6, 3), Children = { age, tools } };
        Grid.SetColumn(side, 2);
        card.Children.Add(side);
        if (footer is not null)
        {
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRowSpan(background, 2);
            Grid.SetRowSpan(surface, 2);
            Grid.SetRow(footer, 1);
            Grid.SetColumnSpan(footer, 3);
            card.Children.Add(footer);
        }

        void Update()
        {
            bool active = card.IsMouseOver || card.IsKeyboardFocusWithin;
            actions.Opacity = active ? 1 : 0;
            if (badge is not null) badge.Opacity = active ? 0 : 1;
        }

        card.MouseEnter += (_, _) => Update();
        card.MouseLeave += (_, _) => Update();
        card.IsKeyboardFocusWithinChanged += (_, _) => Update();
        Update();
        return card;
    }

    private TextBlock NoticeAgeText()
    {
        var age = WeatherText("", 12);
        age.Foreground = UI.Brush(Look.Secondary);
        age.HorizontalAlignment = HorizontalAlignment.Right;
        age.IsHitTestVisible = false;
        return age;
    }

    private string NoticeLabel(NoticeItem item) => $"{item.App}: {item.Title} {item.Body} · {NoticeAge(item.At)}";

    private static string NoticeDetail(NoticeItem item) => item.Body.Length > 0 ? item.Body : item.App;

    private FrameworkElement NoticeBody(NoticeItem item, string detail, bool wrap, out TextBlock line)
    {
        var title = WeatherText(item.Title, 13);
        title.FontWeight = FontWeights.SemiBold;
        line = WeatherText(wrap ? detail : detail.ReplaceLineEndings(" "), 11.5);
        line.Foreground = UI.Brush(Look.Secondary);
        line.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        line.TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, line } };
    }

    private Button NoticeAction(string glyph, double size, string name)
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter), "Glyph");
        presenter.SetValue(UIElement.OpacityProperty, .65);
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var box = new FrameworkElementFactory(typeof(Border));
        box.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        box.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = box };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 1d, "Glyph"));
        template.Triggers.Add(hover);
        var button = new Button
        {
            Content = glyph, Template = template, FontSize = size, Foreground = UI.Brush(Look.Primary),
            Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, MinWidth = 22, Height = 18,
            FocusVisualStyle = (Style)FindResource("PanelKeyboardFocus")
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    // Opens the app that sent a notice: Windows starts it, or brings it forward, from its id in the Apps folder.
    private static bool CanOpenNoticeApp(NoticeItem item) => item.AppId is { Length: > 0 } id && !id.Contains('"');

    private static void OpenNoticeApp(NoticeItem item)
    {
        if (!CanOpenNoticeApp(item)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"shell:AppsFolder\\{item.AppId}\"") { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
    }

    private FrameworkElement NoticeAppMark(NoticeItem item)
    {
        var mark = NoticeMark(item);
        mark.IsHitTestVisible = false;
        if (!CanOpenNoticeApp(item)) return mark;

        var link = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M7,3 H3 V13 H13 V9 M8,2 H14 V8 M14,2 L7,9"),
            Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.4, Stretch = Stretch.Uniform,
            Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        var overlay = new Border
        {
            Background = UI.Brush(Look.Hex("fill")), CornerRadius = new CornerRadius(Corner(7)),
            Child = link, Visibility = Visibility.Hidden
        };
        var button = WidgetSurface(new Grid { IsHitTestVisible = false, Children = { mark, overlay } },
            Loc.T("从图标打开 ", "Open from icon: ") + item.App, () => OpenNoticeApp(item));
        button.Width = mark.Width;
        button.Height = mark.Height;
        button.MouseEnter += (_, _) => UpdateOverlay();
        button.MouseLeave += (_, _) => UpdateOverlay();
        button.IsKeyboardFocusedChanged += (_, _) => UpdateOverlay();

        void UpdateOverlay() => overlay.Visibility = button.IsMouseOver || button.IsKeyboardFocused ? Visibility.Visible : Visibility.Hidden;
        return button;
    }

    // The app's picture, or a bell on a rounded square when Windows gave none.
    private FrameworkElement NoticeMark(NoticeItem item)
    {
        const double size = 30;
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(Corner(7)), Background = UI.Brush(Look.Hex("fillSelected")),
            VerticalAlignment = VerticalAlignment.Center };
        if (item.Picture is { Length: > 0 } bytes && !Look.HighContrast)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new System.IO.MemoryStream(bytes);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                if (TrimNoticeLogo(image) is { } logo)
                {
                    logo.Freeze();
                    tile.Padding = new Thickness(4);
                    tile.Child = new Image { Source = logo, Stretch = Stretch.Uniform };
                    return tile;
                }
            }
            catch (Exception error) when (error is NotSupportedException or System.IO.IOException or InvalidOperationException) { }
        }
        tile.Child = new System.Windows.Shapes.Path
        {
            Data = HudWindow.IconGeometry(HudIcon.Notice), Stroke = UI.Brush(Look.Hex("accent")), StrokeThickness = 1.4, Stretch = Stretch.Uniform,
            Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        return tile;
    }

    private static BitmapSource? TrimNoticeLogo(BitmapSource image)
    {
        int width = image.PixelWidth, height = image.PixelHeight;
        int stride = checked(width * 4);
        var pixels = new byte[checked(height * stride)];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);

        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * stride + x * 4 + 3] == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0) return null;
        if (left == 0 && top == 0 && right == width - 1 && bottom == height - 1) return image;
        return new CroppedBitmap(image, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
    }
}
