using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// A readout (volume, a lock key, a notification...) shown by the collapsed island itself: the row is replaced by the readout and
// the surface takes its width, then returns to the width it had when the readout goes. The width follows the same route as any
// content that asks for another width (FitCompactToContent), so it moves with the island's usual animation.
public partial class MainWindow
{
    private HudMessage? _hudMessage;
    // The length of the island along its edge when a readout started: one without text on a side edge is 80 % of it.
    private double _normalSurface = double.NaN;
    private DispatcherTimer? _hudEnd;
    // When the shown message appeared, so a card built again (a new shape or theme) carries its scrolling on where it was; and the
    // moment (TickCount64) its timer was lengthened to for its scrolling lines.
    private long _hudShownAt, _hudScrollUntil;

    // False when this panel cannot show it in place (open, hidden, or a line); the caller then floats it.
    internal bool ShowHudInline(HudMessage message)
    {
        if (_expanded || !IsVisible || IsSuppressed || DisplayPower.Off || _poweringOff || EffectiveStyle == CompactStyle.Line) return false;
        if (_hudMessage is null) _normalSurface = Collapsed.Surface.Width;
        _hudMessage = message; _hudShownAt = Environment.TickCount64; _hudScrollUntil = 0;
        _hudEnd ??= new DispatcherTimer();
        _hudEnd.Stop();
        if (message.Hold is { } hold && hold > TimeSpan.Zero)
        {
            _hudEnd.Interval = hold;
            _hudEnd.Tick -= EndHudTick;
            _hudEnd.Tick += EndHudTick;
            _hudEnd.Start();
        }
        RenderCompact();
        if (CompactColumnMode) Dispatcher.BeginInvoke(DispatcherPriority.Background, FitCompactToContent);
        return true;
    }

    private void EndHudTick(object? sender, EventArgs e) => EndHud();

    internal void ClearHud(string key)
    {
        if (_hudMessage?.Key == key) EndHud();
    }

    private void EndHud()
    {
        _hudEnd?.Stop();
        if (_hudMessage is null) return;
        _hudMessage = null;
        RenderCompact();
        if (CompactColumnMode) Dispatcher.BeginInvoke(DispatcherPriority.Background, FitCompactToContent);
    }

