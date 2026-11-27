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
    private readonly Border _tint = new();
    private readonly DispatcherTimer _glass = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _glassOn;
    private byte[]? _lastGlass;
    // The glass blur: the screen behind read at 1/GlassShrink of its size and stretched back; the larger, the softer.
    private const double GlassShrink = 4;
    private DockLook _look = DockLook.For(DockStyle.Black, DockClearCards.Black);

    // The style's colours on the bar, the clock and (through RefreshSoon) the tiles; glass started or stopped to match.
    private void ApplyLook()
    {
        var settings = _app.DockSettings;
        _look = DockLook.For(DockSmart.Effective(settings.Style, settings.SunsetDarkMode, _app.DockDaylight), settings.ClearCards);
        _tint.Background = UI.Brush(_look.Bar);
        _bar.BorderBrush = UI.Brush(_look.Rim);
        // A transparent rim would leave a 1px gap around the tint that shows the screen behind; no rim, no border.
        _bar.BorderThickness = new Thickness(settings.Style == DockStyle.Clear || _look.Rim.StartsWith("#00", StringComparison.Ordinal) ? 0 : 1);
        _tint.BorderThickness = new Thickness(0);
        // Liquid, iOS and Midnight: a 2px glossy rim (UpdateGlassRim), drawn as two 1px lines so the inner one can be fainter; iOS's
        // is a cool white and brighter, Liquid's a neutral grey, Midnight's faint.
        _glossStrength = 0;
        switch (DockSmart.Effective(settings.Style, settings.SunsetDarkMode, _app.DockDaylight))
        {
            case DockStyle.Liquid or DockStyle.IOS or DockStyle.Midnight:
                var style = DockSmart.Effective(settings.Style, settings.SunsetDarkMode, _app.DockDaylight);
                // Midnight's rim follows the same pattern, barely there under its deep tint.
                _glossStrength = style switch { DockStyle.IOS => 1, DockStyle.Liquid => 0.75, _ => 0.14 };
                _glossTint = style switch
                {
                    DockStyle.IOS => Color.FromRgb(0xD7, 0xE1, 0xF5), DockStyle.Liquid => Color.FromRgb(0xC8, 0xCC, 0xD2), _ => Color.FromRgb(0xC8, 0xCD, 0xE1)
                };
                _bar.BorderThickness = new Thickness(1);
                _tint.BorderThickness = new Thickness(1);
                break;
        }
        _bar.Background = Brushes.Transparent;
        _bar.Height = OuterBarHeight;
        // The tint sits inside the rim, so its corners are the bar's less the rim's width.
        double radius = settings.FullWidth ? 0 : DockGeometry.CornerRadius(settings.Corner, OuterBarHeight);
        _bar.CornerRadius = new CornerRadius(radius);
        _tint.CornerRadius = new CornerRadius(Math.Max(0, radius - _bar.BorderThickness.Left));
        UpdateGlassRim();
        _lastGlass = null;
        Foreground = UI.Brush(_look.Text);
        // The tiles were drawn in the previous style's colours.
        _signature = "";
        if (_handle == IntPtr.Zero) return;
        // The glass styles read the screen behind the bar for a soft blur, drawn inside the bar's own rounded shape (Windows' blur
        // is too strong and cannot follow the corners); every frame, so a window sliding behind moves smoothly through the glass.
        if (_look.Glass != _glassOn)
        {
            _glassOn = _look.Glass;
            _lastGlass = null;
            if (_glassOn) _glass.Start(); else _glass.Stop();
        }
        // Reading the screen would read the dock too, so while it does the dock is left out of captures; otherwise as the island's
        // setting says.
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

    // Liquid and iOS rims as lumabar draws them (measured): a steady cool line, a little brighter along the top than the bottom,
    // with light caught on the top-left and bottom-right curves and the other two curves in shade. 0 for the other styles.
    private double _glossStrength;
    private Color _glossTint;

    private void UpdateGlassRim()
    {
        if (_glossStrength <= 0 || _bar.ActualWidth < 4 || _bar.ActualHeight < 4) return;
        double width = _bar.ActualWidth, height = _bar.ActualHeight, radius = _bar.CornerRadius.TopLeft, rim = _bar.BorderThickness.Left;
        _bar.BorderBrush = GlassRimBrush(width, height, radius, _glossStrength, _glossTint);
        // The inner line repeats the pattern, fainter, for the second pixel of the rim.
        _tint.BorderBrush = GlassRimBrush(width - 2 * rim, height - 2 * rim, Math.Max(0, radius - rim), _glossStrength * 0.6, _glossTint);
    }

    private static Brush GlassRimBrush(double width, double height, double radius, double strength, Color tint)
    {
        byte Alpha(double share) => (byte)Math.Clamp(share * strength * 255, 0, 255);
        var group = new DrawingGroup();
        var line = new LinearGradientBrush(Color.FromArgb(Alpha(0.45), tint.R, tint.G, tint.B), Color.FromArgb(Alpha(0.31), tint.R, tint.G, tint.B), 90);
        group.Children.Add(new GeometryDrawing(line, null, new RectangleGeometry(new Rect(0, 0, width, height))));
        double reach = Math.Max(radius, height * 0.45) * 1.1;
        void Spot(double x, double y, Color colour) => group.Children.Add(new GeometryDrawing(
            new RadialGradientBrush(colour, Color.FromArgb(0, colour.R, colour.G, colour.B)), null, new EllipseGeometry(new Point(x, y), reach, reach)));
        Spot(0, 0, Color.FromArgb(Alpha(0.85), 0xF5, 0xF8, 0xFF));
        Spot(width, height, Color.FromArgb(Alpha(0.7), 0xF5, 0xF8, 0xFF));
        Spot(width, 0, Color.FromArgb(Alpha(0.5), 0, 0, 0));
        Spot(0, height, Color.FromArgb(Alpha(0.5), 0, 0, 0));
        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height),
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, width, height)
        };
        brush.Freeze();
        return brush;
    }

    // The tiles' cards follow the bar's corners, a little tighter.
    private double TileRadius() => Math.Clamp(DockGeometry.CornerRadius(_app.DockSettings.Corner, _app.DockSettings.Size) - 4, 3, 12);

    // Glass under the bar: the screen behind it, read small and stretched back, a soft blur.
    private void GlassTick()
    {
        if (!_glassOn || _dragging || _reveal <= 0 || _handle == IntPtr.Zero || _bar.ActualWidth < 4 || PresentationSource.FromVisual(_bar) is null) return;
        ReadGlass(_bar.PointToScreen(new Point(0, 0)), _bar.PointToScreen(new Point(_bar.ActualWidth, _bar.ActualHeight)));
    }

    private void ReadGlass(Point from, Point to)
    {
        int x = (int)Math.Floor(from.X), y = (int)Math.Floor(from.Y), width = (int)Math.Ceiling(to.X - from.X), height = (int)Math.Ceiling(to.Y - from.Y);
        if (GlassCapture.Grab(x, y, width, height, GlassShrink) is not { } grabbed) return;
        // A screen bitmap comes back as Bgra32 with every alpha 0, so drawn as is it would be invisible; read as Bgr32 it is opaque.
        var picture = new FormatConvertedBitmap(grabbed, PixelFormats.Bgr32, null, 0);
        picture.Freeze();
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
