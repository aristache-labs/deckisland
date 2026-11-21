namespace BrimDeck.Core;

public enum FocusPhase { Idle, Focus, Break }
public enum FocusSound { None, Asterisk, Exclamation, Beep, Hand, Question }

public sealed class FocusTimer
{
    public const int MinimumMinutes = 1, MaximumMinutes = 120;
    private DateTimeOffset? _endsAt;
    private TimeSpan _paused;
    private TimeSpan _break;
    public FocusPhase Phase { get; private set; }
    public TimeSpan Duration { get; private set; }
    public bool Running => _endsAt.HasValue;
    public static int ClampMinutes(int minutes) => Math.Clamp(minutes, MinimumMinutes, MaximumMinutes);

    public TimeSpan Remaining(DateTimeOffset now) => _endsAt is { } end
        ? TimeSpan.FromTicks(Math.Max(0, (end - now).Ticks)) : _paused;

    public void Toggle(DateTimeOffset now, int focusMinutes, int breakMinutes)
    {
        if (Phase == FocusPhase.Idle)
        {
            Phase = FocusPhase.Focus;
            _break = TimeSpan.FromMinutes(ClampMinutes(breakMinutes));
            Duration = TimeSpan.FromMinutes(ClampMinutes(focusMinutes));
            _endsAt = now + Duration;
        }
        else if (Running)
        {
            _paused = Remaining(now);
            _endsAt = null;
        }
        else _endsAt = now + _paused;
    }

    public FocusPhase Advance(DateTimeOffset now)
    {
        if (_endsAt is not { } end || now < end) return FocusPhase.Idle;
        if (Phase == FocusPhase.Focus && now < end + _break)
        {
            Phase = FocusPhase.Break;
            Duration = _break;
            _endsAt = end + _break;
            return FocusPhase.Focus;
        }
        Reset();
        return FocusPhase.Break;
    }

    public void Reset()
    {
        Phase = FocusPhase.Idle;
        Duration = TimeSpan.Zero;
        _endsAt = null;
        _paused = TimeSpan.Zero;
    }
}
