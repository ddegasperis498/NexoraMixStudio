using System.Windows.Input;
using NexoraMix.Audio.Playback;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;

namespace NexoraMix.App.ViewModels;

public sealed class DeckViewModel : ObservableObject, IDisposable
{
    private readonly MasterAudioEngine _engine;
    private readonly DeckChannel _channel;
    private readonly Action<DeckViewModel> _requestMaster;
    private readonly Action<DeckViewModel> _requestSync;
    private readonly Func<DeckViewModel, Task> _requestExternalToggle;
    private readonly Func<DeckViewModel, Task> _requestExternalStop;
    private AudioTrack? _track;
    private double _positionSeconds;
    private double _volume = 0.9d;
    private bool _isPlaying;
    private int _loopBeats = 8;
    private double _tempoPercent;
    private double _lowEq;
    private double _midEq;
    private double _highEq;
    private double _filter;
    private double _echoMix;
    private double _bitCrush;
    private double _saturation;
    private double _gate;
    private double _compressor;
    private double _roll;
    private double _brake;
    private bool _isMaster;
    private bool _syncEnabled;
    private SyncSnapshot _syncSnapshot = SyncSnapshot.Off;
    private double _meterPeak;
    private DeckState _state = DeckState.Empty;
    private DateTimeOffset? _externalPlaybackStartedAtUtc;
    private double _externalPositionAnchor;
    private DateTimeOffset? _lastTapUtc;
    private readonly List<double> _tapIntervalsSeconds = new();
    private int _tapCount;
    private bool _cutAuditionActive;
    private bool _cutLatchedByPlay;
    private bool _cueMonitorEnabled;

    public DeckViewModel(
        string name,
        DeckId id,
        MasterAudioEngine engine,
        Action<DeckViewModel> requestMaster,
        Action<DeckViewModel> requestSync,
        Func<DeckViewModel, Task> requestExternalToggle,
        Func<DeckViewModel, Task> requestExternalStop)
    {
        Name = name;
        Id = id;
        _engine = engine;
        _channel = engine.GetDeck(id);
        _requestMaster = requestMaster;
        _requestSync = requestSync;
        _requestExternalToggle = requestExternalToggle;
        _requestExternalStop = requestExternalStop;

        PlayPauseCommand = new AsyncRelayCommand(TogglePlayPauseAsync, () => Track is not null);
        CueCommand = new RelayCommand(GoToCue, () => HasLocalAudio);
        SetCueCommand = new RelayCommand(SetCueAtCurrent, () => HasLocalAudio);
        LoopCommand = new RelayCommand(ToggleLoop, () => HasLocalAudio && Bpm > 0);
        StopCommand = new AsyncRelayCommand(StopRequestedAsync, () => Track is not null);
        MasterCommand = new RelayCommand(() => _requestMaster(this), () => HasLocalAudio);
        SyncCommand = new RelayCommand(() => _requestSync(this), () => HasLocalAudio && Bpm > 0);
        NudgeGridBackwardCommand = new RelayCommand(() => NudgeBeatGrid(-0.01d), () => HasLocalAudio && Bpm > 0);
        NudgeGridForwardCommand = new RelayCommand(() => NudgeBeatGrid(0.01d), () => HasLocalAudio && Bpm > 0);
        SetFirstBeatCommand = new RelayCommand(SetFirstBeatAtCurrent, () => HasLocalAudio && Bpm > 0);
        HalfBpmCommand = new RelayCommand(() => ScaleBpm(0.5d), () => Track is not null && Bpm >= 80d);
        DoubleBpmCommand = new RelayCommand(() => ScaleBpm(2d), () => Track is not null && Bpm <= 160d && Bpm > 0);
        BpmDownCommand = new RelayCommand(() => ChangeTargetBpm(-0.1d), () => Track is not null);
        BpmUpCommand = new RelayCommand(() => ChangeTargetBpm(0.1d), () => Track is not null);
        ResetTempoCommand = new RelayCommand(ResetTempo, () => HasLocalAudio);
        TapBpmCommand = new RelayCommand(RegisterTapBpm, () => Track is not null);
        ToggleCueMonitorCommand = new RelayCommand(() => CueMonitorEnabled = !CueMonitorEnabled, () => HasLocalAudio);
    }

