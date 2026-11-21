namespace DeckIsland.Core;

public enum ScratchLanguage { JavaScript, TypeScript, Rust, Python, PlainText }

// The Scratchpad tab's drafts: one per language, kept in the settings under the language's name.
public static class Scratchpad
{
    public const int MaxDraftLength = 50_000;

    public static string Name(ScratchLanguage language) => language switch
    {
        ScratchLanguage.TypeScript => "TypeScript", ScratchLanguage.Rust => "Rust", ScratchLanguage.Python => "Python",
        ScratchLanguage.PlainText => Loc.T("纯文本", "Plain text"), _ => "JavaScript"
    };

    public static string Draft(DeckSettings settings, ScratchLanguage language)
        => settings.ScratchpadDrafts.TryGetValue(language.ToString(), out var text) ? text : "";

    public static void SetDraft(DeckSettings settings, ScratchLanguage language, string text)
    {
        text = Cap(text ?? "");
        if (text.Length == 0) settings.ScratchpadDrafts.Remove(language.ToString());
        else settings.ScratchpadDrafts[language.ToString()] = text;
    }

    // Only known languages with text, each draft within the limit.
    public static Dictionary<string, string> Normalize(Dictionary<string, string>? drafts)
        => (drafts ?? []).Where(pair => Enum.TryParse<ScratchLanguage>(pair.Key, out var language) && Enum.IsDefined(language) && language.ToString() == pair.Key && !string.IsNullOrEmpty(pair.Value))
            .ToDictionary(pair => pair.Key, pair => Cap(pair.Value));

    private static string Cap(string text) => text.Length <= MaxDraftLength ? text : text[..MaxDraftLength];

    // The words each language's colouring marks as keywords; plain text has none.
    public static IReadOnlyList<string> Keywords(ScratchLanguage language) => language switch
    {
        ScratchLanguage.JavaScript => JavaScriptWords,
        ScratchLanguage.TypeScript => [.. JavaScriptWords, "interface", "type", "enum", "implements", "namespace", "declare", "readonly", "public", "private", "protected",
            "abstract", "as", "is", "keyof", "infer", "never", "unknown", "any", "string", "number", "boolean", "export", "import", "from", "satisfies"],
        ScratchLanguage.Rust => ["as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in",
            "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super", "trait", "true", "type", "unsafe", "use",
            "where", "while", "Some", "None", "Ok", "Err", "i32", "i64", "u8", "u32", "u64", "usize", "f32", "f64", "bool", "str", "String", "Vec", "Option", "Result"],
        ScratchLanguage.Python => ["False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except",
            "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield",
            "self", "print", "len", "range"],
        _ => []
    };

    private static readonly string[] JavaScriptWords =
    [
        "async", "await", "function", "return", "const", "let", "var", "if", "else", "for", "of", "in", "while", "do", "break", "continue",
        "switch", "case", "default", "try", "catch", "finally", "throw", "new", "typeof", "instanceof", "delete", "void", "class", "extends",
        "this", "true", "false", "null", "undefined", "export", "import", "from", "yield", "static", "super"
    ];
}
