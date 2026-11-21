using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck;

public partial class App
{
    private readonly DispatcherTimer _focusTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    internal FocusTimer FocusSession { get; } = new();
    internal FocusPhase FocusNotice { get; private set; }
    private DateTimeOffset _focusNoticeUntil;
    private int _focusSeconds = -1;
    private Guid? _focusProfile;
    internal event Action<bool>? FocusChanged;

    internal void ToggleFocus(Guid? profile)
    {
        var now = DateTimeOffset.UtcNow;
        AdvanceFocus(now);
        if (FocusSession.Phase == FocusPhase.Idle) _focusProfile = profile ?? Settings.CurrentProfile;
        FocusNotice = FocusPhase.Idle;
        var settings = SettingsFor(_focusProfile);
        FocusSession.Toggle(now, settings.FocusMinutes, settings.BreakMinutes);
        _focusTick.Tick -= FocusTick;
        _focusTick.Tick += FocusTick;
        RefreshFocus(now, rebuild: true);
    }

    internal void ResetFocus()
    {
        FocusSession.Reset();
        FocusNotice = FocusPhase.Idle;
        RefreshFocus(DateTimeOffset.UtcNow, rebuild: true);
    }

    private void FocusTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var completed = AdvanceFocus(now);
        bool rebuild = completed != FocusPhase.Idle || FocusNotice != FocusPhase.Idle && now >= _focusNoticeUntil;
        if (completed != FocusPhase.Idle)
        {
            FocusNotice = completed;
            _focusNoticeUntil = now.AddSeconds(4);
        }
        else if (rebuild) FocusNotice = FocusPhase.Idle;
        RefreshFocus(now, rebuild);
    }

    private FocusPhase AdvanceFocus(DateTimeOffset now)
    {
        var completed = FocusSession.Advance(now);
        if (completed != FocusPhase.Idle)
        {
            var profile = SettingsFor(_focusProfile);
            PlayFocusSound(completed == FocusPhase.Focus ? profile.FocusEndSound : profile.BreakEndSound);
        }
        return completed;
    }

    internal static void PlayFocusSound(FocusSound sound)
    {
        var systemSound = sound switch
        {
            FocusSound.Asterisk => System.Media.SystemSounds.Asterisk,
            FocusSound.Exclamation => System.Media.SystemSounds.Exclamation,
            FocusSound.Beep => System.Media.SystemSounds.Beep,
            FocusSound.Hand => System.Media.SystemSounds.Hand,
            FocusSound.Question => System.Media.SystemSounds.Question,
            _ => null
        };
        systemSound?.Play();
    }

    private void RefreshFocus(DateTimeOffset now, bool rebuild)
    {
        _focusTick.IsEnabled = FocusSession.Running || FocusNotice != FocusPhase.Idle;
        int seconds = (int)Math.Ceiling(FocusSession.Remaining(now).TotalSeconds);
        if (!rebuild && seconds == _focusSeconds) return;
        _focusSeconds = seconds;
        FocusChanged?.Invoke(rebuild);
    }
}
