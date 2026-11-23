using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

public partial class App
{
    internal VaultStore Vault { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckIsland", "vault.dat"));

    private readonly VaultSession _vaultSession = new();
    private DispatcherTimer? _vaultExpiry;
    internal byte[]? VaultKey => _vaultSession.Key;

    internal void UnlockVault(byte[] key)
    {
        _vaultSession.Open(key, DateTimeOffset.UtcNow);
        ScheduleVaultLock();
    }

    internal void ScheduleVaultLock()
    {
        _vaultExpiry?.Stop();
        if (_vaultSession.Remaining(Settings.VaultLockMinutes, DateTimeOffset.UtcNow) is not { } remaining) return;
        if (remaining <= TimeSpan.Zero) { LockVaultViews(); return; }
        if (_vaultExpiry is null)
        {
            _vaultExpiry = new DispatcherTimer(DispatcherPriority.Send);
            _vaultExpiry.Tick += (_, _) => ScheduleVaultLock();
        }
        _vaultExpiry.Interval = remaining;
        _vaultExpiry.Start();
    }

    private void EndVaultSession()
    {
        _vaultExpiry?.Stop();
        _vaultSession.Lock();
    }

    internal void LockVaultViews()
    {
        EndVaultSession();
        foreach (var deck in Decks) deck.ClearVaultView();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            foreach (var deck in Decks)
                if (deck.IsOpenForHotkey && deck.CurrentPage == DeckPage.Vault) deck.RenderUsage();
        });
    }
}

public sealed partial class SettingsWindow
{
    private bool _vaultSettingsBusy;

