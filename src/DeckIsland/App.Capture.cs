using System.Windows;
using System.Windows.Threading;
using DeckIsland.Native;

namespace DeckIsland;

// The one screen recording, shared by every panel: each shows its time in the collapsed island with pause and stop.
public partial class App
{
    private readonly DispatcherTimer _recordTick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private IDisposable? _recordHold;
    internal ScreenRecorder? Recording { get; private set; }
    // true: a recording started or ended (rebuild); false: its time moved on.
    internal event Action<bool>? RecordingChanged;
    // A capture file was added or removed.
    internal event Action? CapturesChanged;

    internal void InitializeCapture()
    {
        _recordTick.Tick += (_, _) => RecordingChanged?.Invoke(false);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            { if (Settings.HideFromCapture || Recording is not null) ScreenShots.Exclude((Window)sender, true); }));
        if (Settings.HideFromCapture) ScreenShots.Exclude(true);
    }

    internal void ApplyCaptureExclusion() => ScreenShots.Exclude(Settings.HideFromCapture);

    internal IDisposable HoldCaptureExclusion() => ScreenShots.Hold(() => Settings.HideFromCapture);

    internal void CaptureSaved() => CapturesChanged?.Invoke();

    internal async Task StartRecording(DisplayInfo display)
    {
        if (Recording is not null) return;
        _recordHold = HoldCaptureExclusion();
        try
        {
            Recording = await ScreenRecorder.Start(display.Handle, ScreenShots.NewPath(ScreenShots.VideoFolder, "Recording", ".mp4"),
                Settings.CaptureFps, Settings.CaptureSystemAudio, Settings.CaptureMic);
        }
        catch
        {
            _recordHold.Dispose(); _recordHold = null;
            throw;
        }
        _recordTick.Start();
        RecordingChanged?.Invoke(true);
    }

    internal void PauseRecording()
    {
        if (Recording is not { } recording) return;
        recording.Pause(!recording.Paused);
        RecordingChanged?.Invoke(true);
    }

    internal async Task StopRecording()
    {
        if (Recording is not { } recording) return;
        _recordTick.Stop();
        try { await recording.Stop(); }
        finally
        {
            Recording = null;
            _recordHold?.Dispose(); _recordHold = null;
            RecordingChanged?.Invoke(true);
            CapturesChanged?.Invoke();
        }
    }
}
