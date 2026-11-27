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
        bool same = _widgetPopup?.PlacementTarget == anchor;
        CloseWidgetDetails();
        if (same || _dismissedAnchor == anchor && Environment.TickCount64 - _dismissedAt < 350) return;
        _dismissedAnchor = null;
        _preview.HideNow();

        bool weather = kind == DockWidgetKind.ClockWeather || DockWidgets.IsWeather(kind);
        var (content, update) = weather ? WeatherPopupContent() : CalendarDetails();
        var close = DetailButton("×", Loc.T("关闭", "Close"), CloseWidgetDetails);
        close.Width = 30;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        var body = new Grid { Children = { content, close } };
        double workWidth = DeckIsland.Native.WindowsHost.MonitorScreen(Monitor) is { } screen
            ? screen.Work.Width / screen.Scale : SystemParameters.WorkArea.Width;
        var card = new Border
        {
            Width = Math.Min(weather ? 390 : 760, Math.Max(300, workWidth - 24)),
            Padding = new Thickness(weather ? 14 : 18), CornerRadius = new CornerRadius(14),
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
        popup.Opened += (_, _) => close.Focus();
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

    private Button DetailButton(string text, string label, Action click)
    {
        var button = new Button
        {
            Content = text, Height = 28, Cursor = Cursors.Hand, Foreground = UI.Brush(_look.Text),
            Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent
        };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, UI.Brush(_look.CardHover)));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, UI.Brush(Accent)));
        template.Triggers.Add(hover);
        template.Triggers.Add(focus);
        button.Template = template;
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }
}
