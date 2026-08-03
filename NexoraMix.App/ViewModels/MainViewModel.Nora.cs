using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using NexoraMix.App.Services;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;

namespace NexoraMix.App.ViewModels;

public sealed partial class MainViewModel
{
    private NoraDjAssistantService? _noraAssistant;
    private CancellationTokenSource? _noraRefreshCancellation;
    private string _noraStatus = "Nora AI si collegherà al catalogo musicale condiviso.";
    private string _noraCurrentTrack = "Nessuna traccia attiva";
    private bool _isNoraBusy;
    private long _noraRefreshGeneration;
    private string _lastNoraDeckStateSignature = string.Empty;
    private readonly NoraTransitionAdvisor _noraTransitionAdvisor = new();
    private readonly HashSet<string> _noraPlayedTrackKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _noraRecommendationKeys = new(StringComparer.OrdinalIgnoreCase);
    private NoraSetMemorySnapshot? _noraSetSnapshot;
    private string _noraSetStatus = "SET NON AVVIATO";
    private string _noraTransitionDetail = "Seleziona TRANSIZIONE su un suggerimento per vedere il piano operativo.";

    public ObservableCollection<NoraDjRecommendation> NoraRecommendations { get; } = new();
    public ObservableCollection<NoraSetPlanStep> NoraSetPlan { get; } = new();
    public ICommand RefreshNoraCommand { get; private set; } = null!;
    public ICommand LoadNoraSuggestionCommand { get; private set; } = null!;
    public ICommand PreviewNoraSuggestionCommand { get; private set; } = null!;
    public ICommand ShowNoraTransitionCommand { get; private set; } = null!;
    public ICommand NoraExcellentCommand { get; private set; } = null!;
    public ICommand NoraNotSuitableCommand { get; private set; } = null!;
    public ICommand NoraTooEnergeticCommand { get; private set; } = null!;
    public ICommand NoraTooSlowCommand { get; private set; } = null!;
    public ICommand NoraAvoidArtistCommand { get; private set; } = null!;
    public ICommand NoraRegenerateCommand { get; private set; } = null!;
    public ICommand NewNoraSetCommand { get; private set; } = null!;
    public ICommand PauseResumeNoraSetCommand { get; private set; } = null!;
    public ICommand CloseNoraSetCommand { get; private set; } = null!;
    public string NoraStatus { get => _noraStatus; private set => SetProperty(ref _noraStatus, value); }
    public string NoraCurrentTrack { get => _noraCurrentTrack; private set => SetProperty(ref _noraCurrentTrack, value); }
    public bool IsNoraBusy { get => _isNoraBusy; private set => SetProperty(ref _isNoraBusy, value); }
    public string NoraSetStatus { get => _noraSetStatus; private set => SetProperty(ref _noraSetStatus, value); }
    public string NoraTransitionDetail { get => _noraTransitionDetail; private set => SetProperty(ref _noraTransitionDetail, value); }
    public string NoraPauseResumeLabel => _noraSetSnapshot?.Session.Status == NoraSetSessionStatus.Paused ? "RIPRENDI SET" : "PAUSA SET";

    private void InitializeNora()
    {
        _noraAssistant = new NoraDjAssistantService();
        RefreshNoraCommand = new AsyncRelayCommand(RefreshNoraRecommendationsAsync);
        LoadNoraSuggestionCommand = new AsyncRelayCommand(LoadNoraSuggestionAsync);
        PreviewNoraSuggestionCommand = new AsyncRelayCommand(PreviewNoraSuggestionAsync);
        ShowNoraTransitionCommand = new RelayCommand(ShowNoraTransition);
        NoraExcellentCommand = FeedbackCommand(NoraFeedbackType.ExcellentSuggestion);
        NoraNotSuitableCommand = FeedbackCommand(NoraFeedbackType.NotSuitable);
        NoraTooEnergeticCommand = FeedbackCommand(NoraFeedbackType.TooEnergetic);
        NoraTooSlowCommand = FeedbackCommand(NoraFeedbackType.TooSlow);
        NoraAvoidArtistCommand = FeedbackCommand(NoraFeedbackType.AvoidArtist);
        NoraRegenerateCommand = new AsyncRelayCommand(RefreshNoraRecommendationsAsync);
        NewNoraSetCommand = new AsyncRelayCommand(StartNewNoraSetAsync);
        PauseResumeNoraSetCommand = new AsyncRelayCommand(PauseResumeNoraSetAsync);
        CloseNoraSetCommand = new AsyncRelayCommand(CloseNoraSetAsync);
        foreach (var deck in AllDecks) deck.PropertyChanged += OnNoraDeckPropertyChanged;
    }

