using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private const int NoteLines = 4;
    private const double NoteLineHeight = 20;

    // A sticky note on the island: a pencil and a prompt while it is empty, its first lines once it has words. The whole card
    // opens the editor. Redrawn when the text in the settings is another one.
    private Border NoteWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        frame.Padding = new Thickness(0);
        string? drawn = null;
        _widgetUpdates.Add(() =>
        {
            string text = _app.Settings.NoteText;
            if (text == drawn && frame.Child is not null) return;
            drawn = text;
            frame.Child = NoteContent(text);
        });
        return frame;
    }

    private Button NoteContent(string text)
    {
        var ink = UI.Brush(Look.Secondary);
        var pencil = new Path
        {
            Data = Geometry.Parse("M 3,21 L 4,16.5 L 16.5,4 A 2.1,2.1 0 0 1 20,7.5 L 7.5,20 Z M 14,6.5 L 17.5,10"),
            Stroke = ink, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Width = 22, Height = 22, Stretch = Stretch.Uniform
        };
        FrameworkElement content;
        if (string.IsNullOrWhiteSpace(text))
        {
            var prompt = WeatherText(Loc.T("点击写便签", "Click to write a note"), 13);
            prompt.Foreground = ink; prompt.FontWeight = FontWeights.SemiBold; prompt.Margin = new Thickness(0, 8, 0, 0); prompt.HorizontalAlignment = HorizontalAlignment.Center;
            pencil.HorizontalAlignment = HorizontalAlignment.Center;
            content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { pencil, prompt } };
        }
        else
        {
            pencil.Width = pencil.Height = 14; pencil.Margin = new Thickness(0, 4, 8, 0); pencil.VerticalAlignment = VerticalAlignment.Top;
            var note = WeatherText(text.Trim(), 14);
            note.TextWrapping = TextWrapping.Wrap; note.TextTrimming = TextTrimming.CharacterEllipsis;
            note.LineHeight = NoteLineHeight; note.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; note.MaxHeight = NoteLineHeight * NoteLines;
            note.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(pencil, System.Windows.Controls.Dock.Left);
            row.Children.Add(pencil); row.Children.Add(note);
            content = row;
        }
        var button = WidgetSurface(content, Loc.T("便签 · 点击编辑", "Note · click to edit") + (string.IsNullOrWhiteSpace(text) ? "" : " · " + text.Trim()), OpenNote, new CornerRadius(Corner(16)));
        button.Padding = new Thickness(WidgetSide, 8, WidgetSide, 8);
        return button;
    }

    private void OpenNote() => new NoteDialog(_app, Settings.Theme, Look.Current).ShowDialog();
}
