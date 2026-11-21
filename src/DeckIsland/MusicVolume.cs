using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DeckIsland.Native;

// The volume of the app that is playing, which is its own audio session in Windows' mixer: not the master volume. The session is found
// from the player's name (the music source) against the programs that make sound; every session of the program takes the same level, as a
// browser has one per tab. When no session matches and only one program makes sound, that one is taken.
internal static class PlayerVolume
{
    // The levels of the matching sessions: the loudest, and every session to set; empty when none is found.
    public static List<SimpleAudioVolume> Find(string source)
    {
        var found = new List<SimpleAudioVolume>();
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            string wanted = new([.. source.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
            var active = new List<AudioSessionControl>();
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.IsSystemSoundsSession) continue;
                if (session.State == AudioSessionState.AudioSessionStateActive) active.Add(session);
                string name = ProcessName(session);
                if (name.Length >= 3 && wanted.Contains(name, StringComparison.Ordinal)) found.Add(session.SimpleAudioVolume);
            }
            if (found.Count == 0 && active.Count > 0 && active.Select(ProcessName).Distinct().Count() == 1) found.AddRange(active.Select(session => session.SimpleAudioVolume));
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException) { }
        return found;
    }

    private static string ProcessName(AudioSessionControl session)
    {
        try { return new string([.. Process.GetProcessById((int)session.GetProcessID).ProcessName.ToLowerInvariant().Where(char.IsLetterOrDigit)]); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return ""; }
    }

    public static float? Read(string source)
    {
        var sessions = Find(source);
        return sessions.Count == 0 ? null : sessions.Max(session => session.Volume);
    }

    public static void Set(string source, float level)
    {
        foreach (var session in Find(source))
        {
            try { session.Volume = Math.Clamp(level, 0, 1); }
            catch (Exception error) when (error is COMException or InvalidOperationException) { }
        }
    }
}

// The player's volume slider on the music card: a speaker and a thin bar, set by a click or a drag (and the arrow keys when focused).
internal sealed class MusicVolumeBar : FrameworkElement
{
    private double _level;
    private bool _dragging, _hover, _enabled = true;
    private Color _accent, _track, _ink;

    public MusicVolumeBar(Color accent, Color track, Color ink)
    {
        _accent = accent; _track = track; _ink = ink;
        Focusable = true; Cursor = Cursors.Hand; Width = 150; Height = 18; SnapsToDevicePixels = true;
        FocusVisualStyle = null;
    }

    public event Action<double>? Changed;
    public event Action? Dragged;

    public double Level
    {
        get => _level;
        set { if (_dragging) return; _level = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    public bool Available
    {
        get => _enabled;
        set { _enabled = value; Opacity = value ? 1 : .4; Cursor = value ? Cursors.Hand : Cursors.Arrow; InvalidateVisual(); }
    }

    private const double IconWidth = 22;
    private double BarLeft => IconWidth;
    private double BarWidth => Math.Max(10, ActualWidth - IconWidth - 6);

    private void Set(Point at)
    {
        _level = Math.Clamp((at.X - BarLeft) / BarWidth, 0, 1);
        InvalidateVisual();
        Changed?.Invoke(_level);
    }

    protected override void OnMouseEnter(MouseEventArgs e) { _hover = true; InvalidateVisual(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = false; InvalidateVisual(); base.OnMouseLeave(e); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_enabled) return;
        Focus();
        _dragging = true; CaptureMouse();
        Set(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) Set(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false; ReleaseMouseCapture();
        Dragged?.Invoke();
    }

    // The wheel over the bar moves its own level, five per cent a notch, and never reaches the page's wheel or swipe gestures.
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        e.Handled = true;
        if (!_enabled || e.Delta == 0) return;
        _level = Math.Clamp(_level + Math.Sign(e.Delta) * .05, 0, 1);
        InvalidateVisual();
        Changed?.Invoke(_level);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = e.Key switch { Key.Left or Key.Down => -.05, Key.Right or Key.Up => .05, Key.Home => -1, Key.End => 1, _ => 0 };
        if (step == 0 || !_enabled) return;
        _level = Math.Clamp(_level + step, 0, 1);
        InvalidateVisual();
        Changed?.Invoke(_level);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(MusicVolumeBar owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
        protected override string GetNameCore() => "Player volume";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public double Value => Math.Round(owner._level * 100);
        public bool IsReadOnly => !owner._enabled;
        public double LargeChange => 10;
        public double Maximum => 100;
        public double Minimum => 0;
        public double SmallChange => 5;
        public void SetValue(double value) { owner._level = Math.Clamp(value / 100, 0, 1); owner.InvalidateVisual(); owner.Changed?.Invoke(owner._level); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight, mid = h / 2;
        var ink = new SolidColorBrush(_ink); ink.Freeze();
        // The speaker, with waves for the level: none when silent, one when low, two otherwise.
        var pen = new Pen(ink, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var body = Geometry.Parse("M1,-2.6 H3.6 L7.6,-6 V6 L3.6,2.6 H1 Z");
        body = body.Clone(); body.Transform = new TranslateTransform(2, mid); body.Freeze();
        dc.DrawGeometry(null, pen, body);
        if (_level <= .001) dc.DrawGeometry(null, pen, new LineGeometry(new Point(12.5, mid - 3), new Point(17.5, mid + 3)));
        else
        {
            dc.DrawGeometry(null, pen, Geometry.Parse($"M{11.5:0.#},{mid - 2.6:0.#} Q{13.6:0.#},{mid:0.#} {11.5:0.#},{mid + 2.6:0.#}"));
            if (_level > .45) dc.DrawGeometry(null, pen, Geometry.Parse($"M{13.8:0.#},{mid - 5:0.#} Q{17.6:0.#},{mid:0.#} {13.8:0.#},{mid + 5:0.#}"));
        }
        double thickness = _hover || _dragging ? 5 : 4, width = BarWidth;
        var track = new SolidColorBrush(_track); track.Freeze();
        var fill = new SolidColorBrush(_accent); fill.Freeze();
        dc.DrawRoundedRectangle(track, null, new Rect(BarLeft, mid - thickness / 2, width, thickness), thickness / 2, thickness / 2);
        double head = width * _level;
        if (head > 0) dc.DrawRoundedRectangle(fill, null, new Rect(BarLeft, mid - thickness / 2, head, thickness), thickness / 2, thickness / 2);
        if (_hover || _dragging || IsKeyboardFocused) dc.DrawEllipse(ink, null, new Point(BarLeft + head, mid), 4.5, 4.5);
    }
}
