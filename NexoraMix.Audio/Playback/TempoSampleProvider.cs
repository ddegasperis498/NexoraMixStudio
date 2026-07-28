using NAudio.Wave;

namespace NexoraMix.Audio.Playback;

/// <summary>
/// Varia la velocità tramite interpolazione lineare mantenendo il formato di uscita.
/// Questa implementazione sincronizza il tempo ma, diversamente da SoundTouch,
/// modifica anche l'intonazione. L'interfaccia è isolata per consentire un futuro
/// processore time-stretch con key-lock.
/// </summary>
internal sealed class TempoSampleProvider : ISampleProvider, ITimeStretchProcessor
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _sourceBuffer;
    private int _bufferedFrames;
    private double _positionFrames;
    private bool _sourceEnded;
    private long _sourceFramesConsumed;
    private double _targetPlaybackRate = 1d;
    private double _currentPlaybackRate = 1d;

    public TempoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sourceBuffer = new float[16_384 * _channels];
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }
    public long SourceFramesConsumed => _sourceFramesConsumed + (long)Math.Floor(_positionFrames);

    public double PlaybackRate
    {
        get => Volatile.Read(ref _targetPlaybackRate);
        set => Volatile.Write(ref _targetPlaybackRate, Math.Clamp(value, 0.75d, 1.25d));
    }

    public double TempoRatio
    {
        get => PlaybackRate;
        set => PlaybackRate = value;
    }

    public bool KeyLockEnabled => false;

    public int Read(float[] buffer, int offset, int count)
    {
        var requestedFrames = count / _channels;
        var writtenFrames = 0;

        while (writtenFrames < requestedFrames)
        {
            if (!EnsureFramesAvailable(2)) break;

            var baseFrame = (int)Math.Floor(_positionFrames);
            if (baseFrame >= _bufferedFrames) break;
            var nextFrame = Math.Min(baseFrame + 1, _bufferedFrames - 1);
            var fraction = (float)(_positionFrames - baseFrame);

            var destination = offset + writtenFrames * _channels;
            var baseOffset = baseFrame * _channels;
            var nextOffset = nextFrame * _channels;

            for (var channel = 0; channel < _channels; channel++)
            {
                var first = _sourceBuffer[baseOffset + channel];
                var second = _sourceBuffer[nextOffset + channel];
                buffer[destination + channel] = first + (second - first) * fraction;
            }

            _positionFrames += NextSmoothedPlaybackRate();
            writtenFrames++;

            if (_positionFrames >= 4096d) CompactBuffer();
        }

        return writtenFrames * _channels;
    }

    public void Reset()
    {
        _bufferedFrames = 0;
        _positionFrames = 0;
        _sourceEnded = false;
        _sourceFramesConsumed = 0;
        _currentPlaybackRate = PlaybackRate;
    }

    private bool EnsureFramesAvailable(int requiredFrames)
    {
        if (_positionFrames >= 4096d) CompactBuffer();
        var needed = (int)Math.Floor(_positionFrames) + requiredFrames;

        while (_bufferedFrames < needed && !_sourceEnded)
        {
            EnsureCapacity(_bufferedFrames + Math.Max(4096, needed - _bufferedFrames));
            var destinationOffset = _bufferedFrames * _channels;
            var availableSamples = _sourceBuffer.Length - destinationOffset;
            var read = _source.Read(_sourceBuffer, destinationOffset, availableSamples);
            if (read <= 0)
            {
                _sourceEnded = true;
                break;
            }
            _bufferedFrames += read / _channels;
        }

        return _bufferedFrames > (int)Math.Floor(_positionFrames);
    }

    private void CompactBuffer()
    {
        var consumedFrames = (int)Math.Floor(_positionFrames);
        if (consumedFrames <= 0) return;

        if (consumedFrames >= _bufferedFrames)
        {
            _sourceFramesConsumed += consumedFrames;
            _bufferedFrames = 0;
            _positionFrames -= consumedFrames;
            return;
        }

        var remainingFrames = _bufferedFrames - consumedFrames;
        Array.Copy(
            _sourceBuffer,
            consumedFrames * _channels,
            _sourceBuffer,
            0,
            remainingFrames * _channels);

        _sourceFramesConsumed += consumedFrames;
        _bufferedFrames = remainingFrames;
        _positionFrames -= consumedFrames;
    }

    private void EnsureCapacity(int requiredFrames)
    {
        var requiredSamples = requiredFrames * _channels;
        if (_sourceBuffer.Length >= requiredSamples) return;
        var newSize = Math.Max(requiredSamples, _sourceBuffer.Length * 2);
        Array.Resize(ref _sourceBuffer, newSize);
    }

    private double NextSmoothedPlaybackRate()
    {
        var target = PlaybackRate;
        const double maximumStepPerFrame = 0.00008d;
        var delta = target - _currentPlaybackRate;
        if (Math.Abs(delta) <= maximumStepPerFrame)
        {
            _currentPlaybackRate = target;
        }
        else
        {
            _currentPlaybackRate += Math.Sign(delta) * maximumStepPerFrame;
        }

        return _currentPlaybackRate;
    }
}
