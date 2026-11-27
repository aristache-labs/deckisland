using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

// DeckIsland Dock's own settings window, apart from the island's SettingsWindow. It edits a copy of App.DockSettings and hands it
// to App.UpdateDockSettings, which saves dock.json. Pages beyond the basics arrive with the dock's later phases.
internal sealed class DockSettingsWindow : Window
{
    private readonly App _app;
    private readonly SettingsPalette _palette;

    public DockSettingsWindow(App app, SettingsTheme theme)
    {
        _app = app;
        _palette = new SettingsPalette(SettingsPalette.IsDark(theme));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DeckIsland;component/SettingsResources.xaml", UriKind.Relative) });
        _palette.Apply(Resources);
        Title = Loc.T("DeckIsland Dock 设置", "DeckIsland Dock settings");
        Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UI.PanelFont; Background = UI.Brush(_palette.Surface); Foreground = UI.Brush(_palette.Primary);
        Native.SettingsChrome.Apply(this); ResizeMode = ResizeMode.NoResize;
        SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(this, _palette.Dark);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = Build();
    }

    private FrameworkElement Build()
    {
        var s = _app.DockSettings;
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        body.Children.Add(Text(Title, 20, _palette.Primary, true));
        var card = new StackPanel();
        card.Children.Add(Row(Loc.T("位置", "Position"), Segments("DockEdge",
            [(Loc.T("底部", "Bottom"), DockEdge.Bottom), (Loc.T("顶部", "Top"), DockEdge.Top)], s.Edge, value => Change(d => d.Edge = value))));
        card.Children.Add(Row(Loc.T("大小", "Size"), Segments("DockSize",
            [(Loc.T("小", "S"), DockSettings.SizePresets[0]), (Loc.T("中", "M"), DockSettings.SizePresets[1]), (Loc.T("大", "L"), DockSettings.SizePresets[2])],
            s.Size, value => Change(d => d.Size = value))));
        card.Children.Add(Row(Loc.T("Windows 任务栏", "Windows taskbar"), Segments("DockTaskbar",
            [(Loc.T("自动隐藏", "Auto-hide"), DockTaskbarMode.AutoHide), (Loc.T("完全隐藏", "Hide"), DockTaskbarMode.Hide), (Loc.T("不隐藏", "Leave"), DockTaskbarMode.Leave)],
            s.TaskbarMode, value => Change(d => d.TaskbarMode = value))));
        var frame = new Border { Child = card, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 16, 0, 0),
            Background = UI.Brush(_palette.Card), BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1) };
        body.Children.Add(frame);
        return body;
    }

    private void Change(Action<DockSettings> edit)
    {
        var copy = _app.DockSettings.Copy(); edit(copy); _app.UpdateDockSettings(copy);
    }

    private Grid Row(string title, FrameworkElement control)
    {
        var row = new Grid { MinHeight = 48 };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Text(title, 13, _palette.Primary, true); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
        control.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(control, 1); row.Children.Add(control);
        return row;
    }

    // A segmented choice drawn like the island settings' (SettingsSegment).
    private FrameworkElement Segments<T>(string group, (string Name, T Value)[] choices, T current, Action<T> chosen)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, value) in choices)
        {
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = name, GroupName = group, IsChecked = EqualityComparer<T>.Default.Equals(value, current) };
            AutomationProperties.SetName(choice, name);
            choice.Checked += (_, _) => chosen(value);
            panel.Children.Add(choice);
        }
        return new Border { Child = panel, CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Background = UI.Brush(_palette.Inset) };
    }

    private static TextBlock Text(string value, double size, string color, bool bold = false)
    {
        var text = UI.Line(value, size, color); text.TextWrapping = TextWrapping.Wrap;
        if (bold) text.FontWeight = FontWeights.SemiBold;
        return text;
    }
}
