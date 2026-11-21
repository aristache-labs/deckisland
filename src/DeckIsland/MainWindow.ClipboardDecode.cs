using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private bool CanDecodeClipboard => Settings.ClipboardDecode && !Settings.ClipboardPresentationMode;

    private FrameworkElement WithClipboardDecode(Border row, ClipEntry entry)
    {
        if (!CanDecodeClipboard || entry.Kind == ClipKind.Image) return row;
        var line = new DockPanel { Background = Brushes.Transparent };
        var chip = DecodeButton("");
        chip.Visibility = Visibility.Collapsed;
        chip.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(chip, System.Windows.Controls.Dock.Right);
        line.Children.Add(chip);
        line.Children.Add(row);
        var output = new Border { Visibility = Visibility.Collapsed, Padding = new Thickness(8, 5, 8, 8) };
        var wrapper = new StackPanel { Children = { line, output } };
        bool checkedEntry = false;
        ClipboardDecode? decoded = null;

        line.MouseEnter += (_, _) =>
        {
            if (!CanDecodeClipboard) return;
            if (!checkedEntry) { decoded = ClipboardDecode.Detect(entry.Text); checkedEntry = true; }
            if (decoded is null) return;
            chip.Content = decoded.Type;
            AutomationProperties.SetName(chip, $"Decode {decoded.Type}");
            chip.Visibility = Visibility.Visible;
        };
        line.MouseLeave += (_, _) =>
        {
            if (output.Visibility != Visibility.Visible && !chip.IsKeyboardFocusWithin)
                chip.Visibility = Visibility.Collapsed;
        };
        chip.Click += (_, _) =>
        {
            if (!CanDecodeClipboard || decoded is not { } value) return;
            if (output.Visibility == Visibility.Visible)
            {
                output.Visibility = Visibility.Collapsed;
                output.Child = null;
                return;
            }

            var result = new TextBox
            {
                Text = value.Result, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent, Foreground = UI.Brush(Look.Primary),
                BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 12
            };
            AutomationProperties.SetName(result, value.Type);
            string label = value.Tracking ? Loc.T("复制不含跟踪参数的链接", "Copy without tracking") : Loc.T("复制结果", "Copy result");
            var copy = DecodeButton(label);
            copy.Click += (_, _) =>
            {
                if (!CanDecodeClipboard) return;
                copy.Content = _app.Shelf.CopyToolText(value.Result)
                    ? Loc.T("已复制", "Copied") : Loc.T("剪贴板正忙，重试", "Clipboard busy. Retry");
            };
            copy.HorizontalAlignment = HorizontalAlignment.Left;
            output.Child = new StackPanel { Children = { result, copy } };
            output.Visibility = Visibility.Visible;
            output.BringIntoView();
        };
        return wrapper;
    }

    private Button DecodeButton(string title, Action? action = null)
    {
        var button = new Button
        {
            Content = title, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(3),
            Background = UI.Brush(Look.Hex("hover")), Foreground = UI.Brush(Look.Primary),
            BorderBrush = UI.Brush(Look.Tertiary), FontSize = 11
        };
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }
}
