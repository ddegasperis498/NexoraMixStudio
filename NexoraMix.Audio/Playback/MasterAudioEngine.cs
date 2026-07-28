using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NexoraMix.Audio.Sync;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;

namespace NexoraMix.Audio.Playback;

public sealed class MasterAudioEngine : IDisposable
{
    private readonly object _outputGate = new();
    private readonly AudioClock _clock = new();
    private readonly Dictionary<DeckId, DeckChannel> _decks;
    private readonly Dictionary<DeckId, DeckSide> _deckSides;
    private readonly MixingSampleProvider _mixer;
    private readonly VolumeSampleProvider _masterGain;
    private readonly MasterLimiterSampleProvider _limiter;
    private readonly RecordingSampleProvider _recorder;
    private readonly AudioTapSampleProvider _masterTap;
    private readonly CueMixSampleProvider _cueMix;
    private readonly ClockedSampleProvider _clockedOutput;
    private readonly SamplerEngine _sampler;
    private readonly Dictionary<DeckId, double> _lastPhaseError = new();
    private readonly Dictionary<DeckId, double> _lastAppliedSyncRatio = new();
    private WaveOutEvent? _output;
    private WaveOutEvent? _cueOutput;
    private bool _disposed;
    private double _crossfader;
    private int _masterDeviceNumber = -1;
    private int _cueDeviceNumber = -1;

    public MasterAudioEngine(int sampleRate = 44_100)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        _decks = Enum.GetValues<DeckId>()
            .ToDictionary(id => id, id => new DeckChannel(id, WaveFormat, _clock));
        _deckSides = new Dictionary<DeckId, DeckSide>
        {
            [DeckId.A] = DeckSide.Left,
            [DeckId.B] = DeckSide.Right,
            [DeckId.C] = DeckSide.Left,
            [DeckId.D] = DeckSide.Right
        };

        _mixer = new MixingSampleProvider(WaveFormat) { ReadFully = true };
        foreach (var deck in _decks.Values) _mixer.AddMixerInput(deck);
        _sampler = new SamplerEngine(WaveFormat, _clock);
        _mixer.AddMixerInput(_sampler);

