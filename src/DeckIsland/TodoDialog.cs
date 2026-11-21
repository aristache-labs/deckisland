using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// The whole To do list, in the panel's colors as the new-reminder window is: tick, reorder, remove, and add several in a row.
// Every change is saved at once, so Done only closes the window. Only open items can be moved: the arrows beside each one,
// or Alt+Up and Alt+Down on its row, change its priority.
internal sealed class TodoDialog : ControlDialog
{
    private readonly App _app;
    private readonly PanelPalette _colors;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _count;
    private readonly TextBlock _hint;
    private string Color(string token) => _colors[token].ToString();

    internal TodoDialog(App app, SettingsTheme theme, PanelPalette colors) : base(theme, WidgetLayout.Name(WidgetKind.Todo))
    {
        _app = app;
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
        Width = 480;
        Body.Children.Clear();

        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = UI.Brush(Color("textPrimary")), Margin = new Thickness(0, 0, 14, 0),
            Child = new Path { Data = Geometry.Parse("M 0,4 L 3.5,7.5 L 10,0.5"), Stroke = UI.Brush(Color("surface")), StrokeThickness = 1.6, Width = 14, Height = 11, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var title = Text(Title, 19, Color("textPrimary"), true, 24);
        title.VerticalAlignment = VerticalAlignment.Center;
        _count = Text("", 19, Color("accent"), true, 24);
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.Margin = new Thickness(9, 0, 0, 0);
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { badge, title, _count } });

        Body.Children.Add(new ScrollViewer { Content = _rows, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Margin = new Thickness(0, 14, 0, 0) });

