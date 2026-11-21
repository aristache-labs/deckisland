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
    private DispatcherTimer? _hudEnd;

    // False when this panel cannot show it in place (open, hidden, or a line); the caller then floats it.
    internal bool ShowHudInline(HudMessage message)
    {
        if (_expanded || !IsVisible || IsSuppressed || DisplayPower.Off || _poweringOff || EffectiveStyle == CompactStyle.Line) return false;
        _hudMessage = message;
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

    // The readout as one horizontal row: icon (in the accent when something was switched on), text, then a level bar, a figure or a dot.
    // A text longer than maxWidth is cut with an ellipsis. Returns the row and its natural width in DIPs.
    private (StackPanel Parts, double Natural) BuildHudRow(HudMessage hud, double scale, double dpi, double maxWidth)
    {
        double font = Math.Max(1, CompactMusicLayout.Half(11.5 * scale)), line = Math.Max(1, CompactMusicLayout.Whole(16 * scale));
        double gap = Math.Round(8 * scale), icon = Math.Round(14 * scale);
        var parts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        parts.Children.Add(new Path
        {
            Data = HudWindow.IconGeometry(hud.Icon), Stroke = UI.Brush(hud.Lit ? Look.Hex("accent") : Look.Primary), StrokeThickness = Math.Max(1, 1.4 * scale),
            Stretch = System.Windows.Media.Stretch.Uniform, Width = icon, Height = icon, StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, hud.Text.Length > 0 ? gap : 0, 0)
        });
        var text = UI.Fixed(hud.Text, font, line, Look.Primary, FontWeights.SemiBold);
        text.MaxWidth = Math.Round(260 * scale); text.TextTrimming = TextTrimming.CharacterEllipsis; text.ToolTip = hud.Text;
        if (hud.Reserve is { } longest)
        {
            var spare = UI.Fixed(longest, font, line, Look.Primary, FontWeights.SemiBold);
            spare.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            text.MinWidth = Math.Ceiling(spare.DesiredSize.Width);
        }
        if (hud.Text.Length > 0) parts.Children.Add(text);
        parts.ToolTip = hud.Label ?? hud.Text;
        System.Windows.Automation.AutomationProperties.SetName(parts, hud.Label ?? hud.Text);
        if (hud.Level is { } level)
        {
            double width = Math.Round(56 * scale), height = Math.Max(2, Math.Round(4 * scale));
            var bar = new Grid { Width = width, Height = height, Margin = new Thickness(gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = UI.Brush(Look.Hex("track")) });
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = UI.Brush(Look.Hex("accent")), HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Round(width * Math.Clamp(level, 0, 1)) });
            parts.Children.Add(bar);
        }
        if (hud.Figure is { } figure)
        {
            var value = UI.Fixed(figure, font, line, Look.Secondary);
            value.Margin = new Thickness(gap, 0, 0, 0);
            parts.Children.Add(value);
        }
        if (hud.On is { } on)
        {
            double size = Math.Max(5, Math.Round(7 * scale));
            parts.Children.Add(new Ellipse { Width = size, Height = size, Margin = new Thickness(gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
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

    private void RenderCompactHud(HudMessage hud, double scale, double contentWidth)
    {
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX, pad = Math.Round(28 * scale);
        // The readout never grows past the width set for this style (Collapsed view's width, at the panel's scale), so a longer text
        // is cut. Its height, scale and horizontal offset are the surface's own.
        double saved = (EffectiveStyle == CompactStyle.Capsule ? Settings.CapsuleWidth : Settings.NotchWidth) * _scale;
        double cap = Math.Max(0, saved - 2 * Collapsed.Inset);
        var (parts, natural) = BuildHudRow(hud, scale, dpi, cap > pad ? cap - pad : double.PositiveInfinity);
        double asked = Math.Min(natural + pad, cap > 0 ? cap : natural + pad);
        // The surface takes the readout's width, even narrower than the row it replaces.
        if (double.IsNaN(_compactWanted) || Math.Abs(asked - _compactWanted) > .5 || Math.Abs(natural - _compactRequired) > .5)
        {
            _compactWanted = asked; _compactRequired = natural;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitCompactToContent);
        }
        double row = Math.Max(1, CompactMusicLayout.Whole(26 * scale));
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
        if (_hudMessage is not { Text.Length: > 0 } || shape.Style != CompactStyle.Notch || !ColumnFor(CompactStyle.Notch)) return shape;
        double line = Math.Max(Math.Round(14 * shape.Scale), Math.Max(1, CompactMusicLayout.Whole(16 * shape.Scale)));
        double length = Math.Round(CollapsedLimits.BaseHeight * shape.Scale);
        double inset = Math.Max(2, Math.Round((length - line) / 2));
        double x = Math.Max(0, shape.Surface.X + (shape.Surface.Width - length) / 2);
        return shape with { Surface = shape.Surface with { X = x, Width = length }, Inset = inset, ContentWidth = Math.Max(0, length - 2 * inset),
            BottomRadius = Math.Min(shape.BottomRadius, inset), Shoulder = Math.Min(shape.Shoulder, inset) };
    }

    // A notch on a side edge: the readout stays one horizontal row, as at the top; the notch's depth grows to its width and its
    // length to the row's height, then both go back.
    private void RenderHudColumn(HudMessage hud, double scale, double dpi, double column, double room)
    {
        if (hud.Text.Length == 0) { RenderHudVertical(hud, scale, dpi, column, room); return; }
        double pad = Math.Round(28 * scale);
        var (parts, natural) = BuildHudRow(hud, scale, dpi, Math.Round(360 * scale));
        double row = Math.Max(1, CompactMusicLayout.Whole(26 * scale));
        double wantedColumn = CompactColumn.Width(scale, [natural + pad], []);
        // As thin along the edge as the notch's shoulders allow: the one line of text and nothing more.
        double line = Math.Max(Math.Round(14 * scale), Math.Max(1, CompactMusicLayout.Whole(16 * scale)));
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
        double icon = Math.Round(14 * scale), gap = Math.Round(8 * scale), thickness = Math.Max(2, Math.Round(4 * scale)), length = Math.Round(56 * scale);
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
