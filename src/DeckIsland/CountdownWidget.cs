using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    // Days left until the date set in Widgets settings, with its title; rebuilt only when the day changes.
    private Border CountdownWidget(Border frame)
    {
        frame.CornerRadius = new CornerRadius(Corner(16));
        frame.Padding = new Thickness(WidgetSide, 8, WidgetSide, 8);
        var target = Countdown.Parse(Settings.CountdownDate);
        string title = Settings.CountdownTitle;
        if (target is null)
        {
            // Empty, the whole card is the button (hover covers it); with a date it is text with the card's padding.
            frame.Padding = new Thickness(0);
            frame.Child = CountdownEmpty();
            return frame;
        }
        DateOnly shown = default;
        _widgetUpdates.Add(() =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (today == shown && frame.Child is not null) return;
            shown = today;
            frame.Child = CountdownContent(today, target.Value, title);
        });
        return frame;
    }

    // No date yet: a calendar mark over a note, and the whole card opens the settings.
    private Button CountdownEmpty()
    {
        var ink = UI.Brush(Look.Secondary);
        var calendar = new Path
        {
            Data = Geometry.Parse("M 5,5 H 19 A 1,1 0 0 1 20,6 V 19 A 1,1 0 0 1 19,20 H 5 A 1,1 0 0 1 4,19 V 6 A 1,1 0 0 1 5,5 Z M 4,10 H 20 M 8,3 V 7 M 16,3 V 7"),
            Stroke = ink, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        };
        var mark = new Viewbox { Width = 26, Height = 26, Child = new Canvas { Width = 24, Height = 24, Children = { calendar } }, HorizontalAlignment = HorizontalAlignment.Center };
        var note = WeatherText(Loc.T("在设置中设置日期", "Set a date in Settings"), 13);
        note.Foreground = ink;
        note.FontWeight = FontWeights.SemiBold;
        note.Margin = new Thickness(0, 8, 0, 0);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { mark, note } };
        var button = WidgetSurface(body, $"{WidgetLayout.Name(WidgetKind.Countdown)} · {note.Text}", () => _app.OpenSettingsPage(SettingsWindow.WidgetsSettingsPage), new CornerRadius(Corner(16)));
        button.Padding = new Thickness(WidgetSide, 8, WidgetSide, 8);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        return button;
    }

    private FrameworkElement CountdownContent(DateOnly today, DateOnly target, string title)
    {
        var culture = Loc.Culture;
        int days = target.DayNumber - today.DayNumber;
        string unit = days switch
        {
            0 => Loc.T("就是今天", "today"),
            > 0 => days == 1 ? Loc.T("天", "day to go") : Loc.T("天", "days to go"),
            _ => -days == 1 ? Loc.T("天前", "day ago") : Loc.T("天前", "days ago")
        };
        var number = WeatherText(Math.Abs(days).ToString(culture), 34);
        number.FontWeight = FontWeights.SemiBold;
        Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
        var label = WeatherText(unit, 14);
        label.Foreground = UI.Brush(Look.Secondary);
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Margin = new Thickness(8, 0, 0, 7);
        var count = new StackPanel { Orientation = Orientation.Horizontal, Children = { number, label } };
        var top = new Grid { Children = { count } };
        if (title.Length > 0)
        {
            var name = WeatherText(title, 13);
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = UI.Brush(Look.Readable(Look.Hex("accent"), 4.5));
            name.HorizontalAlignment = HorizontalAlignment.Right;
            name.VerticalAlignment = VerticalAlignment.Top;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxWidth = 120;
            top.Children.Add(name);
        }
        var date = WeatherText(target.ToString("ddd d MMM yyyy", culture), 13);
        date.Foreground = UI.Brush(Look.Secondary);
        date.FontWeight = FontWeights.SemiBold;
        date.Margin = new Thickness(0, 4, 0, 0);
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, date } };
        AutomationProperties.SetName(content, $"{(title.Length > 0 ? title : WidgetLayout.Name(WidgetKind.Countdown))} · {Math.Abs(days)} {unit} · {date.Text}");
        return content;
    }
}
