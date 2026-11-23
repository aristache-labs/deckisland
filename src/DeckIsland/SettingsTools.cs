using System.IO;
using System.Windows;
using System.Windows.Controls;
using DeckIsland.Core;

namespace DeckIsland;

// The settings of the Scratchpad, Converter and Terminal tabs.
public sealed partial class SettingsWindow
{
    internal const int ScratchpadSettingsPage = 25, ConverterSettingsPage = 26, TerminalSettingsPage = 27;

    private void ConverterSettings()
    {
        SectionTitle(Loc.T("换算", "Converter"), description: Loc.T("单位和货币换算。", "Units and currencies."));
        var codes = _app.Rates.Codes is { Count: > 0 } known ? known : CommonCurrencies;
        int chosen = Math.Max(0, codes.ToList().IndexOf(S.ConverterBaseCurrency));
        var baseCombo = ProfileCombo(Loc.T("基准货币", "Base currency"), [.. codes.Select(code => _app.Rates.NameOf(code) is var name && name != code ? $"{code} · {name}" : code)], chosen,
            index => Change(s => s.ConverterBaseCurrency = codes[index]), width: 240);
        Group(
            Describe(Row(Loc.T("基准货币", "Base currency"), baseCombo,
                help: () => Loc.T("任意两种货币都通过基准货币换算，它也是默认的货币。各央行发布日期不同，换算显示两者中较早的日期。",
                    "Any two currencies convert through the base currency, which is also the default. The banks publish on different days, so a pair shows the older of its two dates.")),
                Loc.T("换算经过的货币，也是默认货币。", "The pivot for every pair, and the default.")));
    }

    private void CaptureSettings()
    {
        CaptureImageFormat[] formats = [CaptureImageFormat.Png, CaptureImageFormat.Jpeg];
        var format = ProfileCombo(Loc.T("截图格式", "Screenshot format"), [Loc.T("PNG（无损）", "PNG (lossless)"), Loc.T("JPEG（更小）", "JPEG (smaller)")],
            Array.IndexOf(formats, S.CaptureImageFormat), index => Change(s => s.CaptureImageFormat = formats[index]), width: 210);
        int[] heights = [0, 1440, 1080, 720];
        var resolution = ProfileCombo(Loc.T("录屏分辨率", "Recording resolution"), [Loc.T("屏幕原始大小", "Screens' own size"), "1440p", "1080p", "720p"],
            Math.Max(0, Array.IndexOf(heights, S.CaptureMaxHeight)), index => Change(s => s.CaptureMaxHeight = heights[index]), width: 210);
        CaptureQuality[] qualities = [CaptureQuality.Low, CaptureQuality.Standard, CaptureQuality.High];
        var quality = ProfileCombo(Loc.T("录屏质量", "Recording quality"), [Loc.T("低（文件小）", "Low (small files)"), Loc.T("标准", "Standard"), Loc.T("高", "High")],
            Array.IndexOf(qualities, S.CaptureQuality), index => Change(s => s.CaptureQuality = qualities[index]), width: 210);
        int[] rates = [30, 60];
        var fps = ProfileCombo(Loc.T("录屏帧率", "Recording frame rate"), ["30 fps", "60 fps"],
            Math.Max(0, Array.IndexOf(rates, S.CaptureFps)), index => Change(s => s.CaptureFps = rates[index]), width: 210);
        _cardBody = null;
        Target.Children.Add(OptionsCard(Loc.T("截图与录屏", "Capture"),
            Loc.T("截图存到“图片\\屏幕截图”，录屏存到“视频\\DeckIsland\\Recordings”。", "Screenshots go to Pictures\\Screenshots, recordings to Videos\\DeckIsland\\Recordings."),
            Columns(
                Field(Loc.T("截图格式", "Screenshot format"), format, Loc.T("PNG 保留每个像素，JPEG 小得多。", "PNG keeps every pixel; JPEG is much smaller.")),
                Field(Loc.T("录屏分辨率", "Recording resolution"), resolution, Loc.T("多块屏幕录在同一个视频里。", "Several screens go into one video.")),
                Field(Loc.T("录屏质量", "Recording quality"), quality, Loc.T("MP4 · H.264 视频 · AAC 48 kHz 声音。", "MP4 · H.264 video · AAC 48 kHz sound."))),
            Columns(
                Field(Loc.T("录屏帧率", "Recording frame rate"), fps, Loc.T("60 fps 更流畅，文件更大。", "60 fps is smoother, with bigger files.")),
                new Border(), new Border()),
            Rule(),
            Columns(
                SwitchRow(Loc.T("复制到剪贴板", "Copy to the clipboard"), Loc.T("截图也会出现在剪贴板历史里。", "Screenshots also show in the clipboard history."),
                    S.CaptureCopy, value => Change(s => s.CaptureCopy = value)),
                SwitchRow(Loc.T("添加到中转站", "Add to the Shelf"), Loc.T("截图和录屏放进文件中转。", "Screenshots and recordings go to the file transfer."),
                    S.CaptureToShelf, value => Change(s => s.CaptureToShelf = value))),
            Columns(
                SwitchRow(Loc.T("显示录制边框", "Show the capture border"), Loc.T("录制时的黄色边框；关闭需要 Windows 11。", "The yellow border while recording; hiding it needs Windows 11."),
                    S.CaptureBorder, value => Change(s => s.CaptureBorder = value)),
                new Border())));
    }

