using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DeckIsland.Core;

namespace DeckIsland;

public partial class App
{
    internal VaultStore Vault { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckIsland", "vault.dat"));

    internal void LockVaultViews()
    {
        foreach (var deck in Decks) deck.LockVault();
    }
}

public sealed partial class SettingsWindow
{
    private void VaultOptions()
    {
        SectionTitle("Vault", description: Loc.T("密码和双重验证密钥仅加密保存在此电脑。", "Passwords and two-factor keys are encrypted on this PC."));
        Group(Toggle(Loc.T("显示保险库", "Show Vault"), S.VaultPage, value => Change(s => s.VaultPage = value)));
        var status = TextLine("", 12, TextSecondary);
        try
        {
            status.Text = _app.Vault.Read()?.Hello is not null
                ? Loc.T("Windows Hello 保护密码和双重验证码。", "Windows Hello protects passwords and 2FA codes.")
                : Loc.T("Windows Hello 已关闭。", "Windows Hello is off.");
        }
        catch (Exception error) when (VaultPinDialog.StorageError(error)) { status.Text = VaultPinDialog.Unavailable; }
        bool busy = false;
        async Task ChangeHello()
        {
            if (busy) return;
            busy = true;
            try
            {
                _app.LockVaultViews();
                var store = _app.Vault;
                if (store.Read() is not { } current)
                {
                    status.Text = Loc.T("请先设置保险库 PIN。", "Set up your Vault PIN first.");
                    return;
                }
                if (current.Hello is { } enrolled)
                {
                    var key = await VaultHello.Unlock(enrolled);
                    if (key is null) return;
                    try
                    {
                        if (!VaultPinDialog.ReturnToPin(this, store, current, key)) return;
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                    await VaultHello.Delete(enrolled.Name);
                    status.Text = Loc.T("保险库现在使用 PIN。", "Vault now uses its PIN.");
                    return;
                }

                var oldKey = VaultPinDialog.Unlock(this, store);
                if (oldKey is null) return;
                try
                {
                    current = store.Read() ?? throw new IOException();
                    var result = await VaultHello.Enroll();
                    if (result is not { } hello) return;
                    bool saved = false;
                    try
                    {
                        store.ConfigureHello(current, oldKey, hello.Credential, hello.Key);
                        saved = true;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(hello.Key);
                        if (!saved) await VaultHello.Delete(hello.Credential.Name);
                    }
                    status.Text = Loc.T("密码和双重验证码现在需要 Windows Hello。", "Windows Hello now protects passwords and 2FA codes.");
                }
                finally { CryptographicOperations.ZeroMemory(oldKey); }
            }
            catch (Exception error) when (VaultPinDialog.StorageError(error) || VaultHello.PlatformError(error))
            {
                status.Text = VaultHello.PlatformError(error) ? VaultHello.Unavailable : VaultPinDialog.Unavailable;
            }
            finally { busy = false; }
        }
        Group(status,
            Row("Windows Hello", Outline(ActionButton(Loc.T("更改解锁方式", "Change unlock method"), async () => await ChangeHello())),
                TextLine(Loc.T("使用 TPM 支持的 Windows Hello 保护整个保险库。若密钥丢失，必须重置保险库。",
                    "Use TPM-backed Windows Hello for the entire vault. Losing the Windows Hello key requires resetting the vault."), 11, TextSecondary)),
            Row(Loc.T("保险库 PIN 和保护设置", "Vault PIN and protection"), Outline(ActionButton(Loc.T("管理", "Manage"), () =>
            {
                if (busy) return;
                try
                {
                    _app.LockVaultViews();
                    VaultPinDialog.Manage(this, _app.Vault);
                }
                catch (Exception error) when (VaultPinDialog.StorageError(error)) { status.Text = VaultPinDialog.Unavailable; }
            }))),
            Row(Loc.T("重置保险库", "Reset vault"), Outline(ActionButton(Loc.T("重置", "Reset"), async () =>
            {
                if (busy) return;
                string warning = Loc.T("永久删除此电脑上保险库中的所有密码和双重验证密钥？", "Permanently delete every password and two-factor key saved in this vault?");
                if (MessageBox.Show(this, warning, "Vault", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                busy = true;
                try
                {
                    _app.LockVaultViews();
                    VaultHelloCredential? credential = null;
                    try { credential = _app.Vault.Read()?.Hello; }
                    catch (Exception error) when (VaultPinDialog.StorageError(error)) { }
                    _app.Vault.Reset();
                    if (credential is not null) await VaultHello.Delete(credential.Name);
                    status.Text = Loc.T("保险库已重置。", "Vault reset.");
                }
                catch (Exception error) when (VaultPinDialog.StorageError(error)) { status.Text = VaultPinDialog.Unavailable; }
                finally { busy = false; }
            }))));
    }
}

internal static class VaultPinDialog
{
    internal static string Unavailable => Loc.T("无法访问保险库。", "Could not access the vault.");
    internal static bool StorageError(Exception error) => error is IOException or UnauthorizedAccessException or CryptographicException or JsonException;

    internal static void Manage(Window owner, VaultStore store)
    {
        var current = store.Read();
        if (current?.Hello is not null)
        {
            MessageBox.Show(owner, Loc.T("请先使用 Windows Hello 切换回 PIN。", "Use Change unlock method to switch from Windows Hello to a vault PIN."), "Vault");
            return;
        }
        byte[]? oldKey = null;
        Run(owner, current is null ? Loc.T("设置 PIN", "Set up PIN") : Loc.T("管理保险库", "Manage vault"), (body, status, finish) =>
        {
            var old = current is null ? null : Pin(body, Loc.T("当前 PIN", "Current PIN"));
            var label = current is null ? Loc.T("新 PIN", "New PIN") : Loc.T("新 PIN（可选）", "New PIN (optional)");
            var pin = Pin(body, label);
            var confirm = Pin(body, Loc.T("确认 PIN", "Confirm PIN"));
            var passwords = Option(Loc.T("密码需要 PIN", "Require PIN for passwords"), current?.PinPasswords ?? true);
            var codes = Option(Loc.T("双重验证码需要 PIN", "Require PIN for 2FA codes"), current?.PinCodes ?? true);
            body.Children.Add(passwords);
            body.Children.Add(codes);
            finish(() =>
            {
                string? newPin = pin.Password.Length == 0 && current is not null ? null : pin.Password;
                if (newPin is not null && (!VaultRules.ValidPin(newPin) || newPin != confirm.Password))
                { status.Text = Loc.T("请输入并确认四位数字 PIN。", "Enter and confirm a four-digit PIN."); return false; }
                if (old is not null && (oldKey = Check(store, old.Password, status)) is null) { old.Clear(); return false; }
                try { store.Configure(store.Read(), oldKey, newPin, passwords.IsChecked == true, codes.IsChecked == true); }
                finally { if (oldKey is not null) CryptographicOperations.ZeroMemory(oldKey); }
                return true;
            });
            return old ?? pin;
        });
    }

    internal static bool ReturnToPin(Window owner, VaultStore store, VaultStore.Document current, byte[] key)
    {
        bool changed = false;
        Run(owner, Loc.T("改用保险库 PIN", "Switch to vault PIN"), (body, status, finish) =>
        {
            var pin = Pin(body, Loc.T("新 PIN", "New PIN"));
            var confirm = Pin(body, Loc.T("确认 PIN", "Confirm PIN"));
            var passwords = Option(Loc.T("密码需要 PIN", "Require PIN for passwords"), current.PinPasswords);
            var codes = Option(Loc.T("双重验证码需要 PIN", "Require PIN for 2FA codes"), current.PinCodes);
            body.Children.Add(passwords);
            body.Children.Add(codes);
            finish(() =>
            {
                if (!VaultRules.ValidPin(pin.Password) || pin.Password != confirm.Password)
                { status.Text = Loc.T("请输入并确认四位数字 PIN。", "Enter and confirm a four-digit PIN."); return false; }
                store.Configure(current, key, pin.Password, passwords.IsChecked == true, codes.IsChecked == true);
                return changed = true;
            });
            return pin;
        });
        return changed;
    }

    internal static byte[]? Unlock(Window owner, VaultStore store)
    {
        byte[]? key = null;
        Run(owner, Loc.T("解锁保险库", "Unlock vault"), (body, status, finish) =>
        {
            var pin = Pin(body, Loc.T("四位数字 PIN", "Four-digit PIN"));
            finish(() =>
            {
                key = Check(store, pin.Password, status);
                pin.Clear();
                return key is not null;
            });
            return pin;
        });
        return key;
    }

    private static byte[]? Check(VaultStore store, string pin, TextBlock status)
    {
        if (store.Read() is not { } current) return null;
        var now = DateTimeOffset.UtcNow;
        var key = store.Unlock(current, pin, now);
        if (key is null)
        {
            var retry = store.Read()?.RetryAt - now;
            status.Text = retry is { TotalSeconds: > 0 } delay
                ? Loc.T($"请在 {Math.Ceiling(delay.TotalSeconds)} 秒后重试。", $"Try again in {Math.Ceiling(delay.TotalSeconds)} seconds.")
                : Loc.T("PIN 不正确。", "Incorrect PIN.");
        }
        return key;
    }

    private static CheckBox Option(string label, bool value) =>
        new() { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) };

    private static PasswordBox Pin(StackPanel form, string label)
    {
        var box = new PasswordBox { MaxLength = 4, Padding = new Thickness(6), Margin = new Thickness(0, 4, 0, 10) };
        AutomationProperties.SetName(box, label);
        form.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0) });
        form.Children.Add(box);
        return box;
    }

    private static void Run(Window owner, string title, Func<StackPanel, TextBlock, Action<Func<bool>>, PasswordBox> build)
    {
        var window = new Window { Owner = owner, Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, Width = 360, SizeToContent = SizeToContent.Height };
        var body = new StackPanel { Margin = new Thickness(20) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Func<bool>? submit = null;
        var focus = build(body, status, action => submit = action);
        body.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { IsCancel = true, Content = Loc.T("取消", "Cancel"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { IsDefault = true, Content = Loc.T("确认", "Confirm"), MinWidth = 80 };
        ok.Click += (_, _) =>
        {
            try { if (submit?.Invoke() == true) window.DialogResult = true; }
            catch (Exception error) when (StorageError(error)) { status.Text = Unavailable; }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        window.Content = body;
        window.Loaded += (_, _) => focus.Focus();
        window.ShowDialog();
        foreach (var input in body.Children.OfType<PasswordBox>()) input.Clear();
    }
}
