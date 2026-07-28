using NAudio.Wave;

namespace NexoraMix.Audio.Playback;

internal sealed class ClockedSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly AudioClock _clock;

    public ClockedSampleProvider(ISampleProvider source, AudioClock clock)
    {
        _source = source;
        _clock = clock;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        _clock.Advance(read / Math.Max(1, WaveFormat.Channels));
        return read;
    }
}
