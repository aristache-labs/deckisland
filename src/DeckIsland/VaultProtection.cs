using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private readonly List<PasswordBox> _vaultInputs = [];
    private static string VaultUnavailable => Loc.T("无法访问保险库。", "Could not access the vault.");
    private static bool VaultStorageError(Exception error) => error is IOException or UnauthorizedAccessException or CryptographicException or JsonException;

    private StackPanel VaultForm(string title, string? description = null)
    {
        ClearVaultView();
        var body = new StackPanel { MaxWidth = 520, Margin = new Thickness(4, 6, 4, 8) };
        VaultContent.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold,
            Foreground = UI.Brush(Look.Primary), Margin = new Thickness(0, 0, 0, 10) });
        if (description is not null)
        {
            var detail = WeatherText(description, 12);
            detail.TextWrapping = TextWrapping.Wrap;
            detail.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(detail);
        }
        return body;
    }

    private void StyleVaultInput(Control input)
    {
        input.Background = UI.Brush(Look.Hex("fill"));
        input.Foreground = UI.Brush(Look.Primary);
        input.BorderBrush = UI.Brush(Look.Hex("rim"));
        input.BorderThickness = new Thickness(1);
        input.Padding = new Thickness(9, 5, 9, 5);
        input.MinHeight = 31;
        if (input is PasswordBox secret)
        {
            secret.Style = (Style)FindResource("VaultPasswordInput");
            secret.CaretBrush = UI.Brush(Look.Primary);
            InputMethod.SetIsInputMethodEnabled(secret, false);
            _vaultInputs.Add(secret);
        }
        else if (input is TextBox text) text.Style = (Style)FindResource("ShelfTextBox");
        TakeKeyboardOnClick(input);
    }

    private void VaultField(StackPanel form, string label, Control input)
    {
        StyleVaultInput(input);
        input.Margin = new Thickness(0, 3, 0, 8);
        AutomationProperties.SetName(input, label);
        form.Children.Add(WeatherText(label, 12));
        form.Children.Add(input);
    }

    private PasswordBox VaultPin(StackPanel form, string label)
    {
        var pin = new PasswordBox { MaxLength = 4, Width = 132, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 16 };
        pin.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
        VaultField(form, label, pin);
        return pin;
    }

    private TextBlock VaultStatus(StackPanel body)
    {
        var status = WeatherText("", 12);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 6, 0, 6);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        body.Children.Add(status);
        return status;
    }

    private void VaultActions(StackPanel body, string label, Action run)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(VaultButton(Loc.T("取消", "Cancel"), BuildVault));
        var submit = VaultButton(label, run);
        submit.Tag = "Selected";
        buttons.Children.Add(submit);
        body.Children.Add(buttons);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.FocusedElement is not PasswordBox) return;
            e.Handled = true;
            run();
        };
    }

    private byte[]? CheckVaultPin(string pin, TextBlock status)
    {
        if (!VaultRules.ValidPin(pin))
        {
            status.Text = Loc.T("请输入四位数字 PIN。", "Enter four digits.");
            return null;
        }
        var current = Vault.Read() ?? throw new IOException();
        var now = DateTimeOffset.UtcNow;
        var key = Vault.Unlock(current, pin, now);
        if (key is not null) return key;
        var retry = Vault.Read()?.RetryAt - now;
        status.Text = retry is { TotalSeconds: > 0 } delay
            ? Loc.T($"请在 {Math.Ceiling(delay.TotalSeconds)} 秒后重试。", $"Try again in {Math.Ceiling(delay.TotalSeconds)} seconds.")
            : Loc.T("PIN 不正确。", "Incorrect PIN.");
        return null;
    }

    private void VaultUnlockForm(VaultStore.Document document)
    {
        var body = VaultForm(Loc.T("解锁保险库", "Unlock Vault"), document.Hello is null
            ? Loc.T("输入四位数字 PIN。", "Enter your four-digit Vault PIN.")
            : Loc.T("使用 Windows Hello 解密保险库。", "Use Windows Hello to decrypt the vault."));
        var pin = document.Hello is null ? VaultPin(body, Loc.T("保险库 PIN", "Vault PIN")) : null;
        var status = VaultStatus(body);
        VaultActions(body, document.Hello is null ? Loc.T("解锁", "Unlock") : Loc.T("使用 Windows Hello 解锁", "Unlock with Windows Hello"),
            async () => await VaultRun(status, async () =>
            {
                byte[]? key = pin is null ? await VaultHello.Unlock(document.Hello!) : CheckVaultPin(pin.Password, status);
                pin?.Clear();
                if (key is null) return;
                try
                {
                    if (!VaultContent.IsAncestorOf(status) || (pin is null && Vault.Read()?.Revision != document.Revision)) return;
                    _app.UnlockVault(key);
                    key = null;
                    BuildVault();
                }
                finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
            }));
        if (pin is not null) BeginTyping(pin);
    }

    private void VaultSetup()
    {
        var body = VaultForm(Loc.T("设置保险库 PIN", "Set up Vault PIN"), Loc.T("使用四位数字。", "Use four digits."));
        var pin = VaultPin(body, Loc.T("新 PIN", "New PIN"));
        var confirm = VaultPin(body, Loc.T("确认 PIN", "Confirm PIN"));
        var status = VaultStatus(body);
        VaultActions(body, Loc.T("保存", "Save"), async () => await VaultRun(status, () =>
        {
            if (!VaultRules.ValidPin(pin.Password) || pin.Password != confirm.Password)
            {
                status.Text = Loc.T("请输入并确认四位数字 PIN。", "Enter and confirm a four-digit PIN.");
                return Task.CompletedTask;
            }
            Vault.Configure(null, null, pin.Password, pinPasswords: true, pinCodes: true);
            BuildVault();
            return Task.CompletedTask;
        }));
        BeginTyping(pin);
    }

    private async Task VaultRun(TextBlock status, Func<Task> action)
    {
        if (_vaultDialogOpen || !VaultContent.IsAncestorOf(status)) return;
        bool pinned = _toolPinned;
        SetToolPin(true);
        _vaultDialogOpen = true;
        try { await action(); }
        catch (Exception error) when (VaultStorageError(error) || VaultHello.PlatformError(error))
        { status.Text = VaultStorageError(error) ? VaultUnavailable : VaultHello.Unavailable; }
        finally
        {
            _vaultDialogOpen = false;
            if (!pinned) SetToolPin(false);
        }
    }
}
