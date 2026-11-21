using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BrimDeck;

public partial class MainWindow
{
    // Cloud geometry, gradients and rain timing from rainy icon.svg, converted to WPF drawing coordinates.
    private static readonly (double X, double Y, double Length, double Start, double Peak, double End)[] RainPulses =
    [
        (108.875, 167.5, 42.5, 0, 0.15, 0.416667),
        (108.875, 167.5, 21.5, 0.25, 0.4, 0.666667),
        (108.875, 167.5, 42.5, 0.5, 0.65, 0.916667),
        (153.875, 167.5, 27.5, 0.216667, 0.366667, 0.633333),
        (153.875, 167.5, 30.5, 0.5, 0.608, 0.8),
        (153.875, 167.5, 42.5, 0.566667, 0.716667, 0.983333),
        (198.875, 167.5, 25.5, 0, 0.15, 0.416667),
        (218.875, 167.5, 42.5, 0.216017, 0.3, 0.449349),
        (198.875, 167.5, 42.5, 0.4, 0.514, 0.716667),
        (128.875, 167.5, 17.5, 0.216017, 0.3, 0.449349),
        (238.875, 167.5, 42.5, 0, 0.15, 0.416666),
        (238.875, 167.5, 42.5, 0.55, 0.7, 0.966667),
        (173.875, 182.5, 17.5, 0.416667, 0.50065, 0.65),
        (163.875, 187.5, 42.5, 0.533333, 0.683333, 0.95),
        (218.875, 167.5, 42.5, 0.3, 0.383983, 0.533333),
    ];

    private static Viewbox ColourfulRainIcon(double size, bool animate)
    {
        var drawing = new Canvas { Width = 300, Height = 300, ClipToBounds = true };
        foreach (var (x, y, length, start, peak, end) in RainPulses)
        {
            const double stillFrame = .38;
            if (!animate && (stillFrame <= start || stillFrame >= end)) continue;
            var shift = new TranslateTransform();
            var drop = new Line
            {
                X1 = x + length * .363 / 2, X2 = x - length * .363 / 2,
                Y1 = y - length * .932 / 2, Y2 = y + length * .932 / 2,
                Stroke = UI.Brush("#7FD3F5"), StrokeThickness = 11,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = shift, Opacity = animate ? 0 : 1
            };
            drawing.Children.Add(drop);
            if (animate)
            {
                var fade = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(1), RepeatBehavior = RepeatBehavior.Forever,
                    KeyFrames =
                    {
                        new LinearDoubleKeyFrame(0, KeyTime.FromPercent(start)),
                        new LinearDoubleKeyFrame(1, KeyTime.FromPercent(peak)),
                        new LinearDoubleKeyFrame(0, KeyTime.FromPercent(end))
                    }
                };
                var left = RainTravel(start, end, -153);
                var down = RainTravel(start, end, 380);
                drop.IsVisibleChanged += (_, _) =>
                {
                    shift.BeginAnimation(TranslateTransform.XProperty, drop.IsVisible ? left : null);
                    shift.BeginAnimation(TranslateTransform.YProperty, drop.IsVisible ? down : null);
                    drop.BeginAnimation(OpacityProperty, drop.IsVisible ? fade : null);
                };
            }
            else
            {
                shift.X = -153 * (stillFrame - start);
                shift.Y = 380 * (stillFrame - start);
            }
        }

