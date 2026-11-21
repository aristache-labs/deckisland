using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;
using Path = System.Windows.Shapes.Path;

namespace DeckIsland;

// The pin of the Scratchpad, Converter and Terminal tabs: while it is on and one of them is shown, the open panel stays open when the
// pointer leaves it, so typing elsewhere and coming back finds it still there. Esc, the hotkey or closing it by hand still close it,
// and that also takes the pin off.
public partial class MainWindow
{
    private bool _toolPinned;
    private readonly List<(Path Glyph, Button Button)> _toolPins = [];
    private const string PinGlyph = "M9.6,1.6 L14.4,6.4 L12.2,7.3 L9.3,10.2 L9.7,12.8 L8.4,14.1 L1.9,7.6 L3.2,6.3 L5.8,6.7 L8.7,3.8 Z M5.1,10.9 L1.6,14.4";

    private bool ToolPinHolds => _toolPinned && CurrentPage is DeckPage.Scratchpad or DeckPage.Converter or DeckPage.Terminal;

    private Button ToolPinButton()
    {
        var glyph = new Path { Data = Geometry.Parse(PinGlyph), StrokeThickness = 1.4, Width = 12, Height = 12, Stretch = Stretch.Uniform,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var button = new Button { Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Width = 26, Height = 26, Content = glyph, VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => SetToolPin(!_toolPinned);
        _toolPins.Add((glyph, button));
        ShowToolPin(glyph, button);
        return button;
    }

    private void SetToolPin(bool on)
    {
        _toolPinned = on;
        foreach (var (glyph, button) in _toolPins) ShowToolPin(glyph, button);
    }

    private void ShowToolPin(Path glyph, Button button)
    {
        glyph.Stroke = UI.Brush(_toolPinned ? Look.Primary : Look.Secondary);
        glyph.Fill = _toolPinned ? UI.Brush(Look.Primary) : Brushes.Transparent;
        button.Background = UI.Brush(Look.Hex(_toolPinned ? "fillSelected" : "fill"));
        string label = _toolPinned ? Loc.T("取消固定", "Unpin") : Loc.T("固定，保持打开", "Pin open");
        AutomationProperties.SetName(button, label);
        button.ToolTip = _toolPinned ? Loc.T("已固定：移开鼠标时不会收起", "Pinned: stays open when the pointer leaves") : Loc.T("固定：移开鼠标时保持打开", "Pin: stay open when the pointer leaves");
    }
}
