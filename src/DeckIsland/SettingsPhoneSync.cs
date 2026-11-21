using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeckIsland.Core;
using QRCoder;

namespace DeckIsland;

// The "Phone" card of the clipboard sync page: a web page on the local network for a phone with no app (an iPhone, say), opened from a QR
// code, and the same link used by an iOS Shortcut. See ClipboardSyncService.Phone.cs.
public sealed partial class SettingsWindow
{
    private void AddPhoneCard()
    {
        var service = _app.ClipboardSync;
        bool enabled = S.PhoneClipboardSync && S.PhoneSyncToken.Length > 0;
        var content = new StackPanel();
        content.Children.Add(TextLine(Loc.T("手机（浏览器）", "Phone (browser)"), 14, TextPrimary, FontWeights.SemiBold));
        var description = TextLine(Loc.T("无需安装应用：用手机扫描二维码，打开一个网页，就能向这台电脑发送文字和图片，并读取这里最近复制的文字。支持 iPhone 和安卓，也可以用 iOS 快捷指令。",
            "No app to install: scan a QR code with the phone and a web page opens, to send text and pictures to this PC and read the text last copied here. Works with an iPhone or Android, and with an iOS Shortcut."), 12, TextSecondary);
        description.TextWrapping = TextWrapping.Wrap; description.Margin = new Thickness(0, 6, 0, 8);
        content.Children.Add(description);
        content.Children.Add(Toggle(Loc.T("手机页面", "Phone page"), enabled, value =>
        {
            Change(s =>
            {
                s.PhoneClipboardSync = value;
                if (value && s.PhoneSyncToken.Length == 0) s.PhoneSyncToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            });
            ShowPage(ClipboardSyncSettingsPage);
        }, help: Loc.T("在本地网络上开启一个网页，链接里带有密钥。只有本地网络内的设备可以访问。链接是明文 HTTP，请只在可信的网络中使用。",
            "Opens a web page on the local network, with a secret in its link. Only devices on the local network can reach it. The link is plain HTTP, so use it only on a network you trust.")));
        if (enabled)
        {
            string url = service.PhoneUrl();
            if (url.Length == 0)
                content.Children.Add(TextLine(Loc.T("没有找到本地网络地址。", "No local network address was found."), 12, TextSecondary));
            else
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 8) };
                var qr = new System.Windows.Controls.Image { Width = 140, Height = 140, Source = Qr(url), Margin = new Thickness(0, 0, 16, 0) };
                RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);
                System.Windows.Automation.AutomationProperties.SetName(qr, Loc.T("手机页面的二维码", "QR code of the phone page"));
                var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 330 };
                if (service.PhoneStatus.Length > 0) side.Children.Add(TextLine(service.PhoneStatus, 12, TextSecondary));
                var link = TextLine(url, 12, TextPrimary); link.TextWrapping = TextWrapping.Wrap; link.Margin = new Thickness(0, 4, 0, 8);
                side.Children.Add(link);
                var copy = ActionButton(Loc.T("复制链接", "Copy link"), () => { try { Clipboard.SetText(url); } catch (System.Runtime.InteropServices.COMException) { } });
                var renew = ActionButton(Loc.T("新链接", "New link"), () =>
                {
                    if (!Confirm(Loc.T("生成新链接？", "Make a new link?"), Loc.T("旧链接和二维码将失效，手机需要重新扫描。", "The old link and QR code stop working, and the phone has to scan again."),
                        Loc.T("新链接", "New link"), danger: false)) return;
                    Change(s => s.PhoneSyncToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
                    ShowPage(ClipboardSyncSettingsPage);
                });
                foreach (var button in new[] { copy, renew }) { button.Style = (Style)FindResource("SettingsOutlineButton"); }
                renew.Margin = new Thickness(8, 0, 0, 0);
                side.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { copy, renew } });
                row.Children.Add(qr); row.Children.Add(side);
                content.Children.Add(row);
                var shortcut = TextLine(Loc.T(
                    "iOS 快捷指令：用“获取 URL 内容”。读取电脑上的文字：对 链接的 /clip 发送 GET，再接“拷贝到剪贴板”。发送到电脑：对 /clip 发送 POST，请求正文选“文件”，内容为“剪贴板”。图片发到 /image。可以放到背面轻点或主屏幕。",
                    "iOS Shortcut: use “Get Contents of URL”. To read the text on the PC: GET the link with /clip as the path, then “Copy to Clipboard”. To send to the PC: POST to /clip with the request body set to File and the Clipboard as its content. Pictures go to /image. Put it on Back Tap or the Home Screen."), 12, TextSecondary);
                shortcut.TextWrapping = TextWrapping.Wrap; shortcut.Margin = new Thickness(0, 0, 0, 10);
                content.Children.Add(shortcut);
            }
        }
        var card = new Border { Child = content, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 14, 16, 8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
        card.SetResourceReference(Border.BackgroundProperty, "SettingsSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "SettingsBorder");
        Target.Children.Add(card);
    }

    // Black on white, with its quiet border, so a phone's camera reads it on any panel colour.
    private static BitmapImage Qr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8, [0, 0, 0, 255], [255, 255, 255, 255]);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
