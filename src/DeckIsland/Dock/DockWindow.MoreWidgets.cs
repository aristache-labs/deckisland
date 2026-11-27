using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using MediaState = DeckIsland.Core.MediaState;
using DeckIsland.Core;
using DeckIsland.Native;

namespace DeckIsland;

// The dock's second wave of widgets (DockWidgets): network, storage, audio output, weather, an analog clock, now playing, a calendar
// leaf and an AI ring. Each reads what the island already reads (App.DockMonitor, App.CurrentWeather, App.Media, App.Deck.Snapshots,
// App.CalendarEvents); App.ApplyDockData keeps those readings running while a widget needs them.
internal sealed partial class DockWindow
{
    private SystemVolume? _volume;

    // Down and up, each with its speed.
    private (FrameworkElement, Action) Network(double tile)
    {
        var down = Label("–", tile * 0.2, bold: true); var up = Label("–", tile * 0.2, bold: true);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(tile * 0.2, 0, tile * 0.2, 0) };
        stack.Children.Add(Arrowed("↓", down, tile)); stack.Children.Add(Arrowed("↑", up, tile));
        return (stack, () =>
        {
            var sample = _app.DockMonitor?.Network.OrDefault;
            down.Text = sample is null ? "–" : Speed(sample.DownBytesPerSecond);
            up.Text = sample is null ? "–" : Speed(sample.UpBytesPerSecond);
        });
    }

    private FrameworkElement Arrowed(string arrow, TextBlock value, double tile)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var sign = Label(arrow, tile * 0.2, bold: true); sign.Foreground = UI.Brush(Accent); sign.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(sign); row.Children.Add(value);
        return row;
    }

    private static string Speed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_000_000 => $"{bytesPerSecond / 1_000_000:0.0} MB/s",
        >= 1_000 => $"{bytesPerSecond / 1_000:0} KB/s",
        _ => $"{bytesPerSecond:0} B/s"
    };

    // The system drive's free space as a ring of the space used, the free amount under it.
    private (FrameworkElement, Action) Storage(double tile)
    {
        var drive = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var (ring, setRing) = Ring(tile, drive.TrimEnd('\\'));
        return (ring, () =>
        {
            try
            {
                var info = new DriveInfo(drive);
                setRing(1 - (double)info.AvailableFreeSpace / info.TotalSize, $"{info.AvailableFreeSpace / 1_000_000_000}G");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { setRing(null, "–"); }
        });
    }

    // The volume of the default output; scrolling on it changes it in steps of 2%.
    private (FrameworkElement, Action) Volume(double tile)
    {
        var (ring, setRing) = Ring(tile, Loc.T("音量", "Volume"));
        void Show() { _volume ??= SystemVolume.Open(); var level = _volume?.Level; setRing(level, level is { } v ? Math.Round(v * 100).ToString(CultureInfo.CurrentCulture) : "–"); }
        ring.Background = Brushes.Transparent;
        ring.MouseWheel += (_, e) =>
        {
            _volume ??= SystemVolume.Open();
            if (_volume is null) return;
            _volume.Level += e.Delta > 0 ? 0.02f : -0.02f;
            Show(); e.Handled = true;
        };
        return (ring, Show);
    }

    // A meter ring with its figure inside and a caption under it, and how to set them.
    private (Panel, Action<double?, string>) Ring(double tile, string caption)
    {
        double size = tile * 0.62;
        var track = new Ellipse { Width = size, Height = size, Stroke = UI.Brush(_look.Divider), StrokeThickness = 3 };
        var arc = new Path { Width = size, Height = size, Stroke = UI.Brush(Accent), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var figure = Label("–", tile * 0.17, bold: true); figure.HorizontalAlignment = HorizontalAlignment.Center; figure.VerticalAlignment = VerticalAlignment.Center;
        var ring = new Grid { Width = size, Height = size, Margin = new Thickness(0, tile * 0.06, 0, 0) };
        ring.Children.Add(track); ring.Children.Add(arc); ring.Children.Add(figure);
        var label = Label(caption, tile * 0.15, dim: true); label.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(ring); stack.Children.Add(label);
        return (stack, (share, text) => { figure.Text = text; arc.Data = RingArc(size, 3, share ?? 0); });
    }

    // The conditions' sign, the temperature, the conditions and the place, as the island's weather has them.
    private (FrameworkElement, Action) Weather(double tile)
    {
        var icon = Label("–", tile * 0.42); icon.VerticalAlignment = VerticalAlignment.Center; icon.Margin = new Thickness(tile * 0.18, 0, tile * 0.12, 0);
        var temperature = Label("–", tile * 0.26, bold: true);
        var condition = Label("", tile * 0.15, dim: true);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(temperature); text.Children.Add(condition);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon); row.Children.Add(text);
        return (row, () =>
        {
            if (!_app.Settings.WeatherEnabled) { icon.Text = "☁"; temperature.Text = "–"; condition.Text = Loc.T("在设置中开启天气", "Turn on weather"); return; }
            if (_app.CurrentWeather is not { } reading) { icon.Text = "…"; temperature.Text = "–"; condition.Text = Loc.T("读取中", "Reading…"); return; }
            var (sign, words) = MainWindow.WeatherCondition(reading.Code, reading.Daylight);
            bool fahrenheit = _app.Settings.WeatherFahrenheit;
            icon.Text = sign;
            temperature.Text = $"{(int)Math.Round(fahrenheit ? reading.Temperature * 1.8 + 32 : reading.Temperature, MidpointRounding.AwayFromZero)}°";
            condition.Text = words;
        });
    }

    // A minimal clock face: hour and minute hands, a second hand in the accent.
    private (FrameworkElement, Action) AnalogClock(double tile, Func<DateTime>? time = null)
    {
        double size = tile * 0.78, centre = size / 2;
        var face = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(new Ellipse { Width = size, Height = size, Stroke = UI.Brush(_look.Divider), StrokeThickness = 1.2 });
        for (int i = 0; i < 12; i += 3)
        {
            double angle = i * Math.PI / 6;
            face.Children.Add(new Line { X1 = centre + Math.Sin(angle) * centre * 0.78, Y1 = centre - Math.Cos(angle) * centre * 0.78,
                X2 = centre + Math.Sin(angle) * centre * 0.9, Y2 = centre - Math.Cos(angle) * centre * 0.9, Stroke = UI.Brush(_look.Dim), StrokeThickness = 1.4 });
        }
        Line Hand(double length, double thickness, string colour)
        {
            var hand = new Line { X1 = centre, Y1 = centre, Stroke = UI.Brush(colour), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Tag = length };
            face.Children.Add(hand);
            return hand;
        }
        var hour = Hand(centre * 0.5, 2.2, _look.Text); var minute = Hand(centre * 0.75, 1.6, _look.Text); var second = Hand(centre * 0.8, 1, Accent);
        void Point(Line hand, double turns) { double length = (double)hand.Tag, angle = turns * 2 * Math.PI; hand.X2 = centre + Math.Sin(angle) * length; hand.Y2 = centre - Math.Cos(angle) * length; }
        return (face, () =>
        {
            var now = time?.Invoke() ?? DateTime.Now;
            second.Visibility = time is null && _app.DockSettings.ClockSeconds ? Visibility.Visible : Visibility.Collapsed;
            Point(hour, (now.Hour % 12 + now.Minute / 60.0) / 12); Point(minute, (now.Minute + now.Second / 60.0) / 60); Point(second, now.Second / 60.0);
        });
    }

    // lumabar's Now Playing card: the cover, the title and artist, and previous, play or pause and next.
    private (FrameworkElement, Action) NowPlaying(double tile)
    {
        var cover = new Border { Width = tile * 0.76, Height = tile * 0.76, CornerRadius = new CornerRadius(TileRadius() - 2), Margin = new Thickness(tile * 0.12, 0, tile * 0.16, 0),
            Background = UI.Brush(_look.CardHover), ClipToBounds = true };
        var art = new Image { Stretch = Stretch.UniformToFill }; cover.Child = art;
        var title = Label("", tile * 0.18, bold: true); var artist = Label("", tile * 0.15, dim: true);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(title); text.Children.Add(artist);
        var play = MediaButton("M 0,0 L 9,6 L 0,12 Z", tile, MediaCommand.PlayPause);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, tile * 0.12, 0) };
        controls.Children.Add(MediaButton("M 10,0 L 3,6 L 10,12 Z M 0,0 H 2 V 12 H 0 Z", tile, MediaCommand.Previous));
        controls.Children.Add(play);
        controls.Children.Add(MediaButton("M 0,0 L 7,6 L 0,12 Z M 8,0 H 10 V 12 H 8 Z", tile, MediaCommand.Next));
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(cover, System.Windows.Controls.Dock.Left); DockPanel.SetDock(controls, System.Windows.Controls.Dock.Right);
        panel.Children.Add(cover); panel.Children.Add(controls); panel.Children.Add(text);
        return (panel, () =>
        {
            var track = _app.Media?.Current;
            title.Text = track is null ? Loc.T("没有播放", "Not playing") : track.Title;
            artist.Text = track?.Artist ?? "";
            art.Source = track is null ? null : _app.Media?.Cover;
            ((Path)((Border)play).Child).Data = Geometry.Parse(track?.State == MediaState.Playing ? "M 0,0 H 3.5 V 12 H 0 Z M 6.5,0 H 10 V 12 H 6.5 Z" : "M 0,0 L 9,6 L 0,12 Z");
        });
    }

    private Border MediaButton(string glyph, double tile, MediaCommand command)
    {
        var button = new Border { Width = tile * 0.42, Height = tile * 0.42, CornerRadius = new CornerRadius(tile * 0.21), Background = UI.Brush("#00000000"), Cursor = Cursors.Hand,
            Child = new Path { Data = Geometry.Parse(glyph), Fill = UI.Brush(_look.Text), Width = tile * 0.17, Height = tile * 0.17, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        button.MouseEnter += (_, _) => button.Background = UI.Brush(_look.CardHover);
        button.MouseLeave += (_, _) => button.Background = UI.Brush("#00000000");
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        button.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            if (_app.Media is { Current: { } track } media) await media.CommandAsync(track.Id, command);
            UpdateWidgets();
        };
        return button;
    }

    // A calendar leaf: the weekday in red over the day, and a dot for each of today's events, up to three.
    private (FrameworkElement, Action) Calendar(double tile)
    {
        var weekday = Label("", tile * 0.14, bold: true); weekday.Foreground = UI.Brush(DockLook.Weekday); weekday.HorizontalAlignment = HorizontalAlignment.Center;
        var day = Label("", tile * 0.36, bold: true); day.HorizontalAlignment = HorizontalAlignment.Center;
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 5 };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(weekday); stack.Children.Add(day); stack.Children.Add(dots);
        return (stack, () =>
        {
            var now = DateTime.Now;
            weekday.Text = now.ToString("ddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
            day.Text = now.Day.ToString(CultureInfo.CurrentCulture);
            int count = _app.CalendarEvents.Count(e => e.Start.Date <= now.Date && e.End.Date >= now.Date);
            if (dots.Children.Count == Math.Min(3, count)) return;
            dots.Children.Clear();
            for (int i = 0; i < Math.Min(3, count); i++) dots.Children.Add(new Ellipse { Width = 4, Height = 4, Margin = new Thickness(1, 0, 1, 0), Fill = UI.Brush(Accent) });
        });
    }

    // One AI tool's first limit as a ring, the tool's name under it.
    private (FrameworkElement, Action) AiRing(double tile, string option)
    {
        var (ring, setRing) = Ring(tile, "AI");
        var caption = (TextBlock)ring.Children[1];
        return (ring, () =>
        {
            var snapshots = _app.Deck?.Snapshots ?? [];
            var snapshot = snapshots.FirstOrDefault(s => s.Id.ToString() == option && s.Quotas.Count > 0) ?? snapshots.FirstOrDefault(s => s.Quotas.Count > 0);
            caption.Text = snapshot?.Name ?? "AI";
            var quota = snapshot?.Quotas[0];
            setRing(quota is null ? null : quota.UsedPercent / 100, quota is null ? "–" : $"{Math.Round(quota.UsedPercent)}");
        });
    }

    // The Search widget: a search field look-alike that opens Smart Search (DockSearch) over the dock.
    private (FrameworkElement, Action) SearchField(double tile)
    {
        var glass = new Path { Data = Geometry.Parse("M 6,0 A 6,6 0 1 1 5.99,0 M 10.5,10.5 L 15,15"), Stroke = UI.Brush(_look.Dim), StrokeThickness = 1.6,
            Width = tile * 0.26, Height = tile * 0.26, Stretch = Stretch.Uniform, Margin = new Thickness(tile * 0.24, 0, tile * 0.16, 0), VerticalAlignment = VerticalAlignment.Center };
        var hint = Label(Loc.T("搜索", "Search"), tile * 0.2, dim: true); hint.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.IBeam };
        row.Children.Add(glass); row.Children.Add(hint);
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool top = _app.DockSettings.Edge == DockEdge.Top;
            var anchor = _bar.PointToScreen(new Point(_bar.ActualWidth / 2, top ? _bar.ActualHeight : 0));
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            new DockSearch(_app, _look, new Point(anchor.X / scale, anchor.Y / scale), !top).Show();
        };
        return (row, () => { });
    }
}
