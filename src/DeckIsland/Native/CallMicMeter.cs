using NAudio.Wave;

namespace DeckIsland.Native;

// NAudio delivers packets on the capture thread. The UI consumes the peak accumulated since its previous refresh.
internal sealed class CallMicMeter
{
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _samples;
    private readonly float[] _scratch = new float[1024];
    private readonly object _gate = new();
    private float _pendingPeak;
    private bool _hasSamples;

    internal CallMicMeter(WaveFormat format)
    {
        if (format is WaveFormatExtensible extensible) format = extensible.ToStandardWaveFormat();
        _buffer = new BufferedWaveProvider(format) { ReadFully = false };
        _samples = _buffer.ToSampleProvider();
    }

    internal void AddSamples(byte[] bytes, int length)
    {
        if (length == 0) return;
        _buffer.AddSamples(bytes, 0, length);
        float peak = 0;
        int count;
        while ((count = _samples.Read(_scratch, 0, _scratch.Length)) > 0)
        {
            foreach (float sample in _scratch.AsSpan(0, count))
                if (float.IsFinite(sample)) peak = Math.Max(peak, Math.Abs(sample));
        }
        lock (_gate)
        {
            _pendingPeak = Math.Max(_pendingPeak, peak);
            _hasSamples = true;
        }
    }

    internal bool TryTakePeak(out float peak)
    {
        lock (_gate)
        {
            peak = _pendingPeak;
            bool received = _hasSamples;
            _pendingPeak = 0;
            _hasSamples = false;
            return received;
        }
    }
}

// One response drives the bar, ring and speech label. Keep the attack immediate and the release short,
// with a small gap between the speech thresholds so a steady low signal does not flap around the cutoff.
internal struct CallMicResponse
{
    internal double Level { get; private set; }
    internal bool Hearing { get; private set; }

    internal void Update(float peak)
    {
        double target = Math.Min(1, Math.Sqrt(Math.Max(0, peak)) * 2);
        Level = Math.Max(target, Level * .58);
        if (Level < .008) Level = 0;
        Hearing = Level >= (Hearing ? .10 : .14);
    }
}