    private static readonly string[] CommonCurrencies = ["AUD", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "INR", "JPY", "KRW", "MXN", "NOK", "NZD", "PLN", "RON", "SEK", "SGD", "TRY", "USD", "ZAR"];

    private void TerminalSettings()
    {
        SectionTitle(Loc.T("终端", "Terminal"), description: Loc.T("灵动岛收起时会话继续运行；关闭终端标签页会结束会话。", "The session keeps running while the island is collapsed. Turning off the Terminal tab ends the session."));
        var shells = Enum.GetValues<TerminalShell>();
        var shell = ProfileCombo(Loc.T("命令行", "Shell"), [.. shells.Select(TerminalShells.Name)], Array.IndexOf(shells, S.TerminalShell),
            index => Change(s => s.TerminalShell = shells[index]), width: 200);
        var folder = new TextBox { Style = (Style)FindResource("SettingsField"), Height = 30, Padding = new Thickness(10, 0, 10, 0), FontSize = 12.5, Width = 240, Text = S.TerminalStartFolder, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(folder, Loc.T("起始文件夹", "Start folder"));
        folder.LostKeyboardFocus += (_, _) => { if (folder.Text.Trim() != S.TerminalStartFolder) Change(s => s.TerminalStartFolder = folder.Text.Trim()); };
        var browse = ActionButton(Loc.T("选择…", "Choose…"), () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("选择起始文件夹", "Choose the start folder") };
            if (Directory.Exists(S.TerminalStartFolder)) dialog.InitialDirectory = S.TerminalStartFolder;
            if (dialog.ShowDialog(this) != true) return;
            folder.Text = dialog.FolderName;
            Change(s => s.TerminalStartFolder = dialog.FolderName);
        });
        browse.Margin = new Thickness(6, 0, 0, 0);
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { folder, browse } };
        Group(
            Describe(Row(Loc.T("命令行", "Shell"), shell, help: () => Loc.T("没有安装 PowerShell 7 时使用 Windows PowerShell。更改后按终端的重新启动按钮生效。",
                "Windows PowerShell is used where PowerShell 7 is not installed. Changes apply when you press the terminal's restart button.")),
                Loc.T("PowerShell 7、Windows PowerShell 或命令提示符。", "PowerShell 7, Windows PowerShell or Command Prompt.")),
            Describe(Row(Loc.T("起始文件夹", "Start folder"), folderRow, help: () => Loc.T("留空则从用户文件夹开始。", "Left empty, it starts in your user folder.")),
                Loc.T("新会话开始的位置。", "Where a new session starts.")),
            Describe(InlineSlider(Loc.T("字号", "Font size"), S.TerminalFontSize, DeckSettings.MinTerminalFontSize, DeckSettings.MaxTerminalFontSize, 1, "pt",
                value => Change(s => s.TerminalFontSize = (int)value)), Loc.T("终端文字的大小。", "The size of the terminal's text.")));
    }

    private void ScratchpadSizeControls() => PageSizeControls(DeckPage.Scratchpad, Loc.T("代码草稿", "Scratchpad"));
    private void ConverterSizeControls() => PageSizeControls(DeckPage.Converter, Loc.T("换算", "Converter"));
    private void TerminalSizeControls() => PageSizeControls(DeckPage.Terminal, Loc.T("终端", "Terminal"));
}
