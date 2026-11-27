using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

// lumabar's widget picker: categories down the side, a search box, and a live tile for each widget; a tick marks the ones on the
// dock. A click (or Enter) adds a widget or takes it off; the arrow keys move between tiles. While it is open the dock's widgets carry
// remove badges (App.DockPickerOpen). The AI widget is offered once per AI tool that has limits to show.
internal sealed class DockWidgetPicker : Window
{
    private const double PreviewTile = 46;
    private readonly App _app;
    private readonly SettingsPalette _palette;
    private readonly StackPanel _categories = new() { Margin = new Thickness(12, 16, 12, 12) };
    private readonly WrapPanel _tiles = new() { Margin = new Thickness(8, 4, 8, 16) };
    private readonly TextBox _search = new() { Height = 30, Margin = new Thickness(16, 16, 16, 8), Padding = new Thickness(8, 5, 8, 5), FontSize = 13 };
    private DockWidgetCategory? _category;

    public DockWidgetPicker(App app)
    {
        _app = app;
        _palette = new SettingsPalette(SettingsPalette.IsDark(app.Settings.Theme));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DeckIsland;component/SettingsResources.xaml", UriKind.Relative) });
        _palette.Apply(Resources);
        Title = Loc.T("小组件", "Widgets"); Width = 760; Height = 560; MinWidth = 560; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UI.PanelFont; Background = UI.Brush(_palette.Surface); Foreground = UI.Brush(_palette.Primary);
        Native.SettingsChrome.Apply(this);
        SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(this, _palette.Dark);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _search.Background = UI.Brush(_palette.Input); _search.Foreground = UI.Brush(_palette.Primary); _search.BorderBrush = UI.Brush(_palette.InputBorder);
        _search.CaretBrush = UI.Brush(_palette.Primary);
        AutomationProperties.SetName(_search, Loc.T("搜索小组件", "Search widgets"));
        _search.TextChanged += (_, _) => FillTiles();
        KeyboardNavigation.SetDirectionalNavigation(_tiles, KeyboardNavigationMode.Cycle);

        var sidebar = new Border { Width = 170, Background = UI.Brush(_palette.Sidebar), Child = _categories };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tiles };
        var main = new DockPanel();
        DockPanel.SetDock(_search, System.Windows.Controls.Dock.Top); main.Children.Add(_search); main.Children.Add(scroll);
        var root = new DockPanel();
        DockPanel.SetDock(sidebar, System.Windows.Controls.Dock.Left); root.Children.Add(sidebar); root.Children.Add(main);
        // The window draws its own title bar (SettingsChrome), so it carries its own close button; the search box takes clicks
        // even inside the title bar's band.
        var close = new Button { Style = (Style)FindResource("SettingsCloseButton"), Width = 42, Height = 32, Padding = new Thickness(0), Margin = new Thickness(0, 4, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = Loc.T("关闭", "Close"),
            Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1,1 L 11,11 M 11,1 L 1,11"), Width = 12, Height = 12, StrokeThickness = 1.2,
                Stroke = UI.Brush(_palette.Secondary) } };
        AutomationProperties.SetName(close, Loc.T("关闭小组件", "Close widgets"));
        close.Click += (_, _) => Close();
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(_search, true);
        _search.Margin = new Thickness(16, 16, 56, 8);
        var frame = new Grid();
        frame.Children.Add(root); frame.Children.Add(close);
        Content = frame;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => _search.Focus();
        Refresh();
    }

    // Built again when the dock's widgets change, so the ticks follow.
    public void Refresh() { FillCategories(); FillTiles(); }

    private void FillCategories()
    {
        _categories.Children.Clear();
        var heading = new TextBlock { Text = Loc.T("小组件", "Widgets"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 14), Foreground = UI.Brush(_palette.Primary) };
        _categories.Children.Add(heading);
        _categories.Children.Add(CategoryButton(Loc.T("全部", "All"), null));
        foreach (var category in Enum.GetValues<DockWidgetCategory>()) _categories.Children.Add(CategoryButton(DockWidgets.CategoryName(category), category));
    }

    private Button CategoryButton(string name, DockWidgetCategory? category)
    {
        bool selected = _category == category;
        var button = new Button { Style = (Style)FindResource("SettingsNav"), Tag = selected ? "Selected" : null, Margin = new Thickness(0, 0, 0, 3),
            Content = new TextBlock { Text = name, FontSize = 13, Foreground = UI.Brush(selected ? _palette.Primary : _palette.Secondary), FontWeight = selected ? FontWeights.Medium : FontWeights.Normal } };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => { _category = category; Refresh(); };
        return button;
    }

    // One entry per widget, and per AI tool with limits for the AI widget.
    private IEnumerable<(DockWidgetInfo Info, string Option, string Name)> Entries()
    {
        foreach (var info in DockWidgets.Find(_category, _search.Text))
        {
            if (!PerTool(info.Kind)) { yield return (info, "", info.Name); continue; }
            var tools = (_app.Deck?.Snapshots ?? []).Where(s => s.Quotas.Count > 0).ToList();
            if (tools.Count == 0) yield return (info, "", info.Name);
            foreach (var tool in tools) yield return (info, tool.Id.ToString(), $"{tool.Name} · {info.Name}");
        }
    }

    private void FillTiles()
    {
        _tiles.Children.Clear();
        var dock = _app.FirstDock;
        foreach (var (info, option, name) in Entries()) _tiles.Children.Add(Tile(info, option, name, dock));
        if (_tiles.Children.Count == 0)
            _tiles.Children.Add(new TextBlock { Text = Loc.T("没有匹配的小组件。", "No widgets match."), Margin = new Thickness(12), Foreground = UI.Brush(_palette.Secondary) });
    }

    private Button Tile(DockWidgetInfo info, string option, string name, DockWindow? dock)
    {
        var widgets = _app.DockSettings.Widgets;
        // An AI widget without a tool of its own shows the first tool with limits, so its tile is the one ticked.
        string first = FirstTool();
        bool added = widgets.Any(w => w.Kind == info.Kind && (!PerTool(info.Kind) || w.Option == option || w.Option == "" && option == first));
        // A live picture of the widget, drawn by a dock in its own style; none while the dock is off.
        var stage = new Border { Height = 92, CornerRadius = new CornerRadius(10), Background = UI.Brush(_palette.Inset) };
        if (dock is not null)
        {
            var (content, update) = dock.WidgetContent(new DockWidget("preview", info.Kind, option), PreviewTile);
            update();
            stage.Child = dock.PreviewCard(content, info.Width, PreviewTile);
        }
        var title = new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 8, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = UI.Brush(_palette.Primary) };
        var text = new TextBlock { Text = info.Description, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 2, 0), Foreground = UI.Brush(_palette.Secondary) };
        var tick = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = UI.Brush(_app.DockSettings.Accent), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), Visibility = added ? Visibility.Visible : Visibility.Collapsed,
            Child = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,4 L 3,7 L 9,0"), Stroke = Brushes.White, StrokeThickness = 1.8, Width = 9, Height = 7, Stretch = Stretch.Uniform } };
        var stack = new StackPanel(); stack.Children.Add(stage); stack.Children.Add(title); stack.Children.Add(text);
        var grid = new Grid(); grid.Children.Add(stack); grid.Children.Add(tick);
        var card = new Border { Width = 220, Height = 168, Padding = new Thickness(10), CornerRadius = new CornerRadius(12), Background = UI.Brush(_palette.Card),
            BorderBrush = UI.Brush(added ? _app.DockSettings.Accent : _palette.Border), BorderThickness = new Thickness(added ? 1.5 : 1), Child = grid };
        var button = new Button { Content = card, Style = (Style)FindResource("SettingsGlyphButton"), Padding = new Thickness(0), Margin = new Thickness(6), Cursor = Cursors.Hand };
        AutomationProperties.SetName(button, name + (added ? Loc.T("，已添加", ", added") : ""));
        button.Click += (_, _) => Toggle(info.Kind, option, added);
        return button;
    }

    // The AI widgets are offered once for each AI tool with limits.
    private static bool PerTool(DockWidgetKind kind) => kind is DockWidgetKind.AiUsage or DockWidgetKind.AiRing;

    private string FirstTool() => (_app.Deck?.Snapshots ?? []).FirstOrDefault(s => s.Quotas.Count > 0)?.Id.ToString() ?? "";

    private void Toggle(DockWidgetKind kind, string option, bool added)
    {
        var settings = _app.DockSettings;
        var widgets = added
            ? settings.Widgets.Where(w => !(w.Kind == kind && (!PerTool(kind) || w.Option == option || w.Option == "" && option == FirstTool()))).ToList()
            : DockWidgets.Add(settings.Widgets, kind).Select(w => w.Kind == kind && w.Option == "" && !settings.Widgets.Contains(w) ? w with { Option = option } : w).ToList();
        _app.UpdateDockSettings(settings.WithWidgets(widgets));
    }
}
