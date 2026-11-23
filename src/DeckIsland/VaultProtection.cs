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

    private void VaultPinScreen(string title, Func<string, TextBlock, Task> submit)
    {
        ClearVaultView();
        var form = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        VaultContent.Children.Add(form);
        form.Children.Add(new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = UI.Brush(Look.Hex("fillSelected")),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = VaultGlyph("M3,7 H13 V15 H3 Z M5,7 V4 A3,3 0 0 1 11,4 V7", 16)
        });
        var heading = WeatherText(title, 15);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, 10, 0, 8);
        form.Children.Add(heading);

        var input = new Grid { Width = 120, Height = 34, HorizontalAlignment = HorizontalAlignment.Center };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var pin = new PasswordBox { MaxLength = 4, Opacity = 0 };
        StyleVaultInput(pin);
        AutomationProperties.SetName(pin, title);
        pin.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
        input.Children.Add(pin);
        input.Children.Add(dots);
        for (int i = 0; i < 4; i++)
            dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, StrokeThickness = 1,
                Stroke = UI.Brush(Look.Secondary), Margin = new Thickness(7, 0, 7, 0) });
        form.Children.Add(input);

        var hint = WeatherText(Loc.T("输入四位数字", "Type four digits"), 12);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Foreground = UI.Brush(Look.Secondary);
        form.Children.Add(hint);
        var status = VaultStatus(form);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        var cancel = VaultButton(Loc.T("取消", "Cancel"), BuildVault);
        cancel.HorizontalAlignment = HorizontalAlignment.Center;
        form.Children.Add(cancel);

        pin.PasswordChanged += (_, _) =>
        {
            using var masked = pin.SecurePassword;
            int length = masked.Length;
            for (int i = 0; i < dots.Children.Count; i++)
                ((System.Windows.Shapes.Ellipse)dots.Children[i]).Fill = i < length ? UI.Brush(Look.Primary) : null;
            if (length != 4) return;
            Dispatcher.BeginInvoke(async () =>
            {
                if (!VaultContent.IsAncestorOf(pin) || !VaultRules.ValidPin(pin.Password)) return;
                string value = pin.Password;
                pin.Clear();
                await submit(value, status);
                if (VaultContent.IsAncestorOf(status) && status.Text.Length > 0)
                {
                    status.Foreground = UI.Brush(Look.Hex("danger"));
                    foreach (System.Windows.Shapes.Ellipse dot in dots.Children) dot.Stroke = status.Foreground;
                }
            });
        };
        BeginTyping(pin);
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
            : Loc.T($"PIN 不正确，还可尝试 {Math.Max(0, 5 - (Vault.Read()?.Failures ?? 0))} 次。", $"Wrong PIN · {Math.Max(0, 5 - (Vault.Read()?.Failures ?? 0))} tries left");
        return null;
    }

    private void VaultUnlockForm(VaultStore.Document document, Action? resume = null)
    {
        async Task Unlock(string? pin, TextBlock status) => await VaultRun(status, async () =>
        {
            var key = pin is null ? await VaultHello.Unlock(document.Hello!) : CheckVaultPin(pin, status);
            if (key is null) return;
            try
            {
                if (!VaultContent.IsAncestorOf(status) || (pin is null && Vault.Read()?.Revision != document.Revision)) return;
                Vault.PrepareListings(Vault.Read() ?? throw new IOException(), key);
                _app.UnlockVault(key);
                key = null;
                resume?.Invoke();
                BuildVault();
            }
            finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
        });

        if (document.Hello is null)
        {
            VaultPinScreen(Loc.T("输入 PIN", "Enter PIN"), async (pin, status) => await Unlock(pin, status));
            return;
        }
        var body = VaultForm(Loc.T("解锁保险库", "Unlock Vault"), Loc.T("使用 Windows Hello 解密保险库。", "Use Windows Hello to decrypt the vault."));
        var status = VaultStatus(body);
        VaultActions(body, Loc.T("使用 Windows Hello 解锁", "Unlock with Windows Hello"), async () => await Unlock(null, status));
    }

    private void VaultSetup()
    {
        VaultPinScreen(Loc.T("设置 PIN", "Set PIN"), (pin, _) =>
        {
            VaultPinScreen(Loc.T("确认 PIN", "Confirm PIN"), async (confirmation, status) => await VaultRun(status, () =>
            {
                if (pin != confirmation)
                {
                    status.Text = Loc.T("PIN 不匹配，请重试。", "PINs don’t match. Try again.");
                    return Task.CompletedTask;
                }
                Vault.Configure(null, null, confirmation, pinPasswords: true, pinCodes: true);
                BuildVault();
                return Task.CompletedTask;
            }));
            return Task.CompletedTask;
        });
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