        var entry = new TextBox { Height = 44, FontSize = 14, Padding = new Thickness(12, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center, MaxLength = Todos.MaxLength,
            Background = UI.Brush(Color("input")), Foreground = UI.Brush(Color("textPrimary")), BorderBrush = UI.Brush(Color("outline")), CaretBrush = UI.Brush(Color("accent")) };
        AutomationProperties.SetName(entry, Loc.T("待办事项", "Something to do"));
        var placeholder = Text(Loc.T("要做的事……", "Something to do…"), 14, Color("textSecondary"));
        placeholder.Margin = new Thickness(14, 0, 0, 0); placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.IsHitTestVisible = false;
        entry.TextChanged += (_, _) => placeholder.Visibility = entry.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Body.Children.Add(new Grid { Margin = new Thickness(0, 12, 0, 0), Children = { entry, placeholder } });
        _hint = Text(Loc.T("按 Enter 添加，可以连续添加几项。", "Enter adds it. Add a few in a row."), 12, Color("textSecondary"));
        _hint.Margin = new Thickness(2, 8, 0, 0);
        AutomationProperties.SetLiveSetting(_hint, AutomationLiveSetting.Polite);
        Body.Children.Add(_hint);

        var clear = new Button { Content = Loc.T("清除已完成", "Clear done"), Style = (Style)FindResource("ControlDialogButton") };
        var done = new Button { Content = Loc.T("完成", "Done"), Style = (Style)FindResource("ControlDialogButton"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0), MinWidth = 72 };
        var add = new Button { Content = Loc.T("添加", "Add"), Style = (Style)FindResource("ControlDialogPrimaryButton"), MinWidth = 72, Foreground = UI.Brush(Color("onAccent")) };
        void Add()
        {
            if (string.IsNullOrWhiteSpace(entry.Text)) { entry.Focus(); return; }
            if (_app.Settings.TodoItems.Count >= Todos.MaxItems) { _hint.Text = Loc.T($"最多 {Todos.MaxItems} 项。", $"The list holds {Todos.MaxItems} items at most."); return; }
            var text = entry.Text;
            Change(list => Todos.Add(list, text));
            entry.Clear(); entry.Focus();
            _hint.Text = Loc.T("已添加。继续添加，或点完成。", "Added. Another, or Done.");
        }
        add.Click += (_, _) => Add();
        entry.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        clear.Click += (_, _) => Change(Todos.ClearDone);
        done.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        DockPanel.SetDock(clear, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { done, add } };
        DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(clear); footer.Children.Add(right);
        Body.Children.Add(footer);
        _clear = clear;

        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 24);
        Content = new Border { Padding = new Thickness(24), Child = Body };
        Fill();
        Loaded += (_, _) => entry.Focus();
    }

    private readonly Button _clear;

    private void Change(Func<IReadOnlyList<TodoItem>, List<TodoItem>> change, Guid? keepFocusOn = null)
    {
        _app.UpdateTodos(change);
        Fill(keepFocusOn);
    }

    private void Fill(Guid? focus = null)
    {
        var items = _app.Settings.TodoItems;
        int open = Todos.OpenCount(items);
        _count.Text = open > 0 ? open.ToString(Loc.Culture) : "";
        _clear.IsEnabled = items.Any(item => item.Done);
        _rows.Children.Clear();
        _rows.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < items.Count; i++)
        {
            var row = Row(items[i], i, open);
            _rows.Children.Add(row);
            if (items[i].Id == focus) row.Loaded += (_, _) => row.Focus();
        }
    }

    private FrameworkElement Row(TodoItem item, int index, int open)
    {
        var mark = new Button { Style = (Style)FindResource("ControlDialogButton"), Width = 34, Height = 34, Padding = new Thickness(0), MinWidth = 0, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Content = MainWindow.TodoMark(item.Done, Color("accent"), Color("textSecondary"), Color("surface"), 20) };
        AutomationProperties.SetName(mark, (item.Done ? Loc.T("标为未完成：", "Mark not done: ") : Loc.T("标为完成：", "Mark done: ")) + item.Text);
        mark.Click += (_, _) => Change(list => Todos.Toggle(list, item.Id));

        var text = Text(item.Text, 14, item.Done ? Color("textSecondary") : Color("textPrimary"));
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(10, 0, 8, 0);
        if (item.Done) text.TextDecorations = TextDecorations.Strikethrough;

        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!item.Done)
        {
            tools.Children.Add(Tool("M 0,5 L 4.5,0.5 L 9,5", Loc.T("提高优先级：", "Move up: ") + item.Text, index > 0, () => Change(list => Todos.Move(list, item.Id, -1), item.Id)));
            tools.Children.Add(Tool("M 0,0.5 L 4.5,5 L 9,0.5", Loc.T("降低优先级：", "Move down: ") + item.Text, index < open - 1, () => Change(list => Todos.Move(list, item.Id, 1), item.Id)));
        }
        tools.Children.Add(Tool("M 0,0 L 9,9 M 9,0 L 0,9", Loc.T("删除：", "Remove: ") + item.Text, true, () => Change(list => Todos.Remove(list, item.Id))));

        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Focusable = true, Background = Brushes.Transparent, FocusVisualStyle = null };
        AutomationProperties.SetName(row, item.Text + (item.Done ? Loc.T("，已完成", ", done") : Loc.T($"，优先级 {index + 1}", $", priority {index + 1}")));
        // Alt+Up and Alt+Down move the open item under the keyboard focus, as the arrows do.
        row.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (item.Done || Keyboard.Modifiers != ModifierKeys.Alt || key is not (Key.Up or Key.Down)) return;
            e.Handled = true;
            Change(list => Todos.Move(list, item.Id, key == Key.Up ? -1 : 1), item.Id);
        };
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(tools, Dock.Right);
        row.Children.Add(mark); row.Children.Add(tools); row.Children.Add(text);
        return row;
    }

    private Button Tool(string geometry, string name, bool enabled, Action action)
    {
        var button = new Button { Style = (Style)FindResource("ControlDialogButton"), Width = 30, Height = 30, Padding = new Thickness(0), MinWidth = 0, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, IsEnabled = enabled, Opacity = enabled ? 1 : .3, ToolTip = name,
            Content = new Path { Data = Geometry.Parse(geometry), Stroke = UI.Brush(Color("textSecondary")), StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }
}
