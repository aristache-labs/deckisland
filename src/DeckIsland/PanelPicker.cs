using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    // A choice made inside the page instead of in a pop-up (which is a window of its own, that the pointer would leave the panel for):
    // the list covers the page's body until a row is picked, Esc is pressed or the back row is clicked. With search, a box above the
    // list filters it as it is typed.
    private void ShowPanelPicker(Grid host, string title, IReadOnlyList<(string Label, bool Chosen)> items, Action<int> pick, bool search = false)
    {
        ClosePanelPicker(host);
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        var back = new Button { Style = (Style)FindResource("WidgetSurfaceButton"), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 2, 8, 2), Content = PickerRowText("‹  " + title, Look.Secondary) };
        AutomationProperties.SetName(back, Loc.T("返回", "Back"));
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        DockPanel.SetDock(back, System.Windows.Controls.Dock.Left);
        top.Children.Add(back);
        TextBox? box = null;
        if (search)
        {
            box = new TextBox { Style = (Style)FindResource("ShelfTextBox"), Height = 26, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(box, Loc.T("搜索", "Search"));
            TakeKeyboardOnClick(box);
            top.Children.Add(box);
        }
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(scroll);
        var overlay = new Border { Background = UI.Brush(Look.Hex("surface")), Child = panel, Tag = PickerTag };
        Grid.SetRowSpan(overlay, Math.Max(1, host.RowDefinitions.Count)); Grid.SetColumnSpan(overlay, Math.Max(1, host.ColumnDefinitions.Count));

        void Fill(string filter)
        {
            list.Children.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var (label, chosen) = items[i];
                if (filter.Length > 0 && !label.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                int index = i;
                var check = new Path { Data = Geometry.Parse("M1.5,5.2 L4,7.7 L8.5,2.5"), Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.6, Width = 10, Height = 10,
                    Margin = new Thickness(0, 0, 8, 0), Visibility = chosen ? Visibility.Visible : Visibility.Hidden, VerticalAlignment = VerticalAlignment.Center,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
                var row = new Button
                {
                    Style = (Style)FindResource("WidgetSurfaceButton"), Background = Brushes.Transparent, Height = 28, Padding = new Thickness(8, 0, 8, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { check, PickerRowText(label, Look.Primary) } }
                };
                AutomationProperties.SetName(row, label);
                row.Click += (_, _) => { ClosePanelPicker(host); pick(index); };
                list.Children.Add(row);
            }
        }
        Fill("");
        if (box is not null) box.TextChanged += (_, _) => Fill(box.Text.Trim());
        back.Click += (_, _) => ClosePanelPicker(host);
        overlay.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; ClosePanelPicker(host); } };
        host.Children.Add(overlay);
        // The chosen row in view, so a long list (the currencies) opens where the choice is.
        overlay.Loaded += (_, _) =>
        {
            if (list.Children.OfType<Button>().FirstOrDefault(row => row.Content is StackPanel { Children: [Path { Visibility: Visibility.Visible }, ..] }) is { } chosen)
                chosen.BringIntoView();
            if (box is not null && IsActive) Keyboard.Focus(box);
        };
    }

    private const string PickerTag = "panel-picker";

    // True when a picker was open.
    private static bool ClosePanelPicker(Grid host)
    {
        var open = host.Children.OfType<Border>().Where(child => Equals(child.Tag, PickerTag)).ToList();
        foreach (var picker in open) host.Children.Remove(picker);
        return open.Count > 0;
    }

    private static TextBlock PickerRowText(string text, string color) => new()
    {
        Text = text, FontSize = 12.5, Foreground = UI.Brush(color), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    // A rounded chip in the page's header that opens a picker: its label and a small chevron.
    private Button PickerChip(string label, Action open)
    {
        var chevron = new Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L7,0"), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.4, Margin = new Thickness(7, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var chip = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Background = UI.Brush(Look.Hex("fill")), Height = 26, Padding = new Thickness(11, 0, 10, 0),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { PickerRowText(label, Look.Primary), chevron } }
        };
        AutomationProperties.SetName(chip, label);
        chip.Click += (_, _) => open();
        return chip;
    }
}
