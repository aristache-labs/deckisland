using BrimDeck.Core;

internal static class ReminderTests
{
    public static void Run(Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.Utc;
        check("Reminder quick add parses relative, next clock, tomorrow and default times",
            Reminder.Parse("20m milk", now, zone) is { Title: "milk" } relative && relative.Due == now.AddMinutes(20) &&
            Reminder.Parse("19:30 call Mum", now, zone)?.Due == now.AddHours(23.5) &&
            Reminder.Parse("tomorrow 9:00 stand-up", now, zone)?.Due == now.AddHours(13) &&
            Reminder.Parse("read", now, zone)?.Due == now.AddHours(1) &&
            Reminder.Parse("25:00 invalid", now, zone) is null);
        var weekly = Reminder.Create("Review", now, ReminderRepeat.Weekly)!;
        var snoozed = weekly.Snooze(now);
        check("Snooze keeps the original weekly schedule", snoozed.Due == now.AddMinutes(10) &&
            snoozed.Complete(now.AddMinutes(11), zone)?.Due == now.AddDays(7));
        var weekdays = Reminder.Create("Work", now, ReminderRepeat.Weekdays)!;
        check("Weekday reminders skip weekends and one-shot reminders clear", weekdays.Complete(now, zone)?.Due == now.AddDays(3) &&
            Reminder.Create("Once", now, ReminderRepeat.Once)!.Complete(now, zone) is null);
        var settings = new DeckSettings { Reminders = [weekly] };
        settings.Copy().Reminders.Clear();
        var restored = System.Text.Json.JsonSerializer.Deserialize<DeckSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        restored.Normalize();
        check("Reminders persist and are shared across profiles", restored.Reminders.Single() == weekly &&
            !Profiles.IsProfileKey(nameof(DeckSettings.Reminders)) && !DeckPages.Shown(restored, DeckPage.Reminders));
    }
}