        _masterGain = new VolumeSampleProvider(_mixer) { Volume = 0.72f };
        _limiter = new MasterLimiterSampleProvider(_masterGain, -1d);
        _recorder = new RecordingSampleProvider(_limiter);
        _masterTap = new AudioTapSampleProvider(_recorder);
        _clockedOutput = new ClockedSampleProvider(_masterTap, _clock);
        _cueMix = new CueMixSampleProvider(WaveFormat, _decks, _masterTap);
        SetCrossfader(0);
    }

    public WaveFormat WaveFormat { get; }
    public int SampleRate => WaveFormat.SampleRate;
    public long ClockFrame => _clock.FramePosition;
    public double ClockSeconds => ClockFrame / (double)SampleRate;
    public IReadOnlyDictionary<DeckId, DeckChannel> Decks => _decks;
    public DeckChannel DeckA => _decks[DeckId.A];
    public DeckChannel DeckB => _decks[DeckId.B];
    public DeckChannel DeckC => _decks[DeckId.C];
    public DeckChannel DeckD => _decks[DeckId.D];
    public double Crossfader => _crossfader;
    public double PeakDbFs => _limiter.PeakDbFs;
    public long ClippingCount => _limiter.ClippingCount;
    public bool IsRecording => _recorder.IsRecording;
    public string? RecordingPath => _recorder.CurrentPath;
    public Exception? LastOutputError { get; private set; }
    public Exception? LastCueOutputError { get; private set; }
    public SamplerEngine Sampler => _sampler;
    public double CueVolume { get => _cueMix.Volume; set => _cueMix.Volume = value; }
    public bool MasterCueEnabled { get => _cueMix.MasterCueEnabled; set => _cueMix.MasterCueEnabled = value; }

    public DeckChannel GetDeck(DeckId id) => _decks[id];

    public void Load(DeckId id, AudioTrack track) => GetDeck(id).Load(track);
    public void Unload(DeckId id) => GetDeck(id).Unload();

    public void Play(DeckId id)
    {
        ThrowIfDisposed();
        EnsureOutputStarted();
        EnsureCueOutputStarted();
        GetDeck(id).Play();
    }

    public void Pause(DeckId id) => GetDeck(id).Pause();
    public void Stop(DeckId id) => GetDeck(id).Stop();

    public void StopAll()
    {
        foreach (var deck in _decks.Values) deck.Stop();
        _sampler.StopAll();
    }

    public void SetDeckSide(DeckId id, DeckSide side)
    {
        _deckSides[id] = side;
        ApplyCrossfaderGains();
    }

    public DeckSide GetDeckSide(DeckId id) => _deckSides[id];

    public void SetCrossfader(double position)
    {
        _crossfader = Math.Clamp(position, -1d, 1d);
        ApplyCrossfaderGains();
    }

    private void ApplyCrossfaderGains()
    {
        var gains = SyncMath.EqualPowerCrossfade(_crossfader);
        foreach (var pair in _decks)
        {
            var gain = _deckSides[pair.Key] switch
            {
                DeckSide.Left => gains.Left,
                DeckSide.Right => gains.Right,
                DeckSide.Thru => 1d,
                _ => 1d
            };
            pair.Value.SetCrossfadeGain(gain);
        }
    }

    public void SetMasterGain(double gain) => _masterGain.Volume = (float)Math.Clamp(gain, 0d, 1d);
    public void SetDeckCue(DeckId id, bool enabled) => _cueMix.SetDeckCue(id, enabled);
    public bool GetDeckCue(DeckId id) => _cueMix.GetDeckCue(id);
    public void SetCueVolume(double volume) => CueVolume = volume;

    public void ConfigureOutputDevices(int masterDeviceNumber, int cueDeviceNumber)
    {
        lock (_outputGate)
        {
            _masterDeviceNumber = masterDeviceNumber;
            _cueDeviceNumber = cueDeviceNumber;
            var wasRunning = _output is not null || _cueOutput is not null;
            DisposeOutputUnsafe();
            if (wasRunning)
            {
                EnsureOutputStarted();
                EnsureCueOutputStarted();
            }
        }
    }

    public void StartRecording(string path)
    {
        ThrowIfDisposed();
        EnsureOutputStarted();
        EnsureCueOutputStarted();
        _recorder.Start(path);
    }

    public string? StopRecording() => _recorder.Stop();

    public SyncArmResult ArmBeatSync(
        DeckId followerId,
        DeckId masterId,
        bool alignToNextBar = true,
        double maximumTempoPercent = 16d,
        int additionalBars = 0,
        bool alignToPhrase = false)
    {
        ThrowIfDisposed();
        if (followerId == masterId) return SyncArmResult.Failed("Il deck master non può sincronizzarsi con se stesso.");

        var master = GetDeck(masterId);
        var follower = GetDeck(followerId);
        var masterTrack = master.Track;
        var followerTrack = follower.Track;

        if (masterTrack is null || followerTrack is null)
            return SyncArmResult.Failed("Carica file locali nel master e nel follower.");
        if (!master.IsPlaying)
            return SyncArmResult.Failed("Avvia prima il deck master.");
        if (masterTrack.Bpm <= 0 || followerTrack.Bpm <= 0)
            return SyncArmResult.Failed("Analizza BPM e beat-grid di entrambi i brani.");

        var phaseReliable = masterTrack.PhaseConfidence >= 0.03d && followerTrack.PhaseConfidence >= 0.03d;
        var targetRatio = SyncMath.CalculateTempoRatio(master.EffectiveBpm, followerTrack.Bpm);
        if (!SyncMath.IsTempoRatioSupported(targetRatio, maximumTempoPercent))
        {
            return SyncArmResult.Failed(
                $"Differenza BPM eccessiva ({(targetRatio - 1d) * 100d:+0.0;-0.0;0.0}%). Limite ±{maximumTempoPercent:0}%.");
        }

        EnsureOutputStarted();
        follower.SetTempoRatio(targetRatio);

        var boundaryBeats = alignToPhrase
            ? BeatGridMath.PhraseLengthBeats(masterTrack.BeatsPerBar, masterTrack.PhraseLengthBars)
            : alignToNextBar ? Math.Max(1, masterTrack.BeatsPerBar) : 1;
        var sourceDelay = BeatGridMath.SecondsUntilNextBoundary(
            master.PositionSeconds,
            masterTrack.BeatOffsetSeconds,
            masterTrack.Bpm,
            boundaryBeats);
        sourceDelay += Math.Max(0, additionalBars) * Math.Max(1, masterTrack.BeatsPerBar) * 60d / master.EffectiveBpm;
        var outputDelay = sourceDelay / Math.Max(0.01d, master.TempoRatio);
        var scheduledFrame = ClockFrame + Math.Max(1, (long)Math.Ceiling(outputDelay * SampleRate));

        var followerStart = BeatGridMath.QuantizeForward(
            Math.Max(followerTrack.CueInSeconds, followerTrack.BeatOffsetSeconds),
            followerTrack.BeatOffsetSeconds,
            followerTrack.Bpm,
            boundaryBeats);

        if (followerStart >= follower.DurationSeconds - 0.1d)
            followerStart = Math.Max(0, followerTrack.BeatOffsetSeconds);

        follower.ArmStart(scheduledFrame, followerStart);
        _lastPhaseError[followerId] = 0;
        _lastAppliedSyncRatio[followerId] = targetRatio;

        return new SyncArmResult(
            true,
            phaseReliable
                ? $"Sync BPM/fase armato sulla prossima {(alignToPhrase ? "frase" : alignToNextBar ? "misura" : "battuta")}"
                : $"Sync BPM armato sulla prossima {(alignToPhrase ? "frase" : alignToNextBar ? "misura" : "battuta")} · fase stimata",
            scheduledFrame,
            targetRatio,
            followerStart);
    }

    public SyncArmResult EngageLiveBeatSync(
        DeckId followerId,
        DeckId masterId,
        double maximumTempoPercent = 16d)
    {
        ThrowIfDisposed();
        if (followerId == masterId) return SyncArmResult.Failed("Il deck master non può sincronizzarsi con se stesso.");

        var master = GetDeck(masterId);
        var follower = GetDeck(followerId);
        var masterTrack = master.Track;
        var followerTrack = follower.Track;

        if (masterTrack is null || followerTrack is null)
            return SyncArmResult.Failed("Carica file locali nel master e nel follower.");
        if (!master.IsPlaying || !follower.IsPlaying)
            return SyncArmResult.Failed("Il Sync live richiede master e follower già in riproduzione.");
        if (masterTrack.Bpm <= 0 || followerTrack.Bpm <= 0)
            return SyncArmResult.Failed("Analizza BPM e beat-grid di entrambi i brani.");

        var targetRatio = SyncMath.CalculateTempoRatio(master.EffectiveBpm, followerTrack.Bpm);
        if (!SyncMath.IsTempoRatioSupported(targetRatio, maximumTempoPercent))
        {
            return SyncArmResult.Failed(
                $"Differenza BPM eccessiva ({(targetRatio - 1d) * 100d:+0.0;-0.0;0.0}%). Limite ±{maximumTempoPercent:0}%.");
        }

        follower.CancelScheduledStart();
        follower.SetTempoRatio(targetRatio);
        follower.SetSyncState(SyncState.Aligning);
        _lastAppliedSyncRatio[followerId] = targetRatio;

        var phaseError = BeatGridMath.PhaseErrorMilliseconds(
            master.PositionSeconds,
            masterTrack.BeatOffsetSeconds,
            masterTrack.Bpm,
            master.TempoRatio,
            follower.PositionSeconds,
            followerTrack.BeatOffsetSeconds,
            followerTrack.Bpm,
            targetRatio);
        _lastPhaseError[followerId] = phaseError;

        return new SyncArmResult(
            true,
            Math.Abs(phaseError) <= 35d
                ? "Sync live attivo: BPM e battute allineati"
                : "Sync live attivo: BPM agganciato, correzione battuta in corso",
            ClockFrame,
            targetRatio,
            follower.PositionSeconds);
    }

    public SyncSnapshot UpdateSync(DeckId followerId, DeckId masterId, double maximumTempoPercent = 16d)
    {
        var master = GetDeck(masterId);
        var follower = GetDeck(followerId);
        var masterTrack = master.Track;
        var followerTrack = follower.Track;

        if (masterTrack is null || followerTrack is null || masterTrack.Bpm <= 0 || followerTrack.Bpm <= 0)
            return SyncSnapshot.Off;

        var targetRatio = SyncMath.CalculateTempoRatio(master.EffectiveBpm, followerTrack.Bpm);
        if (!SyncMath.IsTempoRatioSupported(targetRatio, maximumTempoPercent))
        {
            follower.SetSyncState(SyncState.Failed);
            return new SyncSnapshot(
                SyncState.Failed,
                targetRatio,
                follower.TempoRatio,
                0,
                0,
                "Differenza BPM oltre il limite configurato");
        }

        if (follower.IsStartScheduled)
        {
            follower.SetSyncState(SyncState.Armed);
            return new SyncSnapshot(
                SyncState.Armed,
                targetRatio,
                follower.TempoRatio,
                0,
                0,
                "In attesa della battuta/misura programmata");
        }

        if (!master.IsPlaying || !follower.IsPlaying)
        {
            return new SyncSnapshot(
                SyncState.Off,
                targetRatio,
                follower.TempoRatio,
                0,
                0,
                "Avvia master e follower per mantenere il Sync");
        }

        var phaseReliable = masterTrack.PhaseConfidence >= 0.03d && followerTrack.PhaseConfidence >= 0.03d;
        if (!phaseReliable)
        {
            follower.SetTempoRatio(targetRatio);
            follower.SetSyncState(SyncState.Synchronized);
            return new SyncSnapshot(
                SyncState.Synchronized,
                targetRatio,
                targetRatio,
                0,
                0,
                "BPM sincronizzati · imposta 1° BEAT per la fase");
        }

        var phaseError = BeatGridMath.PhaseErrorMilliseconds(
            master.PositionSeconds,
            masterTrack.BeatOffsetSeconds,
            masterTrack.Bpm,
            master.TempoRatio,
            follower.PositionSeconds,
            followerTrack.BeatOffsetSeconds,
            followerTrack.Bpm,
            follower.TempoRatio);

        var previous = _lastPhaseError.TryGetValue(followerId, out var value) ? value : phaseError;
        var drift = phaseError - previous;
        _lastPhaseError[followerId] = phaseError;

        var beatMilliseconds = 60_000d / Math.Max(1d, master.EffectiveBpm);
        var correction = Math.Abs(phaseError) < 8d
            ? 0d
            : Math.Clamp(-phaseError / beatMilliseconds * 0.010d, -0.0035d, 0.0035d);
        var requestedRatio = Math.Clamp(targetRatio * (1d + correction), 0.75d, 1.25d);
        var previousRatio = _lastAppliedSyncRatio.TryGetValue(followerId, out var applied)
            ? applied
            : follower.TempoRatio;
        var appliedRatio = SlewLimit(previousRatio, requestedRatio, 0.0009d);
        _lastAppliedSyncRatio[followerId] = appliedRatio;
        follower.SetTempoRatio(appliedRatio);

        var state = Math.Abs(phaseError) <= 35d ? SyncState.Synchronized : SyncState.OutOfPhase;
        follower.SetSyncState(state);
        return new SyncSnapshot(
            state,
            targetRatio,
            appliedRatio,
            phaseError,
            drift,
            state == SyncState.Synchronized ? "Sync attivo" : "Correzione fase in corso");
    }

    public void DisableSync(DeckId id)
    {
        var deck = GetDeck(id);
        deck.CancelScheduledStart();
        deck.SetTempoRatio(1d);
        deck.SetSyncState(SyncState.Off);
        _lastPhaseError.Remove(id);
        _lastAppliedSyncRatio.Remove(id);
    }

    public void ResetStatistics() => _limiter.ResetStatistics();

    public int RenderOffline(float[] buffer, int offset, int count)
    {
        ThrowIfDisposed();
        return _clockedOutput.Read(buffer, offset, count);
    }

    private void EnsureOutputStarted()
    {
        lock (_outputGate)
        {
            ThrowIfDisposed();
            LastOutputError = null;

            if (_output is null)
            {
                _output = new WaveOutEvent
                {
                    DeviceNumber = _masterDeviceNumber,
                    DesiredLatency = 80,
                    NumberOfBuffers = 3
                };
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(_clockedOutput.ToWaveProvider());
            }

            if (_output.PlaybackState != PlaybackState.Playing) _output.Play();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null) LastOutputError = e.Exception;
    }

    private void EnsureCueOutputStarted()
    {
        lock (_outputGate)
        {
            ThrowIfDisposed();
            LastCueOutputError = null;

            if (_cueOutput is null)
            {
                _cueOutput = new WaveOutEvent
                {
                    DeviceNumber = _cueDeviceNumber,
                    DesiredLatency = 80,
                    NumberOfBuffers = 3
                };
                _cueOutput.PlaybackStopped += OnCuePlaybackStopped;
                _cueOutput.Init(_cueMix.ToWaveProvider());
            }

            if (_cueOutput.PlaybackState != PlaybackState.Playing) _cueOutput.Play();
        }
    }

    private void OnCuePlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null) LastCueOutputError = e.Exception;
    }

    private void DisposeOutputUnsafe()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        if (_cueOutput is not null)
        {
            _cueOutput.PlaybackStopped -= OnCuePlaybackStopped;
            _cueOutput.Stop();
            _cueOutput.Dispose();
            _cueOutput = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MasterAudioEngine));
    }

    private static double SlewLimit(double current, double target, double maximumStep)
    {
        if (maximumStep <= 0d) return target;
        var delta = target - current;
        return Math.Abs(delta) <= maximumStep
            ? target
            : current + Math.Sign(delta) * maximumStep;
    }

    public void Dispose()
    {
        lock (_outputGate)
        {
            if (_disposed) return;
            _disposed = true;
            _recorder.Dispose();
            foreach (var deck in _decks.Values) deck.Dispose();
            DisposeOutputUnsafe();
        }
        GC.SuppressFinalize(this);
    }
}
