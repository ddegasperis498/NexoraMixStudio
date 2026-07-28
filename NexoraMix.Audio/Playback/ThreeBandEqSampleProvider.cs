using NAudio.Dsp;
using NAudio.Wave;

namespace NexoraMix.Audio.Playback;

internal sealed class ThreeBandEqSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private BiQuadFilter[] _lowFilters;
    private BiQuadFilter[] _midFilters;
    private BiQuadFilter[] _highFilters;
    private double _lowGainDb;
    private double _midGainDb;
    private double _highGainDb;
    private int _filtersDirty = 1;

    public ThreeBandEqSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = source.WaveFormat;
        _lowFilters = new BiQuadFilter[_channels];
        _midFilters = new BiQuadFilter[_channels];
        _highFilters = new BiQuadFilter[_channels];
        RebuildFilters();
    }

    public WaveFormat WaveFormat { get; }

    public double LowGainDb
    {
        get => Volatile.Read(ref _lowGainDb);
        set { Volatile.Write(ref _lowGainDb, Math.Clamp(value, -24d, 12d)); Interlocked.Exchange(ref _filtersDirty, 1); }
    }

    public double MidGainDb
    {
        get => Volatile.Read(ref _midGainDb);
        set { Volatile.Write(ref _midGainDb, Math.Clamp(value, -24d, 12d)); Interlocked.Exchange(ref _filtersDirty, 1); }
    }

    public double HighGainDb
    {
        get => Volatile.Read(ref _highGainDb);
        set { Volatile.Write(ref _highGainDb, Math.Clamp(value, -24d, 12d)); Interlocked.Exchange(ref _filtersDirty, 1); }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (Interlocked.Exchange(ref _filtersDirty, 0) == 1) RebuildFilters();
        var read = _source.Read(buffer, offset, count);

        for (var sample = 0; sample < read; sample++)
        {
            var channel = sample % _channels;
            var value = buffer[offset + sample];
            value = _lowFilters[channel].Transform(value);
            value = _midFilters[channel].Transform(value);
            value = _highFilters[channel].Transform(value);
            buffer[offset + sample] = value;
        }

        return read;
    }

    public void Reset() => RebuildFilters();

    private void RebuildFilters()
    {
        var sampleRate = WaveFormat.SampleRate;
        var low = (float)LowGainDb;
        var mid = (float)MidGainDb;
        var high = (float)HighGainDb;

        for (var channel = 0; channel < _channels; channel++)
        {
            _lowFilters[channel] = BiQuadFilter.LowShelf(sampleRate, 180f, 0.8f, low);
            _midFilters[channel] = BiQuadFilter.PeakingEQ(sampleRate, 1100f, 0.9f, mid);
            _highFilters[channel] = BiQuadFilter.HighShelf(sampleRate, 5200f, 0.8f, high);
        }
    }
}
