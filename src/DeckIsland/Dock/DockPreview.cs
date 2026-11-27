using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// Live pictures of an app's windows over its dock tile, drawn by DWM (DwmRegisterThumbnail) the way the taskbar's are. Each card
// has the window's title and a close button; a click brings that window forward. It never takes focus.
internal sealed class DockPreview : Window
{
    private const double CardWidth = 220, ThumbHeight = 124, TitleHeight = 28, Pad = 8, Gap = 8;
    private const int GwlExStyle = -20, ExNoActivate = 0x08000000, ExToolWindow = 0x80;
    private const int DwmTnpRectDestination = 0x1, DwmTnpVisible = 0x8, DwmTnpSourceClientAreaOnly = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Size32 { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public int Flags; public Rect32 Destination; public Rect32 Source; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible; [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out Size32 size);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint window, int index, int value);

    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _open = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Canvas _canvas = new();
    private readonly List<nint> _thumbnails = [];
    private readonly Func<DockEdge> _edge;
    private (DockApp App, FrameworkElement Tile)? _pending;
    private string _shownKey = "";

    public DockPreview(Func<DockEdge> edge)
    {
        _edge = edge;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false; SizeToContent = SizeToContent.Manual;
        FontFamily = UI.PanelFont; Foreground = UI.Brush("#F2F2F4");
        Content = new Border { CornerRadius = new CornerRadius(10), Background = UI.Brush("#F21C1C21"), BorderBrush = UI.Brush("#26FFFFFF"), BorderThickness = new Thickness(1), Child = _canvas };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | ExNoActivate | ExToolWindow);
        };
        MouseEnter += (_, _) => _hide.Stop();
        MouseLeave += (_, _) => HideSoon();
        _hide.Tick += (_, _) => HideNow();
        _open.Tick += (_, _) => { _open.Stop(); if (_pending is { } p) Present(p.App, p.Tile); };
    }

    // Shown after the pointer rests a moment, at once when a preview is already up (moving along the dock).
    public void Show(DockApp app, FrameworkElement tile)
    {
        _hide.Stop();
        if (!app.Open) { HideSoon(); return; }
        _pending = (app, tile);
        if (IsVisible) Present(app, tile);
        else { _open.Stop(); _open.Start(); }
    }

    public void HideSoon() { _open.Stop(); _hide.Stop(); _hide.Start(); }

    public void HideNow()
    {
        _open.Stop(); _hide.Stop(); _pending = null; _shownKey = "";
        Clear();
        if (IsVisible) Hide();
    }

    private void Clear()
    {
        foreach (var thumbnail in _thumbnails) DwmUnregisterThumbnail(thumbnail);
        _thumbnails.Clear();
        _canvas.Children.Clear();
    }

    private void Present(DockApp app, FrameworkElement tile)
    {
        // The tiles may have been rebuilt while the preview waited to open.
        if (PresentationSource.FromVisual(tile) is null) { HideNow(); return; }
        string key = app.Key + "|" + string.Join(",", app.Windows.Select(w => w.Handle));
        if (IsVisible && key == _shownKey) return;
        _shownKey = key;
        Clear();
        int count = app.Windows.Count;
        Width = Pad * 2 + count * CardWidth + (count - 1) * Gap;
        Height = Pad * 2 + TitleHeight + ThumbHeight;
        _canvas.Width = Width; _canvas.Height = Height;
        for (int i = 0; i < count; i++) _canvas.Children.Add(Card(app.Windows[i], Pad + i * (CardWidth + Gap)));

        // Above the tile on a bottom dock, below it on a top one, kept on the tile's screen.
        var scale = VisualTreeHelper.GetDpi(tile).DpiScaleX;
        var origin = tile.PointToScreen(new Point(tile.ActualWidth / 2, 0));
        var below = tile.PointToScreen(new Point(0, tile.ActualHeight));
        var (_, work, _) = WindowsHost.PrimaryScreen();
        double left = origin.X / scale - Width / 2, top = _edge() == DockEdge.Top ? below.Y / scale + 10 : origin.Y / scale - Height - 14;
        Left = Math.Clamp(left, work.Left / scale + 8, Math.Max(work.Left / scale + 8, work.Right / scale - Width - 8));
        Top = top;
        if (!IsVisible) base.Show();
        UpdateLayout();
        var handle = new WindowInteropHelper(this).Handle;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        for (int i = 0; i < count; i++)
        {
            if (DwmRegisterThumbnail(handle, (nint)app.Windows[i].Handle, out var thumbnail) != 0) continue;
            _thumbnails.Add(thumbnail);
            DwmQueryThumbnailSourceSize(thumbnail, out var source);
            // Fit the window's shape into the card's picture area.
            double boxLeft = Pad + i * (CardWidth + Gap) + 6, boxTop = Pad + TitleHeight, boxWidth = CardWidth - 12, boxHeight = ThumbHeight - 6;
            double fit = source.Width > 0 && source.Height > 0 ? Math.Min(boxWidth / source.Width, boxHeight / source.Height) : 1;
            double width = source.Width > 0 ? source.Width * fit : boxWidth, height = source.Height > 0 ? source.Height * fit : boxHeight;
            double x = boxLeft + (boxWidth - width) / 2, y = boxTop + (boxHeight - height) / 2;
            var properties = new ThumbnailProperties
            {
                Flags = DwmTnpRectDestination | DwmTnpVisible | DwmTnpSourceClientAreaOnly, Visible = true, SourceClientAreaOnly = false,
                Destination = new Rect32 { Left = (int)(x * dpi), Top = (int)(y * dpi), Right = (int)((x + width) * dpi), Bottom = (int)((y + height) * dpi) }
            };
            DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }
    }

    private FrameworkElement Card(DockWindowInfo window, double left)
    {
        var title = new TextBlock { Text = window.Title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
        var close = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand, Background = UI.Brush("#00FFFFFF"), Margin = new Thickness(0, 0, 3, 0),
            Child = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,0 L 8,8 M 8,0 L 0,8"), Stroke = UI.Brush("#D9F2F2F4"), StrokeThickness = 1.3, Width = 8, Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            ToolTip = Loc.T("关闭窗口", "Close window") };
        close.MouseEnter += (_, _) => close.Background = UI.Brush("#E0C42B1C");
        close.MouseLeave += (_, _) => close.Background = UI.Brush("#00FFFFFF");
        close.MouseLeftButtonUp += (_, e) => { DockAppWindows.Close((nint)window.Handle); HideNow(); e.Handled = true; };
        var head = new DockPanel { Height = TitleHeight, LastChildFill = true, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right); head.Children.Add(close); head.Children.Add(title);
        var card = new Border { Width = CardWidth, Height = TitleHeight + ThumbHeight, CornerRadius = new CornerRadius(7), Background = UI.Brush("#00FFFFFF"), Cursor = Cursors.Hand,
            Child = head };
        card.MouseEnter += (_, _) => card.Background = UI.Brush("#14FFFFFF");
        card.MouseLeave += (_, _) => card.Background = UI.Brush("#00FFFFFF");
        card.MouseLeftButtonUp += (_, _) => { DockAppWindows.Focus((nint)window.Handle); HideNow(); };
        Canvas.SetLeft(card, left); Canvas.SetTop(card, Pad);
        return card;
    }
}
