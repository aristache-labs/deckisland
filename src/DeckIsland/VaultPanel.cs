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
    private bool _vaultCodes, _vaultDialogOpen;
    private DispatcherTimer? _vaultClock;
    private DispatcherTimer? _vaultReveal;
    private TextBlock? _vaultRevealed;
    private VaultStore Vault => _app.Vault;

    private void VaultPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Vault);

    internal void ClearVaultView()
    {
        _vaultClock?.Stop();
        _vaultClock = null;
        HideVaultPassword();
        if (VaultContent is null) return;
        foreach (var box in _vaultInputs) box.Clear();
        _vaultInputs.Clear();
        VaultContent.Children.Clear();
    }

    private void HideVaultPassword()
    {
        _vaultReveal?.Stop();
        _vaultReveal = null;
        if (_vaultRevealed is not null)
        {
            _vaultRevealed.Text = "";
            _vaultRevealed.Visibility = Visibility.Collapsed;
        }
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
        if (_preview || ReminderAlertId is not null) ClearVaultView();
        else if (_expanded && VaultContent.Children.Count == 0) BuildVault();
    }

    private Button VaultButton(string label, Action action)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("PanelOutlineButton"),
            Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(8, 3, 8, 3) };
        button.Click += (_, _) =>
        {
            _app.ScheduleVaultLock();
            if (VaultContent.IsAncestorOf(button)) action();
        };
        return button;
    }

    private const string VaultKeyGlyph = "M14,10 A4,4 0 1 0 10,6 L2,14 V17 H5 V15 H8 V12 L11,9";
    private const string VaultEyeGlyph = "M1,8 Q8,-1 15,8 Q8,17 1,8 Z M10,8 A2,2 0 1 1 6,8 A2,2 0 1 1 10,8";
    private const string VaultEditGlyph = "M2,14 L3,10 L11,2 L14,5 L6,13 Z M9,4 L12,7";
    private const string VaultAccountGlyph = "M11,5 A3,3 0 1 1 5,5 A3,3 0 1 1 11,5 M2,15 Q2,10 8,10 Q14,10 14,15";
    private const string VaultCopyGlyph = "M5,5 H14 V14 H5 Z M2,11 V2 H11";
    private const string VaultGenerateGlyph = "M14,6 A6,6 0 0 0 3,4 M14,2 V6 H10 M2,10 A6,6 0 0 0 13,12 M2,14 V10 H6";

    private System.Windows.Shapes.Path VaultGlyph(string geometry, double size) => new()
    {
        Data = Geometry.Parse(geometry), Width = size, Height = size, Stretch = Stretch.Uniform,
        Stroke = UI.Brush(Look.Secondary), StrokeThickness = 1.25, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };

    private Button VaultIcon(string geometry, string label, Action action)
    {
        var button = VaultButton(label, action);
        button.Style = (Style)FindResource("WidgetSurfaceButton");
        button.Content = VaultGlyph(geometry, 14);
        button.Width = button.Height = 28;
        button.Padding = new Thickness(0);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
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
        void EmptyAction(string label, Action run)
        {
            var button = VaultButton(label, run);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.Margin = new Thickness(0, 12, 0, 0);
            empty.Children.Add(button);
        }
        try
        {
            var document = Vault.Read();
            if (document is null)
            {
                empty.Children.Add(WeatherText(Loc.T("保险库尚未设置。", "Set up Vault to save passwords and two-factor keys."), 13));
                EmptyAction(Loc.T("设置 PIN", "Set up PIN"), VaultSetup);
                root.Children.Add(empty);
                return;
            }

            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(toolbar, DockSide.Top);
            root.Children.Add(toolbar);
            void Section(bool codes)
            {
                _vaultCodes = codes;
                BuildVault();
            }
            var passwords = VaultButton(Loc.T("密码", "Passwords"), () => Section(false));
            var codes = VaultButton("2FA", () => Section(true));
            void MarkSelected(Button button)
            {
                button.Tag = "Selected";
                AutomationProperties.SetItemStatus(button, Loc.T("已选择", "Selected"));
            }
            MarkSelected(_vaultCodes ? codes : passwords);
            toolbar.Children.Add(passwords);
            toolbar.Children.Add(codes);
            if (document.Protected(_vaultCodes) && _app.VaultKey is null)
            {
                toolbar.LastChildFill = false;
                empty.Children.Add(WeatherText(document.Hello is null
                    ? Loc.T("输入 PIN 以访问此部分。", "Enter your PIN to access this section.")
                    : Loc.T("使用 Windows Hello 解密保险库。", "Use Windows Hello to decrypt the vault."), 13));
                string unlock = document.Hello is null ? Loc.T("解锁", "Unlock") : Loc.T("使用 Windows Hello 解锁", "Unlock with Windows Hello");
                EmptyAction(unlock, () => VaultUnlockForm(document));
                root.Children.Add(empty);
                return;
            }
            var entries = Vault.Entries(document, _vaultCodes, _app.VaultKey);
            var search = new TextBox { Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(6, 0, 0, 0) };
            AutomationProperties.SetName(search, Loc.T("搜索保险库", "Search vault"));
            search.ToolTip = Loc.T("按名称或账户搜索", "Search by name or account");
            StyleVaultInput(search);
            toolbar.Children.Add(search);
            var status = WeatherText("", 11);
            DockPanel.SetDock(status, DockSide.Bottom);
            root.Children.Add(status);
            if (_app.VaultKey is not null) actions.Children.Add(VaultButton(Loc.T("锁定", "Lock"), _app.LockVaultViews));
            actions.Children.Add(VaultButton(Loc.T("添加", "Add"), () => EditVault(document, entries, -1)));
            var list = new StackPanel();
            if (entries.Count == 0)
            {
                empty.Children.Add(VaultGlyph(_vaultCodes ? "M1,1 H6 V6 H1 Z M10,1 H15 V6 H10 Z M1,10 H6 V15 H1 Z M10,10 H15 V15 M10,15 V13" : VaultKeyGlyph, 22));
                var title = WeatherText(_vaultCodes ? Loc.T("暂无双重验证码", "No 2FA codes yet") : Loc.T("暂无密码", "No passwords yet"), 15);
                title.HorizontalAlignment = HorizontalAlignment.Center;
                title.Margin = new Thickness(0, 9, 0, 3);
                empty.Children.Add(title);
                var hint = WeatherText(_vaultCodes ? Loc.T("粘贴二维码或输入密钥。", "Paste a QR code or type the key.")
                    : Loc.T("登录信息加密保存在此电脑。", "Logins stay encrypted on this PC."), 12);
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                hint.Foreground = UI.Brush(Look.Secondary);
                empty.Children.Add(hint);
                EmptyAction(_vaultCodes ? "+ " + Loc.T("添加双重验证", "Add 2FA") : "+ " + Loc.T("添加密码", "Add password"), () => EditVault(document, entries, -1));
                root.Children.Add(empty);
                return;
            }
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
                    shown.Visibility = Visibility.Collapsed;
                    var feedback = WeatherText("", 11);
                    DateTimeOffset feedbackUntil = default;
                    feedback.Visibility = Visibility.Collapsed;
                    feedback.Foreground = UI.Brush(Look.Secondary);
                    var detail = new StackPanel { Children = { WeatherText(entry.Name, 14), WeatherText(entry.Account, 11), shown, feedback } };
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
                            tools.Children.Add(VaultIcon(VaultAccountGlyph, Loc.T("复制用户名", "Copy username"), () => CopyVault(entry.Account, false, status)));
                        tools.Children.Add(VaultIcon(VaultEyeGlyph, Loc.T("显示密码", "Show password"), () =>
                        {
                            if (_vaultRevealed == shown) { HideVaultPassword(); return; }
                            HideVaultPassword();
                            _vaultRevealed = shown;
                            shown.Text = entry.Password;
                            shown.Visibility = Visibility.Visible;
                            _vaultReveal = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                            _vaultReveal.Tick += (_, _) => HideVaultPassword();
                            _vaultReveal.Start();
                        }));
                    }
                    tools.Children.Add(VaultIcon(VaultEditGlyph, Loc.T("编辑条目", "Edit entry"), () => EditVault(document, entries, index)));
                    if (entry.Otp is null)
                    {
                        tick.Add(now => { if (now >= feedbackUntil) feedback.Visibility = Visibility.Collapsed; });
                        tools.Children.Add(VaultIcon(VaultKeyGlyph, Loc.T("复制密码", "Copy password"), () =>
                        {
                            CopyVault(entry.Password, true, feedback);
                            feedbackUntil = DateTimeOffset.UtcNow.AddSeconds(30);
                            feedback.Visibility = Visibility.Visible;
                        }));
                    }
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
        catch (Exception error) when (VaultStorageError(error))
        {
            root.Children.Add(WeatherText(VaultUnavailable, 13));
        }
    }

    private void EditVault(VaultStore.Document document, List<VaultEntry> entries, int index)
    {
        var entry = index >= 0 ? entries[index] : null;
        var body = VaultForm(_vaultCodes
            ? entry is null ? Loc.T("添加双重验证", "Add 2FA") : Loc.T("编辑双重验证", "Edit 2FA")
            : entry is null ? Loc.T("添加密码", "Add password") : Loc.T("编辑密码", "Edit password"));
        var status = WeatherText("", 12);
        status.Margin = new Thickness(0, 5, 0, 5);
        string nameLabel = Loc.T("名称", "Name"), accountLabel = Loc.T("用户名或邮箱", "Username or email");
        var name = new TextBox { Text = entry?.Name ?? "", MaxLength = 120 };
        var account = new TextBox { Text = entry?.Account ?? "", MaxLength = 240 };
        CommandManager.AddPreviewExecutedHandler(account, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Copy || e.Command == ApplicationCommands.Cut) e.Handled = true;
        });
        var secret = new PasswordBox { MaxLength = 4096, Password = entry?.Password ?? "" };
        var passwordTools = new DockPanel();
        if (_vaultCodes)
        {
            VaultField(body, nameLabel, name);
            VaultField(body, Loc.T("账户", "Account"), account);
            VaultField(body, Loc.T("密钥或 otpauth URI", "Key or otpauth URI"), secret);
        }
        else
        {
            var fields = new Grid();
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            void Add(string label, Control input, FrameworkElement? content = null)
            {
                int row = fields.RowDefinitions.Count;
                fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var caption = WeatherText(label, 12);
                caption.Margin = new Thickness(10, 0, 8, 0);
                caption.VerticalAlignment = VerticalAlignment.Center;
                StyleVaultInput(input);
                AutomationProperties.SetName(input, label);
                content ??= input;
                content.Margin = new Thickness(0, 3, 7, 3);
                Grid.SetRow(caption, row);
                Grid.SetRow(content, row);
                Grid.SetColumn(content, 1);
                fields.Children.Add(caption);
                fields.Children.Add(content);
            }
            Add(nameLabel, name);
            Add(accountLabel, account);
            Add(Loc.T("密码", "Password"), secret, passwordTools);
            body.Children.Add(new Border { Background = UI.Brush(Look.Hex("fill")), CornerRadius = new CornerRadius(Corner(8)), Child = fields });
        }
        VaultOtp? imported = null;
        bool filling = false;
        secret.PasswordChanged += (_, _) => { if (!filling) imported = null; };
        var options = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
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
            var lengthLabel = WeatherText($"{Loc.T("长度", "Length")} · 20", 12);
            length.ValueChanged += (_, _) => lengthLabel.Text = $"{Loc.T("长度", "Length")} · {(int)length.Value}";
            options.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0),
                Children = { lengthLabel, length } });
            options.Children.Add(digits);
            options.Children.Add(symbols);
            void Tool(Button button)
            {
                DockPanel.SetDock(button, DockSide.Right);
                passwordTools.Children.Add(button);
            }
            Tool(VaultIcon(VaultGenerateGlyph, Loc.T("生成密码", "Generate password"), () =>
            {
                secret.Password = VaultRules.Generate((int)length.Value, digits.IsChecked == true, symbols.IsChecked == true);
                status.Text = "";
            }));
            Tool(VaultIcon(VaultCopyGlyph, Loc.T("复制密码", "Copy password"), () => CopyVault(secret.Password, true, status)));
            var preview = WeatherText("", 12);
            preview.IsHitTestVisible = false;
            preview.Visibility = Visibility.Collapsed;
            preview.Margin = new Thickness(8, 0, 4, 0);
            preview.TextTrimming = TextTrimming.CharacterEllipsis;
            var field = new DockPanel();
            var eye = VaultIcon(VaultEyeGlyph, Loc.T("显示密码", "Show password"), () =>
            {
                if (_vaultRevealed == preview) { HideVaultPassword(); return; }
                if (secret.Password.Length == 0) return;
                HideVaultPassword();
                _vaultRevealed = preview;
                preview.Text = secret.Password;
                preview.Visibility = Visibility.Visible;
                _vaultReveal = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                _vaultReveal.Tick += (_, _) => HideVaultPassword();
                _vaultReveal.Start();
            });
            DockPanel.SetDock(eye, DockSide.Right);
            field.Children.Add(eye);
            field.Children.Add(new Grid { Children = { secret, preview } });
            preview.Background = UI.Brush(Look.Hex("fill"));
            preview.VerticalAlignment = VerticalAlignment.Center;
            passwordTools.Children.Add(field);
        }

        body.Children.Add(status);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, DockSide.Right);
        footer.Children.Add(buttons);
        body.Children.Add(footer);
        if (!_vaultCodes)
        {
            var strength = WeatherText("", 11);
            var meter = new ProgressBar { Minimum = 0, Maximum = 3, Width = 48, Height = 4, Margin = new Thickness(0, 0, 6, 0) };
            footer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { meter, strength } });
            void UpdateStrength()
            {
                string value = secret.Password;
                int variety = (value.Any(char.IsUpper) ? 1 : 0) + (value.Any(char.IsLower) ? 1 : 0) +
                    (value.Any(char.IsDigit) ? 1 : 0) + (value.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
                int level = value.Length == 0 ? 0 : value.Length >= 16 && variety >= 3 && value.Distinct().Count() >= 10 ? 3
                    : value.Length >= 12 && value.Distinct().Count() >= 6 ? 2 : 1;
                meter.Value = level;
                meter.Foreground = UI.Brush(Look.Hex(level == 1 ? "danger" : "accent"));
                strength.Text = level switch { 3 => Loc.T("强", "Strong"), 2 => Loc.T("一般", "Fair"), 1 => Loc.T("弱", "Weak"), _ => "" };
                meter.Visibility = level == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            secret.PasswordChanged += (_, _) => { HideVaultPassword(); UpdateStrength(); };
            UpdateStrength();
        }
        buttons.Children.Add(VaultButton(Loc.T("取消", "Cancel"), BuildVault));
        if (entry is not null)
        {
            var delete = VaultButton(Loc.T("删除", "Delete"), () => { });
            var confirm = VaultButton(Loc.T("确认删除", "Confirm delete"), () => Commit([.. entries.Where((_, i) => i != index)]));
            confirm.Foreground = UI.Brush(Look.Hex("danger"));
            confirm.Visibility = Visibility.Collapsed;
            delete.Click += (_, _) =>
            {
                status.Text = Loc.T("永久删除此条目？", "Permanently delete this entry?");
                delete.Visibility = Visibility.Collapsed;
                confirm.Visibility = Visibility.Visible;
                confirm.Focus();
            };
            buttons.Children.Add(delete);
            buttons.Children.Add(confirm);
        }
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

        void Commit(List<VaultEntry> updated)
        {
            if (!_expanded || CurrentPage != DeckPage.Vault || !VaultContent.Children.OfType<ScrollViewer>().Any(view => view.Content == body)) return;
            try
            {
                Vault.Save(document, _vaultCodes, _app.VaultKey, updated);
                secret.Clear();
                BuildVault();
            }
            catch (Exception error) when (VaultStorageError(error)) { status.Text = VaultUnavailable; }
        }
    }

    private static void CopyVault(string value, bool expire, TextBlock status)
    {
        if (value.Length == 0) return;
        try
        {
            VaultClipboard.Copy(value, expire);
            status.Text = expire ? Loc.T("已复制 · 30 秒后清除", "Copied · clears in 30 s") : Loc.T("已复制。", "Copied.");
        }
        catch (ExternalException) { status.Text = Loc.T("无法写入剪贴板。", "Clipboard is unavailable."); }
    }
}
