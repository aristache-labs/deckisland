using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DeckIsland.Core;
using DockSide = System.Windows.Controls.Dock;

namespace DeckIsland;

public partial class MainWindow
{
    private bool CanDecodeClipboard => Settings.ClipboardDecode && !Settings.ClipboardPresentationMode;
    private Action? _closeClipboardDecode;

    private FrameworkElement WithClipboardDecode(Border row, ClipEntry entry, ContentControl? blockOutput = null)
    {
        if (!CanDecodeClipboard || entry.Kind == ClipKind.Image) return row;
        var line = new Grid { Background = Brushes.Transparent };
        var chip = DecodeButton("");
        chip.Visibility = Visibility.Collapsed;
        chip.HorizontalAlignment = blockOutput is null ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        chip.VerticalAlignment = blockOutput is null ? VerticalAlignment.Center : VerticalAlignment.Top;
        chip.Margin = blockOutput is null ? new Thickness(0, 0, 8, 0) : new Thickness(8, 5, 0, 0);
        var selected = new Border
        {
            CornerRadius = row.CornerRadius, BorderThickness = new Thickness(1),
            BorderBrush = UI.Brush(Look.Accent), IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        var dock = new DockPanel();
        if (blockOutput is null)
        {
            DockPanel.SetDock(chip, DockSide.Right);
            dock.Children.Add(chip);
        }
        dock.Children.Add(row);
        line.Children.Add(dock);
        line.Children.Add(selected);
        if (blockOutput is not null) line.Children.Add(chip);

        var output = blockOutput ?? new ContentControl
        {
            Visibility = Visibility.Collapsed, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var wrapper = new StackPanel { Children = { line } };
        if (blockOutput is null) wrapper.Children.Add(output);
        bool checkedEntry = false;
        ClipboardDecode? decoded = null;

        void Close()
        {
            if (_closeClipboardDecode != Close) return;
            _closeClipboardDecode = null;
            selected.Visibility = output.Visibility = Visibility.Collapsed;
            output.Content = null;
            chip.Visibility = line.IsMouseOver ? Visibility.Visible : Visibility.Collapsed;
        }

        line.MouseEnter += (_, _) =>
        {
            if (!CanDecodeClipboard) return;
            if (!checkedEntry) { decoded = ClipboardDecode.Detect(entry.Text); checkedEntry = true; }
            if (decoded is null) return;
            chip.Content = decoded.Type;
            chip.ToolTip = $"{Loc.T("查看解码结果", "Show decoded")} {decoded.Type}";
            AutomationProperties.SetName(chip, chip.ToolTip.ToString());
            chip.Visibility = Visibility.Visible;
        };
        line.MouseLeave += (_, _) =>
        {
            if (_closeClipboardDecode != Close) chip.Visibility = Visibility.Collapsed;
        };
        wrapper.Unloaded += (_, _) => Close();
        chip.Click += (_, _) =>
        {
            if (!CanDecodeClipboard || decoded is not { } value) return;
            if (_closeClipboardDecode == Close) { Close(); return; }
            _closeClipboardDecode?.Invoke();
            _closeClipboardDecode = Close;
            output.Content = DecodeCard(value, Close);
            selected.Visibility = output.Visibility = Visibility.Visible;
            output.BringIntoView();
        };
        return wrapper;
    }

    private FrameworkElement DecodeCard(ClipboardDecode value, Action close)
    {
        var title = DecodeLabel(value.Type, Look.Secondary);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.FontWeight = FontWeights.SemiBold;
        var dismiss = DecodeButton("×", close);
        dismiss.Width = 24;
        dismiss.ToolTip = Loc.T("关闭解码结果", "Close decoded result");
        AutomationProperties.SetName(dismiss, dismiss.ToolTip.ToString());
        string label = value.Tracking ? Loc.T("复制不含跟踪参数的链接", "Copy without tracking") : Loc.T("复制解码结果", "Copy decoded");
        var copy = DecodeButton(label);
        copy.Click += (_, _) =>
        {
            if (!CanDecodeClipboard) return;
            copy.Content = _app.Shelf.CopyToolText(value.Result)
                ? Loc.T("已复制", "Copied") : Loc.T("剪贴板正忙，重试", "Clipboard busy. Retry");
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { copy, dismiss } };
        DockPanel.SetDock(actions, DockSide.Right);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 5), Children = { actions, title } };
        FrameworkElement content;
        if (value.Token is { } token)
        {
            var columns = new UniformGrid { Columns = 1, Children =
                { DecodeSection(Loc.T("头部", "Header"), token.Header), DecodeSection(Loc.T("载荷", "Payload"), token.Payload) } };
            columns.SizeChanged += (_, e) => columns.Columns = e.NewSize.Width >= 350 ? 2 : 1;
            var status = new WrapPanel { Margin = new Thickness(4, 6, 0, 0) };
            var warning = DecodeLabel(Loc.T("签名未经验证", "Signature has not been verified"), Look.Primary);
            warning.FontWeight = FontWeights.SemiBold;
            warning.Margin = new Thickness(0, 0, 12, 0);
            status.Children.Add(warning);
            status.Children.Add(DecodeLabel($"{Loc.T("到期时间", "Expiry")}: {token.Expiry}", Look.Secondary));
            content = new StackPanel { Children = { columns, status } };
        }
        else content = DecodeSection(value.Tracking ? Loc.T("已移除跟踪参数的链接", "Link without tracking") : value.Type, value.Result, showHeading: false);

        var card = new Border
        {
            Background = UI.Brush(Look.Hex("fill")), CornerRadius = new CornerRadius(Look.Corner(10)),
            Padding = new Thickness(8), Child = content
        };
        return new StackPanel { Margin = new Thickness(8, 6, 8, 8), Children = { toolbar, card } };
    }

    private StackPanel DecodeSection(string heading, string text, bool showHeading = true)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
        if (showHeading) panel.Children.Add(DecodeLabel(heading, Look.Secondary));
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 135,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent,
            Foreground = UI.Brush(Look.Primary), BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(0, 3, 0, 0)
        };
        TakeKeyboardOnClick(box);
        AutomationProperties.SetName(box, heading);
        panel.Children.Add(box);
        return panel;
    }

    private static TextBlock DecodeLabel(string title, string colour) => new()
    {
        Text = title, Foreground = UI.Brush(colour), FontSize = 11, TextWrapping = TextWrapping.Wrap
    };

    private Button DecodeButton(string title, Action? action = null)
    {
        var button = new Button
        {
            Content = title, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(3, 0, 0, 0),
            Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(),
            Background = UI.Brush(Look.Hex("hover")), Foreground = UI.Brush(Look.Primary), FontSize = 11
        };
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }
}
