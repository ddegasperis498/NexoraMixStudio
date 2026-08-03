using NAudio.Wave;
using NexoraMix.Core.Models;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

namespace NexoraMix.Audio.Playback;

public enum PreviewPlaybackState
{
    Empty,
    Ready,
    Playing,
    Paused,
    Error
}

internal sealed class PreviewChannel : ISampleProvider, IDisposable
{
    private readonly object _gate = new();
    private AudioFileReader? _reader;
    private ISampleProvider? _source;
    private double _cueSeconds;
    private PreviewPlaybackState _state;

    public PreviewChannel(WaveFormat waveFormat)
    {
        WaveFormat = waveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public AudioTrack? Track
    {
        get { lock (_gate) return _reader is null ? null : _track; }
    }

    private AudioTrack? _track;

    public PreviewPlaybackState State
    {
        get { lock (_gate) return _state; }
    }

    public bool IsPlaying => State == PreviewPlaybackState.Playing;

    public double PositionSeconds
    {
        get { lock (_gate) return _reader?.CurrentTime.TotalSeconds ?? 0d; }
    }

    public Exception? LastError { get; private set; }

    public void Load(AudioTrack track, double cueSeconds)
    {
        ArgumentNullException.ThrowIfNull(track);
        lock (_gate)
        {
            DisposeReaderUnsafe();
            try
            {
                if (!track.CanLoadToDeck || string.IsNullOrWhiteSpace(track.FilePath))
                    throw new InvalidOperationException("La preview richiede una traccia locale riproducibile.");
                if (!double.IsFinite(cueSeconds))
                    throw new ArgumentOutOfRangeException(nameof(cueSeconds));

                var fullPath = IOPath.GetFullPath(track.FilePath);
                if (!IOFile.Exists(fullPath))
                    throw new System.IO.FileNotFoundException("File preview non trovato.", fullPath);

                _reader = new AudioFileReader(fullPath);
                _track = track;
                _cueSeconds = Math.Clamp(cueSeconds, 0d, _reader.TotalTime.TotalSeconds);
                SeekUnsafe(_cueSeconds);
                LastError = null;
                _state = PreviewPlaybackState.Ready;
            }
            catch (Exception error)
            {
                DisposeReaderUnsafe();
                LastError = error;
                _state = PreviewPlaybackState.Error;
                throw;
            }
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_reader is null) throw new InvalidOperationException("Carica prima una traccia in preview.");
            if (_reader.CurrentTime >= _reader.TotalTime) SeekUnsafe(_cueSeconds);
            LastError = null;
            _state = PreviewPlaybackState.Playing;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_reader is not null && _state == PreviewPlaybackState.Playing)
                _state = PreviewPlaybackState.Paused;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_reader is null) return;
            SeekUnsafe(_cueSeconds);
            _state = PreviewPlaybackState.Ready;
        }
    }

    public void Unload()
    {
        lock (_gate)
        {
            DisposeReaderUnsafe();
            LastError = null;
            _state = PreviewPlaybackState.Empty;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            if (_state != PreviewPlaybackState.Playing || _source is null) return 0;
            try
            {
                var read = _source.Read(buffer, offset, count);
                if (read < count || read == 0) _state = PreviewPlaybackState.Ready;
                return read;
            }
            catch (Exception error)
            {
                LastError = error;
                _state = PreviewPlaybackState.Error;
                return 0;
            }
        }
    }

    private void SeekUnsafe(double seconds)
    {
        if (_reader is null) return;
        _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0d, _reader.TotalTime.TotalSeconds));
        _source = StereoSampleProviderFactory.Create(_reader, WaveFormat.SampleRate);
    }

    private void DisposeReaderUnsafe()
    {
        _reader?.Dispose();
        _reader = null;
        _source = null;
        _track = null;
    }

    public void Dispose()
    {
        lock (_gate) DisposeReaderUnsafe();
        GC.SuppressFinalize(this);
    }
}
