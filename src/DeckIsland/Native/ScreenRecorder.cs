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

// What a recording is made with: the frame rate, the most it may be encoded at (height, 0 for the screens' own size), the bit
// rate per pixel and frame, the sound, and whether Windows draws its yellow border around what is captured.
internal sealed record RecordingOptions(int Fps, int MaxHeight, double BitsPerPixel, bool SystemAudio, bool Mic, bool Border);

// Records one or more monitors into one MP4: each monitor's Windows.Graphics.Capture frames are copied, on the graphics card, into
// one picture laid out as the monitors are, which Media Foundation's H.264 encoder takes through a MediaStreamSource; the system's
// sound and the microphone, mixed, go to its AAC encoder. Time stops while paused.
internal sealed class ScreenRecorder
{
    private const int Rate = 48000, Channels = 2, Chunk = Rate / 50; // 20 ms of sound per sample
    private const int Slots = 4;
    private readonly IDirect3DDevice _device;
    private readonly nint _d3dDevice, _context;
    private readonly List<Screen> _screens = [];
    private readonly Slot[] _slots = new Slot[Slots];
    private readonly int _width, _height;
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
    private readonly RecordingOptions _options;
    private (MediaStreamSourceSampleRequest Request, MediaStreamSourceSampleRequestDeferral Deferral, long Since)? _videoRequest;
    private Slot? _last;
    private bool _fresh;
    private TimeSpan _lastStamp = TimeSpan.FromDays(-1), _pausedTotal, _pausedAt;
    private long _audioFrames;
    private bool _stopping;
    private Task _transcode = Task.CompletedTask, _audioLoop = Task.CompletedTask;
    private Timer? _stall;

    public bool Paused { get; private set; }
    public string Path { get; }
    public TimeSpan Elapsed { get { lock (_gate) return (Paused ? _pausedAt : _clock.Elapsed) - _pausedTotal; } }

    // One monitor: its capture and where its picture goes in the recording.
    private sealed record Screen(Direct3D11CaptureFramePool Pool, GraphicsCaptureSession Session, int X, int Y, int Width, int Height);

    // A picture of the whole recording; Uses counts the samples the encoder still holds.
    private sealed class Slot(nint texture, IDirect3DSurface surface)
    {
        public nint Texture { get; } = texture;
        public IDirect3DSurface Surface { get; } = surface;
        public int Uses;
    }

