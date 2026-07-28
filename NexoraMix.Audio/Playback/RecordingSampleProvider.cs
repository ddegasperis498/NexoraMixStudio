using System.IO;
using NAudio.Wave;

namespace NexoraMix.Audio.Playback;

public sealed class RecordingSampleProvider : ISampleProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly ISampleProvider _source;
    private WaveFileWriter? _writer;
    private bool _disposed;

    public RecordingSampleProvider(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }
    public bool IsRecording
    {
        get { lock (_gate) return _writer is not null; }
    }

    public string? CurrentPath { get; private set; }

    public void Start(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            ThrowIfDisposed();
            StopUnsafe();
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            _writer = new WaveFileWriter(fullPath, WaveFormat);
            CurrentPath = fullPath;
        }
    }

    public string? Stop()
    {
        lock (_gate)
        {
            var path = CurrentPath;
            StopUnsafe();
            return path;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        lock (_gate)
        {
            if (_writer is not null && read > 0)
            {
                _writer.WriteSamples(buffer, offset, read);
            }
        }
        return read;
    }

    private void StopUnsafe()
    {
        _writer?.Dispose();
        _writer = null;
        CurrentPath = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RecordingSampleProvider));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopUnsafe();
        }
        GC.SuppressFinalize(this);
    }
}