    public string Name { get; }
    public DeckId Id { get; }
    public AudioTrack? Track { get => _track; private set { if (SetProperty(ref _track, value)) RaiseDeckProperties(); } }
    public double PositionSeconds { get => _positionSeconds; private set { if (SetProperty(ref _positionSeconds, value)) RaiseTransportProperties(); } }
    public double DurationSeconds => Track?.DurationSeconds > 0 ? Track.DurationSeconds : _channel.DurationSeconds;
    public double Bpm => Track?.Bpm ?? 0;
    public double EffectiveBpm => HasLocalAudio ? Bpm * _channel.TempoRatio : Bpm;
    public double TargetBpm
    {
        get => EffectiveBpm;
        set => SetTargetBpm(value);
    }
    public double CueInSeconds => Track?.CueInSeconds ?? 0;
    public float[] Waveform => Track?.Waveform ?? Array.Empty<float>();
    public double BeatOffsetSeconds => Track?.BeatOffsetSeconds ?? 0;
    public int BeatsPerBar => Track?.BeatsPerBar ?? 4;
    public bool HasLocalAudio => Track?.CanLoadToDeck == true;
    public bool IsSpotifyExternal => Track?.IsExternalOnly == true;
    public bool CanUseMixerControls => HasLocalAudio;
    public bool IsPlaying { get => _isPlaying; private set { if (SetProperty(ref _isPlaying, value)) RaisePropertyChanged(nameof(PlayLabel)); } }
    public string PlayLabel => IsSpotifyExternal
        ? IsPlaying ? "PAUSA SPOTIFY" : "PLAY SPOTIFY"
        : IsPlaying ? "PAUSA" : "PLAY";
    public string CutLabel => _cutAuditionActive ? "CUT ON" : "CUT";
    public string CueMonitorLabel => CueMonitorEnabled ? "CUE ON" : "CUE";
    public string TrackTitle => Track?.Title ?? "Trascina o carica una traccia";
    public string Artist => Track?.Artist ?? $"DECK {Name}";
    public string BpmText => Bpm > 0 ? $"{Bpm:0.0}" : "—";
    public string EffectiveBpmText => EffectiveBpm > 0
        ? IsSpotifyExternal ? $"{EffectiveBpm:0.0} BPM DICHIARATI" : $"{EffectiveBpm:0.00} BPM"
        : "BPM —";
    public string PositionText => FormatTime(PositionSeconds);
    public string RemainingText => $"-{FormatTime(Math.Max(0, DurationSeconds - PositionSeconds))}";
    public string LoopLabel => _channel.IsLoopEnabled ? $"LOOP {LoopBeats} ON" : $"LOOP {LoopBeats}";
    public string MasterLabel => IsSpotifyExternal ? "SPOTIFY ESTERNO" : IsMaster ? "MASTER" : "IMPOSTA MASTER";
    public string SyncLabel => SyncEnabled ? "SYNC ON" : "SYNC";
    public string SyncStateText => IsSpotifyExternal
        ? "ESTERNO · NO SYNC"
        : IsMaster ? "CLOCK MASTER" : _syncSnapshot.Message.ToUpperInvariant();
    public string TempoText => IsSpotifyExternal ? "ORIGINALE" : $"{TempoPercent:+0.0;-0.0;0.0}%";
    public string PlaybackModeText => HasLocalAudio
        ? "MIXER INTERNO · SYNC DISPONIBILE"
        : IsSpotifyExternal
            ? "SPOTIFY ESTERNO · AUDIO ORIGINALE · NO CROSSFADER/SYNC"
            : "DECK VUOTO";
    public string StateText => IsSpotifyExternal
        ? IsPlaying ? "SPOTIFY IN RIPRODUZIONE" : "SPOTIFY PRONTO"
        : _state.ToString().ToUpperInvariant();
    public string MeterText => $"{MeterPeak * 100d:0}%";
    public int CurrentBeatNumber
    {
        get
        {
            if (Bpm <= 0 || PositionSeconds < BeatOffsetSeconds) return 0;
            var beatLength = 60d / Bpm;
            return 1 + (int)Math.Floor((PositionSeconds - BeatOffsetSeconds) / beatLength);
        }
    }
    public int CurrentBarNumber => CurrentBeatNumber > 0
        ? 1 + (CurrentBeatNumber - 1) / Math.Max(1, BeatsPerBar)
        : 0;
    public int BeatInBar => CurrentBeatNumber > 0
        ? 1 + (CurrentBeatNumber - 1) % Math.Max(1, BeatsPerBar)
        : 0;
    public int TotalBeatCount => Track?.DetectedBeatCount > 0
        ? Track.DetectedBeatCount
        : Bpm > 0 && DurationSeconds > BeatOffsetSeconds
            ? 1 + (int)Math.Floor((DurationSeconds - BeatOffsetSeconds) / (60d / Bpm))
            : 0;
    public int TotalBarCount => Track?.EstimatedBarCount > 0
        ? Track.EstimatedBarCount
        : TotalBeatCount > 0
            ? (int)Math.Ceiling(TotalBeatCount / (double)Math.Max(1, BeatsPerBar))
            : 0;
    public string BeatCounterText => CurrentBeatNumber > 0
        ? $"MISURA {CurrentBarNumber} · BATTUTA {BeatInBar}/{BeatsPerBar}"
        : "IN ATTESA DELLA PRIMA BATTUTA";
    public string BeatProgressText => TotalBeatCount > 0
        ? $"BEAT {Math.Min(CurrentBeatNumber, TotalBeatCount)}/{TotalBeatCount} · {TotalBarCount} MISURE"
        : "BATTUTE NON ANCORA CALCOLATE";
    public string AnalysisSummaryText => Bpm > 0
        ? $"{Bpm:0.0} BPM · {TotalBeatCount} battute · {TotalBarCount} misure"
        : "Analizza il file o usa TAP BPM";
    public string TapBpmStatus => _tapCount switch
    {
        0 => "TAP BPM",
        1 => "TAP 1…",
        _ => $"TAP {_tapCount} · {Bpm:0.0}"
    };

    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, Math.Clamp(value, 0, 1.1d))) return;
            if (HasLocalAudio) _channel.SetDeckGain(_volume);
        }
    }

    public int LoopBeats
    {
        get => _loopBeats;
        set
        {
            if (SetProperty(ref _loopBeats, Math.Clamp(value, 1, 32))) RaisePropertyChanged(nameof(LoopLabel));
        }
    }

    public double TempoPercent
    {
        get => _tempoPercent;
        set
        {
            var limited = Math.Clamp(value, -25d, 25d);
            if (!SetProperty(ref _tempoPercent, limited)) return;
            if (HasLocalAudio && !SyncEnabled) _channel.SetTempoRatio(1d + _tempoPercent / 100d);
            RaiseTempoProperties();
        }
    }

    public double LowEq
    {
        get => _lowEq;
        set { if (SetProperty(ref _lowEq, Math.Clamp(value, -24d, 12d))) ApplyEq(); }
    }

    public double MidEq
    {
        get => _midEq;
        set { if (SetProperty(ref _midEq, Math.Clamp(value, -24d, 12d))) ApplyEq(); }
    }

    public double HighEq
    {
        get => _highEq;
        set { if (SetProperty(ref _highEq, Math.Clamp(value, -24d, 12d))) ApplyEq(); }
    }

    public double Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, Math.Clamp(value, -1d, 1d))) return;
            if (HasLocalAudio) _channel.SetFilter(_filter);
        }
    }

    public double EchoMix
    {
        get => _echoMix;
        set
        {
            if (!SetProperty(ref _echoMix, Math.Clamp(value, 0d, 1d))) return;
            if (HasLocalAudio) _channel.SetEchoMix(_echoMix);
        }
    }

    public double BitCrush
    {
        get => _bitCrush;
        set
        {
            if (!SetProperty(ref _bitCrush, Math.Clamp(value, 0d, 1d))) return;
            if (HasLocalAudio) _channel.SetBitCrush(_bitCrush);
        }
    }

    public double Saturation
    {
        get => _saturation;
        set
        {
            if (!SetProperty(ref _saturation, Math.Clamp(value, 0d, 1d))) return;
            if (HasLocalAudio) _channel.SetSaturation(_saturation);
        }
    }

    public double Gate
    {
        get => _gate;
        set
        {
            if (!SetProperty(ref _gate, Math.Clamp(value, 0d, 1d))) return;
            if (HasLocalAudio) _channel.SetGate(_gate);
        }
    }

    public double Compressor
    {
        get => _compressor;
        set
        {
            if (!SetProperty(ref _compressor, Math.Clamp(value, 0d, 1d))) return;
            if (HasLocalAudio) _channel.SetCompressor(_compressor);
        }
    }

    public double Roll
    {
        get => _roll;
        set
        {
            if (!SetProperty(ref _roll, Math.Clamp(value, 0d, 1d))) return;
            if (HasLocalAudio) _channel.SetRoll(_roll);
        }
    }

    public double Brake
    {
        get => _brake;
        set
        {
            if (!SetProperty(ref _brake, Math.Clamp(value, 0d, 1d))) return;
            if (HasLocalAudio) _channel.SetBrake(_brake);
        }
    }

    public bool IsMaster { get => _isMaster; private set { if (SetProperty(ref _isMaster, value)) { RaisePropertyChanged(nameof(MasterLabel)); RaisePropertyChanged(nameof(SyncStateText)); } } }
    public bool SyncEnabled { get => _syncEnabled; private set { if (SetProperty(ref _syncEnabled, value)) RaisePropertyChanged(nameof(SyncLabel)); } }
    public double MeterPeak { get => _meterPeak; private set { if (SetProperty(ref _meterPeak, value)) RaisePropertyChanged(nameof(MeterText)); } }
    public bool CueMonitorEnabled
    {
        get => _cueMonitorEnabled;
        set
        {
            if (!SetProperty(ref _cueMonitorEnabled, value)) return;
            _engine.SetDeckCue(Id, _cueMonitorEnabled);
            RaisePropertyChanged(nameof(CueMonitorLabel));
        }
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand CueCommand { get; }
    public ICommand SetCueCommand { get; }
    public ICommand LoopCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand MasterCommand { get; }
    public ICommand SyncCommand { get; }
    public ICommand NudgeGridBackwardCommand { get; }
    public ICommand NudgeGridForwardCommand { get; }
    public ICommand SetFirstBeatCommand { get; }
    public ICommand HalfBpmCommand { get; }
    public ICommand DoubleBpmCommand { get; }
    public ICommand BpmDownCommand { get; }
    public ICommand BpmUpCommand { get; }
    public ICommand ResetTempoCommand { get; }
    public ICommand TapBpmCommand { get; }
    public ICommand ToggleCueMonitorCommand { get; }

    public void Load(AudioTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!track.CanAssignToDeck)
            throw new InvalidOperationException("La traccia non dispone né di un file locale né di un riferimento Spotify valido.");

        if (track.CanLoadToDeck)
            _engine.Load(Id, track);
        else
            _engine.Unload(Id);

        Track = track;
        PositionSeconds = track.CueInSeconds;
        _externalPositionAnchor = PositionSeconds;
        _externalPlaybackStartedAtUtc = null;
        IsPlaying = false;
        _tempoPercent = 0;
        if (HasLocalAudio) _channel.SetTempoRatio(1d);
        SyncEnabled = false;
        _syncSnapshot = SyncSnapshot.Off;
        LowEq = 0;
        MidEq = 0;
        HighEq = 0;
        Filter = 0;
        EchoMix = 0;
        BitCrush = 0;
        Saturation = 0;
        Gate = 0;
        Compressor = 0;
        Roll = 0;
        Brake = 0;
        _state = DeckState.Ready;
        ResetTapSequence();
        RaiseDeckProperties();
    }

    public void TogglePlayPause()
    {
        if (PlayPauseCommand.CanExecute(null)) PlayPauseCommand.Execute(null);
    }

    public async Task TogglePlayPauseAsync()
    {
        if (IsSpotifyExternal)
        {
            await _requestExternalToggle(this);
            return;
        }

        if (_cutAuditionActive)
        {
            _cutLatchedByPlay = true;
            IsPlaying = true;
            RaisePropertyChanged(nameof(CutLabel));
            RaiseTransportProperties();
            return;
        }

        if (IsPlaying) Pause(); else Play();
    }

    public void Play()
    {
        if (!HasLocalAudio) return;
        _engine.Play(Id);
        IsPlaying = true;
        RaiseTransportProperties();
    }

    public void PressCut()
    {
        if (!HasLocalAudio) return;
        _cutAuditionActive = true;
        _cutLatchedByPlay = false;
        Seek(Track?.CueInSeconds ?? 0d);
        Play();
        RaisePropertyChanged(nameof(CutLabel));
    }

    public void ReleaseCut()
    {
        if (!HasLocalAudio || !_cutAuditionActive) return;
        _cutAuditionActive = false;
        if (!_cutLatchedByPlay)
        {
            Pause();
            Seek(Track?.CueInSeconds ?? 0d);
        }
        _cutLatchedByPlay = false;
        RaisePropertyChanged(nameof(CutLabel));
        RaiseTransportProperties();
    }

    public void Pause()
    {
        if (!HasLocalAudio) return;
        _engine.Pause(Id);
        IsPlaying = false;
        RaiseTransportProperties();
    }

    public void Stop()
    {
        if (HasLocalAudio) _engine.Stop(Id);
        _cutAuditionActive = false;
        _cutLatchedByPlay = false;
        CueMonitorEnabled = false;
        PositionSeconds = Track?.CueInSeconds ?? 0;
        _externalPositionAnchor = PositionSeconds;
        _externalPlaybackStartedAtUtc = null;
        IsPlaying = false;
        SyncEnabled = false;
        _syncSnapshot = SyncSnapshot.Off;
        RaiseTransportProperties();
        RaisePropertyChanged(nameof(SyncLabel));
        RaisePropertyChanged(nameof(SyncStateText));
        RaisePropertyChanged(nameof(StateText));
    }

    public void SetExternalPlaybackState(bool isPlaying, bool resetPosition = false)
    {
        if (!IsSpotifyExternal) return;
        if (resetPosition)
        {
            PositionSeconds = 0;
            _externalPositionAnchor = 0;
        }
        else if (IsPlaying && _externalPlaybackStartedAtUtc is DateTimeOffset started)
        {
            _externalPositionAnchor = Math.Clamp(
                _externalPositionAnchor + (DateTimeOffset.UtcNow - started).TotalSeconds,
                0,
                Math.Max(0, DurationSeconds));
            PositionSeconds = _externalPositionAnchor;
        }

        IsPlaying = isPlaying;
        _externalPlaybackStartedAtUtc = isPlaying ? DateTimeOffset.UtcNow : null;
        RaiseTransportProperties();
        RaisePropertyChanged(nameof(StateText));
    }

    public void Seek(double seconds)
    {
        if (!HasLocalAudio) return;
        _channel.Seek(seconds);
        PositionSeconds = _channel.PositionSeconds;
    }

    public void SetCue(double seconds)
    {
        if (Track is null || !HasLocalAudio) return;
        Track.CueInSeconds = Math.Clamp(seconds, 0, Math.Max(0, DurationSeconds - 0.01d));
        if (!Track.CuePoints.Any(cue => Math.Abs(cue.PositionSeconds - Track.CueInSeconds) < 0.02d))
            Track.CuePoints.Add(new CuePoint { Name = $"Start {Track.CuePoints.Count + 1}", PositionSeconds = Track.CueInSeconds });
        RaiseDeckProperties();
    }

    public void TriggerOrSetHotCue(int index)
    {
        if (Track is null || !HasLocalAudio || index < 1 || index > 8) return;

        var name = $"Hot Cue {index}";
        var cue = Track.CuePoints.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

        if (cue is null)
        {
            Track.CuePoints.Add(new CuePoint
            {
                Name = name,
                PositionSeconds = Math.Clamp(PositionSeconds, 0d, Math.Max(0d, DurationSeconds - 0.01d))
            });
            RaiseDeckProperties();
            return;
        }

        Seek(cue.PositionSeconds);
        if (!IsPlaying) Play();
    }

    public void SetMaster(bool isMaster)
    {
        IsMaster = isMaster && HasLocalAudio;
        if (IsMaster)
        {
            SyncEnabled = false;
            _syncSnapshot = SyncSnapshot.Off;
            _channel.SetSyncState(SyncState.Off);
        }
    }

    public void EnableSync(SyncSnapshot snapshot)
    {
        if (!HasLocalAudio) return;
        SyncEnabled = true;
        ApplySyncSnapshot(snapshot);
    }

    public void DisableSync()
    {
        SyncEnabled = false;
        _syncSnapshot = SyncSnapshot.Off;
        if (HasLocalAudio) _engine.DisableSync(Id);
        _tempoPercent = 0;
        RaisePropertyChanged(nameof(TempoPercent));
        RaiseTempoProperties();
        RaisePropertyChanged(nameof(SyncLabel));
        RaisePropertyChanged(nameof(SyncStateText));
    }

    public void ApplySyncSnapshot(SyncSnapshot snapshot)
    {
        if (!HasLocalAudio) return;
        _syncSnapshot = snapshot;
        _tempoPercent = (_channel.TempoRatio - 1d) * 100d;
        RaisePropertyChanged(nameof(TempoPercent));
        RaisePropertyChanged(nameof(SyncStateText));
        RaiseTempoProperties();
    }

    public void SetAutomixLowEq(double gainDb)
    {
        if (!HasLocalAudio) return;
        _lowEq = Math.Clamp(gainDb, -24d, 12d);
        RaisePropertyChanged(nameof(LowEq));
        ApplyEq();
    }

    public void Tick()
    {
        if (IsSpotifyExternal)
        {
            MeterPeak = 0;
            if (IsPlaying && _externalPlaybackStartedAtUtc is DateTimeOffset started)
            {
                var estimate = _externalPositionAnchor + (DateTimeOffset.UtcNow - started).TotalSeconds;
                PositionSeconds = Math.Clamp(estimate, 0, Math.Max(0, DurationSeconds));
                if (DurationSeconds > 0 && PositionSeconds >= DurationSeconds)
                    SetExternalPlaybackState(false, resetPosition: true);
            }
            RaiseTempoProperties();
            return;
        }

        PositionSeconds = _channel.PositionSeconds;
        IsPlaying = _channel.IsPlaying;
        MeterPeak = _channel.MeterPeak;
        var state = _channel.State;
        if (_state != state)
        {
            _state = state;
            RaisePropertyChanged(nameof(StateText));
        }
        RaiseTempoProperties();
        RaiseBeatProperties();
    }

    private async Task StopRequestedAsync()
    {
        if (IsSpotifyExternal)
        {
            await _requestExternalStop(this);
            return;
        }
        Stop();
    }

    private void GoToCue() => Seek(Track?.CueInSeconds ?? 0);
    private void SetCueAtCurrent() => SetCue(PositionSeconds);

    private void NudgeBeatGrid(double seconds)
    {
        if (Track is null || !HasLocalAudio || Track.Bpm <= 0) return;
        var maximum = Math.Max(0d, DurationSeconds - 0.001d);
        Track.BeatOffsetSeconds = Math.Clamp(Track.BeatOffsetSeconds + seconds, 0d, maximum);
        Track.PhaseConfidence = 1d;
        Track.AnalysisStatus = "Beat-grid corretta manualmente";
        RecalculateTrackBeatTotals();
        RaisePropertyChanged(nameof(BeatOffsetSeconds));
        RaisePropertyChanged(nameof(SyncStateText));
        RaiseBeatProperties();
    }

    private void SetFirstBeatAtCurrent()
    {
        if (Track is null || !HasLocalAudio || Track.Bpm <= 0) return;
        Track.BeatOffsetSeconds = Math.Clamp(PositionSeconds, 0d, Math.Max(0d, DurationSeconds - 0.001d));
        Track.PhaseConfidence = 1d;
        Track.AnalysisStatus = "Prima battuta impostata manualmente";
        RecalculateTrackBeatTotals();
        RaisePropertyChanged(nameof(BeatOffsetSeconds));
        RaisePropertyChanged(nameof(SyncStateText));
        RaiseBeatProperties();
    }

    private void ScaleBpm(double factor)
    {
        if (Track is null || Track.Bpm <= 0) return;
        Track.Bpm = Math.Clamp(Track.Bpm * factor, 40d, 300d);
        Track.AnalysisConfidence = 1d;
        Track.AverageBeatIntervalSeconds = 60d / Track.Bpm;
        Track.DetectedBeatCount = DurationSeconds > BeatOffsetSeconds
            ? 1 + (int)Math.Floor((DurationSeconds - BeatOffsetSeconds) / Track.AverageBeatIntervalSeconds)
            : 0;
        Track.EstimatedBarCount = Track.DetectedBeatCount > 0
            ? (int)Math.Ceiling(Track.DetectedBeatCount / (double)Math.Max(1, BeatsPerBar))
            : 0;
        Track.AnalysisStatus = IsSpotifyExternal
            ? "BPM dichiarato manualmente · riproduzione Spotify invariata"
            : "BPM corretto manualmente";
        RaisePropertyChanged(nameof(Bpm));
        RaisePropertyChanged(nameof(BpmText));
        RaisePropertyChanged(nameof(TargetBpm));
        RaiseTempoProperties();
        RaiseCommandStates();
    }

    private void ChangeTargetBpm(double delta)
    {
        var current = TargetBpm > 0 ? TargetBpm : 120d;
        SetTargetBpm(current + delta);
    }

    private void SetTargetBpm(double value)
    {
        if (Track is null || double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return;
        var requested = Math.Clamp(value, 40d, 300d);

        if (HasLocalAudio && Bpm > 0)
        {
            TempoPercent = Math.Clamp((requested / Bpm - 1d) * 100d, -25d, 25d);
        }
        else
        {
            Track.Bpm = requested;
            Track.AnalysisConfidence = 1d;
            Track.AverageBeatIntervalSeconds = 60d / Track.Bpm;
            Track.DetectedBeatCount = DurationSeconds > BeatOffsetSeconds
                ? 1 + (int)Math.Floor((DurationSeconds - BeatOffsetSeconds) / Track.AverageBeatIntervalSeconds)
                : 0;
            Track.EstimatedBarCount = Track.DetectedBeatCount > 0
                ? (int)Math.Ceiling(Track.DetectedBeatCount / (double)Math.Max(1, BeatsPerBar))
                : 0;
            Track.AnalysisStatus = IsSpotifyExternal
                ? "BPM dichiarato manualmente · Spotify resta alla velocità originale"
                : "BPM impostato manualmente";
            RaisePropertyChanged(nameof(Bpm));
            RaisePropertyChanged(nameof(BpmText));
            RaiseTempoProperties();
        }

        RaisePropertyChanged(nameof(TargetBpm));
        RaiseCommandStates();
    }

    private void ResetTempo()
    {
        if (!HasLocalAudio) return;
        SyncEnabled = false;
        _syncSnapshot = SyncSnapshot.Off;
        _engine.DisableSync(Id);
        TempoPercent = 0;
        RaisePropertyChanged(nameof(SyncLabel));
        RaisePropertyChanged(nameof(SyncStateText));
    }

    private void RecalculateTrackBeatTotals()
    {
        if (Track is null || Track.Bpm <= 0)
        {
            if (Track is not null)
            {
                Track.AverageBeatIntervalSeconds = 0d;
                Track.DetectedBeatCount = 0;
                Track.EstimatedBarCount = 0;
            }
            return;
        }

        Track.AverageBeatIntervalSeconds = 60d / Track.Bpm;
        Track.DetectedBeatCount = DurationSeconds > BeatOffsetSeconds
            ? 1 + (int)Math.Floor((DurationSeconds - BeatOffsetSeconds) / Track.AverageBeatIntervalSeconds)
            : 0;
        Track.EstimatedBarCount = Track.DetectedBeatCount > 0
            ? (int)Math.Ceiling(Track.DetectedBeatCount / (double)Math.Max(1, BeatsPerBar))
            : 0;
    }

    private void RegisterTapBpm()
    {
        if (Track is null) return;

        var now = DateTimeOffset.UtcNow;
        if (_lastTapUtc is null || (now - _lastTapUtc.Value).TotalSeconds > 2.5d)
        {
            _tapIntervalsSeconds.Clear();
            _tapCount = 1;
            _lastTapUtc = now;
            RaisePropertyChanged(nameof(TapBpmStatus));
            return;
        }

        var interval = (now - _lastTapUtc.Value).TotalSeconds;
        _lastTapUtc = now;
        if (interval < 0.25d || interval > 2d)
        {
            ResetTapSequence();
            _tapCount = 1;
            _lastTapUtc = now;
            RaisePropertyChanged(nameof(TapBpmStatus));
            return;
        }

        _tapIntervalsSeconds.Add(interval);
        while (_tapIntervalsSeconds.Count > 12) _tapIntervalsSeconds.RemoveAt(0);
        _tapCount = _tapIntervalsSeconds.Count + 1;

        if (_tapIntervalsSeconds.Count >= 2)
        {
            var ordered = _tapIntervalsSeconds.OrderBy(value => value).ToArray();
            var median = ordered[ordered.Length / 2];
            var accepted = ordered.Where(value => Math.Abs(value - median) <= median * 0.20d).ToArray();
            var averageInterval = accepted.Length > 0 ? accepted.Average() : median;
            var tappedBpm = 60d / averageInterval;
            while (tappedBpm < 70d) tappedBpm *= 2d;
            while (tappedBpm > 200d) tappedBpm /= 2d;

            Track.Bpm = Math.Round(Math.Clamp(tappedBpm, 40d, 300d), 2);
            Track.AverageBeatIntervalSeconds = 60d / Track.Bpm;
            Track.DetectedBeatCount = DurationSeconds > BeatOffsetSeconds
                ? 1 + (int)Math.Floor((DurationSeconds - BeatOffsetSeconds) / Track.AverageBeatIntervalSeconds)
                : 0;
            Track.EstimatedBarCount = Track.DetectedBeatCount > 0
                ? (int)Math.Ceiling(Track.DetectedBeatCount / (double)Math.Max(1, BeatsPerBar))
                : 0;
            Track.AnalysisConfidence = Math.Clamp(0.45d + accepted.Length * 0.05d, 0d, 0.95d);
            Track.AnalysisStatus = $"BPM TAP · {Track.Bpm:0.0} · {_tapCount} battiti campionati";

            if (HasLocalAudio && !SyncEnabled)
            {
                _tempoPercent = 0d;
                _channel.SetTempoRatio(1d);
                RaisePropertyChanged(nameof(TempoPercent));
            }

            RaisePropertyChanged(nameof(Bpm));
            RaisePropertyChanged(nameof(BpmText));
            RaisePropertyChanged(nameof(TargetBpm));
            RaiseTempoProperties();
            RaiseBeatProperties();
            RaiseCommandStates();
        }

        RaisePropertyChanged(nameof(TapBpmStatus));
    }

    private void ResetTapSequence()
    {
        _lastTapUtc = null;
        _tapIntervalsSeconds.Clear();
        _tapCount = 0;
        RaisePropertyChanged(nameof(TapBpmStatus));
    }

    private void ToggleLoop()
    {
        if (Track is null || !HasLocalAudio || Track.Bpm <= 0) return;
        if (_channel.IsLoopEnabled)
        {
            _channel.DisableLoop();
        }
        else
        {
            var beatLength = BeatGridMath.BeatLengthSeconds(Track.Bpm);
            var start = BeatGridMath.QuantizeNearest(PositionSeconds, Track.BeatOffsetSeconds, Track.Bpm);
            _channel.EnableLoop(start, start + beatLength * LoopBeats);
            Seek(start);
        }
        RaisePropertyChanged(nameof(LoopLabel));
    }

    private void ApplyEq()
    {
        if (HasLocalAudio) _channel.SetEq(LowEq, MidEq, HighEq);
    }

    private void RaiseDeckProperties()
    {
        RaisePropertyChanged(nameof(TrackTitle));
        RaisePropertyChanged(nameof(Artist));
        RaisePropertyChanged(nameof(Bpm));
        RaisePropertyChanged(nameof(BpmText));
        RaisePropertyChanged(nameof(EffectiveBpm));
        RaisePropertyChanged(nameof(EffectiveBpmText));
        RaisePropertyChanged(nameof(TargetBpm));
        RaisePropertyChanged(nameof(DurationSeconds));
        RaisePropertyChanged(nameof(Waveform));
        RaisePropertyChanged(nameof(BeatOffsetSeconds));
        RaisePropertyChanged(nameof(BeatsPerBar));
        RaisePropertyChanged(nameof(CueInSeconds));
        RaisePropertyChanged(nameof(HasLocalAudio));
        RaisePropertyChanged(nameof(IsSpotifyExternal));
        RaisePropertyChanged(nameof(CanUseMixerControls));
        RaisePropertyChanged(nameof(PlaybackModeText));
        RaisePropertyChanged(nameof(MasterLabel));
        RaisePropertyChanged(nameof(SyncStateText));
        RaisePropertyChanged(nameof(TempoText));
        RaisePropertyChanged(nameof(Saturation));
        RaisePropertyChanged(nameof(Gate));
        RaisePropertyChanged(nameof(Compressor));
        RaisePropertyChanged(nameof(Roll));
        RaisePropertyChanged(nameof(Brake));
        RaisePropertyChanged(nameof(StateText));
        RaisePropertyChanged(nameof(PlayLabel));
        RaisePropertyChanged(nameof(CutLabel));
        RaisePropertyChanged(nameof(CueMonitorLabel));
        RaisePropertyChanged(nameof(AnalysisSummaryText));
        RaiseBeatProperties();
        RaiseTransportProperties();
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[]
                 {
                     PlayPauseCommand, CueCommand, SetCueCommand, LoopCommand, StopCommand, MasterCommand,
                     SyncCommand, NudgeGridBackwardCommand, NudgeGridForwardCommand, SetFirstBeatCommand,
                     HalfBpmCommand, DoubleBpmCommand, BpmDownCommand, BpmUpCommand, ResetTempoCommand, TapBpmCommand,
                     ToggleCueMonitorCommand
                 })
        {
            switch (command)
            {
                case RelayCommand relay: relay.RaiseCanExecuteChanged(); break;
                case AsyncRelayCommand asyncRelay: asyncRelay.RaiseCanExecuteChanged(); break;
            }
        }
    }

    private void RaiseTransportProperties()
    {
        RaisePropertyChanged(nameof(PositionText));
        RaisePropertyChanged(nameof(RemainingText));
        RaisePropertyChanged(nameof(PositionSeconds));
        RaiseBeatProperties();
    }

    private void RaiseBeatProperties()
    {
        RaisePropertyChanged(nameof(CurrentBeatNumber));
        RaisePropertyChanged(nameof(CurrentBarNumber));
        RaisePropertyChanged(nameof(BeatInBar));
        RaisePropertyChanged(nameof(TotalBeatCount));
        RaisePropertyChanged(nameof(TotalBarCount));
        RaisePropertyChanged(nameof(BeatCounterText));
        RaisePropertyChanged(nameof(BeatProgressText));
        RaisePropertyChanged(nameof(AnalysisSummaryText));
    }

    private void RaiseTempoProperties()
    {
        RaisePropertyChanged(nameof(EffectiveBpm));
        RaisePropertyChanged(nameof(EffectiveBpmText));
        RaisePropertyChanged(nameof(TargetBpm));
        RaisePropertyChanged(nameof(TempoText));
    }

    private static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss\.ff");

    public void Dispose()
    {
        // Il motore audio condiviso viene eliminato dal MainViewModel.
    }
}
