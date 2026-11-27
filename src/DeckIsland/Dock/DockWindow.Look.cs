using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// The dock's look (DockLook): the bar's surface, the clock's card, and glass. Glass is the island's (MainWindow.Glass): the screen
// behind the bar captured a few times a second, shrunk as it is read so it comes out blurred, under the style's tint. A picture
// that has not changed is not drawn again, so the glass rests while nothing moves behind it. While a glass style is on, the dock is
// left out of screen captures, or it would read itself.
internal sealed partial class DockWindow
{
    private const double GlassShrink = 7;
    private readonly Border _tint = new();
    private readonly DispatcherTimer _glass = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly TextBlock _weekday = new() { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = UI.Brush(DockLook.Weekday) };
    private readonly TextBlock _date = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _time = new() { FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Border _clockCard = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(4, 4, 0, 4) };
    private readonly StackPanel _clockArea = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _clockDay = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Shapes.Rectangle _clockDivider = new() { Width = 1, Margin = new Thickness(12, 9, 12, 9) };
    private byte[]? _lastGlass;
    private bool _glassOn;
    private DockLook _look = DockLook.For(DockStyle.Black, DockClearCards.Black);

    private void ClockCard()
    {
        _glass.Tick += (_, _) => GlassTick();
        _clockDay.Children.Add(_weekday); _clockDay.Children.Add(_date);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_clockDay); row.Children.Add(_clockDivider); row.Children.Add(_time);
        _time.VerticalAlignment = VerticalAlignment.Center;
        _clockCard.Child = row;
        _clockArea.Children.Add(_clockCard);
    }

    private void UpdateClock(DateTime now, DockSettings settings)
    {
        _weekday.Text = now.ToString("dddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
        _date.Text = now.ToString("MMM d", CultureInfo.CurrentCulture);
        _time.Text = DockGeometry.Clock(now, settings.Clock24Hour, settings.ClockSeconds, CultureInfo.CurrentCulture);
    }

    // The style's colours on the bar, the clock and (through RefreshSoon) the tiles; glass started or stopped to match.
    private void ApplyLook()
    {
        var settings = _app.DockSettings;
        _look = DockLook.For(DockSmart.Effective(settings.Style, settings.SunsetDarkMode, _app.DockDaylight), settings.ClearCards);
        _tint.Background = UI.Brush(_look.Bar);
        _bar.BorderBrush = UI.Brush(!settings.FullWidth && settings.Style == DockStyle.Black ? "#24FFFFFF" : _look.Rim);
        _bar.BorderThickness = new Thickness(settings.Style == DockStyle.Clear ? 0 : 1);
        if (!_look.Glass) _bar.Background = Brushes.Transparent;
        Foreground = UI.Brush(_look.Text);
        _date.Foreground = _time.Foreground = UI.Brush(_look.Text);
        _clockDivider.Fill = UI.Brush(_look.Divider);
        _clockCard.Background = UI.Brush(settings.AppCards || settings.Style == DockStyle.Clear ? _look.Card : "#00000000");
        _clockCard.BorderBrush = UI.Brush(_look.CardRim); _clockCard.BorderThickness = new Thickness(settings.AppCards || settings.Style == DockStyle.Clear ? 1 : 0);
        _clockCard.CornerRadius = new CornerRadius(TileRadius());
        // The tiles were drawn in the previous style's colours.
        _signature = "";
        if (_handle == IntPtr.Zero) return;
        if (_look.Glass != _glassOn)
        {
            _glassOn = _look.Glass;
            _lastGlass = null;
            if (_glassOn) _glass.Start(); else _glass.Stop();
        }
        // Left out of captures while it reads the screen behind it; otherwise as the island's setting says.
        ScreenShots.Exclude(this, _glassOn || _app.Settings.HideFromCapture);
        if (_glassOn) GlassTick();
    }

    private BitmapSource? _accentCover;
    private string _coverAccent = DockLook.DefaultAccent;

    // The accent the dock wears: the chosen one, or (Album accent) the strongest colour of the cover playing.
    private string Accent
    {
        get
        {
            var settings = _app.DockSettings;
            if (!settings.AlbumAccent || _app.Media?.Cover is not { } cover) return settings.Accent;
            if (!ReferenceEquals(cover, _accentCover))
            {
                _accentCover = cover;
                _coverAccent = MusicBackdrop.Palette(cover, 1) is [var colour] ? $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}" : settings.Accent;
            }
            return _coverAccent;
        }
    }

    // The tiles' cards follow the bar's corners, a little tighter.
    private double TileRadius() => Math.Clamp(DockGeometry.CornerRadius(_app.DockSettings.Corner, _app.DockSettings.Size) - 4, 3, 12);

    private void GlassTick()
    {
        if (!_glassOn || _dragging || _reveal <= 0 || _bar.ActualWidth < 4 || PresentationSource.FromVisual(_bar) is null) return;
        var from = _bar.PointToScreen(new Point(0, 0));
        var to = _bar.PointToScreen(new Point(_bar.ActualWidth, _bar.ActualHeight));
        int x = (int)Math.Floor(from.X), y = (int)Math.Floor(from.Y), width = (int)Math.Ceiling(to.X - from.X), height = (int)Math.Ceiling(to.Y - from.Y);
        if (GlassCapture.Grab(x, y, width, height, GlassShrink) is not { } picture) return;
        int stride = picture.PixelWidth * ((picture.Format.BitsPerPixel + 7) / 8);
        var pixels = new byte[stride * picture.PixelHeight];
        picture.CopyPixels(pixels, stride, 0);
        if (_lastGlass is not null && pixels.AsSpan().SequenceEqual(_lastGlass)) return;
        _lastGlass = pixels;
        var brush = new ImageBrush(picture) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        _bar.Background = brush;
    }

    // The bar's own menu, on its empty parts.
    private ContextMenu BarMenu()
    {
        var menu = NewMenu();
        void Add(string header, Action action, string? colour = null)
        {
            var item = new MenuItem { Header = header };
            if (colour is not null) item.Foreground = UI.Brush(colour);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add(Loc.T("小组件…", "Widgets…"), _app.OpenDockPicker);
        // The profiles, the one in use ticked; built when the menu opens, so it shows the current ones.
        var profiles = new MenuItem { Header = Loc.T("配置", "Profiles") };
        menu.Items.Add(profiles);
        menu.Opened += (_, _) =>
        {
            profiles.Items.Clear();
            var settings = _app.DockSettings;
            foreach (var profile in settings.Profiles)
            {
                var item = new MenuItem { Header = profile.Name, IsCheckable = true, IsChecked = profile.Id == settings.CurrentProfile };
                string id = profile.Id;
                item.Click += (_, _) => { var copy = _app.DockSettings.Copy(); copy.CurrentProfile = id; _app.UpdateDockSettings(copy); };
                profiles.Items.Add(item);
            }
        };
        Add(Loc.T("Dock 设置", "Settings"), () => _app.OpenSettingsPage(SettingsWindow.DockSettingsPage));
        if (!_app.DockSettings.Locked) Add(Loc.T("导入任务栏固定应用", "Import taskbar pins"), ImportTaskbarPins);
        Add(Loc.T("任务管理器", "Task Manager"), () => DockAppWindows.Launch("taskmgr.exe"));
        menu.Items.Add(new Separator());
        Add(Loc.T("关闭 Dock", "Close dock"), _app.TurnOffDock, "#FF453A");
        return menu;
    }

    private static ResourceDictionary? _menuStyles;
    private static ResourceDictionary MenuStyles => _menuStyles ??= new ResourceDictionary { Source = new Uri("/DeckIsland;component/Dock/DockMenu.xaml", UriKind.Relative) };

    // A menu drawn as lumabar's (DockMenu.xaml), in the dock's colours as they are when it opens.
    private ContextMenu NewMenu()
    {
        // Rows and separators take their looks by type (an item container style would also reach the separators, which it cannot style).
        var menu = new ContextMenu { Style = (Style)MenuStyles["DockMenu"] };
        menu.Resources[typeof(MenuItem)] = MenuStyles["DockMenuItem"];
        menu.Resources[MenuItem.SeparatorStyleKey] = MenuStyles["DockMenuSeparator"];
        void Colour()
        {
            // Over the screen rather than the bar, so glass and Clear styles get a solid card.
            bool light = _look.Text == "#FF1D1D1F";
            menu.Resources["DockMenuBack"] = UI.Brush(light ? "#FAF8F8FA" : "#F51E1E21");
            menu.Resources["DockMenuRim"] = UI.Brush(light ? "#1A000000" : "#1FFFFFFF");
            menu.Resources["DockMenuHover"] = UI.Brush(light ? "#14000000" : "#1AFFFFFF");
            menu.Resources["DockMenuText"] = UI.Brush(_look.Text);
            menu.Resources["DockMenuDim"] = UI.Brush(_look.Dim);
            menu.Resources["DockMenuAccent"] = UI.Brush(Accent);
        }
        Colour();
        menu.Opened += (_, _) => Colour();
        return menu;
    }

    private static bool FindTile(DependencyObject source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is Border { Tag: DockApp } or Border { Tag: DockWidget, ContextMenu: not null }) return true;
        return false;
    }
}
