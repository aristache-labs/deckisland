using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// lumabar's Smart Search, opened from the dock's Search widget: apps from the Start menu, a command for the dock said in plain words
// (DockSmart.Parse), a sum worked out, or a web search. Enter runs the first result, the arrow keys move, Esc or a click elsewhere
// closes it.
internal sealed class DockSearch : Window
{
    private const int MaxApps = 6;
    private static List<(string Name, string Path)>? _apps;
    private readonly App _app;
    private readonly DockLook _look;
    private readonly TextBox _box = new() { FontSize = 16, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(4, 6, 4, 6) };
    private readonly StackPanel _results = new() { Margin = new Thickness(0, 6, 0, 0) };
    private List<(string Text, string Detail, Action Run, ImageSource? Icon)> _shown = [];
    private int _selected;

    public DockSearch(App app, DockLook look, Point anchor, bool above)
    {
        _app = app; _look = look;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true;
        ResizeMode = ResizeMode.NoResize; Width = 520; SizeToContent = SizeToContent.Height; FontFamily = UI.PanelFont; Foreground = UI.Brush(look.Text);
        Title = Loc.T("搜索", "Search");
        _box.Foreground = UI.Brush(look.Text); _box.CaretBrush = UI.Brush(look.Text);
        AutomationProperties.SetName(_box, Loc.T("搜索应用和命令", "Search apps and commands"));
        var stack = new StackPanel(); stack.Children.Add(_box); stack.Children.Add(_results);
        // A search card in the dock's style, solid so it reads over anything.
        string surface = look.Glass || look.Bar == "#00000000" ? (look.Text == "#FF1D1D1F" ? "#FAF5F5F7" : "#F21C1C1F") : look.Bar;
        Content = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 12), Background = UI.Brush(surface), BorderBrush = UI.Brush(look.Rim == "#00000000" ? look.CardRim : look.Rim),
            BorderThickness = new Thickness(1), Child = stack };
        _box.TextChanged += (_, _) => { _selected = 0; Fill(); };
        PreviewKeyDown += Keys;
        Deactivated += (_, _) => { if (IsVisible) Close(); };
        Loaded += (_, _) =>
        {
            Left = anchor.X - Width / 2;
            Top = above ? anchor.Y - ActualHeight - 12 : anchor.Y + 12;
            _box.Focus(); Activate();
        };
        SizeChanged += (_, _) => { if (above) Top = anchor.Y - ActualHeight - 12; };
        _ = LoadApps();
        Fill();
    }

    // The Start menu's shortcuts, read once and kept.
    private async Task LoadApps()
    {
        if (_apps is not null) return;
        _apps = await Task.Run(() =>
        {
            var folders = new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<(string, string)>();
            foreach (var folder in folders.Where(Directory.Exists))
            {
                try
                {
                    foreach (var path in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(path);
                        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || !seen.Add(name)) continue;
                        found.Add((name, path));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return found;
        });
        if (IsLoaded) Fill();
    }

    private void Fill()
    {
        var text = _box.Text.Trim();
        var results = new List<(string, string, Action, ImageSource?)>();
        var command = DockSmart.Parse(text);
        if (command.Kind != DockCommandKind.None)
            results.Add((Describe(command), command.Kind == DockCommandKind.Answer ? Loc.T("回车复制", "Enter copies") : Loc.T("命令", "Command"), () => Run(command), null));
        if (text.Length > 0)
        {
            foreach (var (name, path) in (_apps ?? []).Where(a => a.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                         .OrderBy(a => a.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ThenBy(a => a.Name.Length).Take(MaxApps))
                results.Add((name, Loc.T("应用", "App"), () => DockAppWindows.Launch(path), DockAppWindows.Icon(path, 32)));
            results.Add((Loc.T($"在网上搜索“{text}”", $"Search the web for “{text}”"), Loc.T("网页", "Web"),
                () => DockAppWindows.Launch("https://www.bing.com/search?q=" + Uri.EscapeDataString(text)), null));
        }
        _shown = results;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, results.Count - 1));
        _results.Children.Clear();
        if (results.Count == 0)
            _results.Children.Add(new TextBlock { Text = Loc.T("输入应用名、算式，或“volume 30”、“dock to the top”。", "Type an app, a sum, or “volume 30”, “dock to the top”."),
                Foreground = UI.Brush(_look.Dim), FontSize = 12.5, Margin = new Thickness(4, 2, 4, 2), TextWrapping = TextWrapping.Wrap });
        for (int i = 0; i < results.Count; i++) _results.Children.Add(Row(results[i], i));
    }

    private FrameworkElement Row((string Text, string Detail, Action Run, ImageSource? Icon) result, int index)
    {
        var icon = new Image { Width = 22, Height = 22, Source = result.Icon, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = result.Text, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = UI.Brush(_look.Text) };
        var detail = new TextBlock { Text = result.Detail, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = UI.Brush(_look.Dim) };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left); DockPanel.SetDock(detail, System.Windows.Controls.Dock.Right);
        row.Children.Add(icon); row.Children.Add(detail); row.Children.Add(text);
        var frame = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6), Child = row, Cursor = Cursors.Hand,
            Background = UI.Brush(index == _selected ? _look.CardActive : "#00000000") };
        frame.MouseLeftButtonUp += (_, _) => Execute(index);
        return frame;
    }

    private void Keys(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Down: _selected = Math.Min(_selected + 1, _shown.Count - 1); Fill(); e.Handled = true; break;
            case Key.Up: _selected = Math.Max(_selected - 1, 0); Fill(); e.Handled = true; break;
            case Key.Enter: Execute(_selected); e.Handled = true; break;
        }
    }

    private void Execute(int index)
    {
        if (index < 0 || index >= _shown.Count) return;
        var run = _shown[index].Run;
        Close();
        run();
    }

    private static string Describe(DockCommand command) => command.Kind switch
    {
        DockCommandKind.Volume => Loc.T($"音量设为 {command.Number}%", $"Set the volume to {command.Number}%"),
        DockCommandKind.Edge => command.Edge == DockEdge.Top ? Loc.T("把 Dock 移到顶部", "Move the dock to the top") : Loc.T("把 Dock 移到底部", "Move the dock to the bottom"),
        DockCommandKind.Bigger => Loc.T("让 Dock 变大", "Make the dock bigger"),
        DockCommandKind.Smaller => Loc.T("让 Dock 变小", "Make the dock smaller"),
        DockCommandKind.Style => Loc.T($"Dock 样式：{command.Style}", $"Dock style: {command.Style}"),
        DockCommandKind.AutoHide => command.On ? Loc.T("开启自动隐藏", "Turn auto-hide on") : Loc.T("关闭自动隐藏", "Turn auto-hide off"),
        DockCommandKind.Lock => command.On ? Loc.T("锁定 Dock", "Lock the dock") : Loc.T("解锁 Dock", "Unlock the dock"),
        DockCommandKind.OpenSettings => Loc.T("打开 Dock 设置", "Open the dock's settings"),
        DockCommandKind.OpenWidgets => Loc.T("打开小组件", "Open widgets"),
        DockCommandKind.TaskManager => Loc.T("打开任务管理器", "Open Task Manager"),
        DockCommandKind.Answer => $"= {command.Text}",
        _ => command.Text
    };

    private void Run(DockCommand command)
    {
        var settings = _app.DockSettings.Copy();
        switch (command.Kind)
        {
            case DockCommandKind.Volume:
                using (var volume = SystemVolume.Open()) if (volume is not null) volume.Level = command.Number / 100f;
                return;
            case DockCommandKind.Edge: settings.Edge = command.Edge; break;
            case DockCommandKind.Bigger: settings.Size += 6; break;
            case DockCommandKind.Smaller: settings.Size -= 6; break;
            case DockCommandKind.Style: settings.Style = command.Style; break;
            case DockCommandKind.AutoHide: settings.AutoHide = command.On; break;
            case DockCommandKind.Lock: settings.Locked = command.On; break;
            case DockCommandKind.OpenSettings: _app.OpenSettingsPage(SettingsWindow.DockSettingsPage); return;
            case DockCommandKind.OpenWidgets: _app.OpenDockPicker(); return;
            case DockCommandKind.TaskManager: DockAppWindows.Launch("taskmgr.exe"); return;
            case DockCommandKind.Answer: Clipboard.SetText(command.Text); return;
            default: return;
        }
        _app.UpdateDockSettings(settings);
    }
}
