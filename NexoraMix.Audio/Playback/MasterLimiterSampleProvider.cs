using NAudio.Wave;

namespace NexoraMix.Audio.Playback;

internal sealed class MasterLimiterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _threshold;
    private long _clippingCount;
    private double _peakDbFs = -120d;

    public MasterLimiterSampleProvider(ISampleProvider source, double thresholdDbFs = -1d)
    {
        _source = source;
        _threshold = (float)Math.Pow(10d, thresholdDbFs / 20d);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;
    public long ClippingCount => Interlocked.Read(ref _clippingCount);
    public double PeakDbFs => Volatile.Read(ref _peakDbFs);

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        float blockPeak = 0;

        for (var i = 0; i < read; i++)
        {
            var index = offset + i;
            var sample = buffer[index];
            var absolute = Math.Abs(sample);
            blockPeak = Math.Max(blockPeak, absolute);
            if (absolute > 1f) Interlocked.Increment(ref _clippingCount);
            buffer[index] = Math.Clamp(sample, -_threshold, _threshold);
        }

        var peakDb = blockPeak > 0.000001f ? 20d * Math.Log10(blockPeak) : -120d;
        Volatile.Write(ref _peakDbFs, peakDb);
        return read;
    }

    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _clippingCount, 0);
        Volatile.Write(ref _peakDbFs, -120d);
    }
}
