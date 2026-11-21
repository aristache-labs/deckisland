using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// The icon of a readout. Most are a line drawing (HudWindow.IconGeometry); the battery is a battery that holds its charge: an outline with
// a bolt in the charging green, or an outline filled to the charge.
internal static class HudGlyph
{
    private const string Charging = "#30D158";

    public static FrameworkElement Create(HudMessage hud, PanelLook look, double size, double scale)
    {
        if (hud.Icon is HudIcon.Battery or HudIcon.Charging) return Battery(hud, look, size, scale);
        // Finished and canceled: a filled disc with the mark cut out of it in the surface's color.
        if (hud.Icon is HudIcon.Done or HudIcon.Cancel)
        {
            var disc = new Grid { Width = size, Height = size };
            disc.Children.Add(new Ellipse { Fill = UI.Brush(hud.Icon == HudIcon.Done ? look.Hex("accent") : look.Hex("danger")) });
            disc.Children.Add(new Path
            {
                Data = HudWindow.IconGeometry(hud.Icon), Stroke = UI.Brush(look.Hex("surface")), StrokeThickness = Math.Max(1.4, 1.6 * scale), Stretch = Stretch.Uniform,
                Width = size * .52, Height = size * .52, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            return disc;
        }
        return new Path
        {
            Data = HudWindow.IconGeometry(hud.Icon), Stroke = UI.Brush(hud.Lit ? look.Hex("accent") : look.Primary), StrokeThickness = Math.Max(1, 1.4 * scale),
            Stretch = Stretch.Uniform, Width = size, Height = size, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
    }

    private static FrameworkElement Battery(HudMessage hud, PanelLook look, double size, double scale)
    {
        bool charging = hud.Icon == HudIcon.Charging;
        double width = Math.Round(size * 1.55), height = Math.Round(size * .8), nub = Math.Max(2, Math.Round(2 * scale)), line = Math.Max(1, 1.4 * scale);
        double level = Math.Clamp(hud.IconLevel ?? 1, 0, 1);
        var ink = UI.Brush(charging ? Charging : look.Primary);
        double body = width - nub;
        var grid = new Grid { Width = width, Height = height };
        grid.Children.Add(new Border
        {
            Width = body, Height = height, HorizontalAlignment = HorizontalAlignment.Left, BorderBrush = ink, BorderThickness = new Thickness(line),
            CornerRadius = new CornerRadius(Math.Max(2, height * .28))
        });
        grid.Children.Add(new Border
        {
            Width = nub, Height = Math.Round(height * .45), HorizontalAlignment = HorizontalAlignment.Right, Background = ink,
            CornerRadius = new CornerRadius(0, nub / 2, nub / 2, 0)
        });
        if (charging)
        {
            grid.Children.Add(new Path
            {
                Data = Geometry.Parse("M6,0 L1.5,5.6 H5 L4,10 L8.5,4 H5 Z"), Fill = ink, Stretch = Stretch.Uniform, Height = height - 2 * line - 1,
                Width = (height - 2 * line - 1) * .85, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness((body - (height - 2 * line - 1) * .85) / 2, 0, 0, 0)
            });
        }
        else
        {
            double inset = line + Math.Max(1, scale);
            double room = body - 2 * inset;
            grid.Children.Add(new Border
            {
                Width = Math.Max(1, Math.Round(room * level)), Height = height - 2 * inset, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(inset, 0, 0, 0),
                Background = UI.Brush(level <= .2 ? look.Hex("danger") : look.Primary), CornerRadius = new CornerRadius(Math.Max(1, height * .16))
            });
        }
        return grid;
    }
}
