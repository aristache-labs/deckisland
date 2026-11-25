using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

// A monitor color scheme as the panel draws it on the panel theme in use: ftop's palettes in their light or dark variant by the
// theme, or the theme's own colors (its accent and its cyan, pink and lime), or the custom colors over the scheme they start
// from, each moved just far enough to read on the theme's surface. The page is built again when the theme changes
// (Look.Generation is part of its key). The warning and critical colors are the collapsed readings' own settings, or the
// panel theme's.
internal static class MonitorThemeColors
{
    // look: the look of the panel the colors are drawn on; the main look by default (the settings window's previews).
    public static MonitorColors For(DeckSettings settings, PanelLook? look = null) => For(settings.MonitorPalette, settings.MonitorCustomColors, look) with
    {
        Warning = Warning(settings, look), Critical = Critical(settings, look)
    };
    // They color the readings' values too, so they are held to the text minimum (PanelThemes.LevelContrast).
    public static string Warning(DeckSettings settings, PanelLook? look = null)
    { var Look = look ?? PanelTheme.Main; return Drawn(Look, settings.CompactWarningColor ?? Look.WarningDefault, PanelThemes.LevelContrast); }
    public static string Critical(DeckSettings settings, PanelLook? look = null)
    { var Look = look ?? PanelTheme.Main; return Drawn(Look, settings.CompactCriticalColor ?? Look.CriticalDefault, PanelThemes.LevelContrast); }

    public static MonitorColors For(MonitorPalette palette, MonitorCustomColors custom, PanelLook? look = null)
    {
        var Look = look ?? PanelTheme.Main;
        string Shown(string color) => Drawn(Look, color, PanelThemes.GraphicContrast);
        if (palette == MonitorPalette.Custom)
            return custom.Over(For(custom.Base == MonitorPalette.Custom ? MonitorPalette.Theme : custom.Base, custom, Look), Shown);
        var theme = Look.Current;
        if (palette == MonitorPalette.Theme)
            return MonitorColors.FromTheme(Shown(theme["accent"].ToString()),
                [.. new[] { ClockColor.Cyan, ClockColor.Pink, ClockColor.Lime, ClockColor.Amber }.Select(color => Shown(theme.ClockPreset(color).ToString()))],
                Shown(Look.WarningDefault), Shown(Look.CriticalDefault));
        return MonitorColors.For(palette, theme.IsDark).Map(Shown);
    }
    // Opaque "#RRGGBB" as drawn on the theme, which ColorMath needs for the core groups' shades and for telling colors apart.
    private static string Drawn(PanelLook look, string color, double minimum) => ThemeColor.Opaque(look.Readable(ThemeColor.Opaque(color), minimum));

    public static StripColors Strip(DeckSettings settings, PanelLook? look = null) => new(For(settings, look));
}

// The system monitor page, after a hardware dashboard: the readings as rows in one card (MonitorRows), the busiest apps in another
// beside or under it, and the drives in a card under the list when it is at the side. Update hands each part its new reading.
// With more than one graphics adapter the GPU row's counter shows the next adapter.
internal sealed class MonitorView : Grid
{
    // The look of the panel this element is drawn in (PanelLook.Of); the main look outside a panel.
    private PanelLook Look => PanelLook.Of(this);
    private readonly MonitorLayout _layout;
    private readonly MonitorRows? _rows;
    private readonly StorageBlock? _storage;
    private readonly ProcessListView? _list;
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private ProcessCard? _card;
    private ProcessList? _processes;
    private int _cores = Environment.ProcessorCount;
    private bool _motion;
    // The cards the parts sit on: the readings on one, the list on another with the drives in a card under it.
    public const double CardPadding = 10, CardGap = 10;
    // The room the cards take from the page (MainWindow.CurrentMonitorPlan).
    public const double CardChromeHeight = 2 * CardPadding + 18, CardChromeWidth = 4 * (CardPadding + 2) + CardGap;

