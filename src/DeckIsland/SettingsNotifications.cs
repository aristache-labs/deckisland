using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DeckIsland.Core;

namespace DeckIsland;

public sealed partial class SettingsWindow
{
    internal const int NotificationsSettingsPage = 23;

    private void NotificationsSettings()
    {
        SectionTitle(Loc.T("通知", "Notifications"), description: Loc.T("灵动岛上的 Windows 通知。", "Windows notifications on the island."));
        string access = _app.NotificationAccess switch
        {
            NotificationAccess.Ready => "",
            NotificationAccess.NeedsPackage => Loc.T("读取 Windows 通知需要应用包标识：运行 scripts\notifications\register.ps1 注册后重启 DeckIsland。",
                "Reading Windows notifications needs a package identity: run scripts\notifications\register.ps1 as described in its header, then restart DeckIsland."),
            NotificationAccess.Denied => Loc.T("Windows 未授予通知访问权限。请在 Windows 设置 → 隐私和安全性 → 通知中允许。", "Windows has not allowed notification access. Allow it in Windows Settings, Privacy and security, Notifications."),
            NotificationAccess.Unavailable => Loc.T("此设备无法读取通知。", "Notifications cannot be read on this device."),
            _ => Loc.T("正在连接通知…", "Connecting to notifications…")
        };
        // Why notifications cannot be read yet, at the top of the card it concerns.
        var notice = TextLine(access, 12, TextSecondary);
        notice.TextWrapping = TextWrapping.Wrap; notice.Margin = new Thickness(0, 12, 0, 12);
        if (access.Length == 0) notice.Visibility = Visibility.Collapsed;
        Group(notice,
            Describe(Toggle(Loc.T("镜像通知", "Mirror notifications"), S.MirrorNotifications, value => Change(s => s.MirrorNotifications = value),
                help: Loc.T("新通知会显示在灵动岛中。点击即可打开对应应用。", "New notifications appear in the island. Click one to open the app.")),
                Loc.T("在灵动岛上显示新通知。", "Show new notifications on the island.")),
            Describe(InlineSlider(Loc.T("通知时长", "Notification time"), S.NotificationSeconds, 2, 15, 1, "s", value => Change(s => s.NotificationSeconds = (int)value)),
                Loc.T("每条通知停留的时间。", "How long each one stays.")),
            Describe(InlineSlider(Loc.T("长文本通知", "Long text"), S.NotificationScrollSeconds, 5, 30, 1, "s", value => Change(s => s.NotificationScrollSeconds = (int)value)),
                Loc.T("标题或内容太长时会滚动一次，通知停留到滚动结束，但最多这么久。", "A title or text too long to fit scrolls once; the notification stays until it has, but no longer than this.")),
            Describe(Toggle(Loc.T("显示具体时间", "Clock times"), S.NotificationClockTimes, value => Change(s => s.NotificationClockTimes = value),
                help: Loc.T("显示通知到达的时刻（如 14:32），而不是已过去的时间（如 22m）。", "Show when each notification came, like 14:32, instead of how long ago, like 22m.")),
                Loc.T("显示 14:32，而不是 22m。", "14:32 instead of 22m.")),
            Describe(Toggle(Loc.T("提示停留更久", "Keep notices longer"), S.KeepNoticesLonger, value => Change(s => s.KeepNoticesLonger = value),
                help: Loc.T("提示、提醒等消息在灵动岛上停留两倍时间。", "Notices, reminders and other messages stay on the island twice as long, so there is time to read them.")),
                Loc.T("多一倍的时间来阅读。", "Twice the time to read them.")),
            Row(Loc.T("仅在灵动岛显示", "Only in the island"),
                Outline(ActionButton(Loc.T("打开 Windows 设置", "Open Windows settings"), () => UI.OpenUrl("ms-settings:notifications"))),
                TextLine(Loc.T("Windows 仍会为每个应用显示自己的横幅。要只在灵动岛看到某个应用的通知，请在该应用的 Windows 通知设置中关闭“显示通知横幅”，保持通知开启。",
                    "Windows still shows its own banner for each app. To see an app's notifications only in the island, turn off Show notification banners for it in Windows' notification settings. Leave its notifications on."), 11, TextSecondary)));

        SectionTitle(Loc.T("状态提示", "HUDs"), description: Loc.T("电脑上有变化时，灵动岛给出的简短提示。", "Short readouts when something on your PC changes."));
        Group(
            Describe(Toggle(Loc.T("音量", "Volume"), S.HudVolume, value => Change(s => s.HudVolume = value), help: Loc.T("在灵动岛中显示音量变化。", "Show volume changes in the island.")), Loc.T("音量变化时。", "When the volume changes.")),
            Describe(Toggle(Loc.T("亮度", "Brightness"), S.HudBrightness, value => Change(s => s.HudBrightness = value), help: Loc.T("显示屏幕亮度变化（仅限提供背光调节的屏幕）。", "Show screen brightness changes (screens with a backlight control).")), Loc.T("支持背光调节的屏幕。", "Screens with a backlight control.")),
            Describe(Toggle(Loc.T("电池", "Battery"), S.HudBattery, value => Change(s => s.HudBattery = value), help: Loc.T("充电、拔掉电源和低电量。", "Charging, unplugged and low battery.")), Loc.T("充电、拔掉电源和低电量。", "Charging, unplugged and low.")),
            Describe(Toggle(Loc.T("蓝牙设备", "Bluetooth devices"), S.HudBluetooth, value => Change(s => s.HudBluetooth = value),
                help: Loc.T("设备连接与断开，设备报告电量时一并显示。", "Connect and disconnect, with battery level when the device reports one.")), Loc.T("连接与断开。", "Connect and disconnect.")),
            Describe(Toggle(Loc.T("大小写锁定与数字锁定", "Caps Lock and Num Lock"), S.HudLocks, value => Change(s => s.HudLocks = value),
                help: Loc.T("Caps Lock、Num Lock 或 Scroll Lock 开关时显示。", "Show when Caps Lock, Num Lock or Scroll Lock is turned on or off.")), Loc.T("锁定键开关时。", "When a lock key goes on or off.")));

        SectionTitle(Loc.T("实时活动", "Live activities"), description: Loc.T("进行中的事情，持续时一直显示。", "Things in progress, shown while they last."));
        var ignored = new StackPanel();
        void Fill()
        {
            ignored.Children.Clear();
            foreach (string name in S.SensorIgnoredApps)
            {
                var remove = ActionButton(Loc.T("移除", "Remove"), () => { Change(s => s.SensorIgnoredApps = [.. s.SensorIgnoredApps.Where(item => item != name)]); Fill(); });
                AutomationProperties.SetName(remove, Loc.T($"移除 {name}", $"Remove {name}"));
                // Built again after each change, so each row takes the card's columns here too.
                ignored.Children.Add(Reflow(Row(name, remove)));
            }
            ignored.Visibility = ignored.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        Fill();
        var entry = new TextBox { Style = (Style)FindResource("SettingsField"), Width = 160, Height = 30, MaxLength = 80, Padding = new Thickness(10, 0, 10, 0), FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(entry, Loc.T("要忽略的应用", "App to ignore"));
        void Add()
        {
            string name = entry.Text.Trim();
            if (name.Length == 0) return;
            Change(s => s.SensorIgnoredApps = [.. s.SensorIgnoredApps, name]);
            entry.Clear();
            Fill();
        }
        entry.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        Group(
            Describe(Toggle(Loc.T("下载", "Downloads"), S.HudDownloads, value => Change(s => s.HudDownloads = value),
                help: Loc.T("显示下载文件夹中正在到达的文件，完成时给出提示。支持任何浏览器。", "Show files arriving in your Downloads folder, and a readout when one finishes. Works with any browser.")), Loc.T("下载文件夹中正在到达的文件。", "Files arriving in Downloads.")),
            Describe(Toggle(Loc.T("传感器指示点", "Sensor indicator"), S.SensorIndicator, value => Change(s => s.SensorIndicator = value),
                help: Loc.T("麦克风、摄像头或屏幕录制开启时，灵动岛上显示一个小点。", "A dot on the island while the microphone, camera or screen capture is live.")), Loc.T("麦克风、摄像头或录屏开启时。", "While the mic, camera or screen capture is live.")),
            Row(Loc.T("忽略这些应用", "Ignore these apps"), new StackPanel { Orientation = Orientation.Horizontal,
                Children = { entry, Outline(ActionButton(Loc.T("添加应用", "Add app"), Add)) } },
                TextLine(Loc.T("在后台一直占用麦克风的应用可以加在这里，这样只有真正使用时才亮点。应用名即进程名，例如 zoom。",
                    "Apps that keep the microphone open in the background can be added here, so only real use lights the dot. Use the program name, like zoom."), 11, TextSecondary)),
            ignored);
    }

    // A quiet outline button, as the General page's Exit.
    private Button Outline(Button button) { button.Style = (Style)FindResource("SettingsOutlineButton"); return button; }

    private void NotificationSizeControls() => PageSizeControls(DeckPage.Notifications, Loc.T("通知", "Notifications"));
}
