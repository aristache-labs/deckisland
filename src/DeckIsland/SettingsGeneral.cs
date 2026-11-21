using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// The General page's own layout, after the settings pages of Untitled UI: each section is a card with a title and a line about
// it, and inside it every row has the setting's name and a short description at the left and its control at the right, starting
// on the same column for every row. Only General uses these; the shared Row, Toggle, Group and SliderRow serve the other pages,
// so General's rows are built by them and reshaped here (Reflow).
public partial class SettingsWindow
{
    // The label column takes two parts of the row and at most 240; the control column the rest, about 290 at the narrowest window,
    // which the widest control (the window rules' three choices) fits.
    private const double LabelColumnMax = 240, SliderColumn = 280, GeneralCombo = 200, ThemeCardWidth = 88;

    // Marks a card part that follows the row above it without a line between them, such as the font preview.
    private const string NoDivider = "no-divider";

    // A row's short description: kept as the row's automation help text, which a screen reader announces as well, and drawn
    // under the setting's name when the card lays the row out.
    private static T Describe<T>(T row, string description) where T : FrameworkElement
    {
        AutomationProperties.SetHelpText(row, description);
        return row;
    }

    // The page's title gets a line about the page under it and a rule below, as the reference pages head theirs.
    private void GeneralHeader(DockPanel head)
    {
        var title = head.Children[^1];
        head.Children.Remove(title);
        var about = TextLine(Loc.T("启动、外观以及 DeckIsland 在桌面上的行为。", "Startup, appearance and how DeckIsland behaves on your desktop."), 13, TextSecondary);
        about.Margin = new Thickness(0, 4, 0, 0); about.TextWrapping = TextWrapping.Wrap;
        head.Children.Add(new StackPanel { Children = { title, about } });
        head.Margin = new Thickness(0, 0, 0, 18);
        _content.Children.Add(new Border { Height = 1, Background = UI.Brush(Divider), Margin = new Thickness(0, 0, 0, 24) });
    }