    private void VaultOptions()
    {
        SectionTitle("Vault");
        var editor = new StackPanel();
        var status = TextLine("", 12, TextSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var store = _app.Vault;

        bool StorageError(Exception error) => error is IOException or UnauthorizedAccessException or CryptographicException or JsonException;
        async Task Run(Func<Task> work)
        {
            if (_vaultSettingsBusy) return;
            _vaultSettingsBusy = true;
            editor.IsEnabled = false;
            status.Text = "";
            try { await work(); }
            catch (Exception error) when (StorageError(error) || VaultHello.PlatformError(error))
            { status.Text = StorageError(error) ? Loc.T("无法访问保险库。", "Could not access the vault.") : VaultHello.Unavailable; }
            finally { _vaultSettingsBusy = false; editor.IsEnabled = true; }
        }

        void Clear()
        {
            foreach (var box in editor.Children.OfType<PasswordBox>()) box.Clear();
            editor.Children.Clear();
            status.Text = "";
        }
        editor.Unloaded += (_, _) => Clear();

        PasswordBox Pin(string label)
        {
            var box = new PasswordBox
            {
                MaxLength = 4, Width = 160, MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Left,
                Background = UI.Brush(_palette.Card), Foreground = UI.Brush(TextPrimary), CaretBrush = UI.Brush(TextPrimary),
                BorderBrush = UI.Brush(_palette.Border), BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 3, 0, 9)
            };
            AutomationProperties.SetName(box, label);
            editor.Children.Add(TextLine(label, 12, TextPrimary));
            editor.Children.Add(box);
            return box;
        }

        void Actions(string label, Func<Task> submit)
        {
            editor.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children =
            {
                Outline(ActionButton(Loc.T("取消", "Cancel"), Clear)),
                Outline(ActionButton(label, async () => await Run(submit)))
            } });
        }

        byte[]? Authenticate(PasswordBox pin)
        {
            var document = store.Read() ?? throw new IOException();
            var now = DateTimeOffset.UtcNow;
            var key = store.Unlock(document, pin.Password, now);
            pin.Clear();
            if (key is null)
            {
                var retry = store.Read()?.RetryAt - now;
                status.Text = retry is { TotalSeconds: > 0 } delay
                    ? Loc.T($"请在 {Math.Ceiling(delay.TotalSeconds)} 秒后重试。", $"Try again in {Math.Ceiling(delay.TotalSeconds)} seconds.")
                    : Loc.T("请输入正确的四位数字 PIN。", "Enter the correct four-digit PIN.");
            }
            return key;
        }

        void PinOptions(VaultStore.Document document)
        {
            bool hello = document.Hello is not null;
            var old = hello ? null : Pin(Loc.T("当前 PIN", "Current PIN"));
            var next = Pin(hello ? Loc.T("新 PIN", "New PIN") : Loc.T("新 PIN（可选）", "New PIN (optional)"));
            var confirm = Pin(Loc.T("确认 PIN", "Confirm PIN"));
            bool passwords = document.PinPasswords, codes = document.PinCodes;
            editor.Children.Add(Toggle(Loc.T("密码需要 PIN", "Require PIN for passwords"), passwords, value => passwords = value));
            editor.Children.Add(Toggle(Loc.T("双重验证码需要 PIN", "Require PIN for 2FA codes"), codes, value => codes = value));
            Actions(Loc.T("保存", "Save"), async () =>
            {
                string? pin = !hello && next.Password.Length == 0 ? null : next.Password;
                if (pin is not null && (!VaultRules.ValidPin(pin) || pin != confirm.Password))
                {
                    status.Text = Loc.T("请输入并确认四位数字 PIN。", "Enter and confirm a four-digit PIN.");
                    return;
                }
                byte[]? key = old is not null ? Authenticate(old) : await VaultHello.Unlock(document.Hello!);
                if (key is null) return;
                try
                {
                    if (!editor.IsLoaded) return;
                    _app.LockVaultViews();
                    store.Configure(hello ? document : store.Read() ?? throw new IOException(), key, pin, passwords, codes);
                    if (document.Hello is { } former) await VaultHello.Delete(former.Name);
                    Clear();
                    status.Text = Loc.T("保险库保护设置已保存。", "Vault protection saved.");
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            });
            (old ?? next).Focus();
        }

        async Task OpenManagement(bool hello)
        {
            if (_vaultSettingsBusy) return;
            Clear();
            await Run(() =>
            {
                if (store.Read() is not { } document)
                    status.Text = Loc.T("请先在保险库页面设置 PIN。", "Set up your PIN on the Vault page first.");
                else if (!hello && document.Hello is not null)
                    status.Text = Loc.T("Windows Hello 正在保护整个保险库。", "Windows Hello currently protects both sections.");
                else if (!hello || document.Hello is not null)
                    PinOptions(document);
                else
                {
                    var pin = Pin(Loc.T("当前 PIN", "Current PIN"));
                    Actions(Loc.T("启用 Windows Hello", "Enable Windows Hello"), async () =>
                    {
                        var oldKey = Authenticate(pin);
                        if (oldKey is null) return;
                        try
                        {
                            var current = store.Read() ?? throw new IOException();
                            if (await VaultHello.Enroll() is not { } enrollment) return;
                            bool saved = false;
                            try
                            {
                                if (!editor.IsLoaded) return;
                                _app.LockVaultViews();
                                store.ConfigureHello(current, oldKey, enrollment.Credential, enrollment.Key);
                                saved = true;
                                Clear();
                                status.Text = Loc.T("已启用 Windows Hello。", "Windows Hello is enabled.");
                            }
                            finally
                            {
                                CryptographicOperations.ZeroMemory(enrollment.Key);
                                if (!saved) await VaultHello.Delete(enrollment.Credential.Name);
                            }
                        }
                        finally { CryptographicOperations.ZeroMemory(oldKey); }
                    });
                    pin.Focus();
                }
                return Task.CompletedTask;
            });
        }

        int[] timeouts = [.. VaultSession.Timeouts];
        string lockLabel = Loc.T("自动锁定保险库", "Lock Vault after");
        var timeout = ProfileCombo(lockLabel, [.. timeouts.Select(minutes => minutes switch
        {
            0 => Loc.T("退出应用时", "On app exit"),
            >= 60 => Loc.T($"{minutes / 60} 小时", minutes == 60 ? "1 hour" : $"{minutes / 60} hours"),
            _ => Loc.T($"{minutes} 分钟", minutes == 1 ? "1 minute" : $"{minutes} minutes")
        })], Math.Max(0, Array.IndexOf(timeouts, S.VaultLockMinutes)), index =>
        {
            Change(s => s.VaultLockMinutes = timeouts[index]);
            _app.ScheduleVaultLock();
        }, width: 180);

        Group(Toggle(Loc.T("显示保险库", "Show Vault"), S.VaultPage, value => Change(s => s.VaultPage = value)),
            Row(lockLabel, timeout, TextLine(Loc.T("从上次解锁开始计时。", "Measured from the last unlock."), 11, TextSecondary)),
            Row("Windows Hello", Outline(ActionButton(Loc.T("更改解锁方式", "Change unlock method"), async () => await OpenManagement(true)))),
            Row(Loc.T("PIN 和保护设置", "PIN and protection"), Outline(ActionButton(Loc.T("管理", "Manage"), async () => await OpenManagement(false)))),
            Row(Loc.T("重置保险库", "Reset Vault"), Outline(ActionButton(Loc.T("重置", "Reset"), () =>
            {
                if (_vaultSettingsBusy) return;
                Clear();
                status.Text = Loc.T("永久删除所有密码和双重验证密钥？", "Permanently delete all passwords and two-factor keys?");
                Actions(Loc.T("删除所有条目", "Delete all entries"), async () =>
                {
                    VaultHelloCredential? credential = null;
                    try { credential = store.Read()?.Hello; }
                    catch (Exception error) when (StorageError(error)) { }
                    _app.LockVaultViews();
                    store.Reset();
                    if (credential is not null) await VaultHello.Delete(credential.Name);
                    Clear();
                    status.Text = Loc.T("保险库已重置。", "Vault reset.");
                });
            }))),
            status, editor);
    }
}
