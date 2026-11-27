using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

internal sealed partial class DockWindow
{
    private Popup? _widgetPopup;
    private Action? _widgetPopupUpdate;
    private FrameworkElement? _dismissedAnchor;
    private long _dismissedAt;

    private static bool HasWidgetDetails(DockWidgetKind kind) => kind is DockWidgetKind.Calendar or DockWidgetKind.Date
        or DockWidgetKind.Today or DockWidgetKind.ClockWeather || DockWidgets.IsWeather(kind);

    private void CloseWidgetDetails()
    {
        if (_widgetPopup is not { } popup) return;
        _widgetPopup = null;
        _widgetPopupUpdate = null;
        popup.IsOpen = false;
    }

    private void ToggleWidgetDetails(FrameworkElement anchor, DockWidgetKind kind)
    {
        if (!BeginDockPanel(anchor)) return;
        bool weather = DockWidgets.IsWeather(kind);
        var (content, update) = weather ? WeatherPopupContent() : CalendarDetails();
        var close = DetailButton("×", Loc.T("关闭", "Close"), CloseWidgetDetails, radius: weather ? 5 : CalendarRadius(17, 17));
        close.Width = weather ? 30 : 34;
        if (!weather) { close.Height = 34; close.FontSize = 20; close.Background = UI.Brush(CalendarTile); }
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        ShowDockPanel(anchor, new Grid { Children = { content, close } }, update, weather ? 390 : 760, weather ? 14 : 18,
            weather ? 14 : CalendarRadius(14, 30), close);
    }

    // Closes any open panel; false when the click was on the anchor of the panel just closed, which toggles it shut.
    private bool BeginDockPanel(FrameworkElement anchor)
    {
        bool same = _widgetPopup?.PlacementTarget == anchor;
        CloseWidgetDetails();
        if (same || _dismissedAnchor == anchor && Environment.TickCount64 - _dismissedAt < 350) return false;
        _dismissedAnchor = null;
        _preview.HideNow();
        return true;
    }

    // A panel above (or below) the bar on the dock's card: Esc or a click elsewhere closes it, and `update` runs with the widgets.
    private void ShowDockPanel(FrameworkElement anchor, FrameworkElement body, Action update, double width, double padding, double radius,
        FrameworkElement? focus = null)
    {
        double workWidth = DeckIsland.Native.WindowsHost.MonitorScreen(Monitor) is { } screen
            ? screen.Work.Width / screen.Scale : SystemParameters.WorkArea.Width;
        var card = new Border
        {
            Width = Math.Min(width, Math.Max(300, workWidth - 24)),
            Padding = new Thickness(padding), CornerRadius = new CornerRadius(radius),
            Background = UI.Brush(_look.Text == "#FF1D1D1F" ? "#FFF8F8FA" : "#FF202024"),
            BorderBrush = UI.Brush(_look.CardRim), BorderThickness = new Thickness(1), Child = body
        };
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            CloseWidgetDetails();
            e.Handled = true;
        };
        bool top = _app.DockSettings.Edge == DockEdge.Top;
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = top ? PlacementMode.Bottom : PlacementMode.Top,
            VerticalOffset = top ? 8 : -8, AllowsTransparency = true, StaysOpen = false, Child = card
        };
        popup.Opened += (_, _) => (focus ?? card).Focus();
        popup.Closed += (_, _) =>
        {
            if (_widgetPopup != popup) return;
            // WPF releases capture before the click on the anchor is delivered to its tile.
            var point = Mouse.GetPosition(anchor);
            if (Mouse.LeftButton == MouseButtonState.Pressed && new Rect(anchor.RenderSize).Contains(point))
            {
                _dismissedAnchor = anchor;
                _dismissedAt = Environment.TickCount64;
            }
            _widgetPopup = null;
            _widgetPopupUpdate = null;
        };
        _widgetPopup = popup;
        _widgetPopupUpdate = update;
        update();
        DeckIsland.Native.DockAppWindows.Focus(_handle);
        popup.IsOpen = true;
    }

    private Button DetailButton(string text, string label, Action click, double radius = 5, string? hover = null)
    {
        var button = new Button
        {
            Content = text, Height = 28, Cursor = Cursors.Hand, Foreground = UI.Brush(_look.Text),
            Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent
        };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hovered = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hovered.Setters.Add(new Setter(Control.BackgroundProperty, UI.Brush(hover ?? _look.CardHover)));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, UI.Brush(Accent)));
        template.Triggers.Add(hovered);
        template.Triggers.Add(focus);
        button.Template = template;
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }
}
