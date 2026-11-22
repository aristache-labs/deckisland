using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PathGlyph = System.Windows.Shapes.Path;
using DeckIsland.Core;
using DeckIsland.Native;
using DockSide = System.Windows.Controls.Dock;

namespace DeckIsland;

public partial class MainWindow
{
    private bool _colourPicking;

    private void ColourPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Colour);

    private void RenderColour()
    {
        RefreshStatus();
        foreach (var page in DeckPages.Order)
            if (page != DeckPage.Colour) PageContent(page).Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        if (!double.IsFinite(ContentCanvas.Height)) return;
        var header = PanelHeader.For(32, 18);
        if (Settings.UsagePage) header = header.WithControlsOf(CachedUsageHeader());
        ApplyHeader(header with { Row = Math.Max(header.Row, header.Height) });
        ColourContent.Margin = new Thickness(header.Side, 20, header.Side, 16);
        ShowHeaderPin(header);
        var previous = ColourContent.Children.OfType<ColourPanel>().FirstOrDefault();
        if (previous?.Appearance == (Look.Generation, Loc.Language)) return;
        ColourContent.Children.Clear();
        ColourContent.Children.Add(new ColourPanel(this, previous));
    }

    private sealed class ColourPanel : DockPanel
    {
        private static readonly CssColour White = new(255, 255, 255);
        private static readonly CssColour Black = new(0, 0, 0);
        private readonly MainWindow _owner;
        private App _app => _owner._app;
        private PanelLook Look => _owner.Look;
        private readonly TextBox _entry, _against;
        private readonly DockPanel _secondColour;
        private readonly TextBlock _status;
        private readonly TextBlock _empty;
        private readonly Button _clear, _delete;
        private const string PickerGlyph = "M2,10 V13 H5 L12,6 M2,10 L9,3 M8,2 L13,7";
        private const string CopyGlyph = "M6,5 H12 V13 H5 V6 M5,10 H2 V2 H9 V5";
        private readonly StackPanel _values = new(), _contrast = new(), _harmonies = new();
        private readonly StackPanel[] _views;
        private readonly StackPanel _customResult = new(), _customAdjustment = new();
        private readonly ContentControl _customApply = new();
        private readonly StackPanel _standardContrast = new();
        private readonly WrapPanel _recent = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _swatch = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0) };
        private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly List<Button> _sectionButtons = [];
        private int _section;
        public (int Generation, string Language) Appearance { get; }

        public ColourPanel(MainWindow owner, ColourPanel? previous)
        {
            _owner = owner;
            Appearance = (Look.Generation, Loc.Language);
            _entry = Field(Loc.T("颜色值", "Colour value"), previous?._entry.Text ?? _app.Settings.RecentColours.FirstOrDefault() ?? "");
            _against = Field(Loc.T("对比颜色", "Second colour"), previous?._against.Text ?? "");
            _against.FontSize = 11;
            _secondColour = InputRow(_against, "", Loc.T("取对比色", "Pick contrast colour"), compact: true);
            // The suggestion follows the badges on the same line, as on the white and black rows.
            _customAdjustment.Margin = new Thickness(12, 0, 0, 0); _customAdjustment.VerticalAlignment = VerticalAlignment.Center;
            var custom = new WrapPanel { Children = { ContrastLine(_secondColour, _customResult), _customAdjustment } };
            _values.Margin = new Thickness(0, 9, 0, 0);
            _contrast.Children.Add(_standardContrast);
            _contrast.Children.Add(ContrastCard(custom, _customApply));
            _views = [_values, _contrast, _harmonies];
            _status = Text("", 11, Look.Secondary);
            AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

            var sections = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var segments = owner.PanelSegmentTrack(sections);
            var names = new[] { Loc.T("数值", "Values"), Loc.T("对比度", "Contrast"), Loc.T("配色", "Harmonies") };
            for (int i = 0; i < names.Length; i++)
            {
                int section = i;
                var button = owner.PanelSegment(names[i], () => SelectSection(section));
                _sectionButtons.Add(button);
                sections.Children.Add(button);
            }
            // No title row (the page switch names the tab): the value, the pick button and, at the right, the sections share the top line.
            // The pin is in the panel's header, beside the gear.
            var input = InputRow(_entry, "", Loc.T("取色", "Pick color"));
            input.Margin = new Thickness(0);
            segments.Margin = new Thickness(12, 0, 0, 0);
            var top = new DockPanel { Children = { segments, input } };
            SetDock(segments, DockSide.Right);
            SetDock(top, DockSide.Top);
            Children.Add(top);
            var formatHint = Text(Loc.T("十六进制、RGB、HSL、OKLCH 或 CSS 颜色名", "HEX, RGB, HSL, OKLCH or a CSS colour name"), 11, Look.Tertiary);
            var help = new DockPanel { Margin = new Thickness(0, 11, 0, 10), Children = { _status, formatHint } };
            SetDock(_status, DockSide.Right);
            SetDock(help, DockSide.Top);
            Children.Add(help);
            _clear = IconButton("M3,3 L11,11 M11,3 L3,11", Loc.T("清除当前颜色", "Clear current colour"), () => _entry.Clear(), size: 10);
            _delete = IconButton("M3,4 H11 M5,4 V2 H9 V4 M4,4 L5,12 H9 L10,4 M7,6 V10",
                Loc.T("删除最近颜色", "Delete recent colours"), () =>
                {
                    _app.ChangeShared(settings => settings.RecentColours = []);
                    Recent();
                }, size: 14);
            var footerActions = new StackPanel { Orientation = Orientation.Horizontal, Children = { _clear, _delete } };
            SetDock(footerActions, DockSide.Right);
            var recentScroll = new ScrollViewer
            {
                Content = _recent, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var footer = new DockPanel { MinHeight = 30, Margin = new Thickness(9, 8, 9, 0), Children = { footerActions, recentScroll } };
            SetDock(footer, DockSide.Bottom);
            Children.Add(footer);
            _empty = Text(Loc.T("从屏幕取色或输入颜色。", "Pick a colour from the screen or type one."),
                13, Look.Secondary);
            _empty.TextAlignment = TextAlignment.Center;
            _empty.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(new Grid { Children = { _scroll, _empty } });
            Paint();
            Recent();
            SelectSection(previous?._section ?? 0);
        }

        private void SelectSection(int section)
        {
            _section = section;
            _scroll.Content = _views[section];
            _scroll.ScrollToTop();
            for (int i = 0; i < _sectionButtons.Count; i++)
                _owner.StylePanelSegment(_sectionButtons[i], i == section);
        }

        private static TextBlock Text(string value, double size, string colour) => new()
        {
            Text = value, FontSize = size, Foreground = UI.Brush(colour), TextWrapping = TextWrapping.Wrap
        };

        private static Brush BrushFor(CssColour colour) => new SolidColorBrush(Color.FromArgb(colour.A, colour.R, colour.G, colour.B));

        private TextBox Field(string name, string value)
        {
            var field = new TextBox
            {
                Text = value, MaxLength = 256, Height = 30, Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, Foreground = UI.Brush(Look.Primary),
                CaretBrush = UI.Brush(Look.Primary), BorderThickness = new Thickness(0)
            };
            AutomationProperties.SetName(field, name);
            _owner.TakeKeyboardOnClick(field);
            field.TextChanged += (_, _) => Paint();
            field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Commit(field); } };
            return field;
        }

        private DockPanel InputRow(TextBox field, string apply, string sample, bool compact = false)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            if (apply.Length > 0) actions.Children.Add(ActionButton(apply, () => Commit(field)));
            var picker = compact ? IconButton(PickerGlyph, sample) : ActionButton(sample);
            if (!compact)
            {
                string ink = Look.Primary;
                var glyph = ButtonGlyph(PickerGlyph, 12, ink);
                glyph.Margin = new Thickness(0, 0, 7, 0);
                var caption = new TextBlock { Text = sample, Foreground = UI.Brush(ink), FontSize = 12, FontWeight = FontWeights.SemiBold };
                picker.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, caption } };
                picker.Style = (Style)_owner.FindResource("PanelIconButton");
                picker.Background = UI.Brush(Look.Fill);
                picker.BorderBrush = UI.Brush(Look.Outline);
                picker.Padding = new Thickness(11, 0, 11, 0);
                AutomationProperties.SetName(picker, sample);
            }
            picker.Height = 30;
            picker.VerticalAlignment = VerticalAlignment.Center;
            picker.Margin = new Thickness(8, 0, 0, 0);
            picker.Click += async (_, _) =>
            {
                if (_owner._colourPicking) return;
                _owner._colourPicking = true;
                try
                {
                    if (await ScreenColourPicker.Pick() is { } colour && IsLoaded) Choose(field, colour);
                }
                catch (Win32Exception)
                {
                    _status.Text = Loc.T("无法读取屏幕像素。", "Couldn’t read the screen pixels.");
                }
                finally { _owner._colourPicking = false; }
            };
            actions.Children.Add(picker);
            DockPanel.SetDock(actions, DockSide.Right);
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Children = { actions } };
            if (field == _entry)
            {
                DockPanel.SetDock(_swatch, DockSide.Left);
                row.Children.Add(_swatch);
            }
            var chrome = new Border
            {
                CornerRadius = new CornerRadius(Look.Corner(7)), Background = UI.Brush(Look.Fill),
                BorderThickness = new Thickness(1), BorderBrush = UI.Brush(Look.Hex("outline")), Child = field
            };
            if (field == _entry)
            {
                // A colour value is short (#RRGGBB, rgb(…)): a fitted field with the pick button right beside it, not a full-width bar.
                chrome.Width = 170;
                row.HorizontalAlignment = HorizontalAlignment.Left;
            }
            field.IsKeyboardFocusWithinChanged += (_, _) =>
                chrome.BorderBrush = UI.Brush(field.IsKeyboardFocusWithin ? Look.Accent : Look.Hex("outline"));
            if (compact)
            {
                row.Width = 108;
                row.Margin = new Thickness(0, 0, 6, 0);
                // As tall as the contrast samples beside it, so this row is as tall as the white and black rows.
                field.Height = picker.Height = 24;
                var placeholder = Text(Loc.T("第二种颜色", "Second colour"), 11, Look.Tertiary);
                placeholder.VerticalAlignment = VerticalAlignment.Center;
                placeholder.Margin = new Thickness(7, 0, 0, 0);
                placeholder.IsHitTestVisible = false;
                placeholder.TextWrapping = TextWrapping.NoWrap;
                void UpdateHint() => placeholder.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                field.TextChanged += (_, _) => UpdateHint();
                UpdateHint();
                row.Children.Add(new Grid { Children = { chrome, placeholder } });
            }
            else row.Children.Add(chrome);
            return row;
        }

        private Button ActionButton(string label, Action? action = null)
        {
            var button = new Button
            {
                Content = label, Style = (Style)_owner.FindResource("PanelSourceButton"), Width = double.NaN, Height = double.NaN,
                Background = UI.Brush(Look.Hex("fill")), Foreground = UI.Brush(Look.Primary),
                Margin = new Thickness(4, 1, 0, 1), Padding = new Thickness(8, 4, 8, 4)
            };
            if (action is not null) button.Click += (_, _) => action();
            return button;
        }

        private Button IconButton(string geometry, string label, Action? action = null, double size = 12)
        {
            var button = ActionButton("", action);
            button.Width = button.Height = 26;
            button.Padding = new Thickness(0);
            button.Background = Brushes.Transparent;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
            button.Content = ButtonGlyph(geometry, size, Look.Secondary);
            return button;
        }

        private static PathGlyph ButtonGlyph(string geometry, double size, string colour) => new()
        {
            Data = Geometry.Parse(geometry), Width = size, Height = size, Stretch = Stretch.Uniform,
            Stroke = UI.Brush(colour), StrokeThickness = 1.35, VerticalAlignment = VerticalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };

        private void Commit(TextBox field)
        {
            if (CssColour.TryParse(field.Text, out var colour)) Choose(field, colour);
        }

        private void Choose(TextBox field, CssColour colour)
        {
            field.Text = colour.Hex;
            Remember(colour);
        }

        private void Remember(CssColour colour)
        {
            _app.ChangeShared(settings => settings.RecentColours = [colour.Hex, .. settings.RecentColours.Where(hex => hex != colour.Hex).Take(11)]);
            Recent();
        }

        private void Recent()
        {
            _recent.Children.Clear();
            foreach (string hex in _app.Settings.RecentColours)
                if (CssColour.TryParse(hex, out var colour)) _recent.Children.Add(RecentSwatch(colour));
            _delete.IsEnabled = _recent.Children.Count != 0;
            _delete.Opacity = _delete.IsEnabled ? 1 : .4;
            if (!_delete.IsEnabled)
            {
                var hint = Text(Loc.T("选过的颜色会显示在这里", "Chosen colours appear here"), 11, Look.Tertiary);
                hint.Margin = new Thickness(0, 7, 0, 0);
                _recent.Children.Add(hint);
            }
        }

        private void Paint()
        {
            _values.Children.Clear();
            _standardContrast.Children.Clear();
            _customResult.Children.Clear();
            _customAdjustment.Children.Clear();
            _customApply.Content = null;
            _harmonies.Children.Clear();
            bool valid = CssColour.TryParse(_entry.Text, out var colour);
            _clear.IsEnabled = _entry.Text.Length > 0;
            _clear.Opacity = _clear.IsEnabled ? 1 : .4;
            _empty.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            _scroll.Visibility = valid ? Visibility.Visible : Visibility.Hidden;
            _swatch.Background = valid ? BrushFor(colour) : UI.Brush(Look.Hex("fill"));
            _status.Text = !valid && _clear.IsEnabled ? Loc.T("请输入有效的颜色。", "Enter a valid colour.") : "";
            if (!valid) return;

            foreach (var (name, value) in new[]
            {
                ("HEX", colour.Hex), ("RGB", colour.Rgb), ("HSL", colour.Hsl), ("OKLCH", colour.Oklch),
                (Loc.T("CSS 变量", "CSS variable"), $"--colour: {colour.Hex};")
            })
            {
                var copy = IconButton(CopyGlyph, $"{Loc.T("复制", "Copy")} {name}", () =>
                {
                    bool copied = _app.Shelf.CopyToolText(value);
                    if (copied) Remember(colour);
                    _status.Text = copied ? Loc.T("已复制", "Copied") : Loc.T("剪贴板正忙", "Clipboard is busy");
                });
                var label = Text(name, 11.5, Look.Secondary);
                label.Width = 88;
                label.VerticalAlignment = VerticalAlignment.Center;
                var code = Text(value, 12, Look.Primary);
                code.FontFamily = new FontFamily("Consolas");
                code.VerticalAlignment = VerticalAlignment.Center;
                var row = new DockPanel { MinHeight = 28, Margin = new Thickness(0, 0, 0, 4), Children = { copy, label, code } };
                SetDock(copy, DockSide.Right);
                _values.Children.Add(row);
            }

            ContrastRow(colour, White, Loc.T("白色背景", "On white"));
            ContrastRow(colour, Black, Loc.T("黑色背景", "On black"));
            CssColour? second = CssColour.TryParse(_against.Text, out var background) ? background : null;
            _customResult.Children.Add(ContrastPreview(colour, second));
            if (second is { } custom && Suggestion(colour, custom) is { } adjustment)
            {
                _customAdjustment.Children.Add(adjustment.Details);
                _customApply.Content = adjustment.Apply;
            }
            else if (second is null && _against.Text.Length > 0)
                _customAdjustment.Children.Add(Text(Loc.T("请输入有效的颜色。", "Enter a valid colour."), 11, Look.Secondary));

            Harmony(Loc.T("互补", "Complementary"), colour, 0, 180);
            Harmony(Loc.T("三色", "Triad"), colour, 0, 120, 240);
            Harmony(Loc.T("类似", "Analogous"), colour, -30, 0, 30);
        }

        private Border ColourCard(UIElement content) => new()
        {
            Background = UI.Brush(Look.Hex("fill")), CornerRadius = new CornerRadius(Look.Corner(9)),
            Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 4), Child = content
        };

        private void ContrastRow(CssColour colour, CssColour background, string label)
        {
            var title = Text(label, 12, Look.Secondary);
            title.VerticalAlignment = VerticalAlignment.Center;
            var row = ContrastLine(title, ContrastPreview(colour, background));
            // The suggestion follows the badges on the same line, and goes below only when the page is too narrow for both.
            var stack = new WrapPanel { Children = { row } };
            var action = new ContentControl();
            if (Suggestion(colour, background) is { } suggestion)
            {
                suggestion.Details.Margin = new Thickness(12, 0, 0, 0);
                suggestion.Details.VerticalAlignment = VerticalAlignment.Center;
                stack.Children.Add(suggestion.Details);
                action.Content = suggestion.Apply;
            }
            _standardContrast.Children.Add(ContrastCard(stack, action));
        }

        private Border ContrastCard(UIElement content, ContentControl action)
        {
            action.Width = 66;
            action.Margin = new Thickness(8, 0, 4, 0);
            action.HorizontalContentAlignment = HorizontalAlignment.Right;
            action.VerticalContentAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(action, DockSide.Right);
            return ColourCard(new DockPanel { Children = { action, content } });
        }

        private static Grid ContrastLine(UIElement label, UIElement preview)
        {
            var row = new Grid { MinHeight = 30, Children = { label, preview } };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(preview, 1);
            return row;
        }

        private WrapPanel ContrastPreview(CssColour colour, CssColour? background)
        {
            double? ratio = background is { } other ? colour.Contrast(other) : null;
            var samples = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            samples.Children.Add(ContrastSample(background, colour));
            samples.Children.Add(ContrastSample(background.HasValue ? colour : null, background));
            var value = Text(ratio is double number ? number.ToString("0.00", CultureInfo.InvariantCulture) + ":1" : "", 12, Look.Primary);
            value.FontWeight = FontWeights.SemiBold;
            value.Width = 50;
            value.Margin = new Thickness(4, 0, 4, 0);
            value.VerticalAlignment = VerticalAlignment.Center;
            samples.Children.Add(value);
            var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (label, threshold) in new[] { ("AA", 4.5), (Loc.T("AA 大号", "AA large"), 3d), ("AAA", 7d) })
            {
                bool passes = ratio >= threshold;
                var badge = new Border
                {
                    Margin = new Thickness(2, 1, 2, 1), Padding = new Thickness(4, 2, 4, 2),
                    CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Center,
                    Background = passes ? UI.Brush(Look.Hex("fillSelected")) : UI.Brush(Look.Hex("hover")),
                    Child = Text((ratio is null ? "" : passes ? "✓ " : "× ") + label, 10.5, passes ? Look.Primary : Look.Secondary)
                };
                AutomationProperties.SetName(badge, ratio is null ? label : $"{label} {(passes ? Loc.T("通过", "passes") : Loc.T("未通过", "fails"))}");
                badges.Children.Add(badge);
            }
            return new WrapPanel { Children = { samples, badges } };
        }

        private Border ContrastSample(CssColour? background, CssColour? foreground) => new()
        {
            Width = 33, Height = 24, Margin = new Thickness(0, 0, 3, 0), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1), BorderBrush = UI.Brush(Look.Hex("outline")),
            Background = background is { } bg ? BrushFor(bg) : UI.Brush(Look.Hex("hover")),
            Child = new TextBlock
            {
                Text = foreground.HasValue ? "Aa" : "", FontWeight = FontWeights.SemiBold,
                Foreground = foreground is { } fg ? BrushFor(fg) : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };

        private (WrapPanel Details, Button Apply)? Suggestion(CssColour colour, CssColour background)
        {
            if (colour.AaSuggestion(background) is not { } suggestion) return null;
            var apply = ActionButton(Loc.T("应用", "Apply"), () => Choose(_entry, suggestion));
            AutomationProperties.SetName(apply, $"{Loc.T("应用", "Apply")} {suggestion.Hex}");
            apply.Height = 21;
            apply.Margin = new Thickness(0);
            apply.Padding = new Thickness(7, 0, 7, 0);
            apply.FontSize = 11;
            apply.Style = (Style)_owner.FindResource("PanelIconButton");
            apply.BorderBrush = UI.Brush(Look.Outline);
            bool darker = suggestion.Contrast(White) > (colour with { A = 255 }).Contrast(White);
            string label = darker ? Loc.T("调暗以达到 AA", "Darker for AA") : Loc.T("调亮以达到 AA", "Lighter for AA");
            var swatch = new Border
            {
                Height = 13, Width = 13, CornerRadius = new CornerRadius(7), Background = BrushFor(suggestion),
                Margin = new Thickness(6, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center
            };
            string ratio = suggestion.Contrast(background).ToString("0.00", CultureInfo.InvariantCulture);
            var summary = Text(label, 11, Look.Secondary);
            var value = Text($"{suggestion.Hex}   {ratio}:1", 11, Look.Primary);
            summary.VerticalAlignment = value.VerticalAlignment = VerticalAlignment.Center;
            var details = new WrapPanel { Margin = new Thickness(0, 1, 0, 0), Children = { summary, swatch, value } };
            return (details, apply);
        }

        private void Harmony(string title, CssColour colour, params double[] offsets)
        {
            var row = new WrapPanel();
            var label = Text(title, 12, Look.Secondary);
            label.Width = 108;
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            foreach (double offset in offsets)
            {
                var related = offset == 0 ? colour : colour.RotateHue(offset);
                var button = ActionButton("", () => Choose(_entry, related));
                button.Width = 66;
                button.Margin = new Thickness(0, 0, 5, 0);
                button.Padding = new Thickness(0);
                button.Background = Brushes.Transparent;
                button.ToolTip = related.Hex;
                AutomationProperties.SetName(button, $"{Loc.T("使用", "Use")} {related.Hex}");
                // The code is written on the colour, in black or white, whichever reads better on it.
                var value = new TextBlock
                {
                    Text = related.Hex, FontSize = 10, FontFamily = new FontFamily("Consolas"),
                    Foreground = related.Contrast(White) >= related.Contrast(Black) ? Brushes.White : Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                button.Content = new Border { Background = BrushFor(related), Width = 66, Height = 25, CornerRadius = new CornerRadius(6), Child = value };
                row.Children.Add(button);
            }
            _harmonies.Children.Add(ColourCard(row));
        }

        private Button RecentSwatch(CssColour colour)
        {
            var button = ActionButton("", () => Choose(_entry, colour));
            button.Background = Brushes.Transparent;
            button.Padding = new Thickness(0);
            button.Width = button.Height = 24;
            button.Template = FlatPill();
            button.Margin = new Thickness(0, 0, 6, 0);
            button.ToolTip = colour.Hex;
            AutomationProperties.SetName(button, $"{Loc.T("使用", "Use")} {colour.Hex}");
            var swatch = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                Background = BrushFor(colour), BorderBrush = UI.Brush(Look.Hex("outline")), BorderThickness = new Thickness(1)
            };
            button.MouseEnter += (_, _) => swatch.BorderBrush = UI.Brush(Look.Primary);
            button.MouseLeave += (_, _) => swatch.BorderBrush = UI.Brush(Look.Hex("outline"));
            button.Content = swatch;
            return button;
        }
    }
}
