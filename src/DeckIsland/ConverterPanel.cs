using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckIsland.Core;

namespace DeckIsland;

// The Converter tab: units (length, weight, temperature, volume, speed), or currencies at the central banks' reference rates
// (App.Rates), any two of them through the base currency. It opens on the units and currencies used last; those choices are shared by
// every profile and read where they are kept (_app.Settings).
public partial class MainWindow
{
    private Grid? _converterBody;
    private TextBox? _converterAmount;
    private TextBlock? _converterResult, _converterNote;
    private (int Generation, string Language, ConverterMode Mode, string Pair, int Rates, int Width) _converterAppearance;
    private int _ratesVersion;

    private void InitializeConverter()
    {
        Action changed = () => Dispatcher.BeginInvoke(() =>
        {
            _ratesVersion++;
            if (CurrentPage == DeckPage.Converter && Settings.ConverterPage) RenderConverter();
        });
        _app.Rates.Changed += changed;
        Closed += (_, _) => _app.Rates.Changed -= changed;
    }

    // The pin of the Converter, Terminal and Colour tabs, one button in the panel's header left of the settings gear, bare and
    // as big as the gear. RenderPageSwitch hides it on the other tabs.
    private void ShowHeaderPin(PanelHeader header)
    {
        if (ToolPinSlot.Child is not Button pin)
        {
            pin = ToolPinButton();
            pin.Tag = ToolPinBare;
            ToolPinSlot.Child = pin;
            SetToolPin(_toolPinned);
        }
        pin.Width = pin.Height = header.SettingsHeight;
        ToolPinSlot.Visibility = Visibility.Visible;
    }

