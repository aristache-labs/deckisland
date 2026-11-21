using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeckIsland.Core;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DeckIsland;

public enum NotificationAccess { Starting, Ready, NeedsPackage, Denied, Unavailable }

// Windows toast notifications shown in the island. Windows lets only an app with a package identity read them, so a plain
// DeckIsland.exe reports NeedsPackage until scripts/notifications/register.ps1 has registered its sparse package.
public partial class App
{
    internal NoticeLog Notices { get; } = new();
    internal event Action? NoticesChanged;
    internal void ClearNotices() { Notices.Clear(); NoticesChanged?.Invoke(); }
    internal void DismissNotice(NoticeItem item) { Notices.Remove(item); NoticesChanged?.Invoke(); }
    private void AddNotice(NoticeItem item) { Notices.Add(item); NoticesChanged?.Invoke(); }
    internal NotificationAccess NotificationAccess { get; private set; } = NotificationAccess.Starting;
    private readonly HashSet<uint> _toastsSeen = [];
    private readonly Dictionary<string, byte[]?> _toastLogos = [];

    private static bool HasPackageIdentity()
    {
        try { return Package.Current.Id is not null; }
        catch (InvalidOperationException) { return false; }
        catch (COMException) { return false; }
    }

    private async void StartNotifications()
    {
        try
        {
            if (!HasPackageIdentity()) { NotificationAccess = NotificationAccess.NeedsPackage; return; }
            var listener = UserNotificationListener.Current;
            if (await listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed)
            {
                NotificationAccess = NotificationAccess.Denied;
                return;
            }
            // What is already in the Action Center is not announced again.
            foreach (var existing in await listener.GetNotificationsAsync(NotificationKinds.Toast)) _toastsSeen.Add(existing.Id);
            NotificationAccess = NotificationAccess.Ready;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            bool reading = false;
            timer.Tick += async (_, _) =>
            {
                if (reading || !Settings.MirrorNotifications) return;
                reading = true;
                try { await ReadToasts(listener); }
                catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException) { }
                finally { reading = false; }
            };
            timer.Start();
            _hudKeep.Add(timer);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            NotificationAccess = NotificationAccess.Unavailable;
        }
    }

    // The app's picture, kept once per app.
    private async Task<byte[]?> LogoOf(UserNotification toast)
    {
        string id = toast.AppInfo?.AppUserModelId ?? "";
        if (_toastLogos.TryGetValue(id, out var kept)) return kept;
        byte[]? picture = null;
        try
        {
            if (toast.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64)) is { } logo)
            {
                using var stream = (await logo.OpenReadAsync()).AsStreamForRead();
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy);
                picture = copy.ToArray();
            }
        }
        catch (Exception error) when (error is COMException or IOException or InvalidOperationException or UnauthorizedAccessException) { }
        // A program without a package has no logo in its toast; Windows still shows its icon for the app id.
        picture ??= DeckIsland.Native.ShellIcons.ForApp(id, 64);
        return _toastLogos[id] = picture;
    }

    private async Task ReadToasts(UserNotificationListener listener)
    {
        foreach (var toast in await listener.GetNotificationsAsync(NotificationKinds.Toast))
        {
            if (!_toastsSeen.Add(toast.Id)) continue;
            string app = toast.AppInfo?.DisplayInfo?.DisplayName ?? "";
            var texts = toast.Notification?.Visual?.Bindings.FirstOrDefault()?.GetTextElements().Select(item => item.Text).Where(text => text.Length > 0).ToArray() ?? [];
            if (texts.Length == 0) continue;
            string title = texts[0], body = string.Join(" ", texts.Skip(1));
            double seconds = Settings.NotificationSeconds * (Settings.KeepNoticesLonger ? 2 : 1);
            var picture = await LogoOf(toast);
            AddNotice(new NoticeItem(app, title, body, DateTimeOffset.Now, picture, toast.AppInfo?.AppUserModelId));
            ShowHud(new($"notice:{toast.Id}", HudIcon.Notice, title, Hold: TimeSpan.FromSeconds(seconds), Lit: true, Label: $"{app}: {title} {body}".Trim(),
                Body: body.Length > 0 ? body : app, Picture: picture));
        }
    }
}
