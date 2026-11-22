using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using WinRT;

namespace DeckIsland.Native;

// Records one monitor to an MP4: Windows.Graphics.Capture frames and, optionally, the system's sound and the microphone mixed
// together, fed to Media Foundation's H.264/AAC encoder through a MediaStreamSource. Time stops while paused.
internal sealed class ScreenRecorder
{
    private const int Rate = 48000, Channels = 2, Chunk = Rate / 50; // 20 ms of sound per sample
    private readonly IDirect3DDevice _device;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly VideoStreamDescriptor _video;
    private readonly MediaStreamSource _source;
    private readonly List<WasapiCapture> _captures = [];
    private readonly List<BufferedWaveProvider> _buffers = [];
    private readonly MixingSampleProvider? _mixer;
    private readonly Stopwatch _clock = new();
    private readonly TimeSpan _frameGap;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _audioWanted = new(0);
    private readonly Queue<(MediaStreamSourceSampleRequest Request, MediaStreamSourceSampleRequestDeferral Deferral)> _audioRequests = new();
    private (MediaStreamSourceSampleRequest Request, MediaStreamSourceSampleRequestDeferral Deferral, long Since)? _videoRequest;
    private Held? _last;
    private TimeSpan _lastStamp = TimeSpan.FromDays(-1), _pausedTotal, _pausedAt;
    private long _audioFrames;
    private bool _stopping;
    private Task _transcode = Task.CompletedTask, _audioLoop = Task.CompletedTask;
    private Timer? _stall;

    public bool Paused { get; private set; }
    public string Path { get; }
    public TimeSpan Elapsed { get { lock (_gate) return (Paused ? _pausedAt : _clock.Elapsed) - _pausedTotal; } }

    // A capture frame shared by the samples made from it: given back to the pool once replaced and encoded.
    private sealed class Held(Direct3D11CaptureFrame frame)
    {
        public Direct3D11CaptureFrame Frame { get; } = frame;
        public int Uses;
        public bool Replaced;
        public void Release() { if (Replaced && Uses == 0) Frame.Dispose(); }
    }

