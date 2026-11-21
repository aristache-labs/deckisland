namespace DeckIsland.Core;

public enum TerminalShell { PowerShell7, WindowsPowerShell, CommandPrompt }

// The Terminal tab's shells and the folder a session starts in.
public static class TerminalShells
{
    public static string Name(TerminalShell shell) => shell switch
    {
        TerminalShell.WindowsPowerShell => "Windows PowerShell", TerminalShell.CommandPrompt => Loc.T("命令提示符", "Command Prompt"), _ => "PowerShell 7"
    };

    // The program to start: PowerShell 7 falls back to Windows PowerShell where pwsh is not installed (exists says whether a program is on PATH).
    public static string Program(TerminalShell shell, Func<string, bool> exists) => shell switch
    {
        TerminalShell.CommandPrompt => "cmd.exe",
        TerminalShell.WindowsPowerShell => "powershell.exe -NoLogo",
        _ => exists("pwsh.exe") ? "pwsh.exe -NoLogo" : "powershell.exe -NoLogo"
    };

    // The chosen folder while it exists, otherwise the user's profile folder.
    public static string StartFolder(string? chosen, Func<string, bool> directoryExists, string profile)
        => !string.IsNullOrWhiteSpace(chosen) && directoryExists(chosen) ? chosen : profile;

    public static bool OnPath(string program)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { if (File.Exists(Path.Combine(folder.Trim('"'), program))) return true; }
            catch (ArgumentException) { }
        }
        return false;
    }
}
