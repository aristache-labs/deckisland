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

    // False when this panel cannot show it in place (open, hidden, a line or a side-edge column); the caller then floats it.
    internal bool ShowHudInline(HudMessage message)
    {
        if (_expanded || _preview || !IsVisible || IsSuppressed || DisplayPower.Off || _poweringOff || CompactColumnMode || EffectiveStyle == CompactStyle.Line) return false;
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

    private void RenderCompactHud(HudMessage hud, double scale, double contentWidth)
    {
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        double font = Math.Max(1, CompactMusicLayout.Half(11.5 * scale)), line = Math.Max(1, CompactMusicLayout.Whole(16 * scale));
        double gap = Math.Round(8 * scale), icon = Math.Round(14 * scale);
        var parts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        parts.Children.Add(new Path
        {
            Data = HudWindow.IconGeometry(hud.Icon), Stroke = UI.Brush(Look.Primary), StrokeThickness = Math.Max(1, 1.4 * scale), Stretch = System.Windows.Media.Stretch.Uniform,
            Width = icon, Height = icon, StrokeLineJoin = System.Windows.Media.PenLineJoin.Round, StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, gap, 0)
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
        double asked = natural + Math.Round(28 * scale);
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
}