        Cloud("M42.8,-10.369C42.018,-10.777,41.524,-11.58,41.524,-12.462C41.524,-12.462,41.524,-12.477,41.524,-12.477C41.524,-29.111,28.038,-42.596,11.404,-42.596C-2.546,-42.596,-14.274,-33.111,-17.697,-20.239C-18.753,-16.268,-22.415,-13.549,-26.524,-13.549C-26.524,-13.549,-29.827,-13.549,-29.827,-13.549C-45.331,-13.549,-57.899,-0.981,-57.899,14.523C-57.899,14.523,-57.899,14.524,-57.899,14.524C-57.899,30.028,-45.331,42.596,-29.827,42.596C-29.827,42.596,29.827,42.596,29.827,42.596C45.331,42.596,57.899,30.028,57.899,14.524C57.899,14.524,57.899,14.523,57.899,14.523C57.899,3.701,51.772,-5.684,42.8,-10.369Z", "M0 0 C0.5 -0.417 3.333 -2.667 3 -2.5 C3.667 -3.5 3.917 -6.25 4 -6 C3.667 -5.833 0.833 -1.583 1 -1.5 C1.083 -1.25 4.917 -5.083 4.5 -4.5 C4.667 -4.417 2 -1 2 -1", 1.35, 100.686, 104.504, -58.011, 57.787, -.723);
        Cloud("M32.655,-18.466C32.655,-18.466,32.655,-18.466,32.655,-18.466C29.932,-18.466,27.453,-20.097,26.442,-22.625C21.64,-34.629,9.909,-43.112,-3.81,-43.112C-16.242,-43.112,-27.042,-36.147,-32.532,-25.907C-34.474,-22.285,-39.077,-18.075,-42.95,-16.701C-54.892,-12.465,-63.444,-1.071,-63.444,12.323C-63.444,12.323,-63.444,12.323,-63.444,12.323C-63.444,29.327,-49.659,43.112,-32.655,43.112C-32.655,43.112,32.655,43.112,32.655,43.112C49.659,43.112,63.444,29.327,63.444,12.323C63.444,12.323,63.444,12.323,63.444,12.323C63.444,-4.681,49.659,-18.466,32.655,-18.466Z", "M0 0 C0.5 -0.583 2.833 -3.833 3 -3.5 C3.083 -3.167 0.417 2.083 0.5 2 C0.667 2.333 4.167 -1.583 4 -1.5 C4.083 -1.583 0.667 1.917 1 1.5 C0.833 1.583 3 -1 3 -1", 1.76, 162.734, 121.183, -63.64, 63.249, -.602);
        return new Viewbox { Child = drawing, Width = size, Height = size, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };

        void Cloud(string data, string motion, double scale, double x, double y, double gradientStart, double gradientEnd, double gradientY)
        {
            var sway = new TranslateTransform();
            var cloud = new Path
            {
                Data = Geometry.Parse(data),
                Fill = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = new Point(gradientStart, gradientY), EndPoint = new Point(gradientEnd, gradientY),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(37, 94, 150), 0),
                        new GradientStop(Color.FromRgb(82, 163, 202), .5),
                        new GradientStop(Color.FromRgb(127, 232, 255), 1)
                    }
                },
                RenderTransform = new TransformGroup
                {
                    Children = { new ScaleTransform(-scale, scale), new TranslateTransform(x, y), sway }
                }
            };
            drawing.Children.Add(cloud);
            if (!animate) return;
            var route = PathGeometry.CreateFromGeometry(Geometry.Parse(motion));
            var horizontal = CloudSway(route, PathAnimationSource.X);
            var vertical = CloudSway(route, PathAnimationSource.Y);
            cloud.IsVisibleChanged += (_, _) =>
            {
                sway.BeginAnimation(TranslateTransform.XProperty, cloud.IsVisible ? horizontal : null);
                sway.BeginAnimation(TranslateTransform.YProperty, cloud.IsVisible ? vertical : null);
            };
        }
    }

    private static DoubleAnimationUsingKeyFrames RainTravel(double start, double end, double speed) => new()
    {
        Duration = TimeSpan.FromSeconds(1), RepeatBehavior = RepeatBehavior.Forever,
        KeyFrames =
        {
            new LinearDoubleKeyFrame(0, KeyTime.FromPercent(start)),
            new LinearDoubleKeyFrame(speed * (end - start), KeyTime.FromPercent(end))
        }
    };

    private static DoubleAnimationUsingPath CloudSway(PathGeometry route, PathAnimationSource axis) => new()
    {
        PathGeometry = route, Source = axis, Duration = TimeSpan.FromSeconds(1), RepeatBehavior = RepeatBehavior.Forever
    };
}
