namespace DeckIsland.Core;

public static class QuotaAgentProcesses
{
    public static bool IsRunning(AppEntry entry, Func<string, bool> running)
    {
        string[] names = entry.QuotaSource switch
        {
            ProviderId.Codex => ["Codex", "codex-x86_64-pc-windows-msvc", "codex-aarch64-pc-windows-msvc"],
            ProviderId.Claude => ["Claude", "claude-code"],
            ProviderId.Cursor => ["Cursor"],
            ProviderId.Antigravity => ["Antigravity", "agy"],
            ProviderId.GrokBot => ["Grok", "Grok Bot", "GrokBot"],
            ProviderId.DeepSeek or ProviderId.Dsh => ["DeepSeek", "DeepSeek Harness", "dsh"],
            ProviderId.Kimi or ProviderId.KimiApi or ProviderId.KimiPlatform => ["Kimi", "kimi-code"],
            ProviderId.OpenCode or ProviderId.OpenCodeGo => ["OpenCode", "OpenCode Desktop"],
            ProviderId.WorkBuddy or ProviderId.WorkBuddyAi => ["WorkBuddy", "WorkBuddyAI"],
            _ => []
        };
        if (names.Any(name => running(name + ".exe"))) return true;
        string display = entry.Name;
        return display.Length > 0 && running(display.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? display : display + ".exe");
    }
}
