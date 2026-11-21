using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeckIsland.Core;
using NAudio.CoreAudioApi;

namespace DeckIsland;

// Calls: while the microphone is open the island shows how long it has been (a pill with the mic, the time and "live"), and a readout
// says when the microphone is muted or unmuted, by any app. The mute switch is the default microphone's own, so it is the one every
// app sees.
public partial class App
{
    private const string LiveGreen = "#30D158";
    internal event Action? CallsChanged;
    private MMDeviceEnumerator? _callDevices;
    private DateTime? _micLiveSince;
    private bool? _micMuted;
    private bool _callShown;
    // Until when a readout that goes by (volume, a lock key...) is on the island: the call pill waits for it.
    private DateTime _hudTransientUntil;

    internal bool MicMuted => _micMuted == true;
    internal bool MicLive => _micLiveSince is not null;
    internal TimeSpan? MicLiveFor => _micLiveSince is { } since ? DateTime.UtcNow - since : null;

    private MMDevice? CaptureDevice()
    {
        try { return (_callDevices ??= new MMDeviceEnumerator()).GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch (Exception error) when (error is COMException or InvalidOperationException) { return null; }
    }

    internal void ToggleMicMute()
    {
        if (CaptureDevice() is not { } device) return;
        try { device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute; }
        catch (Exception error) when (error is COMException or InvalidOperationException) { return; }
        CheckCalls();
    }

    private void StartCalls()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => CheckCalls();
        timer.Start();
        _hudKeep.Add(timer);
    }

    private void CheckCalls()
    {
        bool? muted = null;
        try { if (CaptureDevice() is { } device) muted = device.AudioEndpointVolume.Mute; }
        catch (Exception error) when (error is COMException or InvalidOperationException) { }
        bool live = false;
        try { live = LiveApps("microphone").Any(app => !HudFormat.Ignored(Settings.SensorIgnoredApps, app)); }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        bool changed = live != (_micLiveSince is not null) || muted != _micMuted;
        if (muted != _micMuted)
        {
            bool first = _micMuted is null;
            _micMuted = muted;
            if (!first && muted is { } now && Settings.MicHud)
                ShowHud(new("mic", now ? HudIcon.MicOff : HudIcon.Mic, now ? "Microphone muted" : "Microphone on", Lit: !now));
        }
        if (live && _micLiveSince is null) _micLiveSince = DateTime.UtcNow;
        else if (!live) _micLiveSince = null;
        if (changed) CallsChanged?.Invoke();
        if (Settings.CallTimer && _micLiveSince is { } since)
        {
            if (DateTime.UtcNow < _hudTransientUntil) return;
            string time = HudFormat.Duration(DateTime.UtcNow - since);
            bool off = muted == true;
            ShowHud(new("call", off ? HudIcon.MicOff : HudIcon.Mic, time, Figure: off ? "muted" : "live", FigureColor: off ? null : LiveGreen, Lit: !off,
                Hold: TimeSpan.Zero, Label: $"{(off ? "Muted call" : "Call")} {time}"));
            _callShown = true;
        }
        else if (_callShown)
        {
            ClearHud("call");
            _callShown = false;
        }
    }
}