    // plan: the layout of what the page shows (ExpandedLayout): only the readings it has a place for are built, so what is not chosen
    // costs nothing here. The rows follow the plan's order. temperatures: the processor's, the graphics adapter's and the drives'
    // temperatures are shown. main and mainChanged are kept for the callers; every row is the same size now.
    public MonitorView(ExpandedPlan plan, MonitorColors colors, MonitorMain main, ProcessSort sort, MonitorProcessView processView,
        Action<MonitorMain> mainChanged, Action<ProcessSort> sortChanged, Action<bool> hold, Action<GpuSample> gpuChanged, bool temperatures = false, bool smallTemperature = false,
        PowerOptions? power = null, FrameworkElement? message = null, CoreDisplay coreDisplay = CoreDisplay.Auto, MonitorRows.ShowMenu? menu = null, IReadOnlyList<ExpandedItem>? tableOrder = null)
    {
        var layout = plan.Steps;
        _layout = layout;
        // Nothing to show: the message (nothing chosen, or nothing readable), and nothing built or read.
        if (plan.Kind is ExpandedKind.Empty or ExpandedKind.Unavailable)
        {
            if (message is not null) Children.Add(message);
            return;
        }
        // The rows in the settings' table order: the layout keeps the CPU and the GPU together (once one band of gauges), the rows do not.
        var order = plan.Parts.Select(part => part.Item).ToList();
        if (tableOrder is not null) order = [.. order.OrderBy(item => tableOrder.ToList().IndexOf(item) is var at and >= 0 ? at : int.MaxValue)];
        bool showsPower = plan.Part(ExpandedItem.Power) is not null && layout.PowerLines > 0 && power is not null;
        _rows = new MonitorRows(order, colors, layout, temperatures, showsPower ? power : null, plan.Part(ExpandedItem.Frames) is not null && layout.FrameLine, gpuChanged, coreDisplay, menu);
        bool sideList = plan.List != ExpandedList.Full && layout.List == ListPlacement.Side;
        if (plan.Part(ExpandedItem.Storage) is not null && layout.StorageLines > 0)
            // In its own card: under the list at the side, otherwise under the readings. One volume per line.
            _storage = new StorageBlock(colors, layout.Body, layout.Caption, layout.StorageRow, layout.StorageLines * layout.StorageColumns, 1, temperatures);
        bool hasReadings = _rows.Children.Count > 0;

        if (plan.List == ExpandedList.Full)
        {
            // The list alone: it fills the page.
            _list = new ProcessListView(layout, sort, processView, sortChanged, hold, Open);
            Children.Add(Card(_list));
        }
        else if (layout.List == ListPlacement.None) Children.Add(WithDrives(hasReadings ? Card(_rows, top: true) : null));
        else
        {
            _list = new ProcessListView(layout, sort, processView, sortChanged, hold, Open);
            FrameworkElement readings = hasReadings ? Card(_rows, top: true) : new Border();
            FrameworkElement listSide = Card(_list);
            if (sideList)
            {
                // The list keeps its share of the width within its bounds; the readings keep at least 260 DIP.
                ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 260 });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardGap) });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MonitorLayout.ListShare / (1 - MonitorLayout.ListShare), GridUnitType.Star),
                    MinWidth = MonitorLayout.ListMinimum, MaxWidth = MonitorLayout.ListMaximum });
                listSide = WithDrives(listSide);
                SetColumn(listSide, 2);
            }
            else
            {
                RowDefinitions.Add(new RowDefinition { Height = new GridLength(MonitorLayout.StackedReadings + 2 * CardPadding) });
                RowDefinitions.Add(new RowDefinition { Height = new GridLength(CardGap) });
                RowDefinitions.Add(new RowDefinition());
                SetRow(listSide, 2);
                readings = WithDrives(readings);
            }
            Children.Add(readings); Children.Add(listSide);
        }
        SetColumnSpan(_overlay, 3); SetRowSpan(_overlay, 3);
        Children.Add(_overlay);
    }

    // A rounded card in the panel's fill; its colors come from the panel it lands in (PanelLook.Of), known once it is loaded.
    // top: the content keeps to the card's top rather than stretching.
    private static Border Card(FrameworkElement child, bool top = false)
    {
        if (top) child.VerticalAlignment = VerticalAlignment.Top;
        var card = new Border { Child = child, ClipToBounds = true, Padding = new Thickness(CardPadding + 2, top ? CardPadding - 4 : CardPadding, CardPadding + 2, top ? CardPadding - 4 : CardPadding) };
        card.Loaded += (_, _) => { var look = PanelLook.Of(card); card.Background = UI.Brush(look.Fill); card.CornerRadius = new CornerRadius(look.Corner(10)); };
        return card;
    }
    // The given part with the drives' card under it, when the page shows the drives.
    private FrameworkElement WithDrives(FrameworkElement? above)
    {
        if (_storage is null) return above ?? new Border();
        var column = new Grid();
        column.RowDefinitions.Add(new RowDefinition());
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var drives = Card(_storage);
        if (above is not null) { drives.Margin = new Thickness(0, CardGap, 0, 0); column.Children.Add(above); }
        SetRow(drives, 1); column.Children.Add(drives);
        return column;
    }

    // A press anywhere in the window outside the card and the list's rows closes the card: the page's blank room, the list's own
    // blank room and its sort buttons, the window's top bar. The click still reaches what it was meant for. On a row the row
    // decides (Open): its own row closes the card, another row opens its own.
    public void WindowPressed()
    {
        if (_card is not null && !_card.IsMouseOver && _list?.PointerOnRow != true) CloseCard(animate: true);
    }
    // Whether the page has the power row (the layout had room for it): the power is read only then.
    public bool ShowsPower => _rows?.ShowsPower == true;
    // Whether the page has the frame rate's row (the layout had room for it): the frame rate is read for it only then.
    public bool ShowsFrames => _rows?.ShowsFrames == true;

    public void Update(MonitorSnapshot snapshot, bool motion)
    {
        _motion = motion;
        _cores = Math.Max(1, snapshot.LogicalProcessors);
        _rows?.Update(snapshot, motion);
        _storage?.Set(snapshot.Storage, motion);
        _list?.Set(snapshot.Processes, _cores, motion);
        _processes = snapshot.Processes.OrDefault;
        // An open card follows its app's readings; it closes when the app has left the list.
        if (_card is not null)
        {
            var sample = _processes?.Top.Concat(_processes.ByMemory).FirstOrDefault(s => s.Id == _card.Id);
            if (sample is null) CloseCard(animate: true); else _card.Show(sample, _cores, _layout.Body, _layout.Caption);
        }
    }

    // Opens an app's card beside its row: to the left of a list at the right, otherwise just right of the row's start.
    private void Open(ProcessSample sample, Rect row)
    {
        if (_list is null) return;
        if (_card?.Id == sample.Id) { CloseCard(animate: true); return; }
        CloseCard(animate: true);
        var card = new ProcessCard(_layout.Body, _layout.Caption, Look);
        card.Show(sample, _cores, _layout.Body, _layout.Caption);
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseCard(animate: true); };
        card.Switched += () => CloseCard(animate: true);
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var origin = _list.TranslatePoint(new Point(row.X, row.Y), this);
        double width = card.DesiredSize.Width, height = card.DesiredSize.Height;
        double x = _layout.List == ListPlacement.Side ? origin.X - width - 12 : Math.Min(origin.X + 36, ActualWidth - width);
        double y = Math.Clamp(origin.Y + row.Height / 2 - height / 2, 0, Math.Max(0, ActualHeight - height));
        Canvas.SetLeft(card, Math.Max(0, x)); Canvas.SetTop(card, y);
        _overlay.Children.Add(card); _overlay.IsHitTestVisible = true;
        _card = card;
        card.Appear(_motion);
    }

    // Asked by the user (or when the app leaves the list) the card fades away; when the page goes, it simply goes.
    public void CloseCard(bool animate = false)
    {
        if (_card is not { } card) return;
        _overlay.IsHitTestVisible = false; _card = null;
        if (animate && _motion) card.Leave(() => _overlay.Children.Remove(card));
        else _overlay.Children.Remove(card);
    }

    public void SetSort(ProcessSort sort) => _list?.SetSort(sort, _motion);
}

