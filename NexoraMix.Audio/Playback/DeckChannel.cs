using NAudio.Wave;
using NexoraMix.Core.Models;

namespace NexoraMix.Audio.Playback;

public sealed class DeckChannel : ISampleProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly AudioClock _clock;
    private AudioFileReader? _reader;
    private TempoSampleProvider? _tempo;
    private ThreeBandEqSampleProvider? _eq;
    private DeckFxSampleProvider? _fx;
    private bool _isPlaying;
    private long _scheduledStartFrame = -1;
    private bool _loopEnabled;
    private double _loopStartSeconds;
    private double _loopEndSeconds;
    private double _deckGain = 0.9d;
    private double _crossfadeGain = 1d;
    private double _meterPeak;
    private DeckState _state = DeckState.Empty;
    private SyncState _syncState = SyncState.Off;
    private double _pipelineStartSeconds;
    private readonly CircularAudioBuffer _cueBuffer;

    public DeckChannel(DeckId id, WaveFormat waveFormat, AudioClock clock)
    {
        Id = id;
        WaveFormat = waveFormat;
        _clock = clock;
        _cueBuffer = new CircularAudioBuffer(waveFormat.SampleRate * waveFormat.Channels * 2);
    }

    public DeckId Id { get; }
    public WaveFormat WaveFormat { get; }
    public AudioTrack? Track { get; private set; }

    public DeckState State
    {
        get { lock (_gate) return _state; }
    }

    public SyncState SyncState
    {
        get { lock (_gate) return _syncState; }
    }

    public bool IsPlaying
    {
        get { lock (_gate) return _isPlaying; }
    }

    public bool IsStartScheduled
    {
        get { lock (_gate) return _scheduledStartFrame >= 0; }
    }

    public long ScheduledStartFrame
    {
        get { lock (_gate) return _scheduledStartFrame; }
    }

    public bool IsLoopEnabled
    {
        get { lock (_gate) return _loopEnabled; }
    }

    public double PositionSeconds
    {
        get
        {
            lock (_gate)
            {
                if (_tempo is null) return _pipelineStartSeconds;
                return _pipelineStartSeconds + _tempo.SourceFramesConsumed / (double)WaveFormat.SampleRate;
            }
        }
    }

    public double DurationSeconds
    {
        get { lock (_gate) return _reader?.TotalTime.TotalSeconds ?? Track?.DurationSeconds ?? 0; }
    }

    public double TempoRatio => _tempo?.PlaybackRate ?? 1d;
    public double EffectiveBpm => (Track?.Bpm ?? 0) * TempoRatio;
    public double MeterPeak => Volatile.Read(ref _meterPeak);
    public double LowGainDb => _eq?.LowGainDb ?? 0;
    public double MidGainDb => _eq?.MidGainDb ?? 0;
    public double HighGainDb => _eq?.HighGainDb ?? 0;
    public double Filter => _fx?.Filter ?? 0;
    public double EchoMix => _fx?.EchoMix ?? 0;
    public double BitCrush => _fx?.BitCrush ?? 0;
    public double Saturation => _fx?.Saturation ?? 0;
    public double Gate => _fx?.Gate ?? 0;
    public double Compressor => _fx?.Compressor ?? 0;
    public double Roll => _fx?.Roll ?? 0;
    public double Brake => _fx?.Brake ?? 0;

    public void Load(AudioTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!track.CanLoadToDeck)
            throw new InvalidOperationException("La traccia deve essere collegata a un file audio locale valido.");

        lock (_gate)
        {
            _state = DeckState.Loading;
            DisposeReaderUnsafe();
            Track = track;
            _reader = new AudioFileReader(track.FilePath!);
            RebuildPipelineUnsafe();
            SeekUnsafe(track.CueInSeconds);
            _isPlaying = false;
            _scheduledStartFrame = -1;
            _loopEnabled = false;
            _syncState = SyncState.Off;
            _state = DeckState.Ready;
        }
    }

    public void Unload()
    {
        lock (_gate)
        {
            _state = DeckState.Stopping;
            DisposeReaderUnsafe();
            _loopEnabled = false;
            _syncState = SyncState.Off;
            Volatile.Write(ref _meterPeak, 0d);
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_reader is null) return;
            if (_reader.CurrentTime.TotalSeconds >= _reader.TotalTime.TotalSeconds - 0.02d)
                SeekUnsafe(Track?.CueInSeconds ?? 0);
            _scheduledStartFrame = -1;
            _isPlaying = true;
            _state = _syncState is SyncState.Synchronized or SyncState.Aligning
                ? DeckState.Synchronized
                : DeckState.Playing;
        }
    }

    public void ArmStart(long masterFrame, double sourcePositionSeconds)
    {
        lock (_gate)
        {
            if (_reader is null) throw new InvalidOperationException("Carica prima una traccia nel deck.");
            SeekUnsafe(sourcePositionSeconds);
            _scheduledStartFrame = Math.Max(_clock.FramePosition, masterFrame);
            _isPlaying = false;
            _syncState = SyncState.Armed;
            _state = DeckState.SyncArmed;
        }
    }

    public void CancelScheduledStart()
    {
        lock (_gate)
        {
            _scheduledStartFrame = -1;
            _syncState = SyncState.Off;
            if (_reader is not null && !_isPlaying) _state = DeckState.Ready;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _scheduledStartFrame = -1;
            _isPlaying = false;
            if (_reader is not null) _state = DeckState.Paused;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _state = DeckState.Stopping;
            _scheduledStartFrame = -1;
            _isPlaying = false;
            _loopEnabled = false;
            _syncState = SyncState.Off;
            if (_reader is not null) SeekUnsafe(Track?.CueInSeconds ?? 0);
            _state = _reader is null ? DeckState.Empty : DeckState.Ready;
        }
    }

    public void Seek(double seconds)
    {
        lock (_gate) SeekUnsafe(seconds);
    }

    public void SetTempoRatio(double ratio)
    {
        var tempo = _tempo;
        if (tempo is not null) tempo.PlaybackRate = ratio;
    }

    public void SetDeckGain(double gain) => Volatile.Write(ref _deckGain, Math.Clamp(gain, 0d, 1.25d));
    public void SetCrossfadeGain(double gain) => Volatile.Write(ref _crossfadeGain, Math.Clamp(gain, 0d, 1d));

    public int ReadCue(float[] buffer, int offset, int count) => _cueBuffer.Read(buffer, offset, count);

    public void SetEq(double lowGainDb, double midGainDb, double highGainDb)
    {
        var eq = _eq;
        if (eq is null) return;
        eq.LowGainDb = lowGainDb;
        eq.MidGainDb = midGainDb;
        eq.HighGainDb = highGainDb;
    }

    public void SetFilter(double value)
    {
        var fx = _fx;
        if (fx is not null) fx.Filter = value;
    }

    public void SetEchoMix(double value)
    {
        var fx = _fx;
        if (fx is not null) fx.EchoMix = value;
    }

    public void SetBitCrush(double value)
    {
        var fx = _fx;
        if (fx is not null) fx.BitCrush = value;
    }

    public void SetSaturation(double value)
    {
        var fx = _fx;
        if (fx is not null) fx.Saturation = value;
    }

    public void SetGate(double value)
    {
        var fx = _fx;
        if (fx is not null) fx.Gate = value;
    }

    public void SetCompressor(double value)
    {
        var fx = _fx;
        if (fx is not null) fx.Compressor = value;
    }

    public void SetRoll(double value)
    {
        var fx = _fx;
        if (fx is not null) fx.Roll = value;
    }

    public void SetBrake(double value)
    {
        var fx = _fx;
        if (fx is not null) fx.Brake = value;
    }

    public void EnableLoop(double startSeconds, double endSeconds)
    {
        lock (_gate)
        {
            if (_reader is null) return;
            var duration = _reader.TotalTime.TotalSeconds;
            _loopStartSeconds = Math.Clamp(startSeconds, 0, Math.Max(0, duration - 0.01d));
            _loopEndSeconds = Math.Clamp(endSeconds, _loopStartSeconds + 0.01d, duration);
            _loopEnabled = _loopEndSeconds > _loopStartSeconds;
            if (_loopEnabled && _isPlaying) _state = DeckState.Looping;
        }
    }

    public void DisableLoop()
    {
        lock (_gate)
        {
            _loopEnabled = false;
            if (_isPlaying) _state = DeckState.Playing;
        }
    }

    public void SetSyncState(SyncState syncState)
    {
        lock (_gate)
        {
            _syncState = syncState;
            if (!_isPlaying) return;
            _state = syncState is SyncState.Synchronized or SyncState.Aligning
                ? DeckState.Synchronized
                : DeckState.Playing;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);

        lock (_gate)
        {
            if (_fx is null || _reader is null) return count;

            var channels = WaveFormat.Channels;
            var requestedFrames = count / channels;
            var startFrameOffset = 0;

            if (!_isPlaying)
            {
                if (_scheduledStartFrame < 0) return count;

                var currentMasterFrame = _clock.FramePosition;
                if (_scheduledStartFrame >= currentMasterFrame + requestedFrames) return count;

                startFrameOffset = (int)Math.Max(0, _scheduledStartFrame - currentMasterFrame);
                _scheduledStartFrame = -1;
                _isPlaying = true;
                _syncState = SyncState.Aligning;
                _state = DeckState.Synchronized;
            }

            var startSampleOffset = startFrameOffset * channels;
            var samplesRequested = count - startSampleOffset;
            var read = _fx.Read(buffer, offset + startSampleOffset, samplesRequested);
            var deckGain = Volatile.Read(ref _deckGain);
            _cueBuffer.Write(buffer, offset + startSampleOffset, read, (float)deckGain);
            var gain = deckGain * Volatile.Read(ref _crossfadeGain);
            double peak = 0;

            for (var i = 0; i < read; i++)
            {
                var index = offset + startSampleOffset + i;
                buffer[index] = (float)(buffer[index] * gain);
                peak = Math.Max(peak, Math.Abs(buffer[index]));
            }
            Volatile.Write(ref _meterPeak, peak);

            var logicalPosition = _pipelineStartSeconds + (_tempo?.SourceFramesConsumed ?? 0) / (double)WaveFormat.SampleRate;
            if (_loopEnabled && logicalPosition >= _loopEndSeconds)
            {
                SeekUnsafe(_loopStartSeconds);
                _state = DeckState.Looping;
            }
            else if (read < samplesRequested)
            {
                _isPlaying = false;
                _state = DeckState.Ready;
            }

            return count;
        }
    }

    private void SeekUnsafe(double seconds)
    {
        if (_reader is null) return;
        var tempoRatio = _tempo?.PlaybackRate ?? 1d;
        var lowGain = _eq?.LowGainDb ?? 0d;
        var midGain = _eq?.MidGainDb ?? 0d;
        var highGain = _eq?.HighGainDb ?? 0d;
        var filter = _fx?.Filter ?? 0d;
        var echo = _fx?.EchoMix ?? 0d;
        var bitCrush = _fx?.BitCrush ?? 0d;
        var saturation = _fx?.Saturation ?? 0d;
        var gate = _fx?.Gate ?? 0d;
        var compressor = _fx?.Compressor ?? 0d;
        var roll = _fx?.Roll ?? 0d;
        var brake = _fx?.Brake ?? 0d;
        var safe = Math.Clamp(seconds, 0, Math.Max(0, _reader.TotalTime.TotalSeconds - 0.01d));
        _reader.CurrentTime = TimeSpan.FromSeconds(safe);
        _pipelineStartSeconds = safe;
        RebuildPipelineUnsafe();
        if (_tempo is not null) _tempo.PlaybackRate = tempoRatio;
        if (_eq is not null)
        {
            _eq.LowGainDb = lowGain;
            _eq.MidGainDb = midGain;
            _eq.HighGainDb = highGain;
        }
        if (_fx is not null)
        {
            _fx.Filter = filter;
            _fx.EchoMix = echo;
            _fx.BitCrush = bitCrush;
            _fx.Saturation = saturation;
            _fx.Gate = gate;
            _fx.Compressor = compressor;
            _fx.Roll = roll;
            _fx.Brake = brake;
        }
    }

    private void RebuildPipelineUnsafe()
    {
        if (_reader is null) return;
        var stereo = StereoSampleProviderFactory.Create(_reader, WaveFormat.SampleRate);
        _tempo = new TempoSampleProvider(stereo);
        _eq = new ThreeBandEqSampleProvider(_tempo);
        _fx = new DeckFxSampleProvider(_eq);
    }

    private void DisposeReaderUnsafe()
    {
        _reader?.Dispose();
        _reader = null;
        _tempo = null;
        _eq = null;
        _fx = null;
        Track = null;
        _pipelineStartSeconds = 0;
        _isPlaying = false;
        _scheduledStartFrame = -1;
        _state = DeckState.Empty;
    }

    public void Dispose()
    {
        lock (_gate) DisposeReaderUnsafe();
        GC.SuppressFinalize(this);
    }
}
