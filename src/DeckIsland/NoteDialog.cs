using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DeckIsland.Core;

namespace DeckIsland;

// The note, in the panel's colors as the To do window is: a large box that keeps what is typed as it is typed, so Done only
// closes the window and Clear empties the box.
internal sealed class NoteDialog : ControlDialog
{
    private readonly PanelPalette _colors;
    private string Color(string token) => _colors[token].ToString();

    internal NoteDialog(App app, SettingsTheme theme, PanelPalette colors) : base(theme, WidgetLayout.Name(WidgetKind.Note))
    {
        _colors = colors;
        foreach (var (resource, token) in new[]
        {
            ("SettingsSurface", "surface"), ("SettingsTextPrimary", "textPrimary"), ("SettingsTextSecondary", "textSecondary"), ("SettingsInset", "fill"),
            ("SettingsInputBorder", "outline"), ("SettingsAccent", "accent"), ("SettingsAccentFill", "accent"), ("SettingsAccentHover", "accent"),
            ("SettingsFillHover", "hover"), ("SettingsFillPressed", "pressed")
        })
            Resources[resource] = UI.Brush(Color(token));
        Background = UI.Brush(Color("surface"));
        Foreground = UI.Brush(Color("textPrimary"));
        SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(this, colors.IsDark);
        Width = 520;
        Body.Children.Clear();
        Body.Children.Add(Text(Title, 19, Color("textPrimary"), true, 26));

        var box = new TextBox
        {
            Text = app.Settings.NoteText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 200, FontSize = 14, Padding = new Thickness(12, 10, 12, 10), VerticalContentAlignment = VerticalAlignment.Top, MaxLength = DeckSettings.MaxNoteLength,
            Background = UI.Brush(Color("input")), Foreground = UI.Brush(Color("textPrimary")), BorderBrush = UI.Brush(Color("outline")), CaretBrush = UI.Brush(Color("accent"))
        };
        AutomationProperties.SetName(box, Loc.T("便签内容", "Note text"));
        box.TextChanged += (_, _) => app.SetNote(box.Text);
        var hint = Text(Loc.T("边写边保存。小组件显示前几行。", "Saved as you type. The widget shows the first few lines."), 12, Color("textSecondary"));
        hint.Margin = new Thickness(2, 8, 0, 0);

        var clear = new Button { Content = Loc.T("清空", "Clear"), Style = (Style)FindResource("ControlDialogButton") };
        var done = new Button { Content = Loc.T("完成", "Done"), Style = (Style)FindResource("ControlDialogPrimaryButton"), MinWidth = 84, IsCancel = true, IsDefault = false,
            Foreground = UI.Brush(Color("onAccent")) };
        clear.Click += (_, _) => { box.Clear(); box.Focus(); };
        done.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        DockPanel.SetDock(clear, Dock.Left); DockPanel.SetDock(done, Dock.Right);
        footer.Children.Add(clear); footer.Children.Add(done);
        Body.Children.Add(new Border { Margin = new Thickness(0, 14, 0, 0), Child = box });
        Body.Children.Add(hint);
        Body.Children.Add(footer);

        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 24);
        Content = new Border { Padding = new Thickness(24), Child = Body };
        Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };
    }
}
