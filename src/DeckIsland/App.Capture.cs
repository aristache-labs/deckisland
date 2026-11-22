using System.IO;
using DeckIsland.Core;
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

    internal async Task StartRecording(IReadOnlyList<DisplayInfo> displays)
    {
        if (Recording is not null || displays.Count == 0) return;
        _recordHold = HoldCaptureExclusion();
        double bits = Settings.CaptureQuality switch { CaptureQuality.Low => 0.04, CaptureQuality.High => 0.14, _ => 0.08 };
        var options = new RecordingOptions(Settings.CaptureFps, Settings.CaptureMaxHeight, bits, Settings.CaptureSystemAudio, Settings.CaptureMic, Settings.CaptureBorder);
        try { Recording = await ScreenRecorder.Start(displays, ScreenShots.NewPath(ScreenShots.VideoFolder, "Recording", ".mp4"), options); }
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
        Recording = null;
        _recordTick.Stop();
        try
        {
            await recording.Stop();
            if (Settings.CaptureToShelf && File.Exists(recording.Path)) Shelf.AddFiles([recording.Path], null);
        }
        finally
        {
            _recordHold?.Dispose(); _recordHold = null;
            RecordingChanged?.Invoke(true);
            CapturesChanged?.Invoke();
        }
    }
}
