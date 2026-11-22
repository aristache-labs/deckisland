using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland.Native;

internal static class ScreenColourPicker
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Area
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    }

    private delegate bool MonitorCallback(nint monitor, nint dc, ref Area bounds, nint data);

    private sealed class Surface(Area area, BitmapSource image)
    {
        public Area Area { get; } = area;
        public BitmapSource Image { get; } = image;
        public Window Window { get; } = new()
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Topmost = true, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Cursor = Cursors.Cross, Background = Brushes.Black, UseLayoutRounding = true
        };
        public Canvas Canvas { get; } = new() { ClipToBounds = true };
        public Image Loupe { get; } = new() { Width = 126, Height = 126, Stretch = Stretch.Fill };
        public TextBlock Hex { get; } = new() { Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas") };
        public Border Lens { get; } = new()
        {
            Background = Brushes.Black, BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
            IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        public nint Handle => new WindowInteropHelper(Window).Handle;

        public void Prepare()
        {
            var screenshot = new Image { Source = Image, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(screenshot, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetBitmapScalingMode(Loupe, BitmapScalingMode.NearestNeighbor);
            var target = new Border { Width = 14, Height = 14, BorderBrush = Brushes.White, BorderThickness = new Thickness(2) };
            var zoom = new Grid { Children = { Loupe, target } };
            Lens.Child = new StackPanel { Children = { zoom, Hex } };
            Canvas.Children.Add(Lens);
            Window.Content = new Grid { Children = { screenshot, Canvas } };
            Window.SourceInitialized += (_, _) => Place(show: false);
        }

        public void Place(bool show)
        {
            uint flags = 0x0010 | (show ? 0x0040u : 0);
            if (!SetWindowPos(Handle, new nint(-1), Area.Left, Area.Top, Area.Width, Area.Height, flags))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public CssColour Read(Point point)
        {
            int x = (int)point.X - Area.Left, y = (int)point.Y - Area.Top;
            byte[] pixel = new byte[4];
            Image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return new(pixel[2], pixel[1], pixel[0]);
        }

        public void Magnify(Point point)
        {
            int x = (int)point.X - Area.Left, y = (int)point.Y - Area.Top;
            const int radius = 4, size = 9, stride = size * 4;
            int left = Math.Max(0, x - radius), top = Math.Max(0, y - radius);
            int width = Math.Min(Area.Width, x + radius + 1) - left, height = Math.Min(Area.Height, y + radius + 1) - top;
            byte[] pixels = new byte[stride * size];
            Image.CopyPixels(new Int32Rect(left, top, width, height), pixels, stride, (top - y + radius) * stride + (left - x + radius) * 4);
            Loupe.Source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            Hex.Text = Read(point).Hex;
            Point dip = Canvas.PointFromScreen(point);
            Canvas.SetLeft(Lens, Math.Clamp(dip.X + (dip.X + 150 > Canvas.ActualWidth ? -146 : 19), 0, Math.Max(0, Canvas.ActualWidth - 128)));
            Canvas.SetTop(Lens, Math.Clamp(dip.Y + (dip.Y + 165 > Canvas.ActualHeight ? -158 : 20), 0, Math.Max(0, Canvas.ActualHeight - 149)));
            Lens.Visibility = Visibility.Visible;
        }
    }

    public static async Task<CssColour?> Pick()
    {
        var hidden = Application.Current.Windows.OfType<Window>().Select(window => new WindowInteropHelper(window).Handle)
            .Where(handle => handle != 0 && IsWindowVisible(handle)).ToArray();
        nint foreground = GetForegroundWindow();
        var surfaces = new List<Surface>();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var result = new TaskCompletionSource<CssColour?>(TaskCreationOptions.RunContinuationsAsynchronously);
        long deadline = Environment.TickCount64 + 60_000;
        Surface? previous = null;
        Point last = new(double.NaN, double.NaN);

        void Finish(bool pick)
        {
            Point point = WindowsHost.Cursor();
            var current = pick ? surfaces.Find(surface => surface.Area.Contains(point)) : null;
            result.TrySetResult(current?.Read(point));
        }

        void Move(int dx, int dy)
        {
            Point point = WindowsHost.Cursor();
            Point next = new(point.X + dx, point.Y + dy);
            if (!surfaces.Any(surface => surface.Area.Contains(next)))
            {
                var area = surfaces.Find(surface => surface.Area.Contains(point))?.Area;
                if (area is not { } bounds) return;
                next = new(Math.Clamp(next.X, bounds.Left, bounds.Right - 1), Math.Clamp(next.Y, bounds.Top, bounds.Bottom - 1));
            }
            SetCursorPos((int)next.X, (int)next.Y);
        }

        void Key(object sender, KeyEventArgs e)
        {
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Escape: Finish(false); break;
                case System.Windows.Input.Key.Enter: Finish(true); break;
                case System.Windows.Input.Key.Left: Move(-step, 0); break;
                case System.Windows.Input.Key.Right: Move(step, 0); break;
                case System.Windows.Input.Key.Up: Move(0, -step); break;
                case System.Windows.Input.Key.Down: Move(0, step); break;
                default: return;
            }
            e.Handled = true;
        }

        void DisplayChanged(object? sender, EventArgs e) => result.TrySetResult(null);

        try
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            foreach (nint handle in hidden) ShowWindow(handle, 0);
            await Task.Delay(85);
            DwmFlush();
            if (result.Task.IsCompleted) return null;

            var monitors = new List<Area>();
            if (!EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Area area, nint data) => { monitors.Add(area); return true; }, 0)
                || monitors.Count == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            foreach (var area in monitors)
            {
                var surface = new Surface(area, Capture(area));
                surfaces.Add(surface);
                surface.Prepare();
                surface.Window.PreviewKeyDown += Key;
                surface.Window.MouseLeftButtonDown += (_, _) => Finish(true);
                surface.Window.MouseRightButtonDown += (_, _) => Finish(false);
                surface.Window.Closed += (_, _) => Finish(false);
            }

            foreach (var surface in surfaces)
            {
                surface.Window.Show();
                surface.Place(show: true);
            }
            var active = surfaces.Find(surface => surface.Area.Contains(WindowsHost.Cursor())) ?? surfaces[0];
            active.Window.Activate();
            timer.Tick += (_, _) =>
            {
                if (Environment.TickCount64 >= deadline) { Finish(false); return; }
                Point point = WindowsHost.Cursor();
                if (point == last) return;
                last = point;
                var current = surfaces.Find(surface => surface.Area.Contains(point));
                if (previous is not null && previous != current) previous.Lens.Visibility = Visibility.Collapsed;
                current?.Magnify(point);
                previous = current;
            };
            timer.Start();
            return await result.Task;
        }
        finally
        {
            timer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            foreach (var surface in surfaces)
                if (surface.Handle != 0) surface.Window.Close();
            foreach (nint handle in hidden) if (IsWindow(handle)) ShowWindow(handle, 8);
            if (foreground != 0) SetForegroundWindow(foreground);
        }
    }

    internal static BitmapSource Capture(int left, int top, int width, int height) => Capture(new Area { Left = left, Top = top, Right = left + width, Bottom = top + height });

    private static BitmapSource Capture(Area area)
    {
        nint screen = GetDC(0);
        nint memory = 0, bitmap = 0, previous = 0;
        try
        {
            if (screen == 0 || (memory = CreateCompatibleDC(screen)) == 0
                || (bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height)) == 0
                || (previous = SelectObject(memory, bitmap)) == 0 || previous == new nint(-1)
                || !BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, 0x40CC0020))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            converted.Freeze();
            return converted;
        }
        finally
        {
            if (previous != 0 && previous != new nint(-1)) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
        }
    }

    [DllImport("user32", SetLastError = true)] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32")] private static extern bool IsWindow(nint window);
    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi")] private static extern int DwmFlush();
    [DllImport("user32", SetLastError = true)] private static extern nint GetDC(nint window);
    [DllImport("user32")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32", SetLastError = true)] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32", SetLastError = true)] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32", SetLastError = true)] private static extern bool BitBlt(nint into, int x, int y, int width, int height, nint from, int left, int top, uint op);
    [DllImport("gdi32")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint obj);
}
