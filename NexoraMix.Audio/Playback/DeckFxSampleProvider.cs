using NAudio.Wave;

namespace NexoraMix.Audio.Playback;

public sealed class DeckFxSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _delayBuffer;
    private readonly float[] _rollBuffer;
    private readonly double[] _filterState;
    private int _delayIndex;
    private int _rollWriteIndex;
    private int _rollReadIndex;
    private int _rollSamplesWritten;
    private double _filter;
    private double _echoMix;
    private double _bitCrush;
    private double _saturation;
    private double _gate;
    private double _compressor;
    private double _roll;
    private double _brake;
    private double _brakePhase;

    public DeckFxSampleProvider(ISampleProvider source, double maximumDelaySeconds = 1d)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        WaveFormat = source.WaveFormat;
        _delayBuffer = new float[Math.Max(WaveFormat.Channels, (int)(WaveFormat.SampleRate * WaveFormat.Channels * maximumDelaySeconds))];
        _rollBuffer = new float[Math.Max(WaveFormat.Channels, (int)(WaveFormat.SampleRate * WaveFormat.Channels * 0.5d))];
        _filterState = new double[WaveFormat.Channels];
    }

    public WaveFormat WaveFormat { get; }

    public double Filter
    {
        get => Volatile.Read(ref _filter);
        set => Volatile.Write(ref _filter, Math.Clamp(value, -1d, 1d));
    }

    public double EchoMix
    {
        get => Volatile.Read(ref _echoMix);
        set => Volatile.Write(ref _echoMix, Math.Clamp(value, 0d, 1d));
    }

    public double BitCrush
    {
        get => Volatile.Read(ref _bitCrush);
        set => Volatile.Write(ref _bitCrush, Math.Clamp(value, 0d, 1d));
    }

    public double Saturation
    {
        get => Volatile.Read(ref _saturation);
        set => Volatile.Write(ref _saturation, Math.Clamp(value, 0d, 1d));
    }

    public double Gate
    {
        get => Volatile.Read(ref _gate);
        set => Volatile.Write(ref _gate, Math.Clamp(value, 0d, 1d));
    }

    public double Compressor
    {
        get => Volatile.Read(ref _compressor);
        set => Volatile.Write(ref _compressor, Math.Clamp(value, 0d, 1d));
    }

    public double Roll
    {
        get => Volatile.Read(ref _roll);
        set => Volatile.Write(ref _roll, Math.Clamp(value, 0d, 1d));
    }

    public double Brake
    {
        get => Volatile.Read(ref _brake);
        set => Volatile.Write(ref _brake, Math.Clamp(value, 0d, 1d));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var filter = Filter;
        var echo = EchoMix;
        var crush = BitCrush;
        var saturation = Saturation;
        var gate = Gate;
        var compressor = Compressor;
        var roll = Roll;
        var brake = Brake;
        var channels = WaveFormat.Channels;
        var sampleRate = WaveFormat.SampleRate;
        var delaySamples = Math.Clamp((int)(sampleRate * channels * 0.375d), channels, _delayBuffer.Length - channels);
        var rollWindowSamples = Math.Clamp(
            (int)(sampleRate * channels * (0.50d - roll * 0.46d)),
            channels * 64,
            _rollBuffer.Length - channels);

        var cutoff = filter < 0
            ? 20_000d * Math.Pow(0.01d, -filter)
            : 20d * Math.Pow(250d, filter);
        cutoff = Math.Clamp(cutoff, 20d, sampleRate * 0.45d);
        var alpha = 1d - Math.Exp(-2d * Math.PI * cutoff / sampleRate);
        var levels = crush <= 0.001d ? 0d : Math.Pow(2d, 16d - 12d * crush);

        for (var i = 0; i < read; i++)
        {
            var channel = i % channels;
            var sample = buffer[offset + i];
            var dryBeforeRoll = sample;
            _rollBuffer[_rollWriteIndex] = sample;
            _rollWriteIndex = (_rollWriteIndex + 1) % _rollBuffer.Length;
            _rollSamplesWritten = Math.Min(_rollBuffer.Length, _rollSamplesWritten + 1);

            if (roll > 0.001d)
            {
                if (_rollSamplesWritten >= rollWindowSamples)
                {
                    if (_rollReadIndex <= 0 || _rollReadIndex >= rollWindowSamples) _rollReadIndex = 0;
                    var readIndex = _rollWriteIndex - rollWindowSamples + _rollReadIndex;
                    while (readIndex < 0) readIndex += _rollBuffer.Length;
                    sample = _rollBuffer[readIndex % _rollBuffer.Length];
                    _rollReadIndex++;
                }
                else
                {
                    sample = dryBeforeRoll;
                }
            }
            else
            {
                _rollReadIndex = 0;
            }

            if (Math.Abs(filter) > 0.001d)
            {
                _filterState[channel] += alpha * (sample - _filterState[channel]);
                sample = filter < 0 ? (float)_filterState[channel] : sample - (float)_filterState[channel];
            }

            if (levels > 0d)
                sample = (float)(Math.Round(sample * levels) / levels);

            if (gate > 0.001d)
            {
                var threshold = 0.004d + gate * 0.08d;
                if (Math.Abs(sample) < threshold) sample *= (float)(1d - gate);
            }

            if (compressor > 0.001d)
            {
                var threshold = 0.55d - compressor * 0.30d;
                var ratio = 1d + compressor * 7d;
                var sign = Math.Sign(sample);
                var magnitude = Math.Abs(sample);
                if (magnitude > threshold)
                    sample = (float)(sign * (threshold + (magnitude - threshold) / ratio));
            }

            if (saturation > 0.001d)
            {
                var drive = 1d + saturation * 6d;
                sample = (float)(Math.Tanh(sample * drive) / Math.Tanh(drive));
            }

            if (brake > 0.001d)
            {
                _brakePhase = Math.Min(1d, _brakePhase + brake / Math.Max(1d, sampleRate * 0.65d));
                sample *= (float)Math.Pow(1d - _brakePhase, 1.7d);
            }
            else
            {
                _brakePhase = 0d;
            }

            if (echo > 0.001d)
            {
                var readIndex = _delayIndex - delaySamples;
                if (readIndex < 0) readIndex += _delayBuffer.Length;
                var delayed = _delayBuffer[readIndex];
                var dry = sample;
                sample = (float)(dry * (1d - echo * 0.35d) + delayed * echo * 0.55d);
                _delayBuffer[_delayIndex] = (float)Math.Clamp(dry + delayed * 0.38d, -1d, 1d);
            }
            else
            {
                _delayBuffer[_delayIndex] = sample;
            }

            _delayIndex++;
            if (_delayIndex >= _delayBuffer.Length) _delayIndex = 0;
            buffer[offset + i] = sample;
        }

        return read;
    }

    public void Reset()
    {
        Array.Clear(_delayBuffer, 0, _delayBuffer.Length);
        Array.Clear(_rollBuffer, 0, _rollBuffer.Length);
        Array.Clear(_filterState, 0, _filterState.Length);
        _delayIndex = 0;
        _rollWriteIndex = 0;
        _rollReadIndex = 0;
        _rollSamplesWritten = 0;
        _brakePhase = 0d;
    }
}
