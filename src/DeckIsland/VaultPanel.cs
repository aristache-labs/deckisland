using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeckIsland.Core;
using DockSide = System.Windows.Controls.Dock;

namespace DeckIsland;

public partial class MainWindow
{
    private byte[]? _vaultKey;
    private bool _vaultCodes, _vaultDialogOpen;
    private int _vaultLockVersion;
    private DispatcherTimer? _vaultClock;
    private DispatcherTimer? _vaultReveal;
    private TextBlock? _vaultRevealed;
    private VaultStore Vault => _app.Vault;

    private void VaultPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Vault);

    internal void LockVault()
    {
        _vaultLockVersion++;
        if (_vaultKey is { } key) CryptographicOperations.ZeroMemory(key);
        _vaultKey = null;
        ClearVaultView();
    }

    private void ClearVaultView()
    {
        _vaultClock?.Stop();
        _vaultClock = null;
        HideVaultPassword();
        if (VaultContent is null) return;
        if (VaultContent.Children.Count == 1 && VaultContent.Children[0] is ScrollViewer { Content: StackPanel editor })
            foreach (var box in editor.Children.OfType<PasswordBox>()) box.Clear();
        VaultContent.Children.Clear();
    }

    private void HideVaultPassword()
    {
        _vaultReveal?.Stop();
        _vaultReveal = null;
        if (_vaultRevealed is not null) _vaultRevealed.Text = "";
        _vaultRevealed = null;
    }

    private void RenderVault()
    {
        foreach (var page in DeckPages.Order)
            if (page != DeckPage.Vault) PageContent(page).Visibility = Visibility.Collapsed;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        if (!double.IsFinite(ContentCanvas.Height)) return;
        var header = PanelHeader.For(ToolbarFor(ContentCanvas.Height), 18);
        ApplyHeader(header);
        ShowHeaderPin(header);
        VaultContent.Margin = new Thickness(header.Side, 0, header.Side, MonitorBottom(ContentCanvas.Height));
        if (_preview || ReminderAlertId is not null) LockVault();
        else if (_expanded && VaultContent.Children.Count == 0) BuildVault();
    }

    private Button VaultButton(string label, Action action)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("PanelOutlineButton"),
            Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(8, 3, 8, 3) };
        button.Click += (_, _) => action();
        return button;
    }

    private void BuildVault()
    {
        ClearVaultView();
        var root = new DockPanel();
        VaultContent.Children.Add(root);
        var top = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
        DockPanel.SetDock(top, DockSide.Top);
        root.Children.Add(top);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, DockSide.Right);
        top.Children.Add(actions);
        top.Children.Add(WeatherText("Vault", 16));
        var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        try
        {
            var document = Vault.Read();
            if (document is null)
            {
                empty.Children.Add(WeatherText(Loc.T("保险库尚未设置。", "Set up Vault to save passwords and two-factor keys."), 13));
                empty.Children.Add(VaultButton(Loc.T("设置", "Set up"), async () => await WithVaultDialog(() =>
                {
                    VaultPinDialog.Manage(this, Vault);
                    return Task.CompletedTask;
                })));
                root.Children.Add(empty);
                return;
            }

            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(toolbar, DockSide.Top);
            root.Children.Add(toolbar);
            void Section(bool codes)
            {
                _vaultCodes = codes;
                LockVault();
                BuildVault();
            }
            var passwords = VaultButton(Loc.T("密码", "Passwords"), () => Section(false));
            var codes = VaultButton("2FA", () => Section(true));
            passwords.IsEnabled = _vaultCodes;
            codes.IsEnabled = !_vaultCodes;
            toolbar.Children.Add(passwords);
            toolbar.Children.Add(codes);
            if (document.Protected(_vaultCodes) && _vaultKey is null)
            {
                toolbar.LastChildFill = false;
                empty.Children.Add(WeatherText(document.Hello is null
                    ? Loc.T("输入 PIN 以访问此部分。", "Enter your PIN to access this section.")
                    : Loc.T("使用 Windows Hello 解密保险库。", "Use Windows Hello to decrypt the vault."), 13));
                string unlock = document.Hello is null ? Loc.T("解锁", "Unlock") : Loc.T("使用 Windows Hello 解锁", "Unlock with Windows Hello");
                empty.Children.Add(VaultButton(unlock, async () => await WithVaultDialog(async () =>
                {
                    int version = _vaultLockVersion;
                    byte[]? key = document.Hello is { } hello ? await VaultHello.Unlock(hello) : VaultPinDialog.Unlock(this, Vault);
                    if (key is null) return;
                    try
                    {
                        if (!_expanded || CurrentPage != DeckPage.Vault || version != _vaultLockVersion) return;
                        if (document.Hello is not null && Vault.Read()?.Revision != document.Revision) return;
                        _vaultKey = key;
                        key = null;
                    }
                    finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
                })));
                root.Children.Add(empty);
                return;
            }
            var entries = Vault.Entries(document, _vaultCodes, _vaultKey);
            var search = new TextBox { Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(6, 0, 0, 0) };
            AutomationProperties.SetName(search, Loc.T("搜索保险库", "Search vault"));
            search.ToolTip = Loc.T("按名称或账户搜索", "Search by name or account");
            TakeKeyboardOnClick(search);
            toolbar.Children.Add(search);
            var status = WeatherText("", 11);
            DockPanel.SetDock(status, DockSide.Bottom);
            root.Children.Add(status);
            if (document.Protected(_vaultCodes)) actions.Children.Add(VaultButton(Loc.T("锁定", "Lock"), () => { LockVault(); BuildVault(); }));
            actions.Children.Add(VaultButton(Loc.T("添加", "Add"), () => EditVault(document, entries, -1)));
            var list = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            void Fill()
            {
                _vaultClock?.Stop();
                _vaultClock = null;
                HideVaultPassword();
                list.Children.Clear();
                var tick = new List<Action<DateTimeOffset>>();
                foreach (var (entry, index) in entries.Select((item, i) => (item, i)))
                {
                    if (!entry.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) &&
                        !entry.Account.Contains(search.Text, StringComparison.OrdinalIgnoreCase)) continue;
                    var row = new DockPanel();
                    var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(tools, DockSide.Right);
                    row.Children.Add(tools);
                    var shown = WeatherText("", 12);
                    var detail = new StackPanel { Children = { WeatherText(entry.Name, 14), WeatherText(entry.Account, 11), shown } };
                    row.Children.Add(detail);
                    if (entry.Otp is { } otp)
                    {
                        var readout = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 100 };
                        tools.Children.Add(readout);
                        tick.Add(now => readout.Text = $"{otp.Code(now)}   {otp.Period - now.ToUnixTimeSeconds() % otp.Period:00}s");
                        tools.Children.Add(VaultButton(Loc.T("复制验证码", "Copy code"), () => CopyVault(otp.Code(DateTimeOffset.UtcNow), true, status)));
                    }
                    else
                    {
                        if (entry.Account.Length > 0)
                            tools.Children.Add(VaultButton(Loc.T("复制账户", "Copy account"), () => CopyVault(entry.Account, false, status)));
                        tools.Children.Add(VaultButton(Loc.T("显示", "Show"), () =>
                        {
                            if (_vaultRevealed == shown) { HideVaultPassword(); return; }
                            HideVaultPassword();
                            _vaultRevealed = shown;
                            shown.Text = entry.Password;
                            _vaultReveal = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                            _vaultReveal.Tick += (_, _) => HideVaultPassword();
                            _vaultReveal.Start();
                        }));
                        tools.Children.Add(VaultButton(Loc.T("复制密码", "Copy password"), () => CopyVault(entry.Password, true, status)));
                    }
                    tools.Children.Add(VaultButton(Loc.T("编辑", "Edit"), () => EditVault(document, entries, index)));
                    list.Children.Add(new Border { Child = row, Background = UI.Brush(Look.Hex("fill")),
                        CornerRadius = new CornerRadius(Corner(8)), Padding = new Thickness(10, 5, 6, 5), Margin = new Thickness(0, 0, 0, 4) });
                }
                if (list.Children.Count == 0) list.Children.Add(WeatherText(entries.Count == 0
                    ? Loc.T("还没有条目。", "No entries yet.") : Loc.T("无匹配项。", "No matching entries."), 13));
                if (tick.Count == 0) return;
                void Update()
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var update in tick) update(now);
                }
                Update();
                _vaultClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _vaultClock.Tick += (_, _) => Update();
                _vaultClock.Start();
            }
            search.TextChanged += (_, _) => Fill();
            Fill();
        }
        catch (Exception error) when (VaultPinDialog.StorageError(error))
        {
            root.Children.Add(WeatherText(VaultPinDialog.Unavailable, 13));
        }
    }

    private async Task WithVaultDialog(Func<Task> action)
    {
        if (_vaultDialogOpen) return;
        bool pinned = _toolPinned;
        if (!pinned) SetToolPin(true);
        _vaultDialogOpen = true;
        try { await action(); }
        catch (Exception error) when (VaultPinDialog.StorageError(error) || VaultHello.PlatformError(error))
        { MessageBox.Show(this, VaultHello.PlatformError(error) ? VaultHello.Unavailable : VaultPinDialog.Unavailable, "Vault"); }
        finally
        {
            _vaultDialogOpen = false;
            if (!pinned) SetToolPin(false);
            if (_expanded && CurrentPage == DeckPage.Vault) BuildVault();
        }
    }

    private void EditVault(VaultStore.Document document, List<VaultEntry> entries, int index)
    {
        ClearVaultView();
        var entry = index >= 0 ? entries[index] : null;
        var body = new StackPanel();
        VaultContent.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var status = WeatherText("", 12);
        string nameLabel = Loc.T("名称", "Name"), accountLabel = Loc.T("用户名或邮箱", "Username or email");
        var name = new TextBox { Text = entry?.Name ?? "", MaxLength = 120 };
        var account = new TextBox { Text = entry?.Account ?? "", MaxLength = 240 };
        CommandManager.AddPreviewExecutedHandler(account, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Copy || e.Command == ApplicationCommands.Cut) e.Handled = true;
        });
        AddField(nameLabel, name);
        AddField(_vaultCodes ? Loc.T("账户", "Account") : accountLabel, account);
        var secret = new PasswordBox { MaxLength = 4096, Password = entry?.Password ?? "" };
        AddField(_vaultCodes ? Loc.T("密钥或 otpauth URI", "Key or otpauth URI") : Loc.T("密码", "Password"), secret);
        VaultOtp? imported = null;
        bool filling = false;
        secret.PasswordChanged += (_, _) => { if (!filling) imported = null; };
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        body.Children.Add(options);

        void PasteQr()
        {
            try
            {
                var text = VaultClipboard.ReadQr();
                var result = text is not null && text.StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase) ? VaultOtp.Parse(text) : null;
                if (result is not { } qr) { status.Text = Loc.T("没有找到有效的 TOTP 二维码。", "No valid TOTP QR code found."); return; }
                filling = true;
                secret.Clear();
                filling = false;
                imported = qr.Otp;
                if (name.Text.Length == 0) name.Text = qr.Name[..Math.Min(qr.Name.Length, name.MaxLength)];
                if (account.Text.Length == 0) account.Text = qr.Account[..Math.Min(qr.Account.Length, account.MaxLength)];
                status.Text = Loc.T("二维码已读取。", "QR code imported.");
            }
            catch (Exception error) when (error is ExternalException or ArgumentException or InvalidOperationException or NotSupportedException)
            { status.Text = Loc.T("无法读取剪贴板中的图片。", "Could not read the clipboard image."); }
        }

        if (_vaultCodes)
        {
            var paste = VaultButton(Loc.T("粘贴二维码截图", "Paste QR screenshot"), PasteQr);
            paste.ToolTip = Loc.T("使用 Win + Shift + S 截图，然后按 Ctrl + V。", "Capture it with Win + Shift + S, then press Ctrl + V.");
            options.Children.Add(paste);
            body.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
                try { if (!Clipboard.ContainsImage()) return; }
                catch (ExternalException) { return; }
                e.Handled = true;
                PasteQr();
            };
        }
        else
        {
            var digits = new CheckBox { Content = Loc.T("数字", "Digits"), IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            var symbols = new CheckBox { Content = Loc.T("符号", "Symbols"), IsChecked = true, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var length = new Slider { Minimum = 12, Maximum = 64, Value = 20, Width = 95, TickFrequency = 1, IsSnapToTickEnabled = true };
            AutomationProperties.SetName(length, Loc.T("密码长度", "Password length"));
            options.Children.Add(length);
            options.Children.Add(digits);
            options.Children.Add(symbols);
            options.Children.Add(VaultButton(Loc.T("生成", "Generate"), () =>
            {
                secret.Password = VaultRules.Generate((int)length.Value, digits.IsChecked == true, symbols.IsChecked == true);
                status.Text = Loc.T("已生成密码。", "Password generated.");
            }));
            options.Children.Add(VaultButton(Loc.T("复制密码", "Copy password"), () => CopyVault(secret.Password, true, status)));
        }

        body.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        body.Children.Add(buttons);
        buttons.Children.Add(VaultButton(Loc.T("取消", "Cancel"), BuildVault));
        if (entry is not null)
            buttons.Children.Add(VaultButton(Loc.T("删除", "Delete"), () =>
            {
                if (MessageBox.Show(this, Loc.T("删除此条目？", "Delete this entry?"), "Vault", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                    Commit([.. entries.Where((_, i) => i != index)]);
            }));
        buttons.Children.Add(VaultButton(Loc.T("保存", "Save"), () =>
        {
            string nameValue = name.Text.Trim(), accountValue = account.Text.Trim(), password = "";
            if (nameValue.Length == 0) { status.Text = Loc.T("请输入名称。", "Enter a name."); return; }
            VaultOtp? otp = null;
            if (_vaultCodes)
            {
                otp = imported ?? (secret.Password.Length == 0 ? entry?.Otp : VaultOtp.Parse(secret.Password)?.Otp);
                if (otp is null) { status.Text = Loc.T("请输入有效的 TOTP 密钥。", "Enter a valid TOTP key."); return; }
            }
            else
            {
                password = secret.Password;
                if (password.Length == 0) { status.Text = Loc.T("请输入密码。", "Enter a password."); return; }
            }
            var edited = new VaultEntry(nameValue, accountValue, password, otp);
            var saved = entries.ToList();
            if (index < 0) saved.Add(edited); else saved[index] = edited;
            Commit(saved);
        }));
        BeginTyping(name);

        void AddField(string label, Control control)
        {
            control.Padding = new Thickness(6, 3, 6, 3);
            control.Margin = new Thickness(0, 2, 0, 5);
            AutomationProperties.SetName(control, label);
            TakeKeyboardOnClick(control);
            body.Children.Add(WeatherText(label, 12));
            body.Children.Add(control);
        }

        void Commit(List<VaultEntry> updated)
        {
            if (!_expanded || CurrentPage != DeckPage.Vault || !VaultContent.Children.OfType<ScrollViewer>().Any(view => view.Content == body)) return;
            try
            {
                Vault.Save(document, _vaultCodes, _vaultKey, updated);
                secret.Clear();
                BuildVault();
            }
            catch (Exception error) when (VaultPinDialog.StorageError(error)) { status.Text = VaultPinDialog.Unavailable; }
        }
    }

    private static void CopyVault(string value, bool expire, TextBlock status)
    {
        if (value.Length == 0) return;
        try
        {
            VaultClipboard.Copy(value, expire);
            status.Text = Loc.T("已复制。", "Copied.");
        }
        catch (ExternalException) { status.Text = Loc.T("无法写入剪贴板。", "Clipboard is unavailable."); }
    }
}
