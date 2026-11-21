using System.IO;
using System.Media;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

public partial class App
{
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<Guid, (DateTimeOffset First, int Slot)> _reminderSeen = [];
    private SoundPlayer? _reminderAlarm;
    internal event Action? RemindersChanged;
    internal IEnumerable<Reminder> OrderedReminders => Settings.Reminders.OrderBy(item => item.Due);

    private void InitializeReminders()
    {
        _reminderTimer.Tick += (_, _) => CheckReminders();
        UpdateReminderTimer();
    }

    private void UpdateReminderTimer() => _reminderTimer.IsEnabled = Settings.Reminders.Count > 0;

    internal void AddReminder(Reminder reminder)
    {
        Settings.Reminders.Add(reminder);
        SaveReminders();
    }

    internal void AnswerReminder(Guid id, bool snooze)
    {
        int index = Settings.Reminders.FindIndex(item => item.Id == id);
        if (index < 0) return;
        var now = DateTimeOffset.Now;
        var reminder = Settings.Reminders[index];
        var next = snooze ? reminder.Snooze(now) : reminder.Complete(now, TimeZoneInfo.Local);
        if (next is null) Settings.Reminders.RemoveAt(index);
        else Settings.Reminders[index] = next;
        _reminderSeen.Remove(id);
        SaveReminders();
    }

    private void SaveReminders()
    {
        QueueSave();
        UpdateReminderTimer();
        RemindersChanged?.Invoke();
    }

    private void CheckReminders()
    {
        var now = DateTimeOffset.Now;
        var active = Decks.FirstOrDefault(deck => deck.ReminderAlertId is not null);
        if (active?.ReminderAlertId is { } id)
        {
            if (Settings.ReminderSound == ReminderSound.Off) StopReminderAlarm();
            if (!_reminderSeen.TryGetValue(id, out var seen) || Settings.ReminderSound != ReminderSound.UntilAnswered) return;
            double elapsed = (now - seen.First).TotalSeconds;
            int slot = (int)(elapsed / 15);
            if (elapsed <= 61 && slot > seen.Slot)
            {
                _reminderSeen[id] = (seen.First, slot);
                PlayReminderAlarm();
            }
            return;
        }
        if (!ShownSettings.Any(settings => settings.RemindersPage)) return;
        var reminder = OrderedReminders.FirstOrDefault(item => item.Due <= now && !_reminderSeen.ContainsKey(item.Id));
        if (reminder is null) return;
        var deck = Decks.FirstOrDefault(panel => panel.IsOpenForHotkey && panel.CanShowReminder)
            ?? Decks.FirstOrDefault(panel => panel.CanShowReminder);
        if (deck?.ShowReminderAlert(reminder) != true) return;
        _reminderSeen[reminder.Id] = (now, 0);
        RemindersChanged?.Invoke();
        if (Settings.ReminderSound != ReminderSound.Off) PlayReminderAlarm();
    }

    private void PlayReminderAlarm()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
        if (File.Exists(path))
        {
            try
            {
                _reminderAlarm ??= new SoundPlayer(path);
                _reminderAlarm.Play();
                return;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException) { }
        }
        SystemSounds.Exclamation.Play();
    }

    internal void StopReminderAlarm()
    {
        _reminderAlarm?.Stop();
        _reminderAlarm?.Dispose();
        _reminderAlarm = null;
    }
}
