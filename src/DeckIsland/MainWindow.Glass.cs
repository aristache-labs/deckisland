using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// Glass, as Crest has it: the screen behind the island shows through it, blurred and slightly bent at the edges, under the theme's
// surface (a tint of it, GlassLook.Tint) and a light rim. Windows' own blur (Acrylic, Mica) does not reach a window that draws its own
// shape, so the screen behind is captured: a few times a second (DeckSettings.GlassFps), only the island's rectangle, shrunk as it is
// read (the shrinking is most of the blur, GlassLook.Shrink). While it captures, DeckIsland is left out of captures so it never reads
// itself; that also keeps it out of screenshots and recordings, and NVIDIA's Instant Replay stops when it sees that.
public partial class MainWindow
{
    private DispatcherTimer? _glassTimer;
    private IDisposable? _glassHold;
    private bool _glassOn;

    // Glass shows on the open panel; on the closed notch or capsule too when chosen, and under a status alert when chosen for those.
    private bool GlassWanted => Settings.Glass && IsVisible && !IsSuppressed && !DisplayPower.Off && !_preview && Look.Current.Effects.Pixel <= 0
        && (_expanded || EffectiveStyle is CompactStyle.Notch or CompactStyle.Capsule && (Settings.GlassCollapsed || Settings.GlassAlerts && _hudMessage is not null));

    // Asked whenever the panel opens or closes, a readout comes or goes, or the settings change.
    internal void UpdateGlass()
    {
        bool want = GlassWanted;
        if (want != _glassOn)
        {
            _glassOn = want;
            if (want)
            {
                _glassHold = _app.HoldCaptureExclusion();
                _glassTimer ??= new DispatcherTimer(DispatcherPriority.Render, Dispatcher);
                _glassTimer.Tick -= GlassTick; _glassTimer.Tick += GlassTick;
            }
            else
            {
                _glassTimer?.Stop();
                _glassHold?.Dispose(); _glassHold = null;
                Island.Glass = null;
            }
            ApplySurface();
        }
        if (!want) return;
        var look = GlassLook.For(Settings);
        Island.GlassZoom = look.Zoom; Island.GlassRim = look.Rim;
        // Plain properties: a slider moved shows at once, not only with the next picture.
        Island.InvalidateVisual();
        _glassTimer!.Interval = TimeSpan.FromSeconds(1d / Math.Clamp(Settings.GlassFps, GlassLook.MinFps, GlassLook.MaxFps));
        _glassTimer.Start();
        GlassTick(null, EventArgs.Empty);
    }

    private void GlassTick(object? sender, EventArgs e)
    {
        if (!GlassWanted) { UpdateGlass(); return; }
        if (Island.ActualWidth < 4 || Island.ActualHeight < 4 || PresentationSource.FromVisual(Island) is null) return;
        var from = Island.PointToScreen(new Point(0, 0));
        var to = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
        int x = (int)Math.Floor(Math.Min(from.X, to.X)), y = (int)Math.Floor(Math.Min(from.Y, to.Y));
        int width = (int)Math.Ceiling(Math.Abs(to.X - from.X)), height = (int)Math.Ceiling(Math.Abs(to.Y - from.Y));
        double shrink = GlassLook.For(Settings).Shrink;
        if (GlassCapture.Grab(x, y, width, height, shrink) is { } picture) Island.Glass = picture;
    }
}

public partial class MainWindow
{
    // A picture of the island as DeckIsland draws it, glass included, saved with the screenshots and opened: the island is left out of
    // real screenshots while the glass is on, so this is how it can be shown. Null when the island is not drawn yet.
    internal string? SaveIslandPicture()
    {
        if (Stage.ActualWidth < 1 || Stage.ActualHeight < 1) return null;
        double dpi = 96 * VisualTreeHelper.GetDpi(this).DpiScaleX;
        var picture = new RenderTargetBitmap((int)Math.Ceiling(Stage.ActualWidth * dpi / 96), (int)Math.Ceiling(Stage.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        picture.Render(Stage);
        picture.Freeze();
        return ScreenShots.Save(picture, jpeg: false);
    }
}

// What the glass settings mean when drawn.
internal readonly record struct GlassLook(double Tint, double Zoom, double Rim, double Shrink)
{
    public const int MinFps = 5, MaxFps = 30, MaxTransparency = 40, ReadableTransparency = 13;
    // Transparency 0 to 40 %: the theme's surface over the glass goes from 85 % covering at 0 to 20 % at 40 %, so even the least lets
    // the glass be seen; refraction: up to 12 % enlargement from the middle; frost: the picture shrunk 1.5 to 10 times as it is read
    // (the shrinking is the blur); rim: the edge's light.
    public static GlassLook For(DeckSettings settings) => new(
        .85 - Math.Clamp(settings.GlassTransparency, 0, MaxTransparency) / (double)MaxTransparency * .65,
        1 + Math.Clamp(settings.GlassRefraction, 0, 100) / 100d * .12,
        Math.Clamp(settings.GlassRim, 0, 100) / 100d,
        1.5 + Math.Clamp(settings.GlassFrost, 0, 100) / 100d * 8.5);
}

// The screen's pixels in a rectangle, shrunk as they are read (Windows averages them, which blurs), as a frozen picture.
internal static class GlassCapture
{
    public static BitmapSource? Grab(int x, int y, int width, int height, double shrink)
    {
        int small = Math.Max(1, (int)Math.Round(width / shrink)), tall = Math.Max(1, (int)Math.Round(height / shrink));
        nint screen = GetDC(0);
        if (screen == 0) return null;
        nint memory = CreateCompatibleDC(screen), bitmap = CreateCompatibleBitmap(screen, small, tall);
        try
        {
            if (memory == 0 || bitmap == 0) return null;
            nint old = SelectObject(memory, bitmap);
            SetStretchBltMode(memory, Halftone); SetBrushOrgEx(memory, 0, 0, 0);
            bool read = StretchBlt(memory, 0, 0, small, tall, screen, x, y, width, height, SourceCopy | CaptureBlt);
            SelectObject(memory, old);
            if (!read) return null;
            var picture = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            picture.Freeze();
            return picture;
        }
        catch (Exception error) when (error is COMException or ArgumentException or OutOfMemoryException) { return null; }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    private const int Halftone = 4;
    private const uint SourceCopy = 0x00CC0020, CaptureBlt = 0x40000000;
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(nint dc, int x, int y, nint previous);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, int sw, int sh, uint operation);
}
