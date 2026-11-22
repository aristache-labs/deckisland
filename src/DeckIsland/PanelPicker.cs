using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;
using DockSide = System.Windows.Controls.Dock;

namespace DeckIsland;

public partial class MainWindow
{
    // The same slim pill strip as the settings' PillChoice (SettingsMusic.PillTemplate): 26 high, 13 round, 14 to the side, 12.5 text.
    private Border PanelSegmentTrack(UIElement choices) => new()
    {
        CornerRadius = new CornerRadius(16), Background = UI.Brush(Look.Fill),
        BorderBrush = UI.Brush(Look.Outline), BorderThickness = new Thickness(1), Padding = new Thickness(3, 2, 1, 2),
        VerticalAlignment = VerticalAlignment.Center, Child = choices
    };

    private Button PanelSegment(string label, Action choose)
    {
        var button = new Button
        {
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Content = label, Tag = false,
            Height = 26, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 2, 0), FontSize = 12.5, FontWeight = FontWeights.Medium
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => choose();
        button.MouseEnter += (_, _) => { if (button.Tag is false) button.Foreground = UI.Brush(Look.Primary); };
        button.MouseLeave += (_, _) => { if (button.Tag is false) button.Foreground = UI.Brush(Look.Secondary); };
        return button;
    }

    private void StylePanelSegment(Button button, bool selected)
    {
        button.Tag = selected;
        button.Background = selected ? UI.Brush(Look.Primary) : Brushes.Transparent;
        button.Foreground = UI.Brush(selected ? Look.Hex("surface") : Look.Secondary);
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Medium;
    }

    // A choice made inside the page instead of in a pop-up (which is a window of its own, that the pointer would leave the panel for):
    // the list covers the page's body until a row is picked, Esc is pressed or the back row is clicked. With search, a box above the
    // list filters it as it is typed.
    private void ShowPanelPicker(Grid host, string title, IReadOnlyList<(string Label, bool Chosen)> items, Action<int> pick, bool search = false)
    {
        ClosePanelPicker(host);
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        var chevron = new Path
        {
            Data = Geometry.Parse("M6,2 L2,6 L6,10"), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        var back = new Button
        {
            Style = (Style)FindResource("PanelIconButton"), Width = 25, Height = 26,
            Background = UI.Brush(Look.Fill), BorderBrush = UI.Brush(Look.Outline), Content = chevron
        };
        back.ToolTip = Loc.T("返回", "Back");
        AutomationProperties.SetName(back, back.ToolTip.ToString());
        var heading = PickerRowText(title, Look.Primary);
        heading.Margin = new Thickness(8, 0, 10, 0);
        heading.FontWeight = FontWeights.SemiBold;
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        DockPanel.SetDock(back, DockSide.Left);
        DockPanel.SetDock(heading, DockSide.Left);
        top.Children.Add(back);
        top.Children.Add(heading);
        TextBox? box = null;
        if (search)
        {
            var searchHint = Loc.T("搜索货币", "Search currency");
            box = new TextBox
            {
                Style = (Style)FindResource("ShelfTextBox"), Height = 27, Padding = new Thickness(9, 0, 9, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(box, searchHint);
            TakeKeyboardOnClick(box);
            var hint = PickerRowText(searchHint, Look.Secondary);
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(10, 0, 8, 0);
            var searchField = new Border
            {
                Background = UI.Brush(Look.Fill), BorderBrush = UI.Brush(Look.Outline), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Look.Corner(7)), Child = new Grid { Children = { box, hint } }
            };
            box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.IsKeyboardFocusWithinChanged += (_, _) => searchField.BorderBrush = UI.Brush(box.IsKeyboardFocusWithin ? Look.Accent : Look.Outline);
            top.Children.Add(searchField);
        }
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, DockSide.Top);
        panel.Children.Add(top);
        panel.Children.Add(scroll);
        var overlay = new Border
        {
            Background = UI.Brush(Look.Hex("surface")), Padding = new Thickness(0, 7, 0, 0),
            ClipToBounds = true, Child = panel, Tag = PickerTag
        };
        Grid.SetRowSpan(overlay, Math.Max(1, host.RowDefinitions.Count)); Grid.SetColumnSpan(overlay, Math.Max(1, host.ColumnDefinitions.Count));

        Button? Fill(string filter)
        {
            list.Children.Clear();
            Button? selected = null;
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
                    Style = (Style)FindResource("WidgetSurfaceButton"), Template = StretchPill(),
                    Background = chosen ? UI.Brush(Look.FillSelected) : UI.Brush(Look.Fill),
                    Height = 30, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 4),
                    Content = new DockPanel { Children = { check, PickerRowText(label, Look.Primary) } }
                };
                if (chosen) ((TextBlock)((DockPanel)row.Content).Children[1]).FontWeight = FontWeights.SemiBold;
                AutomationProperties.SetName(row, label);
                row.Click += (_, _) => { ClosePanelPicker(host); pick(index); };
                list.Children.Add(row);
                if (chosen) selected = row;
            }
            if (list.Children.Count == 0)
                list.Children.Add(PickerRowText(Loc.T("没有匹配项", "No matches"), Look.Secondary));
            return selected;
        }
        var chosenRow = Fill("");
        if (box is not null) box.TextChanged += (_, _) =>
        {
            Fill(box.Text.Trim());
            scroll.ScrollToTop();
        };
        back.Click += (_, _) => ClosePanelPicker(host);
        overlay.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; ClosePanelPicker(host); } };
        host.Children.Add(overlay);
        // The chosen row in view, so a long list (the currencies) opens where the choice is.
        overlay.Loaded += (_, _) =>
        {
            chosenRow?.BringIntoView();
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