    private void OnNoraDeckPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeckViewModel.Track)
            or nameof(DeckViewModel.IsPlaying)
            or nameof(DeckViewModel.Volume)
            or nameof(DeckViewModel.MeterPeak)
            or nameof(DeckViewModel.CueMonitorEnabled)
            or nameof(DeckViewModel.TempoPercent))
            ScheduleNoraRefresh(force: e.PropertyName != nameof(DeckViewModel.MeterPeak));
    }

    private void ScheduleNoraRefresh(bool force = true)
    {
        if (_noraAssistant is null) return;
        var state = CaptureNoraDeckState();
        var signature = NoraDeckStateSignature(state);
        if (!force && string.Equals(signature, _lastNoraDeckStateSignature, StringComparison.Ordinal)) return;
        _lastNoraDeckStateSignature = signature;

        var generation = Interlocked.Increment(ref _noraRefreshGeneration);
        _noraRefreshCancellation?.Cancel();
        _noraRefreshCancellation?.Dispose();
        _noraRefreshCancellation = new CancellationTokenSource();
        _ = DebounceNoraRefreshAsync(generation, _noraRefreshCancellation.Token);
    }

    private async Task DebounceNoraRefreshAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            await RefreshNoraRecommendationsAsync(generation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private Task RefreshNoraRecommendationsAsync()
    {
        var generation = Interlocked.Increment(ref _noraRefreshGeneration);
        _noraRefreshCancellation?.Cancel();
        _noraRefreshCancellation?.Dispose();
        _noraRefreshCancellation = new CancellationTokenSource();
        return RefreshNoraRecommendationsAsync(generation, _noraRefreshCancellation.Token);
    }

    private async Task RefreshNoraRecommendationsAsync(long generation, CancellationToken cancellationToken)
    {
        if (_noraAssistant is null) return;
        var deckState = CaptureNoraDeckState();
        var currentDeckId = deckState.PrimaryAudibleDeck ?? deckState.OutgoingDeck;
        var currentDeck = currentDeckId is DeckId id
            ? AllDecks.FirstOrDefault(deck => deck.Id == id)
            : null;
        var current = currentDeck?.Track;
        if (current is null || deckState.IsAmbiguous && deckState.OutgoingDeck is null)
        {
            NoraCurrentTrack = "Nessuna traccia attiva";
            NoraStatus = deckState.Reason;
            NoraRecommendations.Clear();
            return;
        }

        IsNoraBusy = true;
        NoraCurrentTrack = $"In ascolto sul Deck {currentDeck!.Name}: {current.Artist} — {current.Title} · {currentDeck.EffectiveBpm:0.0} BPM";
        NoraStatus = deckState.IsAmbiguous
            ? $"Nora usa il deck in uscita con confidenza {deckState.Confidence:P0}. {deckState.Reason}"
            : "Nora sta analizzando il brano realmente udibile…";
        try
        {
            await EnsureNoraSetAsync(current, currentDeck, cancellationToken);
            var result = await _noraAssistant.RecommendAsync(current, Library.ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _noraRefreshGeneration)) return;
            NoraRecommendations.Clear();
            var currentProfile = ToNoraProfile(current);
            var currentTiming = new NoraTrackTiming(currentDeck.PositionSeconds,
                Math.Max(currentDeck.DurationSeconds, current.DurationSeconds), currentDeck.BeatOffsetSeconds,
                currentDeck.BeatsPerBar);
            foreach (var recommendation in result.Recommendations.Take(8))
            {
                var candidate = recommendation.Match.Track;
                var candidateTiming = new NoraTrackTiming(0d,
                    Math.Max(0d, recommendation.LocalTrack?.DurationSeconds ?? candidate.DurationSeconds ?? 0d),
                    recommendation.LocalTrack?.BeatOffsetSeconds ?? 0d,
                    recommendation.LocalTrack?.BeatsPerBar ?? 4);
                var advice = _noraTransitionAdvisor.Advise(currentProfile, candidate, deckState,
                    currentTiming, candidateTiming);
                var advised = recommendation with { Advice = advice };
                NoraRecommendations.Add(advised);
                await RecordNoraRecommendationOnceAsync(currentProfile, advised, cancellationToken);
            }
            NoraSetPlan.Clear();
            foreach (var step in result.SetPlan?.Sequence ?? []) NoraSetPlan.Add(step);
            if (result.SetSnapshot is not null) UpdateNoraSetSnapshot(result.SetSnapshot);
            NoraStatus = result.CatalogTrackCount == 0
                ? "Catalogo PuliziaSpazioDev non ancora popolato: suggerimenti basati sui BPM della libreria Mix Studio."
                : $"Nora connessa · {result.CatalogTrackCount} tracce note · {NoraRecommendations.Count} suggerimenti · {result.Status} · {deckState.Reason}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _noraRefreshGeneration)) return;
            AppLog.Error(ex, "Nora DJ Assistant");
            NoraStatus = $"Nora non riesce a leggere il catalogo condiviso: {ex.Message}";
        }
        finally
        {
            if (generation == Volatile.Read(ref _noraRefreshGeneration)) IsNoraBusy = false;
        }
    }

    private async Task LoadNoraSuggestionAsync(object? parameter)
    {
        if (parameter is not NoraDjRecommendation recommendation) return;
        if (recommendation.LocalTrack?.CanLoadToDeck != true)
        {
            Status = $"{recommendation.Title} è nota a Nora ma non è ancora presente nella libreria locale di Mix Studio.";
            return;
        }

        var state = CaptureNoraDeckState();
        if (state.RecommendedLoadDeck is not DeckId targetId)
        {
            Status = "Nora non ha trovato un deck completamente libero: libera un deck prima di caricare il suggerimento.";
            return;
        }

        var target = AllDecks.First(deck => deck.Id == targetId);
        SelectedTrack = recommendation.LocalTrack;
        if (LoadTrackToDeckOnly(recommendation.LocalTrack, target))
        {
            Status = $"Scelta di Nora caricata in sicurezza sul Deck {target.Name}, senza avvio automatico: {recommendation.Artist} — {recommendation.Title} ({recommendation.Compatibility}).";
            await RecordNoraFeedbackAsync(NoraFeedbackType.TrackLoaded, recommendation, false, 0.9d, refresh: false);
            if (_noraAssistant is not null && _noraSetSnapshot is not null)
                await _noraAssistant.RecordLoadedTrackAsync(CreateSetTrack(_noraSetSnapshot.Session.Id,
                    recommendation.LocalTrack, target, wasSuggested: true, recommendation.Advice));
        }
    }

    private AsyncRelayCommand FeedbackCommand(NoraFeedbackType type) =>
        new(parameter => parameter is NoraDjRecommendation recommendation
            ? RecordNoraFeedbackAsync(type, recommendation, true, 1d, refresh: true)
            : Task.CompletedTask);

    private async Task RecordNoraFeedbackAsync(NoraFeedbackType type, NoraDjRecommendation recommendation,
        bool isExplicit, double confidence, bool refresh)
    {
        if (_noraAssistant is null) return;
        var state = CaptureNoraDeckState();
        var deck = (state.PrimaryAudibleDeck ?? state.OutgoingDeck) is DeckId id
            ? AllDecks.FirstOrDefault(item => item.Id == id) : null;
        if (deck?.Track is null) return;
        await _noraAssistant.RecordFeedbackAsync(type, deck.Track, recommendation, isExplicit, confidence);
        NoraStatus = type == NoraFeedbackType.TrackLoaded
            ? "Nora ha registrato il caricamento implicito."
            : $"Feedback {type} registrato: Nora adatta subito la classifica.";
        if (refresh) ScheduleNoraRefresh();
    }

    private async Task PreviewNoraSuggestionAsync(object? parameter)
    {
        if (parameter is not NoraDjRecommendation { LocalTrack.CanLoadToDeck: true } recommendation) return;
        var entry = recommendation.Advice?.EntryTimeSeconds ?? recommendation.LocalTrack.CueInSeconds;
        _audioEngine.LoadPreview(recommendation.LocalTrack, entry);
        _audioEngine.PlayPreview();
        NoraTransitionDetail = $"PREVIEW CUFFIA · {recommendation.Artist} — {recommendation.Title} da {FormatNoraTime(entry)}. Il master non viene modificato.";
        await RecordNoraFeedbackAsync(NoraFeedbackType.HeadphonePreviewed, recommendation, false, 0.7d, refresh: false);
    }

    private void ShowNoraTransition(object? parameter)
    {
        if (parameter is not NoraDjRecommendation recommendation || recommendation.Advice is not { } advice) return;
        NoraTransitionDetail = $"DECK {recommendation.RecommendedDeck} · esci a {recommendation.Exit} · entra a {recommendation.Entry} · " +
                               $"pitch {recommendation.Pitch} · mix {recommendation.Mix} · rischio {recommendation.Risk} · " +
                               $"{advice.BassSwapAdvice} {advice.VocalAdvice}";
    }

    private async Task EnsureNoraSetAsync(AudioTrack current, DeckViewModel deck, CancellationToken cancellationToken)
    {
        if (_noraAssistant is null) return;
        var snapshot = await _noraAssistant.GetActiveSetSnapshotAsync(cancellationToken);
        if (snapshot is null)
        {
            await _noraAssistant.StartSetAsync(NoraSetPhase.WarmUp, new NoraSetTrajectory
            {
                CurrentEnergy = current.AudioFeatures.Energy,
                TargetEnergy = 0.55d,
                TargetPhase = NoraSetPhase.WarmUp,
                MaximumBpmIncreasePerTrack = 5d,
                AllowEnergyDrop = false
            }, cancellationToken);
            snapshot = await _noraAssistant.GetActiveSetSnapshotAsync(cancellationToken);
        }
        if (snapshot is null) return;
        UpdateNoraSetSnapshot(snapshot);
        if (snapshot.Session.Status != NoraSetSessionStatus.Active) return;
        var playedKey = $"{snapshot.Session.Id}:{NoraTrackId(current)}";
        if (_noraPlayedTrackKeys.Add(playedKey))
            await _noraAssistant.RecordPlayedTrackAsync(CreateSetTrack(snapshot.Session.Id, current, deck,
                wasSuggested: false, advice: null), cancellationToken);
    }

    private async Task RecordNoraRecommendationOnceAsync(NoraTrackProfile current,
        NoraDjRecommendation recommendation, CancellationToken cancellationToken)
    {
        if (_noraAssistant is null || _noraSetSnapshot is null) return;
        var key = $"{_noraSetSnapshot.Session.Id}:{current.Id}:{recommendation.Match.Track.Id}:{recommendation.Position}";
        if (!_noraRecommendationKeys.Add(key)) return;
        await _noraAssistant.RecordRecommendationAsync(new NoraSetRecommendation(
            Guid.NewGuid().ToString("N"), _noraSetSnapshot.Session.Id, current.Id,
            recommendation.Match.Track.Id, recommendation.Position, recommendation.Match.Score,
            WasLoaded: null, WasPlayed: null, DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task StartNewNoraSetAsync()
    {
        if (_noraAssistant is null) return;
        if (_noraSetSnapshot is not null)
            await _noraAssistant.CloseSetAsync(_noraSetSnapshot.Session.Id);
        await _noraAssistant.StartSetAsync(NoraSetPhase.WarmUp, new NoraSetTrajectory
        {
            TargetEnergy = 0.55d,
            TargetPhase = NoraSetPhase.WarmUp,
            MaximumBpmIncreasePerTrack = 5d,
            AllowEnergyDrop = false
        });
        _noraPlayedTrackKeys.Clear();
        _noraRecommendationKeys.Clear();
        var snapshot = await _noraAssistant.GetActiveSetSnapshotAsync();
        if (snapshot is not null) UpdateNoraSetSnapshot(snapshot);
        await RefreshNoraRecommendationsAsync();
    }

    private async Task PauseResumeNoraSetAsync()
    {
        if (_noraAssistant is null || _noraSetSnapshot is null) return;
        if (_noraSetSnapshot.Session.Status == NoraSetSessionStatus.Paused)
            await _noraAssistant.ResumeSetAsync(_noraSetSnapshot.Session.Id);
        else
            await _noraAssistant.PauseSetAsync(_noraSetSnapshot.Session.Id);
        var snapshot = await _noraAssistant.GetActiveSetSnapshotAsync();
        if (snapshot is not null) UpdateNoraSetSnapshot(snapshot);
    }

    private async Task CloseNoraSetAsync()
    {
        if (_noraAssistant is null || _noraSetSnapshot is null) return;
        await _noraAssistant.CloseSetAsync(_noraSetSnapshot.Session.Id);
        _noraSetSnapshot = null;
        NoraSetStatus = "SET CHIUSO";
        RaisePropertyChanged(nameof(NoraPauseResumeLabel));
    }

    private void UpdateNoraSetSnapshot(NoraSetMemorySnapshot snapshot)
    {
        _noraSetSnapshot = snapshot;
        NoraSetStatus = $"{snapshot.Session.Status.ToString().ToUpperInvariant()} · {snapshot.Session.Phase} · " +
                        $"{snapshot.RecentTracks.Count} tracce suonate";
        RaisePropertyChanged(nameof(NoraPauseResumeLabel));
    }

    private static NoraTrackProfile ToNoraProfile(AudioTrack track) =>
        new(NoraTrackId(track), track.Title, track.Artist, track.Bpm > 0 ? track.Bpm : null, null, null)
        {
            AudioFeatures = track.AudioFeatures,
            IsFileAvailable = track.CanLoadToDeck,
            DurationSeconds = track.DurationSeconds > 0 ? track.DurationSeconds : null,
            MetadataVersion = track.AnalysisVersion
        };

    private NoraSetSessionTrack CreateSetTrack(string sessionId, AudioTrack track, DeckViewModel deck,
        bool wasSuggested, NoraTransitionAdvice? advice) => new(
        Guid.NewGuid().ToString("N"), sessionId, NoraTrackId(track), track.Title, track.Artist, Genre: null,
        Sequence: _noraSetSnapshot?.RecentTracks.Count + 1 ?? 1,
        LoadedAtUtc: wasSuggested ? DateTimeOffset.UtcNow : null,
        PlayedAtUtc: deck.IsPlaying ? DateTimeOffset.UtcNow : null,
        StoppedAtUtc: null, DeckId: deck.Id.ToString(),
        EffectiveDurationSeconds: track.DurationSeconds > 0 ? track.DurationSeconds : null,
        EffectiveBpm: deck.EffectiveBpm > 0 ? deck.EffectiveBpm : null,
        PitchPercent: deck.TempoPercent, Energy: track.AudioFeatures.Energy,
        CamelotKey: track.AudioFeatures.CamelotKey,
        VocalPresence: track.AudioFeatures.VocalPresence,
        WasNoraSuggested: wasSuggested,
        EntryTimeSeconds: advice?.EntryTimeSeconds, ExitTimeSeconds: advice?.ExitTimeSeconds,
        AudibleDeckId: CaptureNoraDeckState().PrimaryAudibleDeck?.ToString(),
        CrossfaderPosition: Crossfader, DeckVolume: deck.Volume, CueActive: deck.CueMonitorEnabled);

    private static string NoraTrackId(AudioTrack track) => !string.IsNullOrWhiteSpace(track.FilePath)
        ? NoraCatalogRepository.ComputePathId(track.FilePath)
        : "mix:" + track.Id.ToString("N");

    private static string FormatNoraTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(seconds >= 3600d ? @"h\:mm\:ss" : @"m\:ss");

    private NoraDeckState CaptureNoraDeckState() => NoraDeckStateProvider.Evaluate(
        AllDecks.Select(deck => new NoraDeckSnapshot(
            deck.Id,
            _audioEngine.GetDeckSide(deck.Id),
            deck.Track is not null,
            deck.IsPlaying,
            deck.IsMaster,
            deck.CueMonitorEnabled,
            deck.Volume,
            deck.MeterPeak)).ToArray(),
        Crossfader);

    private string NoraDeckStateSignature(NoraDeckState state)
    {
        var decks = string.Join(',', AllDecks.Select(deck =>
            $"{deck.Id}:{deck.Track?.Id}:{deck.IsPlaying}:{deck.CueMonitorEnabled}:{deck.TempoPercent:0.0}"));
        return $"{state.PrimaryAudibleDeck}|{state.OutgoingDeck}|{state.IncomingDeck}|{state.PreviewDeck}|" +
               $"{state.RecommendedLoadDeck}|{state.IsAmbiguous}|{decks}";
    }

    private void DisposeNora()
    {
        foreach (var deck in AllDecks) deck.PropertyChanged -= OnNoraDeckPropertyChanged;
        _noraRefreshCancellation?.Cancel();
        _noraRefreshCancellation?.Dispose();
        _noraAssistant?.Dispose();
    }
}
