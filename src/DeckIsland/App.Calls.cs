using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeckIsland.Core;
using DeckIsland.Native;
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
    private long? _micLiveSince;
    private long _micLastSeen;
    private string _micLiveApp = "";
    private bool? _micMuted;
    private bool _callShown, _checkingCalls;
    // Until when a readout that goes by (volume, a lock key...) is on the island: the call pill waits for it.
    private DateTime _hudTransientUntil;

    internal bool MicMuted => _micMuted == true;
    internal string MicLiveApp => _micLiveApp;
    internal TimeSpan? MicLiveFor => _micLiveSince is { } since ? TimeSpan.FromMilliseconds(Environment.TickCount64 - since) : null;

    private MMDevice? CaptureDevice()
    {
        try { return (_callDevices ??= new MMDeviceEnumerator()).GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch (Exception error) when (error is COMException or InvalidOperationException) { return null; }
    }

    internal void ToggleMicMute()
    {
        using var device = CaptureDevice();
        if (device is null) return;
        try { device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute; }
        catch (Exception error) when (error is COMException or InvalidOperationException) { return; }
        if (ReadMicMute()) CallsChanged?.Invoke();
        UpdateCallHud();
    }

    private void StartCalls()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await CheckCallsAsync();
        timer.Start();
        _hudKeep.Add(timer);
        _hudSources.Add(new Disposer(() => _callDevices?.Dispose()));
    }

    private bool ReadMicMute()
    {
        bool? muted = null;
        try { using var device = CaptureDevice(); muted = device?.AudioEndpointVolume.Mute; }
        catch (Exception error) when (error is COMException or InvalidOperationException) { }
        if (muted == _micMuted) return false;
        bool first = _micMuted is null;
        _micMuted = muted;
        if (!first && muted is { } now && Settings.MicHud)
            ShowHud(new("mic", now ? HudIcon.MicOff : HudIcon.Mic, now ? "Microphone muted" : "Microphone on", Lit: !now));
        return true;
    }

    private async Task CheckCallsAsync()
    {
        if (_checkingCalls || Exiting) return;
        _checkingCalls = true;
        try
        {
            bool mutedChanged = ReadMicMute();
            var active = await Task.Run(() =>
            {
                var names = CallAudio.ActiveInputApps();
                names.AddRange(ReadSensorApps("microphone"));
                return names;
            });
            if (Exiting) return;
            _sensorMicrophoneApps = active;
            UpdateSensorIndicator();
            string? app = CallAudio.MeetingOwner(active, Settings.SensorIgnoredApps, _micLiveApp);
            long now = Environment.TickCount64;
            bool wasLive = _micLiveSince is not null;
            if (app is not null)
            {
                if (!app.Equals(_micLiveApp, StringComparison.OrdinalIgnoreCase)) _micLiveSince = now;
                _micLiveApp = app;
                _micLastSeen = now;
            }
            else if (now - _micLastSeen >= 1500)
            {
                _micLiveSince = null;
                _micLiveApp = "";
            }

            if (mutedChanged || wasLive || _micLiveSince is not null) CallsChanged?.Invoke();
            UpdateCallHud();
        }
        finally { _checkingCalls = false; }
    }

    private void UpdateCallHud()
    {
        if (Settings.CallTimer && MicLiveFor is { } span)
        {
            if (DateTime.UtcNow < _hudTransientUntil) return;
            string time = HudFormat.Duration(span);
            bool off = MicMuted;
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
