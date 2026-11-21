using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using DeckIsland.Core;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace DeckIsland;

// Code editors: the custom quota script box (JavaScript, in the settings palette), and the colouring the Scratchpad tab shares.
internal static class ScriptEditor
{
    // The editor's face is Fonts.Code (Consolas, with Chinese from YaHei UI scaled to two columns).
    public static (Border Frame, TextEditor Editor) Create(string text, SettingsPalette palette)
    {
        var editor = new TextEditor
        {
            Text = text, FontFamily = Fonts.Code, FontSize = 12, Foreground = UI.Brush(palette.Primary), Background = Brushes.Transparent,
            WordWrap = false, ShowLineNumbers = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            SyntaxHighlighting = Highlighting(palette.Dark, ScratchLanguage.JavaScript)
        };
        editor.Options.ConvertTabsToSpaces = true; editor.Options.IndentationSize = 2;
        editor.Options.EnableHyperlinks = false; editor.Options.EnableEmailHyperlinks = false;
        editor.TextArea.Caret.CaretBrush = UI.Brush(palette.Accent);
        var selection = UI.Brush(palette.Dark ? "#4D88B0EE" : "#40347BDF");
        editor.TextArea.SelectionBrush = selection; editor.TextArea.SelectionBorder = null; editor.TextArea.SelectionForeground = null;
        editor.TextArea.SelectionCornerRadius = 0;
        var frame = new Border
        {
            CornerRadius = new CornerRadius(6), Background = UI.Brush(palette.Input), BorderBrush = UI.Brush(palette.Border), BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8), Height = 210, Child = editor
        };
        // The frame follows the other inputs: its border takes the accent colour while the editor has focus.
        editor.IsKeyboardFocusWithinChanged += (_, _) => frame.BorderBrush = UI.Brush(editor.IsKeyboardFocusWithin ? (palette.Dark ? "#348AEB" : "#2374DE") : palette.Border);
        return (frame, editor);
    }

    private static readonly Dictionary<(bool, ScratchLanguage), IHighlightingDefinition> Built = [];

    // The colouring for a language in the dark or light palette; plain text has none. Built once per pair.
    public static IHighlightingDefinition? Highlighting(bool dark, ScratchLanguage language)
    {
        if (language == ScratchLanguage.PlainText) return null;
        if (Built.TryGetValue((dark, language), out var known)) return known;
        string keyword = dark ? "#82AEEB" : "#1F5FBF", text = dark ? "#D9A17A" : "#A3531E", comment = dark ? "#7F8591" : "#767B87", number = dark ? "#7CC7B6" : "#1A7F6E";
        string Quoted(string mark, bool multiline) =>
            $"""<Span color="String" multiline="{(multiline ? "true" : "false")}"><Begin>{mark}</Begin><End>{mark}</End><RuleSet><Span begin="\\" end="." /></RuleSet></Span>""";
        const string cComments = """<Span color="Comment" begin="//" /><Span color="Comment" multiline="true" begin="/\*" end="\*/" />""";
        // Comments and strings: C-style for JavaScript, TypeScript and Rust (Rust's ' starts lifetimes as often as characters, so it is
        // left plain), # and triple quotes for Python.
        string spans = language switch
        {
            ScratchLanguage.Python => """<Span color="Comment" begin="#" />"""
                + """"<Span color="String" multiline="true"><Begin>"""</Begin><End>"""</End></Span>""""
                + """<Span color="String" multiline="true"><Begin>'''</Begin><End>'''</End></Span>"""
                + Quoted("\"", false) + Quoted("'", false),
            ScratchLanguage.Rust => cComments + Quoted("\"", true),
            _ => cComments + Quoted("\"", false) + Quoted("'", false) + Quoted("`", true)
        };
        var xshd = $"""
            <SyntaxDefinition name="DeckIsland{language}" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Comment" foreground="{comment}" />
              <Color name="String" foreground="{text}" />
              <Color name="Keyword" foreground="{keyword}" />
              <Color name="Number" foreground="{number}" />
              <RuleSet>
                {spans}
                <Keywords color="Keyword">{string.Concat(Scratchpad.Keywords(language).Select(word => $"<Word>{word}</Word>"))}</Keywords>
                <Rule color="Number">\b0[xX][0-9a-fA-F]+|(\b\d+(\.[0-9]+)?|\.[0-9]+)([eE][+-]?[0-9]+)?</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using var reader = XmlReader.Create(new StringReader(xshd));
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        Built[(dark, language)] = definition;
        return definition;
    }
}
