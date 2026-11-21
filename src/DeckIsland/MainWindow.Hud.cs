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
        if (_expanded || _preview || !IsVisible || IsSuppressed || DisplayPower.Off || _poweringOff || EffectiveStyle == CompactStyle.Line) return false;
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
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, gap, 0)
        });
        var text = UI.Fixed(hud.Text, font, line, Look.Primary, FontWeights.SemiBold);
        text.MaxWidth = Math.Round(260 * scale); text.TextTrimming = TextTrimming.CharacterEllipsis; text.ToolTip = hud.Text;
        parts.Children.Add(text);
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

    // A notch on a side edge: the readout stays one horizontal row, as at the top; the notch's depth grows to its width and its
    // length to the row's height, then both go back.
    private void RenderHudColumn(HudMessage hud, double scale, double dpi, double column, double room)
    {
        double pad = Math.Round(28 * scale);
        var (parts, natural) = BuildHudRow(hud, scale, dpi, Math.Round(360 * scale));
        double row = Math.Max(1, CompactMusicLayout.Whole(26 * scale));
        double wantedColumn = CompactColumn.Width(scale, [natural + pad], []);
        double wanted = DevicePixels.Up(row + Math.Round(10 * scale), dpi);
        if (double.IsNaN(_compactWanted) || Math.Abs(wanted - _compactWanted) > .5 || Math.Abs(row - _compactRequired) > .5 || double.IsNaN(_compactColumn) || Math.Abs(wantedColumn - _compactColumn) > .5)
        {
            _compactWanted = wanted; _compactRequired = row; _compactColumn = wantedColumn;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitCompactToContent);
        }
        parts.HorizontalAlignment = HorizontalAlignment.Center; parts.VerticalAlignment = VerticalAlignment.Center;
        var host = new Grid { Width = column, Height = room, ClipToBounds = true };
        host.Children.Add(parts);
        CompactRings.Children.Add(host);
        UpdateMusicTimer();
        _stripShowing = false; UpdateMonitorDemand();
    }
}
