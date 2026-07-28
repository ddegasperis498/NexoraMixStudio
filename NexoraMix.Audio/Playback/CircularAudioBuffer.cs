namespace NexoraMix.Audio.Playback;

internal sealed class CircularAudioBuffer
{
    private readonly object _gate = new();
    private readonly float[] _buffer;
    private int _read;
    private int _write;
    private int _available;

    public CircularAudioBuffer(int capacitySamples)
    {
        _buffer = new float[Math.Max(2, capacitySamples)];
    }

    public void Clear()
    {
        lock (_gate)
        {
            _read = 0;
            _write = 0;
            _available = 0;
        }
    }

    public void Write(float[] source, int offset, int count, float gain = 1f)
    {
        if (count <= 0) return;
        lock (_gate)
        {
            for (var i = 0; i < count; i++)
            {
                _buffer[_write] = source[offset + i] * gain;
                _write = (_write + 1) % _buffer.Length;
                if (_available < _buffer.Length)
                {
                    _available++;
                }
                else
                {
                    _read = (_read + 1) % _buffer.Length;
                }
            }
        }
    }

    public int Read(float[] destination, int offset, int count)
    {
        if (count <= 0) return 0;
        lock (_gate)
        {
            var read = Math.Min(count, _available);
            for (var i = 0; i < read; i++)
            {
                destination[offset + i] = _buffer[_read];
                _read = (_read + 1) % _buffer.Length;
            }
            _available -= read;
            return read;
        }
    }
}