    private ScreenRecorder(IReadOnlyList<DisplayInfo> displays, string path, RecordingOptions options)
    {
        Path = path;
        _options = options;
        _frameGap = TimeSpan.FromSeconds(0.9 / options.Fps);
        (_device, _d3dDevice, _context) = CreateDevice();
        int left = (int)displays.Min(d => d.Bounds.Left), top = (int)displays.Min(d => d.Bounds.Top);
        _width = ((int)displays.Max(d => d.Bounds.Right) - left) & ~1;
        _height = ((int)displays.Max(d => d.Bounds.Bottom) - top) & ~1;
        for (int i = 0; i < Slots; i++) _slots[i] = CreateSlot(_width, _height);
        foreach (var display in displays)
        {
            var item = ItemForMonitor(display.Handle);
            var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            var session = pool.CreateCaptureSession(item);
            if (!options.Border) HideBorder(session);
            var screen = new Screen(pool, session, (int)display.Bounds.Left - left, (int)display.Bounds.Top - top, item.Size.Width, item.Size.Height);
            pool.FrameArrived += (_, _) => { try { FrameArrived(screen); } catch (Exception ex) when (ex is COMException or ArgumentException or ObjectDisposedException or InvalidOperationException) { } };
            _screens.Add(screen);
        }
        _video = new VideoStreamDescriptor(VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_width, (uint)_height));
        if (options.SystemAudio || options.Mic)
        {
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, Channels)) { ReadFully = true };
            if (options.SystemAudio) AddAudio(new WasapiLoopbackCapture());
            if (options.Mic) AddAudio(new WasapiCapture());
            _source = new MediaStreamSource(_video, new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(Rate, Channels, 16)));
        }
        else _source = new MediaStreamSource(_video);
        _source.BufferTime = TimeSpan.Zero;
        _source.CanSeek = false;
        _source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        _source.SampleRequested += SampleRequested;
    }

    public static async Task<ScreenRecorder> Start(IReadOnlyList<DisplayInfo> displays, string path, RecordingOptions options)
    {
        // Hiding the border needs the user's consent once (GraphicsCaptureAccess, Borderless); without it Windows keeps the border.
        if (!options.Border) await Task.Run(AskBorderless);
        var recorder = new ScreenRecorder(displays, path, options);
        try { await recorder.Begin(); }
        catch { await recorder.Stop(); throw; }
        return recorder;
    }

    // The size the video is encoded at: the screens' own, or scaled down to the chosen height, and never wider than H.264's 4096.
    public static (int Width, int Height) OutputSize(int width, int height, int maxHeight)
    {
        double scale = Math.Min(1, Math.Min(maxHeight > 0 ? maxHeight / (double)height : 1, 4096.0 / width));
        return ((int)(width * scale) & ~1, (int)(height * scale) & ~1);
    }

    private async Task Begin()
    {
        var (width, height) = OutputSize(_width, _height, _options.MaxHeight);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)width; profile.Video.Height = (uint)height;
        profile.Video.FrameRate.Numerator = (uint)_options.Fps; profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Video.Bitrate = (uint)Math.Clamp(width * (double)height * _options.Fps * _options.BitsPerPixel, 2_000_000, 80_000_000);
        profile.Audio = _mixer is null ? null : AudioEncodingProperties.CreateAac(Rate, Channels, 192_000);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var file = new FileStream(Path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(_source, file.AsRandomAccessStream(), profile);
        if (!prepared.CanTranscode) { await file.DisposeAsync(); throw new InvalidOperationException("The encoder refused: " + prepared.FailureReason); }
        _clock.Start();
        foreach (var capture in _captures) capture.StartRecording();
        foreach (var screen in _screens) screen.Session.StartCapture();
        _transcode = prepared.TranscodeAsync().AsTask().ContinueWith(async done => { await file.DisposeAsync(); await done; }).Unwrap();
        if (_mixer is not null) _audioLoop = Task.Run(AudioLoop);
        // A still screen sends no frames; the last picture is sent again so the sound keeps going.
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
        foreach (var screen in _screens) { try { screen.Session.Dispose(); } catch (Exception ex) when (ex is COMException or ObjectDisposedException) { } }
        foreach (var capture in _captures) { try { capture.StopRecording(); } catch (Exception ex) when (ex is COMException or InvalidOperationException) { } }
        try { await _audioLoop; await _transcode; }
        finally
        {
            foreach (var capture in _captures) capture.Dispose();
            lock (_gate)
            {
                foreach (var screen in _screens) screen.Pool.Dispose();
                foreach (var slot in _slots) Marshal.Release(slot.Texture);
                Marshal.Release(_context);
                Marshal.Release(_d3dDevice);
            }
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
            if (request.StreamDescriptor is VideoStreamDescriptor) { _videoRequest = (request, request.GetDeferral(), Environment.TickCount64); if (_fresh) Send(); }
            else { _audioRequests.Enqueue((request, request.GetDeferral())); _audioWanted.Release(); }
        }
    }

    // Copies a monitor's new frame into a free picture of the whole recording, which then holds every monitor's latest frame.
    private void FrameArrived(Screen screen)
    {
        using var frame = screen.Pool.TryGetNextFrame();
        if (frame is null) return;
        lock (_gate)
        {
            if (_stopping || Paused) return;
            var slot = _last is { Uses: 0 } last ? last : Array.Find(_slots, s => s.Uses == 0);
            if (slot is null) return; // the encoder holds every picture: this frame is dropped
            if (!ReferenceEquals(slot, _last) && _last is not null) CopyWhole(_last, slot);
            nint source = TextureOf(frame.Surface);
            try
            {
                var box = new Box { Right = (uint)Math.Min(screen.Width, frame.ContentSize.Width), Bottom = (uint)Math.Min(screen.Height, frame.ContentSize.Height), Back = 1 };
                box.Right = Math.Min(box.Right, (uint)(_width - screen.X)); box.Bottom = Math.Min(box.Bottom, (uint)(_height - screen.Y));
                Vtable<CopyRegion>(_context, 46)(_context, slot.Texture, 0, (uint)screen.X, (uint)screen.Y, 0, source, 0, ref box);
            }
            finally { Marshal.Release(source); }
            _last = slot;
            _fresh = true;
            Send();
        }
    }

    // Carries the previous picture over to a new one, so monitors without a new frame keep theirs.
    private void CopyWhole(Slot from, Slot to) => Vtable<CopyResource>(_context, 47)(_context, to.Texture, from.Texture);

    private void ResendLast()
    {
        lock (_gate)
            if (!_stopping && !Paused && _last is not null && _videoRequest is { } wanted && Environment.TickCount64 - wanted.Since >= 100) { _fresh = true; Send(); }
    }

    // Under _gate.
    private void Send()
    {
        if (_videoRequest is not { } wanted || _last is not { } slot) return;
        var stamp = _clock.Elapsed - _pausedTotal;
        if (stamp - _lastStamp < _frameGap) return;
        _lastStamp = stamp;
        _videoRequest = null;
        _fresh = false;
        slot.Uses++;
        var sample = MediaStreamSample.CreateFromDirect3D11Surface(slot.Surface, stamp);
        sample.Processed += (_, _) => { lock (_gate) slot.Uses--; };
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

    // ---- Direct3D and capture interop ----

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Box { public uint Left, Top, Front, Right, Bottom, Back; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture2D(nint self, ref TextureDesc desc, nint initial, out nint texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyRegion(nint self, nint target, uint targetIndex, uint x, uint y, uint z, nint source, uint sourceIndex, ref Box box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResource(nint self, nint target, nint source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetDeviceContext(nint self, out nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInterface(nint self, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PutBool(nint self, byte value);

    private static T Vtable<T>(nint self, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * IntPtr.Size));

    private Slot CreateSlot(int width, int height)
    {
        // B8G8R8A8_UNORM, default usage, render target and shader resource (what the encoder's converter reads).
        var desc = new TextureDesc { Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = 87, SampleCount = 1, BindFlags = 0x20 | 0x8 };
        Marshal.ThrowExceptionForHR(Vtable<CreateTexture2D>(_d3dDevice, 5)(_d3dDevice, ref desc, 0, out nint texture));
        var dxgiSurface = new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(texture, in dxgiSurface, out nint surface));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(surface, out nint inspectable));
            try { return new Slot(texture, MarshalInterface<IDirect3DSurface>.FromAbi(inspectable)); }
            finally { Marshal.Release(inspectable); }
        }
        finally { Marshal.Release(surface); }
    }

    // The ID3D11Texture2D behind a capture frame's surface; the caller releases it.
    private static nint TextureOf(IDirect3DSurface surface)
    {
        nint unknown = ((IWinRTObject)surface).NativeObject.ThisPtr;
        {
            var accessGuid = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in accessGuid, out nint access));
            try
            {
                var textureGuid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                Marshal.ThrowExceptionForHR(Vtable<GetInterface>(access, 3)(access, ref textureGuid, out nint texture));
                return texture;
            }
            finally { Marshal.Release(access); }
        }
    }

    // GraphicsCaptureSession.IsBorderRequired (Windows 11) through its interface, since the projection this app builds against
    // predates it. Where Windows does not have it, or refuses, the border stays.
    private static void HideBorder(GraphicsCaptureSession session)
    {
        // The object's own pointer, held by the projection (not to be released here).
        nint unknown = ((IWinRTObject)session).NativeObject.ThisPtr;
        var session3 = new Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be");
        if (Marshal.QueryInterface(unknown, in session3, out nint third) != 0) return;
        try { Vtable<PutBool>(third, 7)(third, 0); }
        finally { Marshal.Release(third); }
    }

    // GraphicsCaptureAccess.RequestAccessAsync(Borderless), through its interfaces (the projection this app builds against predates
    // it). The package must declare graphicsCaptureWithoutBorder (scripts/notifications/AppxManifest.xml). Any failure leaves the border.
    private static void AskBorderless()
    {
        nint name = 0, statics = 0, operation = 0, info = 0;
        try
        {
            const string Class = "Windows.Graphics.Capture.GraphicsCaptureAccess";
            if (WindowsCreateString(Class, Class.Length, out name) != 0) return;
            var staticsGuid = new Guid("743ed370-06ec-5040-a58a-901f0f757095");
            if (RoGetActivationFactory(name, ref staticsGuid, out statics) != 0) return;
            if (Vtable<RequestAccess>(statics, 6)(statics, 0 /* Borderless */, out operation) != 0) return;
            var infoGuid = new Guid("00000036-0000-0000-C000-000000000046");
            if (Marshal.QueryInterface(operation, in infoGuid, out info) != 0) return;
            // Waits while Windows asks; Started is 0.
            for (int waited = 0; waited < 120_000; waited += 50)
            {
                if (Vtable<GetInt>(info, 7)(info, out int status) != 0 || status != 0) break;
                Thread.Sleep(50);
            }
        }
        finally
        {
            if (info != 0) Marshal.Release(info);
            if (operation != 0) Marshal.Release(operation);
            if (statics != 0) Marshal.Release(statics);
            if (name != 0) WindowsDeleteString(name);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RequestAccess(nint self, int kind, out nint operation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInt(nint self, out int value);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out nint hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint hstring);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);

    private static (IDirect3DDevice Device, nint D3dDevice, nint Context) CreateDevice()
    {
        const int Hardware = 1, Warp = 5; const uint BgraSupport = 0x20, SdkVersion = 7;
        int hr = D3D11CreateDevice(0, Hardware, 0, BgraSupport, 0, 0, SdkVersion, out nint device, out _, out nint context);
        if (hr < 0) hr = D3D11CreateDevice(0, Warp, 0, BgraSupport, 0, 0, SdkVersion, out device, out _, out context);
        Marshal.ThrowExceptionForHR(hr);
        var dxgiGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in dxgiGuid, out nint dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out nint inspectable));
            try { return (MarshalInterface<IDirect3DDevice>.FromAbi(inspectable), device, context); }
            finally { Marshal.Release(inspectable); }
        }
        finally { Marshal.Release(dxgi); }
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
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11SurfaceFromDXGISurface(nint dxgiSurface, out nint graphicsSurface);
}
