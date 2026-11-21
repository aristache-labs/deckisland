using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    // The first items of the list, each ticked off with a click; the plus and the "+N" chip open the full list.
    // Redrawn when the list in the settings is a new one, that is after any change from here, the dialog or another island.
    private const int TodoRows = 2;

    private Border TodoWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        frame.Padding = new Thickness(WidgetSide, 8, WidgetSide - 6, 8);
        IReadOnlyList<TodoItem>? drawn = null;
        _widgetUpdates.Add(() =>
        {
            var items = _app.Settings.TodoItems;
            if (ReferenceEquals(items, drawn) && frame.Child is not null) return;
            drawn = items;
            frame.Child = TodoContent(items);
        });
        return frame;
    }

    private FrameworkElement TodoContent(IReadOnlyList<TodoItem> items)
    {
        string accent = Look.Readable(Look.Hex("accent"), 4.5);
        int open = Todos.OpenCount(items), hidden = Math.Max(0, items.Count - TodoRows);

        var title = WeatherText(WidgetLayout.Name(WidgetKind.Todo), 14);
        title.FontWeight = FontWeights.SemiBold;
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        if (open > 0)
        {
            var count = WeatherText(open.ToString(Loc.Culture), 14);
            count.FontWeight = FontWeights.SemiBold; count.Foreground = UI.Brush(accent); count.Margin = new Thickness(7, 0, 0, 0);
            name.Children.Add(count);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (hidden > 0)
        {
            var more = WeatherText($"+{hidden}", 12);
            more.FontWeight = FontWeights.SemiBold; more.Foreground = UI.Brush(accent); more.VerticalAlignment = VerticalAlignment.Center;
            var chevron = new Path { Data = Geometry.Parse("M 0,0 L 3.5,3.5 L 7,0"), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.3,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Margin = new Thickness(5, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var chip = WidgetSurface(new StackPanel { Orientation = Orientation.Horizontal, Children = { more, chevron } },
                Loc.T($"还有 {hidden} 项，打开待办", $"{hidden} more, open To do"), OpenTodos);
            chip.Background = UI.Brush(Look.Hex("fillSelected")); chip.Padding = new Thickness(10, 3, 10, 3); chip.Height = 26; chip.Margin = new Thickness(0, 0, 6, 0);
            actions.Children.Add(chip);
        }
        var plus = WidgetSurface(new Path { Data = Geometry.Parse("M 5,0 V 10 M 0,5 H 10"), Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Loc.T("添加待办", "Add to do"), OpenTodos);
        plus.Background = UI.Brush(Look.Hex("fillSelected")); plus.Width = plus.Height = 26;
        actions.Children.Add(plus);
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new Grid { Children = { name, actions } } } };

        if (items.Count == 0)
        {
            var empty = WeatherText(Loc.T("没有待办。点 + 添加。", "Nothing to do. + adds one."), 13);
            empty.Foreground = UI.Brush(Look.Secondary); empty.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(empty);
        }
        foreach (var item in items.Take(TodoRows)) content.Children.Add(TodoRow(item, accent));
        AutomationProperties.SetName(content, $"{WidgetLayout.Name(WidgetKind.Todo)} · {Loc.T($"{open} 项未完成", $"{open} open")}");
        return content;
    }

    private Button TodoRow(TodoItem item, string accent)
    {
        var text = WeatherText(item.Text, 13);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(8, 0, 0, 0);
        if (item.Done) { text.Foreground = UI.Brush(Look.Secondary); text.TextDecorations = TextDecorations.Strikethrough; }
        var row = new DockPanel { Children = { TodoMark(item.Done, accent, Look.Secondary, Look.Hex("surface"), 16), text } };
        var button = WidgetSurface(row, (item.Done ? Loc.T("标为未完成：", "Mark not done: ") : Loc.T("标为完成：", "Mark done: ")) + item.Text,
            () => _app.UpdateTodos(list => Todos.Toggle(list, item.Id)));
        button.Padding = new Thickness(0, 3, 6, 3);
        button.Margin = new Thickness(-2, 3, 0, 0);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        return button;
    }

    // An empty ring while open; a filled disc with a check once done.
    internal static FrameworkElement TodoMark(bool done, string accent, string ring, string check, double size)
    {
        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Ellipse { Stroke = done ? null : UI.Brush(ring), StrokeThickness = 1.5, Fill = done ? UI.Brush(accent) : Brushes.Transparent });
        if (done)
            grid.Children.Add(new Path { Data = Geometry.Parse("M 0,3 L 2.6,5.6 L 7.5,0.5"), Stroke = UI.Brush(check), StrokeThickness = 1.5, Width = size * .5, Height = size * .4,
                Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return grid;
    }

    private void OpenTodos() => new TodoDialog(_app, Settings.Theme, Look.Current).ShowDialog();
}