    private ScreenRecorder(GraphicsCaptureItem item, string path, int fps, bool system, bool mic)
    {
        Path = path;
        _frameGap = TimeSpan.FromSeconds(0.9 / fps);
        _device = CreateDevice();
        int width = item.Size.Width & ~1, height = item.Size.Height & ~1;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
        _session = _pool.CreateCaptureSession(item);
        _video = new VideoStreamDescriptor(VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height));
        if (system || mic)
        {
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, Channels)) { ReadFully = true };
            if (system) AddAudio(new WasapiLoopbackCapture());
            if (mic) AddAudio(new WasapiCapture());
            _source = new MediaStreamSource(_video, new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(Rate, Channels, 16)));
        }
        else _source = new MediaStreamSource(_video);
        _source.BufferTime = TimeSpan.Zero;
        _source.CanSeek = false;
        _source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        _source.SampleRequested += SampleRequested;
        _pool.FrameArrived += (_, _) => { try { FrameArrived(); } catch (Exception ex) when (ex is COMException or ArgumentException or ObjectDisposedException or InvalidOperationException) { } };
    }

    public static async Task<ScreenRecorder> Start(IntPtr monitor, string path, int fps, bool system, bool mic)
    {
        var recorder = new ScreenRecorder(ItemForMonitor(monitor), path, fps, system, mic);
        try { await recorder.Begin(fps); }
        catch { await recorder.Stop(); throw; }
        return recorder;
    }

    private async Task Begin(int fps)
    {
        var size = _video.EncodingProperties;
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = size.Width; profile.Video.Height = size.Height;
        profile.Video.FrameRate.Numerator = (uint)fps; profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Video.Bitrate = (uint)Math.Clamp(size.Width * (double)size.Height * fps * 0.08, 4_000_000, 40_000_000);
        profile.Audio = _mixer is null ? null : AudioEncodingProperties.CreateAac(Rate, Channels, 192_000);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var file = new FileStream(Path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(_source, file.AsRandomAccessStream(), profile);
        if (!prepared.CanTranscode) { await file.DisposeAsync(); throw new InvalidOperationException("The encoder refused: " + prepared.FailureReason); }
        _clock.Start();
        foreach (var capture in _captures) capture.StartRecording();
        _session.StartCapture();
        _transcode = prepared.TranscodeAsync().AsTask().ContinueWith(async done => { await file.DisposeAsync(); await done; }).Unwrap();
        if (_mixer is not null) _audioLoop = Task.Run(AudioLoop);
        // A still screen sends no frames; the last one is sent again so the sound keeps going.
        _stall = new Timer(_ =>
        {
            // A timer callback that throws ends the process; a failed resend only skips a frame.
            try { ResendLast(); } catch (Exception ex) when (ex is COMException or ArgumentException or ObjectDisposedException or InvalidOperationException) { }
        }, null, 100, 100);
    }

    public void Pause(bool pause)
    {
        lock (_gate)
        {
            if (pause == Paused || _stopping) return;
            Paused = pause;
            if (pause) _pausedAt = _clock.Elapsed;
            else { _pausedTotal += _clock.Elapsed - _pausedAt; foreach (var buffer in _buffers) buffer.ClearBuffer(); }
        }
    }

    // Ends the streams and waits for the file to be finished.
    public async Task Stop()
    {
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
            if (_videoRequest is { } video) { video.Request.Sample = null; video.Deferral.Complete(); _videoRequest = null; }
        }
        _stall?.Dispose();
        _audioWanted.Release();
        try { _session.Dispose(); } catch (Exception ex) when (ex is COMException or ObjectDisposedException) { }
        foreach (var capture in _captures) { try { capture.StopRecording(); } catch (Exception ex) when (ex is COMException or InvalidOperationException) { } }
        try { await _audioLoop; await _transcode; }
        finally
        {
            foreach (var capture in _captures) capture.Dispose();
            _pool.Dispose();
            lock (_gate) { if (_last is { } last) { last.Replaced = true; last.Release(); } }
        }
    }

    private void AddAudio(WasapiCapture capture)
    {
        var buffer = new BufferedWaveProvider(capture.WaveFormat) { ReadFully = true, DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(1) };
        capture.DataAvailable += (_, e) => { if (!Paused) buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); };
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        else if (samples.WaveFormat.Channels > 2) samples = new MultiplexingSampleProvider([samples], 2);
        if (samples.WaveFormat.SampleRate != Rate) samples = new WdlResamplingSampleProvider(samples, Rate);
        _mixer!.AddMixerInput(samples);
        _captures.Add(capture);
        _buffers.Add(buffer);
    }

    private void SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs e)
    {
        var request = e.Request;
        lock (_gate)
        {
            if (_stopping) { request.Sample = null; return; }
            if (request.StreamDescriptor is VideoStreamDescriptor) _videoRequest = (request, request.GetDeferral(), Environment.TickCount64);
            else { _audioRequests.Enqueue((request, request.GetDeferral())); _audioWanted.Release(); }
        }
    }

    private void FrameArrived()
    {
        var frame = _pool.TryGetNextFrame();
        if (frame is null) return;
        lock (_gate)
        {
            if (_stopping) { frame.Dispose(); return; }
            if (_last is { } old) { old.Replaced = true; old.Release(); }
            _last = new Held(frame);
            if (!Paused) Send(_last);
        }
    }

    private void ResendLast()
    {
        lock (_gate)
            if (!_stopping && !Paused && _last is { } last && _videoRequest is { } wanted && Environment.TickCount64 - wanted.Since >= 100) Send(last);
    }

    // Under _gate.
    private void Send(Held held)
    {
        if (_videoRequest is not { } wanted) return;
        var stamp = _clock.Elapsed - _pausedTotal;
        if (stamp - _lastStamp < _frameGap) return;
        _lastStamp = stamp;
        _videoRequest = null;
        held.Uses++;
        var sample = MediaStreamSample.CreateFromDirect3D11Surface(held.Frame.Surface, stamp);
        sample.Processed += (_, _) => { lock (_gate) { held.Uses--; held.Release(); } };
        wanted.Request.Sample = sample;
        wanted.Deferral.Complete();
    }

    // Answers each sound request with the next 20 ms once the recording's clock has reached it, so the sound keeps to real time
    // even while nothing plays (the buffers then give silence).
    private async Task AudioLoop()
    {
        var floats = new float[Chunk * Channels];
        while (true)
        {
            await _audioWanted.WaitAsync();
            (MediaStreamSourceSampleRequest Request, MediaStreamSourceSampleRequestDeferral Deferral) next;
            lock (_gate)
            {
                if (_stopping) { while (_audioRequests.TryDequeue(out var left)) { left.Request.Sample = null; left.Deferral.Complete(); } return; }
                if (!_audioRequests.TryDequeue(out next)) continue;
            }
            var stamp = TimeSpan.FromSeconds(_audioFrames / (double)Rate);
            var end = TimeSpan.FromSeconds((_audioFrames + Chunk) / (double)Rate);
            while (Elapsed < end && !_stopping) await Task.Delay(5);
            if (_stopping) { next.Request.Sample = null; next.Deferral.Complete(); _audioWanted.Release(); continue; }
            _mixer!.Read(floats, 0, floats.Length);
            var bytes = new byte[floats.Length * 2];
            for (int i = 0; i < floats.Length; i++)
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), (short)Math.Clamp(floats[i] * 32767f, short.MinValue, short.MaxValue));
            var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), stamp);
            sample.Duration = end - stamp;
            _audioFrames += Chunk;
            next.Request.Sample = sample;
            next.Deferral.Complete();
        }
    }

    // ---- Direct3D and capture item interop ----

    private static IDirect3DDevice CreateDevice()
    {
        const int Hardware = 1, Warp = 5; const uint BgraSupport = 0x20, SdkVersion = 7;
        int hr = D3D11CreateDevice(0, Hardware, 0, BgraSupport, 0, 0, SdkVersion, out nint device, out _, out nint context);
        if (hr < 0) hr = D3D11CreateDevice(0, Warp, 0, BgraSupport, 0, 0, SdkVersion, out device, out _, out context);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var dxgiGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in dxgiGuid, out nint dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out nint inspectable));
                try { return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
                finally { Marshal.Release(inspectable); }
            }
            finally { Marshal.Release(dxgi); }
        }
        finally { Marshal.Release(context); Marshal.Release(device); }
    }

    private static GraphicsCaptureItem ItemForMonitor(IntPtr monitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        nint pointer = interop.CreateForMonitor(monitor, ref itemGuid);
        try { return GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, ref Guid iid);
        nint CreateForMonitor(nint monitor, ref Guid iid);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint levelCount, uint sdkVersion,
        out nint device, out int level, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
