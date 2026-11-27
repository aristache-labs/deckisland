using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DeckIsland.Core;

namespace DeckIsland;

internal sealed partial class DockWindow
{
    private readonly Border _dropMarker = new()
    {
        IsHitTestVisible = false, Width = 3, CornerRadius = new CornerRadius(1.5),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Visibility = Visibility.Collapsed
    };
    private Dictionary<string, Point>? _dropOrigins;

    private readonly record struct WidgetDrop(DockWidgetPlacement Placement, string? BeforeApp, string? BeforeWidget, double X);

    private void MoveWidgetDrag(MouseEventArgs e)
    {
        if (_press is not { Tile.Tag: DockWidget } press || _app.DockSettings.Locked) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndWidgetDrag(press.Tile); return; }
        var at = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(at.X - press.Start.X) < DragThreshold && Math.Abs(at.Y - press.Start.Y) < DragThreshold) return;
            if (!press.Tile.IsMouseCaptured && !press.Tile.CaptureMouse()) { _press = null; return; }
            _dragging = true;
            _preview.HideNow();
            Panel.SetZIndex(press.Tile, 1);
            if (press.Tile.Parent is UIElement parent) Panel.SetZIndex(parent, 1);
            press.Tile.Opacity = 0.78;
            var offset = TileShift(press.Tile);
            offset.BeginAnimation(TranslateTransform.XProperty, null);
            offset.BeginAnimation(TranslateTransform.YProperty, null);
            _dropMarker.Background = UI.Brush(Accent);
        }
        var shift = TileShift(press.Tile);
        shift.X = at.X - press.Start.X;
        shift.Y = at.Y - press.Start.Y;
        var target = DropAt(press.Tile, at);
        _dropMarker.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        if (target is { } drop)
        {
            var bar = BoundsOf(_bar);
            _dropMarker.Height = Math.Max(10, _bar.ActualHeight - 14);
            _dropMarker.Margin = new Thickness(Math.Clamp(drop.X - 1.5, 0, Math.Max(0, ActualWidth - 3)), bar.Top + 7, 0, 0);
        }
        e.Handled = true;
    }

    private void EndWidgetDrag(Border frame)
    {
        _press = null;
        _dragging = false;
        frame.Opacity = 1;
        if (frame.RenderTransform is TransformGroup transforms && transforms.Children.LastOrDefault() is TranslateTransform shift)
            shift.X = shift.Y = 0;
        Panel.SetZIndex(frame, 0);
        if (frame.Parent is UIElement parent) Panel.SetZIndex(parent, 0);
        _dropMarker.Visibility = Visibility.Collapsed;
        if (frame.IsMouseCaptured) frame.ReleaseMouseCapture();
    }

    private void ReleaseWidget(Border frame, DockWidget widget, Point at)
    {
        var drop = DropAt(frame, at);
        var origins = TileOrigins();
        EndWidgetDrag(frame);
        if (drop is not { } target) { AnimateTilePositions(origins); return; }
        var settings = _app.DockSettings;
        var moved = DockWidgets.Drop(settings.Widgets, widget.Id, target.Placement, target.BeforeApp, target.BeforeWidget);
        if (moved.SequenceEqual(settings.Widgets)) { AnimateTilePositions(origins); return; }
        _dropOrigins = origins;
        _app.UpdateDockSettings(settings.WithWidgets(moved));
    }

    private WidgetDrop? DropAt(Border dragged, Point at)
    {
        var bar = BoundsOf(_bar);
        bar.Inflate(_tileSize / 2, _tileSize / 2);
        if (!bar.Contains(at)) return null;
        var end = BoundsOf(_trailing);
        double rightStart = end.Width > 0 ? end.Left - _tileSize / 3
            : _app.DockSettings.FullWidth ? bar.Right - _tileSize : BoundsOf(_apps).Right + _spacing / 2;
        if (at.X >= rightStart)
        {
            var next = _trailing.Children.OfType<Border>().FirstOrDefault(tile => tile != dragged && at.X < Midpoint(BoundsOf(tile)));
            return new(DockWidgetPlacement.Right, null, (next?.Tag as DockWidget)?.Id, next is null ? end.Right : BoundsOf(next).Left);
        }

        var following = _apps.Children.OfType<Border>().FirstOrDefault(tile => tile != dragged && (tile.Tag is DockWidget or DockApp)
            && at.X < Midpoint(BoundsOf(tile)));
        var nextWidget = following?.Tag as DockWidget;
        string? app = following?.Tag is DockApp nextApp ? nextApp.Key : nextWidget?.BeforeApp;
        return new(DockWidgetPlacement.MainBar, app, nextWidget?.Id, following is null ? BoundsOf(_apps).Right : BoundsOf(following).Left);
    }

    private Rect BoundsOf(FrameworkElement element) => new(element.TranslatePoint(new Point(), this), element.RenderSize);
    private static double Midpoint(Rect bounds) => bounds.Left + bounds.Width / 2;

    private IEnumerable<Border> PositionedTiles() => _apps.Children.OfType<Border>().Concat(_trailing.Children.OfType<Border>())
        .Where(tile => tile.Tag is DockWidget or DockApp);

    private static string TileKey(Border tile) => tile.Tag is DockWidget widget ? "widget:" + widget.Id : "app:" + ((DockApp)tile.Tag).Key;

    private Dictionary<string, Point> TileOrigins() => PositionedTiles()
        .ToDictionary(TileKey, tile => BoundsOf(tile).TopLeft);

    private static TranslateTransform TileShift(Border tile)
    {
        if (tile.RenderTransform is TransformGroup existing && existing.Children.LastOrDefault() is TranslateTransform offset) return offset;
        var group = new TransformGroup();
        if (tile.RenderTransform is ScaleTransform scale) group.Children.Add(scale);
        var shift = new TranslateTransform();
        group.Children.Add(shift);
        tile.RenderTransform = group;
        return shift;
    }

    private void AnimateTilePositions(Dictionary<string, Point> origins)
    {
        if (SystemParameters.ClientAreaAnimation != true || !IsLoaded) return;
        UpdateLayout();
        foreach (var tile in PositionedTiles())
        {
            if (!origins.TryGetValue(TileKey(tile), out var before)) continue;
            var now = BoundsOf(tile).TopLeft;
            var shift = TileShift(tile);
            Slide(TranslateTransform.XProperty, before.X - now.X + shift.X);
            Slide(TranslateTransform.YProperty, before.Y - now.Y + shift.Y);
            void Slide(DependencyProperty property, double from)
            {
                if (Math.Abs(from) < 0.5) return;
                shift.BeginAnimation(property, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(175))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                });
            }
        }
    }
}
