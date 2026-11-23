using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    // Every widget as a card with its live preview; a click puts it in the next free position or takes it off the island.
    // The badge shows the position it holds, so the order follows the order the widgets were added in.
    private void WidgetGallery()
    {
        SectionTitle(Loc.T("岛上的小组件", "On the island"), description: Loc.T("在展开视图设置中选择要显示的标签页。", "Choose which tabs appear under Expanded view settings."));
        var count = TextLine("", 12, TextSecondary);
        count.TextWrapping = TextWrapping.Wrap;
        count.Margin = new Thickness(0, 14, 0, 10);

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-6, 0, -6, 14) };
        var cards = new List<(WidgetKind Kind, ToggleButton Card, Border Badge)>();
        void Refresh()
        {
            var slots = S.WidgetSlots;
            int used = slots.Count(slot => slot != WidgetKind.Empty);
            count.Text = Loc.T($"已放置 {used} / {slots.Length} 个。点击小组件即可添加或移除，数字表示它在岛上的位置。",
                $"{used} of {slots.Length} on the island. Click a widget to add or remove it; the number is the position it holds.");
            foreach (var (kind, card, badge) in cards)
            {
                int position = Array.IndexOf(slots, kind);
                card.IsChecked = position >= 0;
                badge.Child = Badge(badge, position);
                // A full island greys out the widgets that are not on it; they come back as soon as one is taken off.
                if (card.Content is UIElement content) content.Opacity = position < 0 && used >= slots.Length ? .35 : 1;
            }
        }
        foreach (var kind in Enum.GetValues<WidgetKind>().Where(kind => kind != WidgetKind.Empty))
        {
            var badge = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 1, 0, 0) };
            var card = new ToggleButton { Style = (Style)FindResource("SettingsWidgetCard"), Margin = new Thickness(6), Content = WidgetCardContent(kind, badge) };
            AutomationProperties.SetName(card, $"{WidgetLayout.Name(kind)} · {WidgetNote(kind)}");
            // The toggle's own state is set by Refresh from the saved slots, never by the click itself.
            card.Click += (_, _) =>
            {
                bool changed = ToggleWidget(kind);
                Refresh();
                if (!changed) FullNotice(badge);
            };
            cards.Add((kind, card, badge));
            grid.Children.Add(card);
        }
        Group(new StackPanel { Children = { count, grid } });
        Refresh();
    }

    private ToolTip? _fullNotice;
    private System.Windows.Threading.DispatcherTimer? _fullNoticeTimer;

    // Said beside the plus that was pressed, for a few seconds: the island holds four widgets at most.
    private void FullNotice(FrameworkElement badge)
    {
        if (_fullNotice is not null) _fullNotice.IsOpen = false;
        int most = S.WidgetSlots.Length;
        _fullNotice = new ToolTip
        {
            Content = Loc.T($"已选择 {most} 个小组件，最多只能放 {most} 个。请先移除一个再添加。",
                $"{most} widgets are already selected, and {most} is the most the island holds. Remove one first."),
            PlacementTarget = badge, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, VerticalOffset = -6, StaysOpen = true
        };
        AutomationProperties.SetLiveSetting(_fullNotice, AutomationLiveSetting.Assertive);
        _fullNotice.IsOpen = true;
        _fullNoticeTimer?.Stop();
        _fullNoticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        var shown = _fullNotice;
        _fullNoticeTimer.Tick += (_, _) => { _fullNoticeTimer?.Stop(); shown.IsOpen = false; };
        _fullNoticeTimer.Start();
    }

    // Adding fills the first free position; removing closes the gap so the numbers stay 1, 2, 3, 4.
    private bool ToggleWidget(WidgetKind kind)
    {
        var slots = S.WidgetSlots;
        var shown = slots.Where(slot => slot != WidgetKind.Empty).ToList();
        if (!shown.Remove(kind))
        {
            if (shown.Count >= slots.Length) return false;
            shown.Add(kind);
        }
        Change(s => s.WidgetSlots = [.. shown, .. Enumerable.Repeat(WidgetKind.Empty, slots.Length - shown.Count)]);
        return true;
    }

    private FrameworkElement WidgetCardContent(WidgetKind kind, Border badge)
    {
        var stage = new Border { Height = 140, ClipToBounds = true, Padding = new Thickness(6, 4, 6, 0), IsHitTestVisible = false };
        try
        {
            // Only the widget itself, on the card's own background.
            var (preview, _) = _app.PreviewDeck.WidgetPreview(kind);
            stage.Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = preview };
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or NullReferenceException)
        {
            // A widget that cannot draw here still gets its card, with its name in place of the picture.
            stage.Background = UI.Brush(Inset);
            stage.Child = new TextBlock { Text = WidgetLayout.Name(kind), Foreground = UI.Brush(TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        var name = TextLine(WidgetLayout.Name(kind), 13.5, TextPrimary, FontWeights.SemiBold);
        var note = TextLine(WidgetNote(kind), 11.5, TextSecondary);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 3, 0, 0);
        var footer = new Grid { Margin = new Thickness(4, 12, 2, 2) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new StackPanel { Children = { name, note } });
        Grid.SetColumn(badge, 1);
        footer.Children.Add(badge);
        return new StackPanel { Children = { stage, footer } };
    }

    // Filled with the position number while the widget is on the island, an outlined plus while it is not.
    private UIElement Badge(Border badge, int position)
    {
        if (position >= 0)
        {
            badge.Background = UI.Brush(TextPrimary); badge.BorderThickness = new Thickness(0);
            return new TextBlock { Text = (position + 1).ToString(Loc.Culture), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(_palette.Surface),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        badge.Background = Brushes.Transparent; badge.BorderBrush = UI.Brush(_palette.Border); badge.BorderThickness = new Thickness(1.2);
        return new Path { Data = Geometry.Parse("M 5,0 V 10 M 0,5 H 10"), Stroke = UI.Brush(TextSecondary), StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    private static string WidgetNote(WidgetKind kind) => kind switch
    {
        WidgetKind.Weather => Loc.T("当前天气，以及今天的最低和最高气温。", "Current conditions with today's low and high."),
        WidgetKind.Monitor => Loc.T("CPU、GPU、内存和网络，一眼看清。", "CPU, GPU, memory and network at a glance."),
        WidgetKind.Focus => Loc.T("在岛上直接开始的专注计时器。", "A focus timer you start right on the island."),
        WidgetKind.Devices => Loc.T("显示器，以及已连接设备的电量。", "Your display and the battery of connected devices."),
        WidgetKind.Quotas => Loc.T("你的 AI 套餐已用了多少。", "How much of your AI plans you have used."),
        WidgetKind.Today => Loc.T("今天的日期和所在的这一周。", "Today's date and the week around it."),
        WidgetKind.Clock => Loc.T("你的时间，外加最多三个城市。", "Your time, plus up to three cities."),
        WidgetKind.Countdown => Loc.T("距离你设定的日期还有几天。", "Days left until a date you set."),
        WidgetKind.Todo => Loc.T("一个可以勾选、排序的简短清单。", "A short list you tick off and put in order."),
        WidgetKind.Note => Loc.T("一张直接在岛上书写的便签。", "A sticky note you write on right there."),
        WidgetKind.SunMoon => Loc.T("所在地的日出和日落，以及今晚的月相。", "Sunrise and sunset where you are, and tonight's moon phase."),
        WidgetKind.Progress => Loc.T("今天、本周、本月和今年已经过去了多少。", "How much of today, this week, this month and this year has passed."),
        WidgetKind.ScreenTime => Loc.T("今天每个应用在前台多久，只在这台电脑上统计和保存。", "How long each app was in front today, counted and kept on this PC only."),
        _ => ""
    };
}
