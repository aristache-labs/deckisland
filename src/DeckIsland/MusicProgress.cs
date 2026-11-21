using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

// The music card's progress bar, drawn in the style the settings choose (DeckSettings.MusicProgress): Wave ripples while the song plays and
// lies flat when it is paused; Pixels shimmer behind a glowing head; Comet pulses and sheds sparks; Song shape draws the song as bars; Simple
// is a plain line; Hearts fill one by one. It only draws: the seek slider over it takes the pointer and the keys, and with the slider's own
// bar hidden this is what the bar looks like. The animated styles redraw about thirty times a second, and only while the song plays.
internal sealed class MusicProgress : FrameworkElement
{
    private readonly Slider _slider;
    private readonly DispatcherTimer _frame;
    private readonly long _started = Environment.TickCount64;
    private readonly int _seed;
    private double _calm = 1;
    private long _calmAt = Environment.TickCount64;

    public MusicProgress(Slider slider, MusicProgressStyle style, Color accent, Color track, bool playing, string song)
    {
        _slider = slider; Shape = style; Accent = accent; Track = track; Playing = playing;
        _seed = song.GetHashCode();
        IsHitTestVisible = false;
        _calm = playing ? 1 : 0;
        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _frame.Tick += (_, _) => { Step(); InvalidateVisual(); };
        slider.ValueChanged += (_, _) => InvalidateVisual();
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => _frame.Stop();
    }

    public MusicProgressStyle Shape { get; }
    public Color Accent { get; }
    public Color Track { get; }
    private bool _playing;
    public bool Playing { get => _playing; set { _playing = value; _calmAt = Environment.TickCount64; if (IsLoaded) Restart(); } }

    private bool Animated => Shape is MusicProgressStyle.Wave or MusicProgressStyle.Pixels or MusicProgressStyle.Comet or MusicProgressStyle.SongShape or MusicProgressStyle.Hearts;

    private void Restart()
    {
        // A paused wave settles to a line over a moment, so the timer runs until it has; the other styles stop at once.
        if (Animated && (Playing || _calm > .001)) _frame.Start(); else _frame.Stop();
        InvalidateVisual();
    }

    private void Step()
    {
        double target = Playing ? 1 : 0, seconds = Math.Max(0, (Environment.TickCount64 - _calmAt) / 1000d);
        _calm = target + (_calm - target) * Math.Exp(-seconds * 6);
        _calmAt = Environment.TickCount64;
        if (!Playing && _calm < .001) { _calm = 0; _frame.Stop(); }
    }

    private double Seconds => (Environment.TickCount64 - _started) / 1000d;
    private double Fraction => Math.Clamp(_slider.Maximum > _slider.Minimum ? (_slider.Value - _slider.Minimum) / (_slider.Maximum - _slider.Minimum) : 0, 0, 1);

