using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

public partial class MainWindow
{
    private Guid[] _visibleQuotaAgentIds = [];

    private IReadOnlyList<AppEntry> RunningQuotaApps() =>
        Settings.ConfiguredApps.Where(entry => QuotaAgentProcesses.IsRunning(entry, ProcessSnapshot.AnyRecent)).ToArray();

    private void InitializeQuotaAgentPresence()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            if (_expanded || !IsVisible || IsSuppressed || DisplayPower.Off ||
                !Settings.ShowsSummary(EffectiveStyle) || FocusCompactOnly) return;
            if (MinimalParts() is { } minimal && !minimal.Contains(CompactPart.Rings)) return;
            var ids = RunningQuotaApps().Select(entry => entry.InstanceId).ToArray();
            if (_visibleQuotaAgentIds.SequenceEqual(ids)) return;
            _visibleQuotaAgentIds = ids;
            RenderCompact();
        };
        Loaded += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }
}
