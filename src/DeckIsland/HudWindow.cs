using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeckIsland.Core;

namespace DeckIsland;

// The readout under the collapsed island (volume, brightness, a lock key...): a pill at the top of the screen that never takes
// focus, in the panel theme's colors. The same message key updates it in place.
internal sealed class HudWindow : Window
{
    private const int GwlExStyle = -20, NoActivate = 0x08000000, ToolWindow = 0x80, Transparent = 0x20;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);

    private static readonly Dictionary<HudIcon, string> Icons = new()
    {
        [HudIcon.Volume] = "M2,8 H5 L9,4.5 V15.5 L5,12 H2 Z M12,7 Q14.5,10 12,13 M14.5,4.5 Q19,10 14.5,15.5",
        [HudIcon.Muted] = "M2,8 H5 L9,4.5 V15.5 L5,12 H2 Z M12.5,7.5 L18,12.5 M18,7.5 L12.5,12.5",
        [HudIcon.Brightness] = "M10,6 A4,4 0 1 0 10.01,6 M10,1 V3 M10,17 V19 M1,10 H3 M17,10 H19 M3.6,3.6 L5,5 M15,15 L16.4,16.4 M3.6,16.4 L5,15 M15,5 L16.4,3.6",
        [HudIcon.Battery] = "M2,6 H15 V14 H2 Z M15,8.5 H17.5 V11.5 H15",
        [HudIcon.Charging] = "M2,6 H15 V14 H2 Z M15,8.5 H17.5 V11.5 H15 M9.5,7.5 L7,10.5 H10.5 L8.5,13.5",
        [HudIcon.Bluetooth] = "M5,6 L14,14 L9.5,18 V2 L14,6 L5,14",
        [HudIcon.Lock] = "M10,2 L17,10 H13 V16 H7 V10 H3 Z",
        [HudIcon.Download] = "M10,3 V12 M6,8.5 L10,12.5 L14,8.5 M3,16 H17",
        [HudIcon.Done] = "M4,10 L8.5,14.5 L16,6",
        [HudIcon.Notice] = "M3.5,14.5 C6,12.5 5.5,11.5 5.5,8.5 A4.5,4.5 0 0 1 14.5,8.5 C14.5,11.5 14,12.5 16.5,14.5 Z M8,17 A2,2 0 0 0 12,17"
    };

    private readonly DispatcherTimer _hide = new();
    private readonly Border _pill = new() { CornerRadius = new CornerRadius(0, 0, 18, 18), Padding = new Thickness(16, 0, 16, 0), Height = 38 };
    private readonly Path _icon = new() { Width = 17, Height = 17, Stretch = Stretch.Uniform, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid _bar = new() { Height = 7, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3.5) };
    private readonly Border _track = new() { CornerRadius = new CornerRadius(3.5) };
    private readonly Ellipse _dot = new() { Width = 9, Height = 9, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _figure = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private string _key = "";

    internal HudWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false; Width = 300; Height = 38;
        SizeToContent = SizeToContent.Manual; Opacity = 0;
        var row = new DockPanel { LastChildFill = true };
        _icon.Margin = new Thickness(0, 0, 11, 0);
        DockPanel.SetDock(_icon, Dock.Left);
        row.Children.Add(_icon);
        _figure.Margin = _dot.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(_figure, Dock.Right);
        DockPanel.SetDock(_dot, Dock.Right);
        DockPanel.SetDock(_bar, Dock.Right);
        _bar.Margin = new Thickness(14, 0, 0, 0);
        _bar.Children.Add(_track); _bar.Children.Add(_fill);
        row.Children.Add(_figure); row.Children.Add(_dot); row.Children.Add(_bar); row.Children.Add(_text);
        _pill.Child = row;
        Content = _pill;
        // The fill follows the track's width, whatever it is laid out at.
        _bar.SizeChanged += (_, _) => SetFill(_level);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | NoActivate | ToolWindow | Transparent);
        };
        _hide.Tick += (_, _) => Dismiss();
    }

    private double _level;

    internal void Present(HudMessage message, PanelLook look)
    {
        _pill.Background = UI.Brush(look.Hex("surface"));
        var ink = UI.Brush(look.Primary);
        _icon.Stroke = ink; _text.Foreground = ink;
        _figure.Foreground = UI.Brush(look.Secondary);
        _track.Background = UI.Brush(look.Hex("track"));
        _fill.Background = UI.Brush(look.Hex("accent"));
        _icon.Data = Geometry.Parse(Icons[message.Icon]);
        _text.Text = message.Text;
        _bar.Visibility = message.Level is null ? Visibility.Collapsed : Visibility.Visible;
        _dot.Visibility = message.On is null ? Visibility.Collapsed : Visibility.Visible;
        _figure.Visibility = message.Figure is null ? Visibility.Collapsed : Visibility.Visible;
        _figure.Text = message.Figure ?? "";
        if (message.On is { } on)
        {
            _dot.Fill = on ? UI.Brush(look.Hex("accent")) : Brushes.Transparent;
            _dot.Stroke = on ? UI.Brush(look.Hex("accent")) : UI.Brush(look.Secondary);
        }
        _level = Math.Clamp(message.Level ?? 0, 0, 1);
        SetFill(_level);
        AutomationProperties.SetName(this, message.Text + (message.Figure is null ? "" : " " + message.Figure));
        Left = SystemParameters.PrimaryScreenWidth / 2 - Width / 2;
        Top = 0;
        if (!IsVisible) Show();
        if (_key != message.Key || Opacity < 1) BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        _key = message.Key;
        _hide.Stop();
        // A held message (a download in progress) stays until Clear; the others go after their hold.
        if (message.Hold is { } hold && hold > TimeSpan.Zero) { _hide.Interval = hold; _hide.Start(); }
    }

    private void SetFill(double level) => _fill.Width = Math.Max(0, _bar.ActualWidth * level);

    internal void Clear(string key)
    {
        if (_key == key) Dismiss();
    }

    private void Dismiss()
    {
        _hide.Stop();
        _key = "";
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => { if (_key == "") Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }
}
