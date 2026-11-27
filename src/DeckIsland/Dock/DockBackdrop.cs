using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeckIsland;

// The glass styles' backdrop: a window of its own, just under the dock and exactly the bar's shape, that Windows itself fills with
// acrylic blur of whatever is behind. The dock no longer reads the screen for its glass, so it no longer has to hide from captures.
internal sealed class DockBackdrop : IDisposable
{
    private readonly Window _window;
    private nint _handle;
    private (int X, int Y, int Width, int Height, int Radius) _shown;
    private bool _visible;

    public DockBackdrop()
    {
        _window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, ResizeMode = ResizeMode.NoResize, Focusable = false, IsHitTestVisible = false,
            Width = 1, Height = 1, Left = -32000, Top = -32000, Title = "DeckIsland Dock Glass"
        };
        _window.SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(_window).Handle;
            SetWindowLong(_handle, GwlExStyle, GetWindowLong(_handle, GwlExStyle) | ExNoActivate | ExToolWindow | ExTransparent);
            EnableAcrylic(_handle);
        };
    }

    // Shows the blur at a rectangle in screen pixels, under `dock`, with rounded corners of `radius` pixels.
    public void Show(nint dock, int x, int y, int width, int height, int radius)
    {
        if (!_window.IsVisible) _window.Show();
        if (_handle == 0) return;
        var shape = (x, y, width, height, radius);
        if (shape != _shown)
        {
            // A rounded region clips the blur to the bar's corners; none for a square bar.
            nint region = radius > 0 ? CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2) : 0;
            SetWindowRgn(_handle, region, true);
            _shown = shape;
        }
        // Directly under the dock, which stays the topmost of the two.
        SetWindowPos(_handle, dock, x, y, width, height, SwpNoActivate | SwpShowWindow);
        _visible = true;
    }

    public void Hide()
    {
        if (!_visible || _handle == 0) return;
        SetWindowPos(_handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpHideWindow);
        _visible = false;
    }

    public void Dispose() => _window.Close();

    // Acrylic with an almost clear tint: the dock's own tint over it sets each style's colour.
    private static void EnableAcrylic(nint handle)
    {
        var accent = new AccentPolicy { AccentState = AccentAcrylic, GradientColor = 0x01000000 };
        int size = Marshal.SizeOf(accent);
        nint pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, pointer, false);
            var data = new CompositionData { Attribute = AccentPolicyAttribute, Data = pointer, Size = size };
            SetWindowCompositionAttribute(handle, ref data);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private const int GwlExStyle = -20, ExNoActivate = 0x08000000, ExToolWindow = 0x80, ExTransparent = 0x20;
    private const int AccentAcrylic = 4, AccentPolicyAttribute = 19;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpShowWindow = 0x40, SwpHideWindow = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionData { public int Attribute; public nint Data; public int Size; }

    [DllImport("user32")] private static extern int SetWindowCompositionAttribute(nint window, ref CompositionData data);
    [DllImport("user32")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
}
