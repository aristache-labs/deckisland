using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DeckIsland.Native;

// Screenshots of the screens or of a dragged region, the folders captures are kept in, and leaving DeckIsland's own windows out.
internal static class ScreenShots
{
    private const uint ExcludeFromCapture = 0x11; // WDA_EXCLUDEFROMCAPTURE, Windows 10 2004 and later
    private static int _held;

    // Pictures\Screenshots, where Win+PrtScn saves.
    public static string ShotFolder => KnownFolder(new Guid("b7bede81-df94-4682-a7d8-57a52620b86f"))
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    public static string VideoFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DeckIsland", "Recordings");

    public static string NewPath(string folder, string kind, string extension)
        => System.IO.Path.Combine(folder, $"{kind} {DateTime.Now:yyyy-MM-dd HHmmss}{extension}");

    // Every DeckIsland window is left out of captures while `on` (or always, with `always`).
    public static void Exclude(bool always) { foreach (Window window in Application.Current.Windows) Exclude(window, always || _held > 0); }
    public static void Exclude(Window window, bool on)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle != 0) SetWindowDisplayAffinity(handle, on ? ExcludeFromCapture : 0);
    }

    // Leaves DeckIsland out for the duration of a capture.
    public static IDisposable Hold(Func<bool> always)
    {
        _held++;
        Exclude(always());
        return new Release(() => { _held--; Exclude(always()); });
    }

    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }

    public static BitmapSource Grab(Rect pixels) => ScreenColourPicker.Capture((int)pixels.Left, (int)pixels.Top, (int)pixels.Width, (int)pixels.Height);

    public static string Save(BitmapSource image, bool jpeg)
    {
        string folder = ShotFolder;
        Directory.CreateDirectory(folder);
        string path = NewPath(folder, "Screenshot", jpeg ? ".jpg" : ".png");
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(path)) encoder.Save(file);
        return path;
    }

    // Frozen pictures of every monitor, one window over each; a drag on one picks a region of it. Null when cancelled (Esc, right click).
    public static async Task<BitmapSource?> PickRegion(IReadOnlyList<DisplayInfo> displays)
    {
        var result = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = new List<Window>();
        try
        {
            foreach (var display in displays)
            {
                var shot = Grab(display.Bounds);
                var image = new Image { Source = shot, Stretch = Stretch.Fill };
                var shade = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), IsHitTestVisible = false };
                var frame = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                var canvas = new Canvas { Background = Brushes.Transparent, Children = { frame } };
                var window = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                    WindowStartupLocation = WindowStartupLocation.Manual, Cursor = Cursors.Cross, Background = Brushes.Black,
                    Content = new Grid { Children = { image, shade, canvas } }
                };
                var area = display.Bounds;
                Point? start = null;
                void Shade(Rect hole)
                {
                    var all = new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
                    shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(hole));
                }
                window.Loaded += (_, _) => Shade(Rect.Empty);
                canvas.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(canvas); canvas.CaptureMouse(); };
                canvas.MouseMove += (_, e) =>
                {
                    if (start is not { } from) return;
                    var rect = new Rect(from, e.GetPosition(canvas));
                    Canvas.SetLeft(frame, rect.X); Canvas.SetTop(frame, rect.Y); frame.Width = rect.Width; frame.Height = rect.Height;
                    frame.Visibility = Visibility.Visible;
                    Shade(rect);
                };
                canvas.MouseLeftButtonUp += (_, e) =>
                {
                    if (start is not { } from) return;
                    canvas.ReleaseMouseCapture();
                    var rect = new Rect(from, e.GetPosition(canvas));
                    start = null;
                    // From the window's DIPs to the picture's pixels.
                    double sx = shot.PixelWidth / canvas.ActualWidth, sy = shot.PixelHeight / canvas.ActualHeight;
                    var pixels = new Int32Rect((int)(rect.X * sx), (int)(rect.Y * sy), (int)Math.Round(rect.Width * sx), (int)Math.Round(rect.Height * sy));
                    if (pixels.Width < 4 || pixels.Height < 4) { frame.Visibility = Visibility.Collapsed; Shade(Rect.Empty); return; }
                    var cropped = new CroppedBitmap(shot, pixels);
                    cropped.Freeze();
                    result.TrySetResult(cropped);
                };
                window.MouseRightButtonDown += (_, _) => result.TrySetResult(null);
                window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) result.TrySetResult(null); };
                window.Closed += (_, _) => result.TrySetResult(null);
                window.SourceInitialized += (_, _) =>
                    SetWindowPos(new WindowInteropHelper(window).Handle, -1, (int)area.Left, (int)area.Top, (int)area.Width, (int)area.Height, 0x0010);
                windows.Add(window);
            }
            foreach (var window in windows) window.Show();
            var cursor = WindowsHost.Cursor();
            int active = Math.Max(0, displays.ToList().FindIndex(d => d.Bounds.Contains(cursor)));
            if (windows.Count > 0) windows[active].Activate();
            return await result.Task;
        }
        finally { foreach (var window in windows) window.Close(); }
    }

    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, 0, out nint path) != 0) return null;
        try { return Marshal.PtrToStringUni(path); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("user32")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("shell32")] private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
}
