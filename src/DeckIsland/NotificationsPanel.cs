using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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
    private TextBlock? _noticeCount;
    private Button? _noticeClear;
    private readonly HashSet<string> _noticeOpen = [];
    // Which notice of a closed stack is on top, by stack (the arrows under the pointer step it).
    private readonly Dictionary<string, int> _noticeTop = [];
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
            DevicesContent.Visibility = WeatherContent.Visibility = FocusContent.Visibility = RemindersContent.Visibility = Visibility.Collapsed;
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
        _noticeClear.Click += (_, _) => { _noticeOpen.Clear(); _noticeTop.Clear(); _app.ClearNotices(); };
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
        body.Children.Add(new Grid
        {
            Children = { new ScrollViewer { Content = _noticeRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, _noticeEmpty }
        });
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
        var items = _app.Notices.Items;
        var groups = items.GroupBy(NoticeGroup).ToList();
        _noticeOpen.RemoveWhere(key => groups.All(group => group.Key != key || group.Count() < 2));
        foreach (var group in groups)
        {
            var notices = group.ToList();
            if (notices.Count == 1) { rows.Children.Add(NoticeRow(notices[0], 0, () => OpenNoticeApp(notices[0]))); continue; }
            bool open = _noticeOpen.Contains(group.Key);
            string app = notices[0].App;
            if (open)
            {
                var head = new DockPanel { Margin = new Thickness(6, 2, 6, 4) };
                var less = new Button { Content = Loc.T("收起 ˄", "Show less ˄"), Style = (Style)FindResource("PanelOutlineButton") };
                less.Click += (_, _) => { _noticeOpen.Remove(group.Key); FillNoticeRows(); };
                AutomationProperties.SetName(less, Loc.T($"收起 {app}", $"Show less of {app}"));
                DockPanel.SetDock(less, System.Windows.Controls.Dock.Right);
                var title = WeatherText($"{app} · {notices.Count}", 12.5);
                title.Foreground = UI.Brush(Look.Secondary);
                title.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(less);
                head.Children.Add(title);
                rows.Children.Add(head);
                foreach (var notice in notices) rows.Children.Add(NoticeRow(notice, 0, () => OpenNoticeApp(notice)));
                continue;
            }
            // Closed: the newest on top of two edges of the ones under it; pressing it opens the stack.
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            int shown = Math.Clamp(_noticeTop.GetValueOrDefault(group.Key), 0, notices.Count - 1);
            var top = NoticeRow(notices[shown], notices.Count - 1, () => { _noticeOpen.Add(group.Key); FillNoticeRows(); }, margin: false);
            stack.Children.Add(StackStepper(top, group.Key, shown, notices.Count));
            foreach (var (inset, opacity) in new[] { (10d, .6), (20d, .35) })
                stack.Children.Add(new Border
                {
                    Height = 5, Margin = new Thickness(inset, -1, inset, 0), Opacity = opacity, Background = UI.Brush(Look.Hex("fill")),
                    CornerRadius = new CornerRadius(0, 0, Corner(6), Corner(6)), IsHitTestVisible = false
                });
            rows.Children.Add(stack);
        }
        bool none = rows.Children.Count == 0;
        empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        if (_noticeClear is not null) _noticeClear.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        if (_noticeCount is not null)
        {
            int total = items.Count;
            _noticeCount.Text = none ? "" : _noticeOpen.Count > 0 ? Loc.T($"共 {total} 条，可滚动", $"1-{total} of {total}, scroll for more")
                : total == 1 ? Loc.T("1 条通知", "1 notice") : Loc.T($"{total} 条通知", $"{total} notices");
            _noticeCount.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // The top row of a closed stack with "‹ 1 / 3 ›" over its right side while the pointer (or the keyboard) is on it, to change
    // which notice of the stack is on top.
    private FrameworkElement StackStepper(FrameworkElement row, string key, int shown, int count)
    {
        Button Step(string glyph, int by, string name)
        {
            var button = new Button { Content = glyph, Style = (Style)FindResource("PanelOutlineButton"), MinWidth = 24, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(2, 0, 0, 0) };
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => { _noticeTop[key] = (shown + by + count) % count; FillNoticeRows(); };
            return button;
        }
        var position = WeatherText($"{shown + 1} / {count}", 12);
        position.Foreground = UI.Brush(Look.Secondary);
        position.VerticalAlignment = VerticalAlignment.Center;
        position.Margin = new Thickness(4, 0, 4, 0);
        var strip = new Border
        {
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { Step("‹", -1, Loc.T("上一条", "Previous notice")), position, Step("›", 1, Loc.T("下一条", "Next notice")) } },
            Background = UI.Brush(Look.Hex("fillSelected")), CornerRadius = new CornerRadius(Corner(8)), Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 64, 0), Opacity = 0
        };
        var host = new Grid { Children = { row, strip } };
        void Update() => strip.Opacity = host.IsMouseOver || host.IsKeyboardFocusWithin ? 1 : 0;
        host.MouseEnter += (_, _) => Update();
        host.MouseLeave += (_, _) => Update();
        host.IsKeyboardFocusWithinChanged += (_, _) => Update();
        return host;
    }

    // One notice as a pressable row: the picture, the title over the message, its age (and how many more are stacked under it).
    private FrameworkElement NoticeRow(NoticeItem item, int more, Action press, bool margin = true)
    {
        var row = new DockPanel { LastChildFill = true };
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
        var age = WeatherText(NoticeAge(item.At), 12);
        age.Foreground = UI.Brush(Look.Secondary);
        age.HorizontalAlignment = HorizontalAlignment.Right;
        side.Children.Add(age);
        if (more > 0)
        {
            var badge = WeatherText($"+{more}", 12);
            badge.FontWeight = FontWeights.SemiBold;
            badge.Foreground = UI.Brush(Look.Readable(Look.Hex("accent"), 4.5));
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            side.Children.Add(badge);
        }
        DockPanel.SetDock(side, System.Windows.Controls.Dock.Right);
        row.Children.Add(side);
        row.Children.Add(NoticeMark(item));
        var title = WeatherText(item.Title, 13);
        title.FontWeight = FontWeights.SemiBold;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        string detail = item.Body.Length > 0 ? item.Body : item.App;
        if (detail.Length > 0)
        {
            var body = WeatherText(detail, 11.5);
            body.Foreground = UI.Brush(Look.Secondary);
            body.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(body);
        }
        row.Children.Add(text);
        string label = $"{item.App}: {item.Title} {item.Body} · {NoticeAge(item.At)}{(more > 0 ? $" · +{more}" : "")}".Trim();
        var frame = new Border { Child = row, Padding = new Thickness(8, 6, 6, 6), CornerRadius = new CornerRadius(Corner(10)), Background = UI.Brush(Look.Hex("fill")) };
        var button = WidgetSurface(frame, label, press);
        button.ToolTip = more > 0 ? Loc.T("展开此应用的通知", "Open the stack") : item.AppId is { Length: > 0 } ? Loc.T("打开应用", "Open the app") : label;
        if (margin) button.Margin = new Thickness(0, 0, 0, 4);
        return button;
    }

    // Opens the app that sent a notice: Windows starts it, or brings it forward, from its id in the Apps folder.
    private static void OpenNoticeApp(NoticeItem item)
    {
        if (item.AppId is not { Length: > 0 } id || id.Contains('"')) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"shell:AppsFolder\\{id}\"") { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
    }

    // The app's picture, or a bell on a rounded square when Windows gave none.
    private FrameworkElement NoticeMark(NoticeItem item)
    {
        const double size = 30;
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(Corner(7)), Background = UI.Brush(Look.Hex("fillSelected")),
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        if (item.Picture is { Length: > 0 } bytes && !Look.HighContrast)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new System.IO.MemoryStream(bytes);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                tile.Padding = new Thickness(4);
                tile.Child = new Image { Source = image, Stretch = Stretch.Uniform };
                return tile;
            }
            catch (Exception error) when (error is NotSupportedException or System.IO.IOException or InvalidOperationException) { }
        }
        tile.Child = new Path
        {
            Data = HudWindow.IconGeometry(HudIcon.Notice), Stroke = UI.Brush(Look.Hex("accent")), StrokeThickness = 1.4, Stretch = Stretch.Uniform,
            Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        return tile;
    }
}
