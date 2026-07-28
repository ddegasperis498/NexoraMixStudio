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
    private readonly MixingSampleProvider _mixer;
    private readonly VolumeSampleProvider _masterGain;
    private readonly MasterLimiterSampleProvider _limiter;
    private readonly ClockedSampleProvider _clockedOutput;
    private readonly Dictionary<DeckId, double> _lastPhaseError = new();
    private WaveOutEvent? _output;
    private bool _disposed;
    private double _crossfader;

    public MasterAudioEngine(int sampleRate = 44_100)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        DeckA = new DeckChannel(DeckId.A, WaveFormat, _clock);
        DeckB = new DeckChannel(DeckId.B, WaveFormat, _clock);

        _mixer = new MixingSampleProvider(WaveFormat) { ReadFully = true };
        _mixer.AddMixerInput(DeckA);
        _mixer.AddMixerInput(DeckB);
        _masterGain = new VolumeSampleProvider(_mixer) { Volume = 0.82f };
        _limiter = new MasterLimiterSampleProvider(_masterGain, -1d);
        _clockedOutput = new ClockedSampleProvider(_limiter, _clock);
        SetCrossfader(0);
    }

    public WaveFormat WaveFormat { get; }
    public int SampleRate => WaveFormat.SampleRate;
    public long ClockFrame => _clock.FramePosition;
    public double ClockSeconds => ClockFrame / (double)SampleRate;
    public DeckChannel DeckA { get; }
    public DeckChannel DeckB { get; }
    public double Crossfader => _crossfader;
    public double PeakDbFs => _limiter.PeakDbFs;
    public long ClippingCount => _limiter.ClippingCount;
    public Exception? LastOutputError { get; private set; }

    public DeckChannel GetDeck(DeckId id) => id == DeckId.A ? DeckA : DeckB;

    public void Load(DeckId id, AudioTrack track) => GetDeck(id).Load(track);
    public void Unload(DeckId id) => GetDeck(id).Unload();

    public void Play(DeckId id)
    {
        ThrowIfDisposed();
        EnsureOutputStarted();
        GetDeck(id).Play();
    }

    public void Pause(DeckId id) => GetDeck(id).Pause();
    public void Stop(DeckId id) => GetDeck(id).Stop();

    public void StopAll()
    {
        DeckA.Stop();
        DeckB.Stop();
    }

    public void SetCrossfader(double position)
    {
        _crossfader = Math.Clamp(position, -1d, 1d);
        var gains = SyncMath.EqualPowerCrossfade(_crossfader);
        DeckA.SetCrossfadeGain(gains.Left);
        DeckB.SetCrossfadeGain(gains.Right);
    }

    public void SetMasterGain(double gain) => _masterGain.Volume = (float)Math.Clamp(gain, 0d, 1d);

    public SyncArmResult ArmBeatSync(DeckId followerId, DeckId masterId, bool alignToNextBar = true, double maximumTempoPercent = 16d)
    {
        ThrowIfDisposed();
        if (followerId == masterId) return SyncArmResult.Failed("Il deck master non può sincronizzarsi con se stesso.");

        var master = GetDeck(masterId);
        var follower = GetDeck(followerId);
        var masterTrack = master.Track;
        var followerTrack = follower.Track;

        if (masterTrack is null || followerTrack is null)
            return SyncArmResult.Failed("Carica una traccia locale in entrambi i deck.");
        if (!master.IsPlaying)
            return SyncArmResult.Failed("Avvia prima il deck master.");
        if (masterTrack.Bpm <= 0 || followerTrack.Bpm <= 0)
            return SyncArmResult.Failed("Analizza BPM e beat-grid di entrambi i brani.");
        var phaseReliable = masterTrack.PhaseConfidence >= 0.03d && followerTrack.PhaseConfidence >= 0.03d;

        var targetRatio = SyncMath.CalculateTempoRatio(master.EffectiveBpm, followerTrack.Bpm);
        if (!SyncMath.IsTempoRatioSupported(targetRatio, maximumTempoPercent))
            return SyncArmResult.Failed($"Differenza BPM eccessiva ({(targetRatio - 1d) * 100d:+0.0;-0.0;0.0}%). Limite ±{maximumTempoPercent:0}%.");

        EnsureOutputStarted();
        follower.SetTempoRatio(targetRatio);

        var boundaryBeats = alignToNextBar ? Math.Max(1, masterTrack.BeatsPerBar) : 1;
        var sourceDelay = BeatGridMath.SecondsUntilNextBoundary(
            master.PositionSeconds,
            masterTrack.BeatOffsetSeconds,
            masterTrack.Bpm,
            boundaryBeats);
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

        return new SyncArmResult(
            true,
            phaseReliable
                ? $"Sync BPM e fase armato sulla prossima {(alignToNextBar ? "misura" : "battuta")}"
                : $"Sync BPM armato sulla prossima {(alignToNextBar ? "misura" : "battuta")} · fase stimata",
            scheduledFrame,
            targetRatio,
            followerStart);
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
                "In attesa della prossima misura");
        }

        if (!master.IsPlaying || !follower.IsPlaying)
            return new SyncSnapshot(
                SyncState.Off,
                targetRatio,
                follower.TempoRatio,
                0,
                0,
                "Avvia entrambi i deck per mantenere il sync");

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
                "BPM sincronizzati · imposta 1° BEAT per allineare anche la fase");
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
        var correction = Math.Abs(phaseError) < 4d
            ? 0d
            : Math.Clamp(-phaseError / beatMilliseconds * 0.035d, -0.012d, 0.012d);
        var appliedRatio = Math.Clamp(targetRatio * (1d + correction), 0.84d, 1.16d);
        follower.SetTempoRatio(appliedRatio);

        var state = Math.Abs(phaseError) <= 35d ? SyncState.Synchronized : SyncState.OutOfPhase;
        follower.SetSyncState(state);
        var message = state == SyncState.Synchronized
            ? "Sync attivo"
            : "Correzione fase in corso";

        return new SyncSnapshot(
            state,
            targetRatio,
            appliedRatio,
            phaseError,
            drift,
            message);
    }

    public void DisableSync(DeckId id)
    {
        var deck = GetDeck(id);
        deck.CancelScheduledStart();
        deck.SetTempoRatio(1d);
        deck.SetSyncState(SyncState.Off);
        _lastPhaseError.Remove(id);
    }

    public void ResetStatistics() => _limiter.ResetStatistics();

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

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MasterAudioEngine));
    }

    public void Dispose()
    {
        lock (_outputGate)
        {
            if (_disposed) return;
            _disposed = true;
            DeckA.Dispose();
            DeckB.Dispose();
            if (_output is not null)
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                _output.Stop();
                _output.Dispose();
                _output = null;
            }
        }
        GC.SuppressFinalize(this);
    }
}