    private static Brush Solid(Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    // A stable pseudo-random number in [0, 1) for a bar or a spark, so the same song always draws the same shape.
    private double Noise(int index, int salt = 0)
    {
        unchecked
        {
            uint h = (uint)(_seed * 73856093) ^ (uint)(index * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 8 || h < 2) return;
        switch (Shape)
        {
            case MusicProgressStyle.Wave: Wave(dc, w, h); break;
            case MusicProgressStyle.Pixels: Pixels(dc, w, h); break;
            case MusicProgressStyle.Comet: Comet(dc, w, h); break;
            case MusicProgressStyle.SongShape: SongShape(dc, w, h); break;
            case MusicProgressStyle.Hearts: Hearts(dc, w, h); break;
            default: Plain(dc, w, h); break;
        }
    }

    private void Plain(DrawingContext dc, double w, double h)
    {
        double y = h / 2, thickness = 3, head = Fraction * w;
        dc.DrawRoundedRectangle(Solid(Track), null, new Rect(0, y - thickness / 2, w, thickness), thickness / 2, thickness / 2);
        if (head > 0) dc.DrawRoundedRectangle(Solid(Accent), null, new Rect(0, y - thickness / 2, head, thickness), thickness / 2, thickness / 2);
    }

    // A sine that ripples from the head backwards; its height follows how much the song is playing, so a pause lays it flat.
    private void Wave(DrawingContext dc, double w, double h)
    {
        double y = h / 2, head = Fraction * w, amplitude = Math.Min(h * .2, 3) * _calm, phase = Seconds * 2.2;
        var rest = new Pen(Solid(Track), 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(rest, new Point(Math.Min(w, head + 3), y), new Point(w, y));
        if (head > 1)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(0, y + Math.Sin(-phase) * amplitude), false, false);
                for (double x = 2; x <= head; x += 2)
                    context.LineTo(new Point(x, y + Math.Sin(x / 6.2 - phase) * amplitude), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(Solid(Accent), 2.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        }
        dc.DrawRoundedRectangle(Solid(Accent), null, new Rect(head - 1.5, y - 6, 3, 12), 1.5, 1.5);
    }

    // Small squares along the line: the played ones shimmer, the rest are dim, and the head is a brighter, larger square with a glow.
    private void Pixels(DrawingContext dc, double w, double h)
    {
        const double cell = 3, gap = 2.2;
        double step = cell + gap, head = Fraction * w, y = Math.Round(h / 2 - cell / 2);
        int count = (int)(w / step);
        for (int i = 0; i < count; i++)
        {
            double x = i * step;
            if (x > head)
            {
                dc.DrawRectangle(Solid(Track, .75), null, new Rect(x, y, cell, cell));
                continue;
            }
            double shimmer = .55 + .45 * Math.Sin(Seconds * 4 + i * .55) * _calm + .45 * (1 - _calm);
            dc.DrawRectangle(Solid(Accent, shimmer), null, new Rect(x, y, cell, cell));
        }
        double pulse = 1 + .25 * Math.Sin(Seconds * 6) * _calm;
        dc.DrawEllipse(Solid(Accent, .25), null, new Point(head, h / 2), 7 * pulse, 7 * pulse);
        dc.DrawRectangle(Solid(Accent), null, new Rect(head - 2.5, h / 2 - 4.5, 5, 9));
    }

    // A thin line with a head that pulses and sheds sparks behind it, which fade as they drift back along the line.
    private void Comet(DrawingContext dc, double w, double h)
    {
        double y = h / 2, head = Fraction * w, pulse = 1 + .3 * Math.Sin(Seconds * 5) * _calm;
        dc.DrawRoundedRectangle(Solid(Track), null, new Rect(0, y - 1, w, 2), 1, 1);
        if (head > 0) dc.DrawRoundedRectangle(Solid(Accent, .7), null, new Rect(0, y - 1, head, 2), 1, 1);
        if (_calm > .05)
            for (int i = 0; i < 9; i++)
            {
                double life = (Seconds * (.9 + Noise(i) * .8) + Noise(i, 1)) % 1;
                double x = head - life * (10 + 22 * Noise(i, 2)), lift = (Noise(i, 3) - .5) * 2 * (3 + 7 * life);
                if (x < 0) continue;
                dc.DrawEllipse(Solid(Accent, (1 - life) * .85 * _calm), null, new Point(x, y + lift), 1.1 + (1 - life), 1.1 + (1 - life));
            }
        dc.DrawEllipse(Solid(Accent, .22), null, new Point(head, y), 8 * pulse, 8 * pulse);
        dc.DrawEllipse(Solid(Accent), null, new Point(head, y), 3.2 * pulse, 3.2 * pulse);
    }

    // The song as fine bars (no real waveform is read: a shape drawn from the song's name, the same each time), the played ones in the accent.
    // While it plays the bars near the head rise and fall, and the movement fades out along the played part.
    private void SongShape(DrawingContext dc, double w, double h)
    {
        const double bar = 1.7, gap = 1.4;
        double step = bar + gap, head = Fraction * w;
        int count = (int)(w / step);
        for (int i = 0; i < count; i++)
        {
            double x = i * step, envelope = .5 + .5 * Math.Sin(Math.PI * (.1 + .8 * i / Math.Max(1, count - 1)));
            double height = h * (.16 + .74 * Noise(i) * envelope);
            double near = Math.Exp(-Math.Abs(x - head) / 70);
            height *= 1 + .3 * Math.Sin(Seconds * 5 + i * .7) * _calm * (.25 + .75 * near);
            height = Math.Clamp(Math.Max(2.5, height), 2.5, h);
            dc.DrawRoundedRectangle(Solid(x <= head ? Accent : Track, x <= head ? 1 : .8), null, new Rect(x, (h - height) / 2, bar, height), .85, .85);
        }
    }

    // Small hearts in a row: the played ones filled in the accent and beating in turn, the one being played larger and lighter, and the rest
    // only outlined. The beat is gentle and stops, with the others' movement, when the song is paused.
    private void Hearts(DrawingContext dc, double w, double h)
    {
        double size = Math.Min(h - 2, 9), step = size + 3.6;
        int count = Math.Max(1, (int)((w + 3.6) / step));
        double filled = Fraction * count, left = (w - (count * step - 3.6)) / 2;
        int current = (int)Math.Min(count - 1, Math.Floor(filled));
        var outline = new Pen(Solid(Track, .95), 1);
        var light = Color.FromRgb((byte)(Accent.R + (255 - Accent.R) * .75), (byte)(Accent.G + (255 - Accent.G) * .75), (byte)(Accent.B + (255 - Accent.B) * .75));
        for (int i = 0; i < count; i++)
        {
            double cx = left + i * step + size / 2, cy = h / 2;
            double beat = 1 + .13 * Math.Sin(Seconds * 3.4 - i * .55) * _calm;
            if (i < current)
            {
                dc.DrawGeometry(Solid(Accent), null, Heart(cx, cy, size * beat));
            }
            else if (i == current && filled > 0)
            {
                double pulse = 1.28 + .16 * Math.Sin(Seconds * 5.2) * _calm;
                dc.DrawGeometry(Solid(light), null, Heart(cx, cy, size * pulse));
            }
            else dc.DrawGeometry(null, outline, Heart(cx, cy, size));
        }
    }

    // A heart of the given size centred on a point.
    private static Geometry Heart(double cx, double cy, double size)
    {
        double s = size / 10;
        var shaped = Geometry.Parse("M5,9.2 C-1.5,5 .3,0 3,0 C4.2,0 5,.9 5,1.8 C5,.9 5.8,0 7,0 C9.7,0 11.5,5 5,9.2 Z").Clone();
        shaped.Transform = new TransformGroup { Children = { new ScaleTransform(s, s), new TranslateTransform(cx - 5 * s, cy - 4.6 * s) } };
        shaped.Freeze();
        return shaped;
    }

    protected override Size MeasureOverride(Size available) => new(double.IsInfinity(available.Width) ? 0 : available.Width, double.IsInfinity(available.Height) ? 0 : available.Height);
}
