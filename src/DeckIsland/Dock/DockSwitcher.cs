using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// lumabar's Win+T: the open apps in a row, as the taskbar's switcher shows them. The arrow keys (or T again) move between apps, Tab
// between the chosen app's windows, Enter opens the window, Esc or a click elsewhere closes it.
internal sealed class DockSwitcher : Window
{
    private readonly List<DockApp> _apps;
    private readonly DockLook _look;
    private readonly string _accent;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _windows = new() { Margin = new Thickness(0, 10, 0, 0) };
    private int _app, _window;

    public DockSwitcher(IReadOnlyList<DockApp> apps, DockLook look, string accent)
    {
        _apps = apps.Where(a => a.Open).ToList(); _look = look; _accent = accent;
        // Start on the app after the one in front, as Alt+Tab does.
        int front = _apps.FindIndex(a => a.Active);
        _app = _apps.Count == 0 ? 0 : (front + 1) % _apps.Count;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UI.PanelFont; Foreground = UI.Brush(look.Text); Title = Loc.T("切换应用", "Switch apps");
        bool light = look.Text == "#FF1D1D1F";
        var stack = new StackPanel(); stack.Children.Add(_row); stack.Children.Add(_windows);
        Content = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16), MinWidth = 260, Child = stack,
            Background = UI.Brush(light ? "#FAF5F5F7" : "#F21C1C1F"), BorderBrush = UI.Brush(light ? "#1A000000" : "#1FFFFFFF"), BorderThickness = new Thickness(1) };
        PreviewKeyDown += Keys;
        Deactivated += (_, _) => { if (IsVisible) Close(); };
        Loaded += (_, _) => { Activate(); Focus(); };
        Fill();
    }

    private void Fill()
    {
        _row.Children.Clear(); _windows.Children.Clear();
        if (_apps.Count == 0)
        {
            _row.Children.Add(new TextBlock { Text = Loc.T("没有打开的应用", "No open apps"), FontSize = 14, Foreground = UI.Brush(_look.Dim), Margin = new Thickness(8) });
            return;
        }
        for (int i = 0; i < _apps.Count; i++)
        {
            var app = _apps[i];
            var icon = new Image { Width = 44, Height = 44, Source = DockAppWindows.Icon(app.Pinned ? app.Launch : app.Windows[0].Exe, 64) };
            var name = new TextBlock { Text = app.Name, FontSize = 11.5, Width = 76, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0), Foreground = UI.Brush(i == _app ? _look.Text : _look.Dim) };
            var tile = new StackPanel(); tile.Children.Add(icon); tile.Children.Add(name);
            int index = i;
            var frame = new Border { Padding = new Thickness(8), Margin = new Thickness(3), CornerRadius = new CornerRadius(12), Child = tile, Cursor = Cursors.Hand,
                Background = UI.Brush(i == _app ? _look.CardActive : "#00000000"), BorderBrush = UI.Brush(i == _app ? _accent : "#00000000"), BorderThickness = new Thickness(1.5) };
            frame.MouseLeftButtonUp += (_, _) => { _app = index; _window = 0; Open(); };
            _row.Children.Add(frame);
        }
        var windows = _apps[_app].Windows;
        _window = Math.Clamp(_window, 0, windows.Count - 1);
        for (int i = 0; i < windows.Count; i++)
        {
            int index = i;
            var line = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand,
                Background = UI.Brush(i == _window && windows.Count > 1 ? _look.CardActive : "#00000000"),
                Child = new TextBlock { Text = windows[i].Title, FontSize = 12.5, MaxWidth = 520, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = UI.Brush(_look.Text) } };
            line.MouseLeftButtonUp += (_, _) => { _window = index; Open(); };
            _windows.Children.Add(line);
        }
    }

    private void Keys(object sender, KeyEventArgs e)
    {
        if (_apps.Count == 0) { if (e.Key is Key.Escape or Key.Enter) Close(); return; }
        switch (e.Key)
        {
            case Key.Right or Key.T: _app = (_app + 1) % _apps.Count; _window = 0; Fill(); break;
            case Key.Left: _app = (_app - 1 + _apps.Count) % _apps.Count; _window = 0; Fill(); break;
            case Key.Tab: _window = (_window + 1) % _apps[_app].Windows.Count; Fill(); break;
            case Key.Enter: Open(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    public void Next()
    {
        if (_apps.Count == 0) return;
        _app = (_app + 1) % _apps.Count; _window = 0; Fill();
    }

    private void Open()
    {
        var window = _apps[_app].Windows[_window];
        Close();
        DockAppWindows.Focus((nint)window.Handle);
    }
}
