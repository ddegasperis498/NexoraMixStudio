using NAudio.Wave;

namespace NexoraMix.Audio.Playback;

internal sealed class AudioTapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly CircularAudioBuffer _tap;

    public AudioTapSampleProvider(ISampleProvider source, double capacitySeconds = 2d)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        _tap = new CircularAudioBuffer((int)Math.Ceiling(WaveFormat.SampleRate * WaveFormat.Channels * capacitySeconds));
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        _tap.Write(buffer, offset, read);
        return read;
    }

    public int ReadTap(float[] buffer, int offset, int count) => _tap.Read(buffer, offset, count);
}
