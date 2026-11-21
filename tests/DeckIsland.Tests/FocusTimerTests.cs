using DeckIsland.Core;

internal static class FocusTimerTests
{
    public static void Run(Action<string, bool> check)
    {
        var timer = new FocusTimer();
        var start = DateTimeOffset.UnixEpoch;
        timer.Toggle(start, 1, 5);
        timer.Toggle(start.AddSeconds(15), 25, 10);
        check("Focus pause preserves time across delayed ticks", timer is { Phase: FocusPhase.Focus, Running: false } &&
            timer.Remaining(start.AddHours(1)) == TimeSpan.FromSeconds(45));
        var resume = start.AddMinutes(1);
        timer.Toggle(resume, 25, 10);
        check("Focus resume keeps original duration and break", timer.Advance(resume.AddSeconds(50)) == FocusPhase.Focus &&
            timer.Phase == FocusPhase.Break && timer.Remaining(resume.AddSeconds(50)) == TimeSpan.FromSeconds(295));
        check("Focus break completes once and returns to idle", timer.Advance(resume.AddMinutes(6)) == FocusPhase.Break &&
            timer.Advance(resume.AddMinutes(7)) == FocusPhase.Idle && !timer.Running);
        timer.Toggle(start, 1, 1);
        check("A late focus tick does not restart a break that already elapsed", timer.Advance(start.AddMinutes(3)) == FocusPhase.Break &&
            timer.Phase == FocusPhase.Idle);
        timer.Toggle(start, 25, 5);
        timer.Reset();
        check("Reset clears the active focus countdown", timer.Phase == FocusPhase.Idle && timer.Remaining(start) == TimeSpan.Zero);
        var settings = new DeckSettings();
        check("Focus is opt-in for existing settings", !DeckPages.Shown(settings, DeckPage.Focus) && !settings.ShowsFocus(CompactStyle.Notch));
        settings.FocusPage = true;
        check("Enabled Focus can be ordered in both layouts", DeckPages.Visible(settings).Contains(DeckPage.Focus) &&
            settings.ShowsFocus(CompactStyle.Notch) && settings.ShowsFocus(CompactStyle.Capsule) &&
            settings.NotchOrder.Contains(CompactPart.Focus));
        check("Focus simplifies Notch and Capsule by default", settings.FocusSimplifiesCompact(CompactStyle.Notch) &&
            settings.FocusSimplifiesCompact(CompactStyle.Capsule) && !settings.FocusSimplifiesCompact(CompactStyle.Line));
        settings.FocusHideOtherWidgets = false;
        check("Disabling simplified Focus keeps the timer in the normal widget row",
            !settings.FocusSimplifiesCompact(CompactStyle.Notch) && settings.ShowsFocus(CompactStyle.Notch));
        settings.FocusEndSound = FocusSound.None;
        settings.BreakEndSound = FocusSound.Question;
        var saved = System.Text.Json.JsonSerializer.Serialize(settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize<DeckSettings>(saved)!;
        restored.Normalize();
        check("Focus ends with Information and a break with Critical stop, whatever a saved file holds", restored.FocusEndSound == FocusSound.Asterisk &&
            restored.BreakEndSound == FocusSound.Hand);
        check("Focus and break theme colors differ and meet contrast in both base themes",
            new[] { PanelThemes.Dark, PanelThemes.Light }.All(id =>
            {
                var palette = PanelThemes.Resolve(PanelThemes.BuiltIn(id)!);
                return palette["focusProgress"] != palette["breakProgress"] &&
                    palette["focusProgress"].MinimumContrast(palette.SurfaceStops) >= PanelThemes.GraphicContrast &&
                    palette["breakProgress"].MinimumContrast(palette.SurfaceStops) >= PanelThemes.GraphicContrast;
            }));
    }
}