// The monitor page giving way to a new build of itself (a resize across a layout step, a sensor found or missing, a new main
// gauge): the new page fades in while every page it replaces fades out from where it is, so their opacities always add up to one
// and the page never dims, even when a drag crosses several steps in quick succession. Nothing jumps to its new place in one
// frame; the pages that leave take no input. Without animations the new page simply replaces them.
internal static class MonitorCrossfade
{
    private const double Seconds = Eased.Seconds * .6;

    public static void Swap(Panel host, FrameworkElement next, FrameworkElement old, bool motion)
    {
        if (!motion || !host.Children.Contains(old)) { host.Children.Clear(); host.Children.Add(next); return; }
        var leaving = host.Children.OfType<UIElement>().Select(child => (Element: child, From: child.Opacity)).ToList();
        foreach (var (element, _) in leaving) { element.IsHitTestVisible = false; element.BeginAnimation(UIElement.OpacityProperty, null); }
        // The opacities the leaving pages had, after any animation of their own was taken off.
        leaving = [.. leaving.Select(page => (page.Element, page.Element.Opacity))];
        next.Opacity = 0;
        host.Children.Add(next);
        var fade = new Eased(0, Seconds);
        fade.Set(1, true);
        Motion.Start(host, now =>
        {
            double t = Math.Clamp(fade.At(now), 0, 1);
            next.Opacity = t;
            foreach (var (element, from) in leaving) element.Opacity = from * (1 - t);
            if (fade.Moving(now)) return true;
            next.Opacity = 1;
            foreach (var (element, _) in leaving) host.Children.Remove(element);
            return false;
        });
    }
}
