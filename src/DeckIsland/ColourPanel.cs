using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        var header = PanelHeader.For(30, 18);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        ColourContent.Margin = new Thickness(header.Side, 0, header.Side, 8);
        var previous = ColourContent.Children.OfType<ColourPanel>().FirstOrDefault();
        if (previous?.Appearance == (Look.Generation, Loc.Language)) return;
        if (previous is not null) _toolPins.RemoveAll(item => item.Button == previous.Pin);
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
        private readonly TextBlock _status;
        private readonly StackPanel _values = new(), _contrast = new(), _harmonies = new();
        private readonly StackPanel[] _views;
        private readonly WrapPanel _recent = new();
        private readonly Border _swatch = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0) };
        private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly List<Button> _sectionButtons = [];
        private int _section;
        public (int Generation, string Language) Appearance { get; }
        public Button Pin { get; }

        public ColourPanel(MainWindow owner, ColourPanel? previous)
        {
            _owner = owner;
            Appearance = (Look.Generation, Loc.Language);
            _entry = Field(Loc.T("颜色值", "Colour value"), previous?._entry.Text ?? _app.Settings.RecentColours.FirstOrDefault() ?? "");
            _against = Field(Loc.T("对比颜色", "Contrast colour"), previous?._against.Text ?? "#808080");
            var contrastInput = InputRow(_against, Loc.T("应用", "Use"), Loc.T("取对比色", "Pick contrast colour"));
            var contrastView = new StackPanel { Children = { contrastInput, _contrast } };
            _views = [_values, contrastView, _harmonies];
            _status = Text("", 11, Look.Secondary);
            AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

            var sections = new StackPanel { Orientation = Orientation.Horizontal };
            var names = new[] { Loc.T("数值", "Values"), Loc.T("对比度", "Contrast"), Loc.T("配色", "Harmonies") };
            for (int i = 0; i < names.Length; i++)
            {
                int section = i;
                var button = ActionButton(names[i], () => SelectSection(section));
                _sectionButtons.Add(button);
                sections.Children.Add(button);
            }
            Pin = owner.ToolPinButton();
            Pin.Margin = new Thickness(6, 0, 0, 0);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Children = { sections, Pin } };
            var title = Text(Loc.T("颜色", "Colour"), 14, Look.Primary);
            title.FontWeight = FontWeights.SemiBold;
            title.VerticalAlignment = VerticalAlignment.Center;
            var header = new DockPanel { Children = { controls, title } };
            SetDock(controls, DockSide.Right);
            SetDock(header, DockSide.Top);
            Children.Add(header);
            var input = InputRow(_entry, "", Loc.T("从屏幕取色", "Pick from screen"));
            SetDock(input, DockSide.Top);
            Children.Add(input);
            var formatHint = Text(Loc.T("十六进制、RGB、HSL、OKLCH 或 CSS 颜色名", "HEX, RGB, HSL, OKLCH or a CSS colour name"), 11, Look.Tertiary);
            var help = new DockPanel { Margin = new Thickness(0, 4, 0, 4), Children = { _status, formatHint } };
            SetDock(_status, DockSide.Right);
            SetDock(help, DockSide.Top);
            Children.Add(help);
            _recent.Margin = new Thickness(0, 4, 0, 0);
            SetDock(_recent, DockSide.Bottom);
            Children.Add(_recent);
            Children.Add(_scroll);
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
            {
                bool current = i == section;
                _sectionButtons[i].Background = UI.Brush(Look.Hex(current ? "fillSelected" : "fill"));
                _sectionButtons[i].Foreground = UI.Brush(current ? Look.Primary : Look.Secondary);
            }
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
                Text = value, MaxLength = 256, Height = 32, Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = UI.Brush(Look.Hex("fill")), Foreground = UI.Brush(Look.Primary),
                CaretBrush = UI.Brush(Look.Primary), BorderThickness = new Thickness(0)
            };
            AutomationProperties.SetName(field, name);
            _owner.TakeKeyboardOnClick(field);
            field.TextChanged += (_, _) => Paint();
            field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Commit(field); } };
            return field;
        }

        private DockPanel InputRow(TextBox field, string apply, string sample)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            if (apply.Length > 0) actions.Children.Add(ActionButton(apply, () => Commit(field)));
            var picker = ActionButton(sample);
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
            row.Children.Add(field);
            return row;
        }

        private Button ActionButton(string label, Action? action = null)
        {
            var button = new Button
            {
                Content = label, Style = (Style)_owner.FindResource("WidgetSurfaceButton"), Template = FlatPill(),
                Background = UI.Brush(Look.Hex("fill")), Foreground = UI.Brush(Look.Primary),
                Margin = new Thickness(4, 1, 0, 1), Padding = new Thickness(8, 4, 8, 4)
            };
            if (action is not null) button.Click += (_, _) => action();
            return button;
        }

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
                if (CssColour.TryParse(hex, out var colour)) _recent.Children.Add(Swatch(colour, () => Choose(_entry, colour), compact: true));
        }

        private void Paint()
        {
            _values.Children.Clear();
            _contrast.Children.Clear();
            _harmonies.Children.Clear();
            if (!CssColour.TryParse(_entry.Text, out var colour))
            {
                _swatch.Background = UI.Brush(Look.Hex("fill"));
                _status.Text = _entry.Text.Length == 0 ? "" : Loc.T("请输入有效的颜色。", "Enter a valid colour.");
                foreach (var panel in new[] { _values, _contrast, _harmonies })
                {
                    var message = Text(Loc.T("从屏幕取色或输入颜色。", "Pick a colour from the screen or type one."), 13, Look.Secondary);
                    message.Margin = new Thickness(0, 18, 0, 0);
                    message.TextAlignment = TextAlignment.Center;
                    panel.Children.Add(message);
                }
                return;
            }

            _swatch.Background = BrushFor(colour);
            _status.Text = "";
            foreach (var (name, value) in new[] { ("HEX", colour.Hex), ("RGB", colour.Rgb), ("HSL", colour.Hsl), ("OKLCH", colour.Oklch), ("CSS", $"--colour: {colour.Hex};") })
            {
                var button = ActionButton($"{name}   {value}", () =>
                {
                    bool copied = _app.Shelf.CopyToolText(value);
                    if (copied) Remember(colour);
                    _status.Text = copied ? Loc.T("已复制", "Copied") : Loc.T("剪贴板正忙", "Clipboard is busy");
                });
                AutomationProperties.SetName(button, $"Copy {name}: {value}");
                button.HorizontalAlignment = HorizontalAlignment.Left;
                _values.Children.Add(button);
            }

            ContrastRow(colour, White, Loc.T("白色", "White"));
            ContrastRow(colour, Black, Loc.T("黑色", "Black"));
            if (CssColour.TryParse(_against.Text, out var background)) ContrastRow(colour, background, Loc.T("自定义", "Custom"));
            else _contrast.Children.Add(Text(Loc.T("输入有效的对比颜色。", "Enter a valid contrast colour."), 12, Look.Secondary));

            Harmony(Loc.T("互补", "Complementary"), colour, 180);
            Harmony(Loc.T("三色", "Triad"), colour, 120, 240);
            Harmony(Loc.T("类似", "Analogous"), colour, -30, 30);
        }

        private void ContrastRow(CssColour colour, CssColour background, string label)
        {
            double ratio = colour.Contrast(background);
            string status(double threshold) => ratio >= threshold ? "✓" : "×";
            string value = ratio.ToString("0.00", CultureInfo.InvariantCulture);
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            if (colour.AaSuggestion(background) is { } suggestion)
            {
                var use = ActionButton($"{Loc.T("使用", "Use")} {suggestion.Hex}", () => Choose(_entry, suggestion));
                DockPanel.SetDock(use, DockSide.Right);
                row.Children.Add(use);
            }
            string large = Loc.T("AA 大号", "AA large");
            var description = Text($"{label}  {value}:1   AA {status(4.5)}   {large} {status(3)}   AAA {status(7)}", 12, Look.Primary);
            description.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(description);
            _contrast.Children.Add(row);
        }

        private void Harmony(string title, CssColour colour, params double[] offsets)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var label = Text(title, 12, Look.Primary);
            label.Width = 110;
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            foreach (double offset in offsets)
            {
                var related = colour.RotateHue(offset);
                row.Children.Add(Swatch(related, () => Choose(_entry, related)));
            }
            _harmonies.Children.Add(row);
        }

        private Button Swatch(CssColour colour, Action select, bool compact = false)
        {
            var button = ActionButton(colour.Hex, select);
            button.ToolTip = colour.Hex;
            AutomationProperties.SetName(button, $"{Loc.T("使用", "Use")} {colour.Hex}");
            var swatch = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(3),
                Background = BrushFor(colour),
                BorderBrush = UI.Brush(Look.Hex("outline")), BorderThickness = new Thickness(1)
            };
            button.Content = compact ? swatch : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { swatch, new TextBlock { Text = colour.Hex, Margin = new Thickness(5, 0, 0, 0) } }
            };
            return button;
        }
    }
}
