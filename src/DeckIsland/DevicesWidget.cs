using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using BrimDeck.Core;
using BrimDeck.Native;

namespace BrimDeck;

public partial class MainWindow
{
    // A ring (the Devices page's empty-state mark) with an optional faint device glyph, or just the glyph.
    private sealed class DeviceMark(PanelLook look, DeviceKind kind, double size, bool ring) : FrameworkElement
    {
        protected override Size MeasureOverride(Size available) => new(size, size);

        protected override void OnRender(DrawingContext drawing)
        {
            var center = new Point(size / 2, size / 2);
            if (ring)
            {
                DevicesView.Ring(drawing, center, size - 2, 4, null, look.Primary, look: look);
                drawing.PushOpacity(.35);
            }
            double glyph = ring ? size * .46 : size;
            DeviceGlyphs.Draw(drawing, kind, new Rect(center.X - glyph / 2, center.Y - glyph / 2, glyph, glyph), look.Primary);
            if (ring) drawing.Pop();
        }
    }

    private Border DevicesWidget(Border frame, bool detailed)
    {
        frame.CornerRadius = new CornerRadius(16);
        frame.Padding = new Thickness(10, 8, 10, 8);
        string title = WidgetLayout.Name(WidgetKind.Devices);

        var mark = new DeviceMark(Look, DeviceKind.Other, 38, true) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 7) };
        var heading = WeatherText(Loc.T("没有已连接的无线设备", "No wireless devices connected"), 13);
        heading.Foreground = UI.Brush(Look.Secondary);
        var hint = WeatherText(Loc.T("连接蓝牙耳机、键鼠、手柄，或插上罗技等接收器后会显示在这里",
            "Bluetooth headphones, keyboards, mice and controllers, and receivers such as Logitech's, show here once connected"), 11);
        hint.Foreground = UI.Brush(Look.Tertiary);
        foreach (var text in new[] { heading, hint }) { text.TextAlignment = TextAlignment.Center; text.TextWrapping = TextWrapping.Wrap; }
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        hint.MaxHeight = 30;
        hint.Margin = new Thickness(0, 2, 0, 0);
        var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { mark, heading, hint } };

        var tiles = new UniformGrid { Columns = 1, VerticalAlignment = VerticalAlignment.Top };
        var viewport = new ScrollViewer
        {
            Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = true, FocusVisualStyle = (Style)FindResource("PanelKeyboardFocus")
        };
        AutomationProperties.SetName(viewport, title);
        frame.SizeChanged += (_, _) =>
        {
            mark.Visibility = frame.ActualHeight >= 120 ? Visibility.Visible : Visibility.Collapsed;
            tiles.Columns = Math.Clamp((int)((frame.ActualWidth - 20) / 150), 1, Math.Max(1, tiles.Children.Count));
        };

        FrameworkElement content = new Grid { Children = { viewport, empty } };
        if (detailed)
        {
            var link = WidgetSurface(content, Loc.T("打开设备", "Open Display and devices"), () => SelectPage(DeckPage.Devices));
            link.Padding = new Thickness(0);
            content = link;
        }
        frame.Child = content;

        string last = "";
        _widgetUpdates.Add(() =>
        {
            var devices = ShownDevices.Where(device => device.Percent is not null).ToArray();
            viewport.Visibility = devices.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = devices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            string key = string.Join('|', devices.Select(device => $"{device.Key}:{device.Percent}:{device.Charging}:{device.Asleep}"));
            if (key == last) return;
            last = key;
            tiles.Children.Clear();
            foreach (var device in devices) tiles.Children.Add(DeviceTile(device));
            tiles.Columns = Math.Clamp((int)((frame.ActualWidth - 20) / 150), 1, Math.Max(1, devices.Length));
        });
        return frame;
    }

    private FrameworkElement DeviceTile(DeviceBattery device)
    {
        int percent = Math.Clamp(device.Percent ?? 0, 0, 100);
        string color = Look.Readable(percent <= 20 && !device.Charging ? Look.Hex("danger") : Look.Primary, 4.5);
        var mark = new DeviceMark(Look, device.Kind, 18, false) { Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        var value = WeatherText($"{percent}%", 13);
        value.FontWeight = FontWeights.SemiBold;
        value.Foreground = UI.Brush(color);
        value.Margin = new Thickness(6, 0, 0, 0);
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var name = WeatherText(device.Name, 12);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        name.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(mark, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
        var row = new DockPanel { Children = { mark, value, name } };
        var bar = new QuotaBar($"device:{device.Key}", percent / 100.0, color, Animates) { Height = 3, Margin = new Thickness(0, 4, 0, 0) };
        string charging = device.Charging ? $" · {Loc.T("充电中", "Charging")}" : "";
        var tile = new Border
        {
            Margin = new Thickness(3), Padding = new Thickness(9, 7, 9, 7), CornerRadius = new CornerRadius(11),
            Background = UI.Brush(Look.Hex("fillSelected")), Child = new StackPanel { Children = { row, bar } },
            ToolTip = $"{device.Name} · {percent}%{charging}"
        };
        AutomationProperties.SetName(tile, $"{device.Name} · {percent}%{charging}");
        return tile;
    }
}