    // The readout as one horizontal row of one width, with or without text: the icon (in the accent when something was switched on) at
    // the left, the text in the middle, and the dot or figure at the right. Without text the level bar takes the middle. A text that does
    // not fit is cut with an ellipsis. Returns the row and its width in DIPs.
    private (FrameworkElement Parts, double Natural) BuildHudRow(HudMessage hud, double scale, double dpi, double maxWidth)
    {
        double font = Math.Max(1, CompactMusicLayout.Half(11.5 * scale)), line = Math.Max(1, CompactMusicLayout.Whole(16 * scale));
        double gap = Math.Round(8 * scale), icon = Math.Round(14 * scale);
        double total = Math.Min(Math.Round(240 * scale), maxWidth);
        var grid = new Grid { Width = total, VerticalAlignment = VerticalAlignment.Center, ToolTip = hud.Label ?? hud.Text };
        System.Windows.Automation.AutomationProperties.SetName(grid, hud.Label ?? hud.Text);
        var mark = HudGlyph.Create(hud, Look, icon, scale);
        mark.VerticalAlignment = VerticalAlignment.Center; mark.HorizontalAlignment = HorizontalAlignment.Left;
        double glyph = Math.Max(icon, double.IsNaN(mark.Width) ? icon : mark.Width);
        bool textless = hud.Text.Length == 0;
        // What stands at the right: the figure, then the dot.
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (hud.Figure is { } figure) right.Children.Add(UI.Fixed(figure, font, line, hud.FigureColor ?? Look.Secondary));
        if (hud.On is { } on)
        {
            double size = Math.Max(5, Math.Round(7 * scale));
            right.Children.Add(new Ellipse { Width = size, Height = size, Margin = new Thickness(right.Children.Count > 0 ? gap : 0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = on ? UI.Brush(Look.Hex("accent")) : System.Windows.Media.Brushes.Transparent, StrokeThickness = 1.3,
                Stroke = on ? UI.Brush(Look.Hex("accent")) : UI.Brush(Look.Secondary) });
        }
        right.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Both sides are as wide as the wider one, so the text stands in the middle of the row.
        double side = textless ? glyph + gap : Math.Max(glyph, Math.Ceiling(right.DesiredSize.Width)) + gap;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(side) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(textless ? 0 : side) });
        grid.Children.Add(mark);
        if (!textless)
        {
            var text = UI.Fixed(hud.Text, font, line, Look.Primary, FontWeights.SemiBold);
            text.TextTrimming = TextTrimming.CharacterEllipsis; text.ToolTip = hud.Text; text.TextAlignment = TextAlignment.Center;
            text.ClearValue(FrameworkElement.WidthProperty); text.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            Grid.SetColumn(right, 2); grid.Children.Add(right);
        }
        else if (hud.Level is { } level)
        {
            double height = Math.Max(2, Math.Round(4 * scale));
            var bar = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.0001, Math.Clamp(level, 0, 1)), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.0001, 1 - Math.Clamp(level, 0, 1)), GridUnitType.Star) });
            Grid.SetColumnSpan(bar, 2);
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = UI.Brush(Look.Hex("track")), SnapsToDevicePixels = true });
            var fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = UI.Brush(Look.Hex("accent")) };
            Grid.SetColumn(fill, 0); bar.Children.Add(fill);
            // The track runs under the fill across both columns.
            Grid.SetColumnSpan(bar.Children[0], 2);
            Grid.SetColumn(bar, 1); grid.Children.Add(bar);
        }
        return (grid, total);
    }

    // The row as it stands on a side edge: icon, text, then the dot, figure or bar after it, each as wide as it needs, so the notch or the
    // capsule takes the readout's own width there. A text longer than maxWidth is cut with an ellipsis.
    private (FrameworkElement Parts, double Natural) BuildHudRowNatural(HudMessage hud, double scale, double dpi, double maxWidth)
    {
        double font = Math.Max(1, CompactMusicLayout.Half(11.5 * scale)), line = Math.Max(1, CompactMusicLayout.Whole(16 * scale));
        double gap = Math.Round(8 * scale), icon = Math.Round(14 * scale), after = Math.Round(44 * scale);
        var parts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = hud.Label ?? hud.Text };
        System.Windows.Automation.AutomationProperties.SetName(parts, hud.Label ?? hud.Text);
        var glyph = HudGlyph.Create(hud, Look, icon, scale);
        glyph.VerticalAlignment = VerticalAlignment.Center; glyph.Margin = new Thickness(0, 0, hud.Text.Length > 0 ? gap : 0, 0);
        parts.Children.Add(glyph);
        var text = UI.Fixed(hud.Text, font, line, Look.Primary, FontWeights.SemiBold);
        text.MaxWidth = Math.Round(260 * scale); text.TextTrimming = TextTrimming.CharacterEllipsis; text.ToolTip = hud.Text;
        if (hud.Reserve is { } longest)
        {
            var spare = UI.Fixed(longest, font, line, Look.Primary, FontWeights.SemiBold);
            spare.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            text.MinWidth = Math.Ceiling(spare.DesiredSize.Width);
        }
        if (hud.Text.Length > 0) parts.Children.Add(text);
        if (hud.Level is { } level)
        {
            double width = Math.Round((hud.Text.Length > 0 ? 56 : 180) * scale), height = Math.Max(2, Math.Round(4 * scale));
            var bar = new Grid { Width = width, Height = height, Margin = new Thickness(hud.Text.Length > 0 ? Math.Round(18 * scale) : gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = UI.Brush(Look.Hex("track")) });
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = UI.Brush(Look.Hex("accent")), HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Round(width * Math.Clamp(level, 0, 1)) });
            parts.Children.Add(bar);
        }
        if (hud.Figure is { } figure)
        {
            var value = UI.Fixed(figure, font, line, hud.FigureColor ?? Look.Secondary);
            value.Margin = new Thickness(hud.Text.Length > 0 ? after : gap, 0, 0, 0);
            parts.Children.Add(value);
        }
        if (hud.On is { } on)
        {
            double size = Math.Max(5, Math.Round(7 * scale));
            parts.Children.Add(new Ellipse { Width = size, Height = size, Margin = new Thickness(hud.Text.Length > 0 ? after : gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = on ? UI.Brush(Look.Hex("accent")) : System.Windows.Media.Brushes.Transparent, StrokeThickness = 1.3,
                Stroke = on ? UI.Brush(Look.Hex("accent")) : UI.Brush(Look.Secondary) });
        }
        parts.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double natural = Math.Ceiling(parts.DesiredSize.Width * dpi) / dpi;
        if (natural > maxWidth)
        {
            text.MaxWidth = Math.Max(Math.Round(24 * scale), text.DesiredSize.Width - (natural - maxWidth));
            parts.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            natural = Math.Ceiling(parts.DesiredSize.Width * dpi) / dpi;
        }
        return (parts, natural);
    }

    // A notification is a card: the app's picture, the title and the message under it. It is taller than the row, so the island grows
    // downward for it; on a side edge it stays a card too, as thin along the edge as the card is high.
    private bool HudIsCard => _hudMessage is { Body.Length: > 0 } && EffectiveStyle != CompactStyle.Line;

    private CollapsedShape HudTall(CollapsedShape shape)
    {
        if (!HudIsCard || ColumnFor(shape.Style) || shape.Style is not (CompactStyle.Notch or CompactStyle.Capsule)) return shape;
        double height = Math.Round(CollapsedLimits.BaseHeight * shape.Scale * 1.75);
        double radius = Math.Min(Math.Round(20 * shape.Scale), height / 2);
        return shape with { Surface = shape.Surface with { Height = height }, BottomRadius = shape.Style == CompactStyle.Capsule ? height / 2 : Math.Max(shape.BottomRadius, radius),
            TopRadius = shape.Style == CompactStyle.Capsule ? height / 2 : shape.TopRadius };
    }

    private (FrameworkElement Parts, double Natural) BuildHudCard(HudMessage hud, double scale, double maxWidth)
    {
        double font = Math.Max(1, CompactMusicLayout.Half(12.5 * scale)), small = Math.Max(1, CompactMusicLayout.Half(11 * scale));
        double picture = Math.Round(34 * scale), gap = Math.Round(12 * scale), total = Math.Min(Math.Round(360 * scale), maxWidth);
        var grid = new Grid { Width = total, VerticalAlignment = VerticalAlignment.Center, ToolTip = hud.Label ?? hud.Text };
        System.Windows.Automation.AutomationProperties.SetName(grid, hud.Label ?? hud.Text);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FrameworkElement Fallback() => new Border { Width = picture, Height = picture, CornerRadius = new CornerRadius(Corner(8)), Background = UI.Brush(Look.Hex("fillSelected")),
            Child = new Path { Data = HudWindow.IconGeometry(hud.Icon), Stroke = UI.Brush(Look.Hex("accent")), StrokeThickness = Math.Max(1, 1.4 * scale), Stretch = System.Windows.Media.Stretch.Uniform,
                Width = Math.Round(16 * scale), Height = Math.Round(16 * scale), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        FrameworkElement mark;
        if (hud.Picture is { Length: > 0 } bytes && !Look.HighContrast)
        {
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.StreamSource = new System.IO.MemoryStream(bytes);
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                mark = new Border { Width = picture, Height = picture, CornerRadius = new CornerRadius(Corner(8)), Background = UI.Brush(Look.Hex("fillSelected")), Padding = new Thickness(Math.Round(4 * scale)),
                    Child = new Image { Source = image, Stretch = System.Windows.Media.Stretch.Uniform } };
            }
            catch (Exception error) when (error is NotSupportedException or System.IO.IOException or InvalidOperationException) { mark = Fallback(); }
        }
        else mark = Fallback();
        mark.VerticalAlignment = VerticalAlignment.Center; mark.Margin = new Thickness(0, 0, gap, 0);
        grid.Children.Add(mark);
        var title = UI.Fixed(hud.Text, font, Math.Max(1, CompactMusicLayout.Whole(17 * scale)), Look.Primary, FontWeights.SemiBold);
        var body = UI.Fixed(hud.Body ?? "", small, Math.Max(1, CompactMusicLayout.Whole(15 * scale)), Look.Secondary);
        foreach (var line in new[] { title, body }) { line.ClearValue(FrameworkElement.WidthProperty); line.TextTrimming = TextTrimming.CharacterEllipsis; line.HorizontalAlignment = HorizontalAlignment.Stretch; }
        double room = Math.Max(1, total - picture - gap);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { HudScrolling(title, room, scale, hud), HudScrolling(body, room, scale, hud) } };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return (grid, total);
    }

    // A card's line as it shows: cut with an ellipsis while it fits or cannot scroll, else scrolling left once (HudScroll), started where
    // the time since the message appeared has it. The first card built for a message lengthens its timer for the longest line.
    private FrameworkElement HudScrolling(TextBlock line, double room, double scale, HudMessage hud)
    {
        line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double overflow = line.DesiredSize.Width - room;
        double hold = hud.Hold?.TotalSeconds ?? 0;
        var (scroll, total) = HudScroll.Plan(overflow / Math.Max(.1, scale), hold, Settings.NotificationScrollSeconds);
        if (scroll <= 0 || hold <= 0) return line;
        long until = _hudShownAt + (long)(total * 1000);
        if (total > hold && until > _hudScrollUntil && _hudEnd is { IsEnabled: true } && ReferenceEquals(_hudMessage, hud))
        {
            _hudScrollUntil = until;
            _hudEnd.Stop(); _hudEnd.Interval = TimeSpan.FromMilliseconds(Math.Max(1, until - Environment.TickCount64)); _hudEnd.Start();
        }
        line.TextTrimming = TextTrimming.None; line.HorizontalAlignment = HorizontalAlignment.Left;
        line.Width = line.DesiredSize.Width;
        var move = new System.Windows.Media.TranslateTransform();
        line.RenderTransform = move;
        double distance = Math.Min(overflow, scroll * HudScroll.Speed * scale);
        var slide = new System.Windows.Media.Animation.DoubleAnimation(0, -distance, TimeSpan.FromSeconds(scroll))
        {
            BeginTime = TimeSpan.FromSeconds(HudScroll.Lead - (Environment.TickCount64 - _hudShownAt) / 1000.0),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        move.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        // The room clips the line; the canvas leaves it its whole width, so the end that scrolls in is drawn.
        var window = new Canvas { Height = line.Height, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { line } };
        return window;
    }

    private void RenderCompactHud(HudMessage hud, double scale, double contentWidth)
    {
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX, pad = Math.Round(28 * scale);
        // A capsule's ends take less of the width than a notch's (16 against 30 DIP at the base height), so it asks for the difference
        // to be as wide as the notch.
        if (EffectiveStyle == CompactStyle.Capsule) pad += 2 * Math.Round((CollapsedLimits.NotchInset - CollapsedLimits.CapsuleInset) * scale);
        // Every readout has one width, the notch's and the capsule's alike (the capsule's shorter ends are made up in pad), within the
        // screen. Its height, scale and horizontal offset are the surface's own.
        double room = Math.Max(0, MonitorBoundsDip.Width - 2 * Math.Round(24 * scale) - pad);
        var (parts, natural) = hud.Body is { Length: > 0 } ? BuildHudCard(hud, scale, room) : BuildHudRow(hud, scale, dpi, room);
        double asked = natural + pad;
        // The surface takes the readout's width, even narrower than the row it replaces.
        if (double.IsNaN(_compactWanted) || Math.Abs(asked - _compactWanted) > .5 || Math.Abs(natural - _compactRequired) > .5)
        {
            _compactWanted = asked; _compactRequired = natural;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitCompactToContent);
        }
        double row = Math.Max(1, CompactMusicLayout.Whole((hud.Body is { Length: > 0 } ? 46 : 26) * scale));
        var grid = new Grid { Width = SnapDown(contentWidth, dpi), Height = row };
        parts.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(parts);
        CompactRings.Children.Add(grid);
        UpdateMusicTimer();
        _stripShowing = false; UpdateMonitorDemand();
    }

    // On a side edge the notch is as thin along the edge as the top one is high (the height setting), with insets just wide enough for
    // the one line, instead of the 60 DIP the shoulders ask of a long row.
    private CollapsedShape HudThin(CollapsedShape shape)
    {
        if (_hudMessage is not { Text.Length: > 0 } || !ColumnFor(shape.Style)) return shape;
        // The content box is the row's own (26 DIP at the notch's scale, which the height setting gives), with small ends.
        double line = Math.Max(1, CompactMusicLayout.Whole((HudIsCard ? 46 : 26) * shape.Scale));
        // The ends flare like the notch's shoulders do at the top: the end at the screen edge melts into it with concave curves, which
        // take the inset, while the far end keeps the corner radius the Corners setting gives.
        bool capsule = shape.Style == CompactStyle.Capsule;
        // A capsule has no shoulders to flare: just a small rounded end each side of the one line.
        double inset = Math.Max(2, Math.Round((capsule ? 7 : 19) * shape.Scale));
        double length = line + 2 * inset;
        double x = Math.Max(0, shape.Surface.X + (shape.Surface.Width - length) / 2);
        return shape with { Surface = shape.Surface with { X = x, Width = length }, Inset = inset, ContentWidth = Math.Max(0, length - 2 * inset),
            BottomRadius = Math.Min(shape.BottomRadius, length / 2), TopRadius = Math.Min(shape.TopRadius, length / 2), Shoulder = Math.Min(shape.Shoulder, inset) };
    }

    // A notch on a side edge: the readout stays one horizontal row, as at the top; the notch's depth grows to its width and its
    // length to the row's height, then both go back.
    private void RenderHudColumn(HudMessage hud, double scale, double dpi, double column, double room)
    {
        if (hud.Text.Length == 0) { RenderHudVertical(hud, scale, dpi, column, room); return; }
        double pad = Math.Round(28 * scale);
        // Across: as wide as the readout, up to the width set for the notch (Size and position).
        double saved = Settings.NotchWidth * _scale;
        double across = Math.Min(Math.Round(360 * scale), Math.Max(Math.Round(60 * scale), saved - pad));
        var (parts, natural) = hud.Body is { Length: > 0 } ? BuildHudCard(hud, scale, across) : BuildHudRowNatural(hud, scale, dpi, across);
        double row = Math.Max(1, CompactMusicLayout.Whole(26 * scale));
        double wantedColumn = CompactColumn.Width(scale, [natural + pad], []);
        // Along the edge: the row's own height, as at the top.
        double line = Math.Max(1, CompactMusicLayout.Whole((hud.Body is { Length: > 0 } ? 46 : 26) * scale));
        double wanted = DevicePixels.Up(line, dpi);
        if (double.IsNaN(_compactWanted) || Math.Abs(wanted - _compactWanted) > .5 || Math.Abs(line - _compactRequired) > .5 || double.IsNaN(_compactColumn) || Math.Abs(wantedColumn - _compactColumn) > .5)
        {
            _compactWanted = wanted; _compactRequired = line; _compactColumn = wantedColumn;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitCompactToContent);
        }
        parts.HorizontalAlignment = HorizontalAlignment.Center; parts.VerticalAlignment = VerticalAlignment.Center;
        var host = new Grid { Width = column, Height = room, ClipToBounds = true };
        host.Children.Add(parts);
        CompactRings.Children.Add(host);
        UpdateMusicTimer();
        _stripShowing = false; UpdateMonitorDemand();
    }

    // A readout without text (volume, brightness) on a side edge stands upright: the icon above a level bar that fills from the
    // bottom. Text is never turned, so a readout with text keeps its row (RenderHudColumn).
    private void RenderHudVertical(HudMessage hud, double scale, double dpi, double column, double room)
    {
        double icon = Math.Round(14 * scale), gap = Math.Round(8 * scale), thickness = Math.Max(2, Math.Round(4 * scale));
        // The readout is 80 % as long as the island was, its bar taking what the icon leaves.
        double shown = double.IsNaN(_normalSurface) ? 0 : Math.Round(_normalSurface * .8) - 2 * Collapsed.Inset;
        double length = Math.Max(Math.Round(56 * scale), shown - icon - gap - Math.Round(8 * scale));
        var mark = new Path
        {
            Data = HudWindow.IconGeometry(hud.Icon), Stroke = UI.Brush(hud.Lit ? Look.Hex("accent") : Look.Primary), StrokeThickness = Math.Max(1, 1.4 * scale),
            Stretch = System.Windows.Media.Stretch.Uniform, Width = icon, Height = icon, StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Center
        };
        var panel = new StackPanel { Width = column, VerticalAlignment = VerticalAlignment.Center, ToolTip = hud.Label };
        System.Windows.Automation.AutomationProperties.SetName(panel, hud.Label ?? "");
        panel.Children.Add(mark);
        double total = icon;
        if (hud.Level is { } level)
        {
            var bar = new Grid { Width = thickness, Height = length, Margin = new Thickness(0, gap, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(thickness / 2), Background = UI.Brush(Look.Hex("track")) });
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(thickness / 2), Background = UI.Brush(Look.Hex("accent")), VerticalAlignment = VerticalAlignment.Bottom,
                Height = Math.Round(length * Math.Clamp(level, 0, 1)) });
            panel.Children.Add(bar);
            total += gap + length;
        }
        double wanted = DevicePixels.Up(total, dpi), wantedColumn = CompactColumn.Width(scale, [icon], []);
        if (double.IsNaN(_compactWanted) || Math.Abs(wanted - _compactWanted) > .5 || Math.Abs(icon - _compactRequired) > .5 || double.IsNaN(_compactColumn) || Math.Abs(wantedColumn - _compactColumn) > .5)
        {
            _compactWanted = wanted; _compactRequired = icon; _compactColumn = wantedColumn;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitCompactToContent);
        }
        var host = new Grid { Width = column, Height = room, ClipToBounds = true };
        host.Children.Add(panel);
        CompactRings.Children.Add(host);
        UpdateMusicTimer();
        _stripShowing = false; UpdateMonitorDemand();
    }
}