    private void ConverterPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Converter);

    private DeckSettings Shared => _app.Settings;

    private void RenderConverter()
    {
        RefreshStatus();
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility = ShelfContent.Visibility = DevicesContent.Visibility = WeatherContent.Visibility =
            FocusContent.Visibility = RemindersContent.Visibility = NotificationsContent.Visibility = CallsContent.Visibility = ScratchpadContent.Visibility = TerminalContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        if (!double.IsFinite(ContentCanvas.Height)) return;
        var header = PanelHeader.For(30, 18);
        if (Settings.UsagePage) header = header.WithControlsOf(CachedUsageHeader());
        ApplyHeader(header with { Row = Math.Max(header.Row, header.Height) });
        ShowHeaderPin(header);
        ConverterContent.Margin = new Thickness(header.Side, 0, header.Side, Math.Max(12, header.Side - 4));
        if (Shared.ConverterMode == ConverterMode.Currency) RefreshRates();
        var (from, to) = ConverterPair();
        var appearance = (Look.Generation, Loc.Language, Shared.ConverterMode, $"{Shared.ConverterUnitCategory}|{from}|{to}", _ratesVersion, (int)ContentCanvas.Width);
        if (_converterAppearance == appearance && ConverterContent.Children.Count > 0) return;
        _converterAppearance = appearance;
        ConverterContent.Children.Clear();
        BuildConverter();
    }

    // The rates are fetched in the background when a day old or when the base changed; the page is built again when they land.
    private void RefreshRates()
    {
        var rates = _app.Rates; string baseCode = Shared.ConverterBaseCurrency;
        if (!rates.IsStale(baseCode)) return;
        _ = Task.Run(async () =>
        {
            try { await rates.RefreshAsync(baseCode, cancellation: _app.Lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        });
    }

    // The two sides as they are shown: unit ids, or currency codes (the base and one other until chosen).
    private (string From, string To) ConverterPair()
    {
        var s = Shared;
        if (s.ConverterMode == ConverterMode.Units)
            return (UnitConversion.Find(s.ConverterUnitCategory, s.ConverterUnitFrom).Id, UnitConversion.Find(s.ConverterUnitCategory, s.ConverterUnitTo, to: true).Id);
        string from = s.ConverterCurrencyFrom.Length > 0 ? s.ConverterCurrencyFrom : s.ConverterBaseCurrency;
        string to = s.ConverterCurrencyTo.Length > 0 ? s.ConverterCurrencyTo : s.ConverterBaseCurrency == "USD" ? "EUR" : "USD";
        return (from, to);
    }

    private void BuildConverter()
    {
        var s = Shared;
        bool currency = s.ConverterMode == ConverterMode.Currency;
        var (from, to) = ConverterPair();

        // The page switch names the tab, so there is no title row: the two sides fill the top, and the mode, the unit category and
        // the rates' date share one line under them, clear of the header.
        _converterAmount = new TextBox
        {
            Text = s.ConverterAmount, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(Look.Primary), CaretBrush = UI.Brush(Look.Primary),
            SelectionBrush = UI.Brush(Look.Accent), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(-2, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center, FocusVisualStyle = null, Height = ConverterValueHeight
        };
        AutomationProperties.SetName(_converterAmount, Loc.T("数量", "Amount"));
        TakeKeyboardOnClick(_converterAmount);
        _converterAmount.TextChanged += (_, _) => { _app.ChangeShared(settings => settings.ConverterAmount = _converterAmount.Text.Length <= 40 ? _converterAmount.Text : settings.ConverterAmount); UpdateConversion(); };
        _converterResult = WeatherText("", 18);
        _converterResult.FontWeight = FontWeights.SemiBold;
        _converterResult.Height = ConverterValueHeight;
        _converterResult.LineHeight = ConverterValueHeight; _converterResult.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

        var swap = new Button { Style = (Style)FindResource("WidgetSurfaceButton"), Template = FlatPill(), Background = UI.Brush(Look.Hex("fill")), Width = 30, Height = 30,
            Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Content = new Path { Data = Geometry.Parse("M 4,8 H 19 M 15,4 L 19,8 L 15,12 M 20,16 H 5 M 9,12 L 5,16 L 9,20"), Stroke = UI.Brush(Look.Primary), StrokeThickness = 1.6,
                Width = 13, Height = 13, Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round } };
        AutomationProperties.SetName(swap, Loc.T("交换", "Swap"));
        swap.Click += (_, _) => SwapConverterSides();

        var cards = new Grid();
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        var leftCard = ConverterCard(_converterAmount, SideLabel(currency, from), anchor => PickConverterSide(anchor, fromSide: true));
        var rightCard = ConverterCard(_converterResult, SideLabel(currency, to), anchor => PickConverterSide(anchor, fromSide: false));
        Grid.SetColumn(swap, 1); Grid.SetColumn(rightCard, 2);
        cards.Children.Add(leftCard); cards.Children.Add(swap); cards.Children.Add(rightCard);

        // Under the cards, centred: the unit category, then the mode switch; the rates' date under them.

        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (!currency)
        {
            var category = CategoryChip(UnitConversion.Name(s.ConverterUnitCategory));
            category.Margin = new Thickness(0, 0, 8, 0);
            controls.Children.Add(category);
        }
        controls.Children.Add(ModeSwitch(currency));
        _converterNote = WeatherText("", 11.5);
        _converterNote.Foreground = UI.Brush(Look.Secondary);
        _converterNote.TextAlignment = TextAlignment.Center;
        _converterNote.Margin = new Thickness(0, 6, 0, 0);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { controls, _converterNote } };

        var body = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(footer, 1);
        body.Children.Add(cards); body.Children.Add(footer);
        _converterBody = body;
        ConverterContent.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top,
            Child = new Border { Width = Math.Max(300, ContentCanvas.Width - PanelHeader.For(30, 18).Side * 2), Child = body } });
        UpdateConversion();
    }

    private const double ConverterValueHeight = 28;

    // A side: its value on top and, under it, the full-width picker of its unit or currency, the name at the left and the chevron at the right.
    private Border ConverterCard(FrameworkElement value, string label, Action<FrameworkElement> open)
    {
        var text = PickerRowText(label, Look.Primary);
        text.FontSize = 12;
        var chevron = new Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L7,0"), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.4, Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(chevron, System.Windows.Controls.Dock.Right);
        row.Children.Add(chevron); row.Children.Add(text);
        var picker = new Button { Style = (Style)FindResource("WidgetSurfaceButton"), Template = StretchPill(), Background = UI.Brush(Look.Hex("fillSelected")), Height = 26,
            Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 6, 0, 0), Content = row };
        AutomationProperties.SetName(picker, label);
        picker.Click += (_, _) => open(picker);
        return new Border { CornerRadius = new CornerRadius(12), Background = UI.Brush(Look.Hex("fill")), Padding = new Thickness(12, 8, 10, 10),
            Child = new StackPanel { Children = { value, picker } } };
    }

    // FlatPill with its content stretched across, for a picker whose name sits at the left.
    private static ControlTemplate StretchPill()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .85));
        template.Triggers.Add(hover);
        return template;
    }

    private Border ModeSwitch(bool currency)
    {
        Button Segment(string label, bool on, ConverterMode mode)
        {
            var button = PanelSegment(label, () =>
            {
                if (on) return;
                _app.ChangeShared(settings => settings.ConverterMode = mode);
                RenderConverter();
            });
            StylePanelSegment(button, on);
            return button;
        }
        var units = Segment(Loc.T("单位", "Units"), !currency, ConverterMode.Units);
        var money = Segment(Loc.T("货币", "Currency"), currency, ConverterMode.Currency);
        return PanelSegmentTrack(new StackPanel { Orientation = Orientation.Horizontal, Children = { units, money } });
    }

    // The unit category as one more pill in the mode switch's own track, so the two read as the same control family.
    private Border CategoryChip(string label)
    {
        var chevron = new Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L7,0"), Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.4, Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        Border? track = null;
        var button = PanelSegment(label, () => { if (track is not null) PickUnitCategory(track); });
        button.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, chevron } };
        button.Padding = new Thickness(14, 0, 12, 0);
        StylePanelSegment(button, false);
        button.Foreground = UI.Brush(Look.Primary); button.Tag = null; // always reads as a value, not an unchosen option
        return track = PanelSegmentTrack(button);
    }

    private string SideLabel(bool currency, string id)
        => currency ? CurrencyLabel(id) : UnitConversion.Find(Shared.ConverterUnitCategory, id).Label;

    // "GBP · British Pound", or the code alone until the names are known.
    private string CurrencyLabel(string code) => _app.Rates.NameOf(code) is var name && name != code ? $"{code} · {name}" : code;

    private void UpdateConversion()
    {
        if (_converterAmount is null || _converterResult is null || _converterNote is null) return;
        var s = Shared;
        var (from, to) = ConverterPair();
        bool parsed = ParseAmount(_converterAmount.Text, out var amount);
        if (s.ConverterMode == ConverterMode.Units)
        {
            var unitFrom = UnitConversion.Find(s.ConverterUnitCategory, from);
            var unitTo = UnitConversion.Find(s.ConverterUnitCategory, to);
            _converterResult.Text = parsed ? FormatNumber(UnitConversion.Convert((double)amount, unitFrom, unitTo)) : "–";
            _converterNote.Text = "";
            return;
        }
        var rates = _app.Rates;
        var value = parsed ? rates.Convert(amount, from, to) : null;
        _converterResult.Text = value is { } v ? FormatMoney(v, to) : "–";
        _converterNote.Text = rates.DateFor(from, to) is { } date
            ? Loc.T($"央行参考汇率，{date:M月d日}", $"Central bank reference rates, {date.ToString("d MMM", CultureInfo.InvariantCulture)}")
            : rates.Codes.Count == 0 ? (rates.LastError is null ? Loc.T("正在获取汇率…", "Fetching rates…") : Loc.T("汇率不可用", "Rates unavailable"))
            : rates.Convert(1, from, to) is null ? Loc.T("此货币没有汇率", "No rate for this currency") : "";
    }

    // The amount as typed, in this PC's number format or with a point.
    private static bool ParseAmount(string text, out decimal amount)
    {
        text = text.Trim().Replace(" ", "").Replace(" ", "");
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static string FormatNumber(double value)
        => !double.IsFinite(value) ? "–" : Math.Abs(value) >= 1e12 || value != 0 && Math.Abs(value) < 1e-6 ? value.ToString("G6", CultureInfo.CurrentCulture) : value.ToString("#,0.######", CultureInfo.CurrentCulture);

    private static readonly Lazy<Dictionary<string, string>> CurrencySymbols = new(() =>
    {
        var symbols = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                symbols.TryAdd(region.ISOCurrencySymbol, region.CurrencySymbol);
            }
            catch (ArgumentException) { }
        }
        return symbols;
    });

    // €119.93 where the currency has a symbol of its own, otherwise 119.93 XYZ.
    private static string FormatMoney(decimal value, string code)
    {
        string number = value.ToString(Math.Abs(value) >= 1 ? "#,0.00" : "0.######", CultureInfo.CurrentCulture);
        return CurrencySymbols.Value.TryGetValue(code, out var symbol) && symbol != code && symbol.Length <= 3 ? symbol + number : $"{number} {code}";
    }

    // The pickers drop down under their button as a classic menu, over the panel and past its edge if needed; typing a
    // letter jumps to the first matching row (the currency list is long).
    private void PickConverterSide(FrameworkElement anchor, bool fromSide)
    {
        if (_converterBody is null) return;
        var s = Shared;
        var (from, to) = ConverterPair();
        string current = fromSide ? from : to;
        if (s.ConverterMode == ConverterMode.Units)
        {
            var units = UnitConversion.In(s.ConverterUnitCategory);
            ShowAnchoredMenu(anchor, [.. units.Select(unit => (unit.Label, unit.Id == current))], index =>
            {
                _app.ChangeShared(settings => { if (fromSide) settings.ConverterUnitFrom = units[index].Id; else settings.ConverterUnitTo = units[index].Id; });
                RenderConverter();
            });
            return;
        }
        var codes = _app.Rates.Codes;
        if (codes.Count == 0) return;
        ShowAnchoredMenu(anchor, [.. codes.Select(code => (CurrencyLabel(code), code == current))], index =>
        {
            _app.ChangeShared(settings => { if (fromSide) settings.ConverterCurrencyFrom = codes[index]; else settings.ConverterCurrencyTo = codes[index]; });
            RenderConverter();
        });
    }

    private void PickUnitCategory(FrameworkElement anchor)
    {
        if (_converterBody is null) return;
        var categories = Enum.GetValues<UnitCategory>();
        ShowAnchoredMenu(anchor, [.. categories.Select(category => (UnitConversion.Name(category), category == Shared.ConverterUnitCategory))], index =>
        {
            // Another category's units mean nothing here: the new one opens on its own pair.
            _app.ChangeShared(settings => { settings.ConverterUnitCategory = categories[index]; settings.ConverterUnitFrom = ""; settings.ConverterUnitTo = ""; });
            RenderConverter();
        });
    }

    private void SwapConverterSides()
    {
        var (from, to) = ConverterPair();
        _app.ChangeShared(settings =>
        {
            if (settings.ConverterMode == ConverterMode.Units) { settings.ConverterUnitFrom = to; settings.ConverterUnitTo = from; }
            else { settings.ConverterCurrencyFrom = to; settings.ConverterCurrencyTo = from; }
        });
        RenderConverter();
    }
}
