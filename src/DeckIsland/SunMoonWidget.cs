using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private const double MoonSize = 38;

    // Sunrise and sunset where the weather is read, and tonight's moon with the name of its phase. The times are worked out on the
    // PC (SunMoon) and shown in its time format and zone; without a place in Weather they are dashes. Redrawn when the place, the
    // day, the format or the moon's look changes.
    private Border SunMoonWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        frame.Padding = new Thickness(WidgetSide, 8, WidgetSide, 8);
        string? drawn = null;
        _widgetUpdates.Add(() =>
        {
            var now = DateTime.Now;
            var place = _app.CurrentWeather?.Place;
            var phase = SunMoon.Phase(now.ToUniversalTime());
            string key = $"{DateOnly.FromDateTime(now)}:{place?.Latitude}:{place?.Longitude}:{Settings.TimePattern}:{(int)(phase.Cycle * 200)}:{Loc.Language}:{Look.Generation}";
            if (key == drawn && frame.Child is not null) return;
            drawn = key;
            frame.Child = SunMoonContent(now, place, phase);
        });
        return frame;
    }

    private FrameworkElement SunMoonContent(DateTime now, WeatherPoint? place, MoonPhase phase)
    {
        (DateTime Rise, DateTime Set)? sun = place is null ? null : SunMoon.Sun(DateOnly.FromDateTime(now), place.Latitude, place.Longitude);
        string Clock(DateTime? utc) => utc is { } value ? value.ToLocalTime().ToString(Settings.TimePattern, Loc.Culture) : "—";
        string rise = Clock(sun?.Rise), set = Clock(sun?.Set);

        FrameworkElement Line(string geometry, string time)
        {
            var icon = new Path
            {
                Data = Geometry.Parse(geometry), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Width = 18, Height = 18, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
            };
            var text = WeatherText(time, 17);
            text.FontWeight = FontWeights.SemiBold;
            text.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4), Children = { icon, text } };
        }
        var times = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children =
        {
            Line("M 1,15 H 17 M 4.5,15 A 4.5,4.5 0 0 1 13.5,15 M 9,5 V 1.5 M 7,3.3 L 9,1.2 L 11,3.3", rise),
            Line("M 1,15 H 17 M 4.5,15 A 4.5,4.5 0 0 1 13.5,15 M 9,1.5 V 5 M 7,3.2 L 9,5.2 L 11,3.2", set)
        } };

        var moon = Moon(phase, MoonSize, UI.Brush(Look.Primary), UI.Brush(Look.Hex("track")));
        moon.HorizontalAlignment = HorizontalAlignment.Right;
        var name = WeatherText(phase.Name, 12);
        name.Foreground = UI.Brush(Look.Secondary);
        name.HorizontalAlignment = HorizontalAlignment.Right;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 4, 0, 0);
        var right = new DockPanel { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(name, System.Windows.Controls.Dock.Bottom);
        right.Children.Add(name); right.Children.Add(moon);
        // The phase in words, for anyone who wonders what "waxing gibbous" is.
        right.ToolTip = phase.Detail;

        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 1);
        grid.Children.Add(times); grid.Children.Add(right);
        AutomationProperties.SetName(grid, $"{WidgetLayout.Name(WidgetKind.SunMoon)} · {Loc.T("日出", "Sunrise")} {rise} · {Loc.T("日落", "Sunset")} {set} · {phase.Detail}");
        return grid;
    }

    // The moon as a disc: its dark side, and over it the lit part. The lit part's edge on the sun's side is the disc's own rim; its
    // other edge is the terminator, half an ellipse as wide as the cosine of the phase says, bulging toward the lit side for a
    // crescent and toward the dark side for a gibbous. Drawn for a waxing moon (lit on the right, as in the northern sky) and
    // mirrored while it wanes.
    internal static FrameworkElement Moon(MoonPhase phase, double size, Brush lit, Brush dark)
    {
        double r = size / 2, cos = Math.Cos(2 * Math.PI * phase.Cycle), rx = Math.Max(r * Math.Abs(cos), .01);
        var figure = new PathFigure { StartPoint = new Point(r, 0), IsClosed = true };
        figure.Segments.Add(new ArcSegment(new Point(r, size), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new ArcSegment(new Point(r, 0), new Size(rx, r), 0, false, cos > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
        var shape = new Path { Data = new PathGeometry([figure]), Fill = lit, Width = size, Height = size };
        if (!phase.Waxing) { shape.RenderTransformOrigin = new Point(.5, .5); shape.RenderTransform = new ScaleTransform(-1, 1); }
        var disc = new Ellipse { Width = size, Height = size, Fill = dark };
        return new Grid { Width = size, Height = size, Children = { disc, shape } };
    }
}