    // A section: its title and what it is for in a header, a line, then the rows with a thin line between them.
    private void GeneralCard(string title, string description, params FrameworkElement[] rows)
    {
        var name = TextLine(title, 15, TextPrimary, FontWeights.SemiBold);
        var about = TextLine(description, 12.5, TextSecondary);
        about.TextWrapping = TextWrapping.Wrap; about.Margin = new Thickness(0, 3, 0, 0);
        var header = new StackPanel { Margin = new Thickness(20, 16, 20, 14), Children = { name } };
        if (description.Length > 0) header.Children.Add(about);
        var body = new StackPanel { Margin = new Thickness(20, 2, 20, 2) };
        foreach (var row in rows.Select(Reflow))
        {
            if (body.Children.Count > 0 && row.Tag as string != NoDivider) body.Children.Add(Divide());
            body.Children.Add(row);
        }
        Target.Children.Add(new Border
        {
            Background = UI.Brush(_palette.Card), BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 0, 20),
            Child = new StackPanel { Children = { header, new Border { Height = 1, Background = UI.Brush(Divider) }, body } }
        });
    }

    private Border Divide() => new() { Height = 1, Background = UI.Brush(_palette.RowDivider) };

    // Reshapes a row built by the shared helpers into General's two columns. A Row grid keeps its children and gets the columns,
    // the description and the spacing; a switch without help (a Border around a CheckBox that carries its own label) becomes a
    // row with the same CheckBox, so its handlers and any reference to it stay; a panel of rows is reshaped row by row. Anything
    // else (the font preview, the app rules list) is left as it is.
    private FrameworkElement Reflow(FrameworkElement row)
    {
        switch (row)
        {
            case Grid { ColumnDefinitions.Count: 2 } grid when grid.Children.OfType<FrameworkElement>().Any(child => Grid.GetColumn(child) == 0):
                Columns(grid);
                foreach (var child in grid.Children.OfType<FrameworkElement>())
                {
                    if (Grid.GetColumn(child) == 1) { child.HorizontalAlignment = HorizontalAlignment.Left; continue; }
                    child.Margin = new Thickness(0, 0, 20, 0);
                    Wrap(child);
                    if (child is StackPanel labels && AutomationProperties.GetHelpText(grid) is { Length: > 0 } text) labels.Children.Add(Description(text));
                }
                return grid;
            case Border { Child: CheckBox toggle } frame when toggle.Content is TextBlock:
                frame.Child = null;
                toggle.Content = null; toggle.Padding = new Thickness(0);
                var reshaped = new Grid();
                var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0), Children = { SettingLabel(AutomationProperties.GetName(toggle)) } };
                if (AutomationProperties.GetHelpText(frame) is { Length: > 0 } help) labelStack.Children.Add(Description(help));
                toggle.HorizontalAlignment = HorizontalAlignment.Left; toggle.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(toggle, 1);
                reshaped.Children.Add(labelStack); reshaped.Children.Add(toggle);
                Columns(reshaped);
                reshaped.Visibility = frame.Visibility;
                return reshaped;
            case StackPanel panel:
                for (int i = 0; i < panel.Children.Count; i++)
                    if (panel.Children[i] is FrameworkElement child && child is not Border { Child: null })
                    {
                        var replaced = Reflow(child);
                        if (!ReferenceEquals(replaced, child)) { panel.Children.RemoveAt(i); panel.Children.Insert(i, replaced); }
                    }
                return panel;
            default:
                return row;
        }
    }

    // General's row geometry: the label in two parts of the width (at most LabelColumnMax), the control in three.
    private static void Columns(Grid row)
    {
        row.ColumnDefinitions.Clear();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MaxWidth = LabelColumnMax });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.MinHeight = 48; row.Height = double.NaN; row.Margin = new Thickness(0, 12, 0, 12);
    }

    // A long setting name breaks onto a second line in the label column instead of ending in an ellipsis.
    private static void Wrap(DependencyObject labels)
    {
        if (labels is TextBlock name) { name.TextWrapping = TextWrapping.Wrap; name.TextTrimming = TextTrimming.None; return; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(labels); i++) Wrap(VisualTreeHelper.GetChild(labels, i));
        if (labels is Panel panel) foreach (UIElement child in panel.Children) if (child is TextBlock text) Wrap(text);
    }

    private TextBlock Description(string text)
    {
        var line = TextLine(text, 12.5, TextTertiary);
        line.TextWrapping = TextWrapping.Wrap; line.Margin = new Thickness(0, 2, 0, 0);
        return line;
    }

    // A slider and its number box together, the same length on every slider row.
    private Grid InlineSlider(string title, double value, double min, double max, double tick, string unit, Action<double> changed, string? help = null, bool keepPreview = false)
    {
        var controls = NumericControls(title, value, min, max, tick, unit, changed);
        if (keepPreview) { KeepPreview(controls.Slider); KeepPreview(controls.Number); }
        var panel = new Grid { Width = SliderColumn };
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.Slider.Margin = new Thickness(0, 0, 12, 0);
        controls.Number.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(controls.Number, 1);
        panel.Children.Add(controls.Slider); panel.Children.Add(controls.Number);
        return Row(title, panel, help: help is null ? null : () => help);
    }

    // A slider row that does not apply reads as off as a whole (SW-12): its name and number box fade as the slider does.
    private static void EnableRow(Grid row, bool enabled)
    {
        foreach (var part in row.Children.OfType<FrameworkElement>())
        {
            if (Grid.GetColumn(part) == 0) { part.Opacity = enabled ? 1 : .4; continue; }
            part.IsEnabled = enabled;
            if (part is Panel panel)
                foreach (var inner in panel.Children.OfType<FrameworkElement>().Where(inner => inner is not Slider)) inner.Opacity = enabled ? 1 : .4;
        }
    }

    // A sample of the chosen interface font in a quiet inset under the font rows.
    private FrameworkElement FontPreview(TextBlock sample)
    {
        var caption = TextLine(Loc.T("预览", "Preview"), 12, TextTertiary);
        caption.Margin = new Thickness(0, 0, 0, 4);
        sample.MaxWidth = double.PositiveInfinity;
        sample.TextTrimming = TextTrimming.None;
        sample.TextWrapping = TextWrapping.Wrap;
        return new Border
        {
            Tag = NoDivider, Background = UI.Brush(_palette.Inset), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 14), Child = new StackPanel { Children = { caption, sample } }
        };
    }

    // System, Light and Dark as three small pictures of a window, the chosen one ringed and ticked (SettingsChoice).
    private FrameworkElement ThemeCards()
    {
        var cards = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var theme in Enum.GetValues<SettingsTheme>())
        {
            string name = theme switch { SettingsTheme.System => Loc.T("跟随系统", "System"), SettingsTheme.Light => Loc.T("浅色", "Light"), _ => Loc.T("深色", "Dark") };
            var card = new RadioButton
            {
                Style = (Style)FindResource("SettingsChoice"), Content = ThemePreview(theme), Tag = name, GroupName = "Theme", IsChecked = S.Theme == theme,
                Width = ThemeCardWidth, Margin = new Thickness(0, 0, cards.Children.Count < 2 ? 8 : 0, 0)
            };
            AutomationProperties.SetName(card, name);
            card.Checked += (_, _) => { Change(s => s.Theme = theme); RebuildTheme(); };
            cards.Children.Add(card);
        }
        return cards;
    }

    // A window in the theme's own colors: a sidebar, two lines of text, a card and an accent bar. System shows Light on the left
    // half and Dark on the right.
    private static FrameworkElement ThemePreview(SettingsTheme theme)
    {
        const double width = 78, height = 50;
        FrameworkElement Window(bool dark)
        {
            var palette = new SettingsPalette(dark);
            var canvas = new Canvas { Width = width, Height = height, Background = UI.Brush(palette.Surface) };
            void Add(UIElement part, double left, double top) { Canvas.SetLeft(part, left); Canvas.SetTop(part, top); canvas.Children.Add(part); }
            Add(new Rectangle { Width = 20, Height = height, Fill = UI.Brush(palette.Sidebar) }, 0, 0);
            Add(new Rectangle { Width = 12, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = UI.Brush(palette.Tertiary) }, 4, 7);
            Add(new Rectangle { Width = 10, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = UI.Brush(palette.Divider) }, 4, 14);
            Add(new Rectangle { Width = 26, Height = 4, RadiusX = 2, RadiusY = 2, Fill = UI.Brush(palette.Primary) }, 27, 7);
            Add(new Rectangle { Width = 40, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = UI.Brush(palette.Tertiary) }, 27, 15);
            Add(new Rectangle { Width = 44, Height = 18, RadiusX = 3, RadiusY = 3, Fill = UI.Brush(palette.Inset) }, 27, 24);
            Add(new Rectangle { Width = 14, Height = 4, RadiusX = 2, RadiusY = 2, Fill = UI.Brush(palette.Accent) }, 31, 31);
            return canvas;
        }
        var picture = new Grid { Width = width, Height = height, Clip = new RectangleGeometry(new Rect(0, 0, width, height), 7, 7) };
        if (theme != SettingsTheme.Dark) picture.Children.Add(Window(false));
        if (theme != SettingsTheme.Light)
        {
            var dark = Window(true);
            if (theme == SettingsTheme.System) dark.Clip = new RectangleGeometry(new Rect(width / 2, 0, width / 2, height));
            picture.Children.Add(dark);
        }
        picture.HorizontalAlignment = HorizontalAlignment.Center;
        return picture;
    }
}
