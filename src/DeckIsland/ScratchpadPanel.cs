using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DeckIsland.Core;
using ICSharpCode.AvalonEdit;

namespace DeckIsland;

// The Scratchpad tab: a code pad with colouring for JavaScript, TypeScript, Rust and Python, or plain text. Each language keeps its own
// draft, saved as it is typed (App.SetScratchDraft).
public partial class MainWindow
{
    private TextEditor? _scratchEditor;
    private Grid? _scratchBody;
    private (int Generation, string Language) _scratchAppearance;

    private void InitializeScratchpad() { }

    private void ScratchpadPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Scratchpad);

    private void RenderScratchpad()
    {
        RefreshStatus();
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility = ShelfContent.Visibility = DevicesContent.Visibility = WeatherContent.Visibility =
            FocusContent.Visibility = RemindersContent.Visibility = NotificationsContent.Visibility = CallsContent.Visibility = ConverterContent.Visibility = TerminalContent.Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        if (!double.IsFinite(ContentCanvas.Height)) return;
        var header = PanelHeader.For(30, 18);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        ScratchpadContent.Margin = new Thickness(header.Side, 0, header.Side, 12);
        var appearance = (Look.Generation, Loc.Language);
        if (_scratchAppearance == appearance && _scratchBody is not null && ScratchpadContent.Children.Contains(_scratchBody)) return;
        _scratchAppearance = appearance;
        ScratchpadContent.Children.Clear();
        BuildScratchpad();
    }

    private void BuildScratchpad()
    {
        // Drafts are shared by every profile (Profiles.GlobalKeys), so they are read where they are kept.
        var language = _app.Settings.ScratchpadLanguage;
        var title = WeatherText(Loc.T("代码草稿", "Code scratchpad"), 14);
        title.FontWeight = FontWeights.SemiBold;
        var chip = PickerChip(Scratchpad.Name(language), PickScratchLanguage);
        chip.HorizontalAlignment = HorizontalAlignment.Right;
        var top = new Grid { Margin = new Thickness(0, 0, 0, 8), Children = { title, chip } };

        var editor = new TextEditor
        {
            Text = Scratchpad.Draft(_app.Settings, language), FontFamily = Fonts.Code, FontSize = 12.5, Foreground = UI.Brush(Look.Primary), Background = Brushes.Transparent,
            WordWrap = false, ShowLineNumbers = true, LineNumbersForeground = UI.Brush(Look.Hex("track")),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            SyntaxHighlighting = ScriptEditor.Highlighting(Look.Current.IsDark, language)
        };
        editor.Options.ConvertTabsToSpaces = true; editor.Options.IndentationSize = language == ScratchLanguage.Python || language == ScratchLanguage.Rust ? 4 : 2;
        editor.Options.EnableHyperlinks = false; editor.Options.EnableEmailHyperlinks = false;
        editor.TextArea.Caret.CaretBrush = UI.Brush(Look.Accent);
        editor.TextArea.SelectionBrush = UI.Brush(Look.Current.IsDark ? "#4D88B0EE" : "#40347BDF");
        editor.TextArea.SelectionBorder = null; editor.TextArea.SelectionForeground = null; editor.TextArea.SelectionCornerRadius = 0;
        AutomationProperties.SetName(editor, Loc.T("代码", "Code"));
        editor.TextChanged += (_, _) => _app.SetScratchDraft(language, editor.Text);
        TakeKeyboardOnClick(editor);
        // The placeholder shows while the draft is empty.
        var placeholder = WeatherText(Loc.T("在此写代码，保存在本机", "Write code here, saved on this PC"), 12.5);
        placeholder.Foreground = UI.Brush(Look.Hex("track")); placeholder.FontFamily = Fonts.Code; placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Top; placeholder.Margin = new Thickness(30, 0, 0, 0);
        void Placeholder() => placeholder.Visibility = editor.Document.TextLength == 0 ? Visibility.Visible : Visibility.Collapsed;
        editor.TextChanged += (_, _) => Placeholder();
        Placeholder();
        var frame = new Border { CornerRadius = new CornerRadius(10), Background = UI.Brush(Look.Hex("fill")), Padding = new Thickness(8, 8, 6, 6), Child = new Grid { Children = { editor, placeholder } } };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(frame, 1);
        body.Children.Add(top);
        body.Children.Add(frame);
        _scratchEditor = editor; _scratchBody = body;
        ScratchpadContent.Children.Add(body);
    }

    private void PickScratchLanguage()
    {
        if (_scratchBody is null) return;
        var languages = Enum.GetValues<ScratchLanguage>();
        ShowPanelPicker(ScratchpadContent, Loc.T("语言", "Language"), [.. languages.Select(language => (Scratchpad.Name(language), language == _app.Settings.ScratchpadLanguage))],
            index => SwitchScratchLanguage(languages[index]));
    }

    // The draft of the language left is already saved; the editor takes the new language's draft and colouring.
    private void SwitchScratchLanguage(ScratchLanguage language)
    {
        _app.SetScratchLanguage(language);
        _scratchAppearance = default;
        RenderScratchpad();
    }
}
