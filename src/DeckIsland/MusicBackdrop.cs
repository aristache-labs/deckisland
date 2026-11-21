using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeckIsland.Core;

namespace DeckIsland;

// Behind the music card: the cover's colours as soft streaks (radial washes that fade to nothing), or nothing at all when the island's plain
// surface is chosen. The streaks are drawn once per song and sit under the page, which draws on top of them.
internal sealed class MusicBackdrop : FrameworkElement
{
    private Color[] _colors = [];

    public MusicBackdrop() { IsHitTestVisible = false; }

    public void SetColors(Color[] colors)
    {
        _colors = colors;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (_colors.Length == 0 || w < 20 || h < 20) return;
        // Washes of the cover's colours: the strongest two over the upper part, behind the titles, and the cover's average, muted, low down.
        // They are kept soft, and a scrim darkens the lower part, so the transport and the progress bar stay easy to read over them.
        Color vivid = _colors[0], second = _colors.Length > 2 ? _colors[1] : vivid, average = _colors[^1];
        (Color Color, double X, double Y, double Reach, double Strength)[] washes =
            [(vivid, .12, .12, .8, .42), (second, .95, .1, .7, .3), (average, .5, .95, .85, .26)];
        foreach (var wash in washes)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(255 * wash.Strength), wash.Color.R, wash.Color.G, wash.Color.B), 0),
                    new GradientStop(Color.FromArgb((byte)(255 * wash.Strength * .4), wash.Color.R, wash.Color.G, wash.Color.B), .55),
                    new GradientStop(Color.FromArgb(0, wash.Color.R, wash.Color.G, wash.Color.B), 1)
                }
            };
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(w * wash.X, h * wash.Y), w * wash.Reach, h * wash.Reach * 1.2);
        }
        var surface = (Color)ColorConverter.ConvertFromString(PanelLook.Of(this).Hex("surface"));
        var scrim = new LinearGradientBrush(Color.FromArgb(0, surface.R, surface.G, surface.B), Color.FromArgb(150, surface.R, surface.G, surface.B), 90)
        {
            GradientStops = { new GradientStop(Color.FromArgb(0, surface.R, surface.G, surface.B), 0), new GradientStop(Color.FromArgb(0, surface.R, surface.G, surface.B), .3),
                new GradientStop(Color.FromArgb(150, surface.R, surface.G, surface.B), 1) }
        };
        scrim.Freeze();
        dc.DrawRectangle(scrim, null, new Rect(0, 0, w, h));
    }

    // The cover's strongest colours, most prominent first: its pixels, scaled down, are grouped by hue, and each group weighs by how many
    // pixels it has and how vivid and light they are, so a grey border does not outweigh the picture.
    public static Color[] Palette(BitmapSource? cover, int count = 3)
    {
        if (cover is null || cover.PixelWidth < 2 || cover.PixelHeight < 2) return [];
        try
        {
            const int side = 24;
            var scaled = new TransformedBitmap(cover, new ScaleTransform((double)side / cover.PixelWidth, (double)side / cover.PixelHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            var groups = new (double Weight, double R, double G, double B)[12];
            double sumR = 0, sumG = 0, sumB = 0, seen = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                double b = pixels[i] / 255d, g = pixels[i + 1] / 255d, r = pixels[i + 2] / 255d, a = pixels[i + 3] / 255d;
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), chroma = max - min;
                if (a >= .5) { sumR += r; sumG += g; sumB += b; seen++; }
                if (a < .5 || max < .12 || chroma < .08) continue;
                double hue = max == r ? ((g - b) / chroma + 6) % 6 : max == g ? (b - r) / chroma + 2 : (r - g) / chroma + 4;
                int bucket = (int)(hue * 2) % 12;
                double weight = chroma * (.4 + max);
                var group = groups[bucket];
                groups[bucket] = (group.Weight + weight, group.R + r * weight, group.G + g * weight, group.B + b * weight);
            }
            // The vivid colours first, the average of the whole cover last (the washes at the lower part take that one).
            var vivid = groups.Where(group => group.Weight > 0).OrderByDescending(group => group.Weight).Take(2)
                .Select(group => Color.FromRgb((byte)Math.Round(group.R / group.Weight * 255), (byte)Math.Round(group.G / group.Weight * 255), (byte)Math.Round(group.B / group.Weight * 255))).ToList();
            if (seen > 0) vivid.Add(Color.FromRgb((byte)Math.Round(sumR / seen * 255), (byte)Math.Round(sumG / seen * 255), (byte)Math.Round(sumB / seen * 255)));
            return [.. vivid];
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { return []; }
    }
}
