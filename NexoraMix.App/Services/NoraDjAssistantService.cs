using NexoraMix.Core.Models;
using NexoraMix.Core.Services;

namespace NexoraMix.App.Services;

public sealed record NoraDjRecommendation(
    NoraTrackMatch Match,
    AudioTrack? LocalTrack)
{
    public NoraTransitionAdvice? Advice { get; init; }
    public int Position { get; init; }
    public string Title => Match.Track.Title;
    public string Artist => Match.Track.Artist;
    public string Bpm => Match.Track.Bpm is > 0 ? $"{Match.Track.Bpm:0.0}" : "—";
    public string Year => Match.Track.Year?.ToString() ?? "—";
    public string Genre => string.IsNullOrWhiteSpace(Match.Track.Genre) ? "—" : Match.Track.Genre;
    public string Compatibility => $"{Match.Score:0}%";
    public string Explanation => Match.Explanation;
    public string Camelot => Match.Track.AudioFeatures?.CamelotKey ?? "—";
    public string Energy => Match.Track.AudioFeatures?.Energy is double value ? $"{value:P0}" : "—";
    public string Lufs => Match.Track.AudioFeatures?.EstimatedIntegratedLufs is double value ? $"{value:0.0} LUFS" : "—";
    public string Vocal => Match.Track.AudioFeatures?.VocalPresence is { } vocal && vocal != VocalPresence.Unknown
        ? vocal.ToString() : "—";
    public string Pitch => Advice?.SuggestedPitchPercent is double value ? $"{value:+0.00;-0.00;0.00}%" : "—";
    public string Confidence => Advice is null ? $"{Match.Confidence:P0}" : $"{Advice.Confidence:P0}";
    public string RecommendedDeck => Advice?.RecommendedDeck?.ToString() ?? "—";
    public string Entry => Advice is null ? "—" : $"{FormatTime(Advice.EntryTimeSeconds)} · {Advice.EntrySection}";
    public string Exit => Advice is null ? "—" : $"{FormatTime(Advice.ExitTimeSeconds)} · {Advice.ExitSection}";
    public string Mix => Advice is null ? "—" : $"{Advice.TransitionBeats} beat · bass {Advice.BassSwapBeat}";
    public string Risk => Advice?.Risk.ToString().ToUpperInvariant() ?? "—";
    public string FactorBreakdown => Match.Factors.Count == 0
        ? Match.Explanation
        : string.Join("  ·  ", Match.Factors.OrderByDescending(factor => Math.Abs(factor.Contribution))
            .Take(5).Select(factor => $"{factor.Name} {factor.Contribution:+0.0;-0.0;0.0}"));
    public bool IsAvailableLocally => LocalTrack?.CanLoadToDeck == true;
    public string Availability => IsAvailableLocally ? "PRONTA" : "NEL CATALOGO";

    private static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(seconds >= 3600d ? @"h\:mm\:ss" : @"m\:ss");
}

public sealed record NoraDjRecommendationResult(
    IReadOnlyList<NoraDjRecommendation> Recommendations,
    int CatalogTrackCount,
    int SharedTeachingCount,
    string CatalogPath,
    NoraCatalogMetrics CatalogMetrics)
{
    public NoraSetPlan? SetPlan { get; init; }
    public NoraSetMemorySnapshot? SetSnapshot { get; init; }
    public string Status { get; init; } = "Pronta";
}

public sealed class NoraDjAssistantService : IDisposable
{
    private const string DefaultProfileId = "default";
    private static readonly TimeSpan PersonalizationCacheLifetime = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _personalizationGate = new(1, 1);
    private readonly NoraCatalogRepository _catalog = new();
    private readonly NoraPersonalizationStore _personalizationStore = new();
    private readonly NoraSetMemoryStore _setMemoryStore = new();
    private readonly NoraPersonalizationService _personalization = new();
    private readonly NoraSetPlanner _setPlanner = new();
    private NoraPersonalizationSnapshot? _personalizationSnapshot;
    private DateTimeOffset _personalizationCacheExpiresAtUtc;

    public async Task<NoraDjRecommendationResult> RecommendAsync(
        AudioTrack currentTrack,
        IReadOnlyCollection<AudioTrack> localLibrary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentTrack);
        ArgumentNullException.ThrowIfNull(localLibrary);
        cancellationToken.ThrowIfCancellationRequested();

        var localFiles = localLibrary
            .Where(track => track.CanLoadToDeck && !string.IsNullOrWhiteSpace(track.FilePath))
            .Select(track => new NoraCatalogLocalFile(
                track.FilePath!,
                NoraCatalogRepository.ComputePathId(track.FilePath!),
                track.Bpm > 0 ? track.Bpm : null,
                track.AnalysisConfidence > 0 ? track.AnalysisConfidence : null,
                track.AudioFeatures.Status == AudioFeatureAnalysisStatus.Unknown ? null : track.AudioFeatures))
            .ToArray();
        await _catalog.UpsertLocalFilesAsync(localFiles, cancellationToken).ConfigureAwait(false);

        var localById = localLibrary
            .Where(track => !string.IsNullOrWhiteSpace(track.FilePath))
            .GroupBy(track => NoraCatalogRepository.ComputePathId(track.FilePath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var currentId = !string.IsNullOrWhiteSpace(currentTrack.FilePath)
            ? NoraCatalogRepository.ComputePathId(currentTrack.FilePath)
            : "mix:" + currentTrack.Id.ToString("N");
        var catalogCurrent = await _catalog.FindTrackAsync(currentId, cancellationToken).ConfigureAwait(false);
        var current = catalogCurrent is null
            ? ToProfile(currentTrack, currentId)
            : MergeProfile(ToProfile(catalogCurrent), ToProfile(currentTrack, currentId));

        var catalogResult = await _catalog.QueryCandidatesAsync(new(
            current.Id, current.Bpm, current.Year, current.Genre), cancellationToken).ConfigureAwait(false);
        var candidates = catalogResult.Tracks.Select(ToProfile).ToList();
        var catalogIds = candidates.Select(track => track.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        candidates.AddRange(localLibrary
            .Where(track => !ReferenceEquals(track, currentTrack))
            .Select(track => ToProfile(track, !string.IsNullOrWhiteSpace(track.FilePath)
                ? NoraCatalogRepository.ComputePathId(track.FilePath)
                : "mix:" + track.Id.ToString("N")))
            .Where(track => !catalogIds.Contains(track.Id)));

        var distinctCandidates = candidates
            .Where(candidate => !string.Equals(candidate.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => $"{candidate.Title.Trim()}\u001f{candidate.Artist.Trim()}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var personalization = await GetPersonalizationSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var personalizationContext = _personalization.CreateRankingContext(personalization, current.Artist);
        var setSnapshot = await _setMemoryStore.GetActiveSnapshotAsync(DefaultProfileId, cancellationToken)
            .ConfigureAwait(false);
        var rankingContext = personalizationContext with
        {
            RecentTrackIds = setSnapshot?.RecentTrackIds ?? personalizationContext.RecentTrackIds,
            RecentArtists = setSnapshot?.RecentArtists ?? personalizationContext.RecentArtists,
            TargetEnergy = setSnapshot?.TargetEnergy ?? personalizationContext.TargetEnergy
        };
        var matches = NoraTrackCompatibility.Rank(current, distinctCandidates, rankingContext, weights: null, limit: 8);
        var recommendations = new List<NoraDjRecommendation>(matches.Count);
        for (var index = 0; index < matches.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = matches[index];
            var local = localById.GetValueOrDefault(match.Track.Id);
            if (local?.CanLoadToDeck != true)
            {
                var path = await _catalog.ResolveBestFileAsync(match.Track.Id, cancellationToken).ConfigureAwait(false);
                if (path is not null)
                {
                    local = new AudioTrack
                    {
                        Title = match.Track.Title,
                        Artist = match.Track.Artist,
                        Bpm = match.Track.Bpm ?? 0,
                        FilePath = path,
                        SourceKind = TrackSourceKind.LocalFile,
                        DurationSeconds = match.Track.DurationSeconds ?? 0d,
                        AudioFeatures = match.Track.AudioFeatures ?? new TrackAudioFeatures(),
                        AnalysisConfidence = match.Track.AudioFeatures?.OverallConfidence ?? 0d,
                        AnalysisVersion = match.Track.MetadataVersion
                    };
                }
            }
            recommendations.Add(new(match, local) { Position = index + 1 });
        }

        // Mantiene il contratto SharedTeachingCount, ma conta gli insegnamenti
        // realmente persistiti nel profilo DJ locale e riproducibile.
        var teachingCount = personalization.FeedbackCount;
        NoraSetPlan? plan = null;
        var status = "Suggerimenti aggiornati";
        var planCandidates = distinctCandidates.Where(candidate => candidate.IsFileAvailable == true).ToArray();
        if (planCandidates.Length >= 3)
        {
            var trajectory = setSnapshot?.Session.Trajectory ?? new NoraSetTrajectory
            {
                CurrentEnergy = current.AudioFeatures?.Energy,
                TargetEnergy = 0.55d,
                TargetPhase = NoraSetPhase.WarmUp,
                MaximumBpmIncreasePerTrack = 5d,
                AllowEnergyDrop = false
            };
            try
            {
                plan = _setPlanner.CreatePlan(current, planCandidates, trajectory, rankingContext,
                    new NoraSetPlannerOptions { PlanLength = 3 });
                status = $"Piano di 3 tracce pronto · fase {plan.Phase}";
            }
            catch (InvalidOperationException ex)
            {
                status = $"Suggerimenti pronti · piano non disponibile: {ex.Message}";
            }
        }
        else
        {
            status = $"Suggerimenti pronti · servono ancora {3 - planCandidates.Length} file disponibili per il piano";
        }

        return new(recommendations, _catalog.CatalogTrackCount, teachingCount,
            _catalog.DatabasePath, catalogResult.Metrics)
        {
            SetPlan = plan,
            SetSnapshot = setSnapshot,
            Status = status
        };
    }

    public async Task<NoraPersonalizationSnapshot> RecordFeedbackAsync(
        NoraFeedbackType type,
        AudioTrack currentTrack,
        NoraDjRecommendation recommendation,
        bool isExplicit = true,
        double confidence = 1d,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentTrack);
        ArgumentNullException.ThrowIfNull(recommendation);
        var sourceId = !string.IsNullOrWhiteSpace(currentTrack.FilePath)
            ? NoraCatalogRepository.ComputePathId(currentTrack.FilePath)
            : "mix:" + currentTrack.Id.ToString("N");
        var feedback = new NoraFeedbackEvent(
            Guid.NewGuid().ToString("N"),
            DefaultProfileId,
            type,
            isExplicit,
            Math.Clamp(confidence, 0d, 1d),
            sourceId,
            currentTrack.Artist,
            null,
            recommendation.Match.Track.Id,
            recommendation.Match.Track.Artist,
            recommendation.Match.Track.Genre,
            new NoraFeedbackContext
            {
                SourceBpm = currentTrack.Bpm > 0 ? currentTrack.Bpm : null,
                CandidateBpm = recommendation.Match.Track.Bpm,
                SourceEnergy = currentTrack.AudioFeatures.Energy,
                CandidateEnergy = recommendation.Match.Track.AudioFeatures?.Energy,
                LocalHour = DateTimeOffset.Now.Hour
            },
            DateTimeOffset.UtcNow);
        var snapshot = await _personalizationStore.RecordFeedbackAsync(feedback, cancellationToken).ConfigureAwait(false);
        var activeSet = await _setMemoryStore.GetActiveSnapshotAsync(DefaultProfileId, cancellationToken).ConfigureAwait(false);
        if (activeSet?.Session.Status == NoraSetSessionStatus.Active)
        {
            await _setMemoryStore.RecordFeedbackAsync(new NoraSetFeedback(
                "set-" + feedback.Id,
                activeSet.Session.Id,
                RecommendationId: null,
                type,
                isExplicit,
                feedback.Confidence,
                feedback.CreatedAtUtc), cancellationToken).ConfigureAwait(false);
        }
        _personalizationSnapshot = null;
        _personalizationCacheExpiresAtUtc = DateTimeOffset.MinValue;
        return snapshot;
    }

    private async Task<NoraPersonalizationSnapshot> GetPersonalizationSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_personalizationSnapshot is not null && DateTimeOffset.UtcNow < _personalizationCacheExpiresAtUtc)
            return _personalizationSnapshot;
        await _personalizationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_personalizationSnapshot is not null && DateTimeOffset.UtcNow < _personalizationCacheExpiresAtUtc)
                return _personalizationSnapshot;
            // Un singolo feedback esplicito deve avere effetto; la soglia resta
            // configurabile per ogni profilo nello store.
            var profile = await _personalizationStore.GetOrCreateProfileAsync(DefaultProfileId,
                settings: new NoraPersonalizationSettings { MinimumFeedbackCount = 1 },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (profile.Settings == new NoraPersonalizationSettings())
            {
                await _personalizationStore.SaveProfileAsync(profile with
                {
                    Settings = profile.Settings with { MinimumFeedbackCount = 1 }
                }, cancellationToken).ConfigureAwait(false);
            }
            _personalizationSnapshot = await _personalizationStore.LoadSnapshotAsync(
                DefaultProfileId, cancellationToken).ConfigureAwait(false);
            _personalizationCacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(PersonalizationCacheLifetime);
            return _personalizationSnapshot;
        }
        finally
        {
            _personalizationGate.Release();
        }
    }

    public Task<NoraSetSession> StartSetAsync(
        NoraSetPhase phase,
        NoraSetTrajectory? trajectory = null,
        CancellationToken cancellationToken = default) =>
        _setMemoryStore.StartSessionAsync(DefaultProfileId, phase, trajectory, cancellationToken);

    public Task PauseSetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _setMemoryStore.PauseSessionAsync(sessionId, cancellationToken);

    public Task ResumeSetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _setMemoryStore.ResumeSessionAsync(sessionId, cancellationToken);

    public Task CloseSetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _setMemoryStore.CloseSessionAsync(sessionId, cancellationToken);

    public Task<NoraSetMemorySnapshot?> GetActiveSetSnapshotAsync(CancellationToken cancellationToken = default) =>
        _setMemoryStore.GetActiveSnapshotAsync(DefaultProfileId, cancellationToken);

    public Task RecordLoadedTrackAsync(
        NoraSetSessionTrack track,
        CancellationToken cancellationToken = default) =>
        _setMemoryStore.RecordLoadedTrackAsync(track, cancellationToken);

    public Task RecordPlayedTrackAsync(
        NoraSetSessionTrack track,
        CancellationToken cancellationToken = default) =>
        _setMemoryStore.RecordPlayedTrackAsync(track, cancellationToken);

    public Task RecordTransitionAsync(
        NoraSetTransition transition,
        CancellationToken cancellationToken = default) =>
        _setMemoryStore.RecordTransitionAsync(transition, cancellationToken);

    public Task RecordRecommendationAsync(
        NoraSetRecommendation recommendation,
        CancellationToken cancellationToken = default) =>
        _setMemoryStore.RecordRecommendationAsync(recommendation, cancellationToken);

    private static NoraTrackProfile ToProfile(NoraCatalogTrack track) =>
        new(track.Id, track.Title, track.Artist, track.Bpm, track.Year, track.Genre)
        {
            AudioFeatures = track.AudioFeatures,
            IsFileAvailable = track.IsFileAvailable,
            DurationSeconds = track.DurationSeconds,
            MetadataVersion = track.AudioFeatures?.AnalysisVersion ?? 1
        };

    private static NoraTrackProfile MergeProfile(NoraTrackProfile catalog, NoraTrackProfile local) => catalog with
    {
        Bpm = catalog.Bpm is > 0 ? catalog.Bpm : local.Bpm,
        AudioFeatures = catalog.AudioFeatures is { Status: not AudioFeatureAnalysisStatus.Unknown }
            ? catalog.AudioFeatures : local.AudioFeatures,
        IsFileAvailable = catalog.IsFileAvailable == true || local.IsFileAvailable == true,
        DurationSeconds = catalog.DurationSeconds is > 0 ? catalog.DurationSeconds : local.DurationSeconds,
        MetadataVersion = Math.Max(catalog.MetadataVersion, local.MetadataVersion)
    };

    private static NoraTrackProfile ToProfile(AudioTrack track, string id) =>
        new(id, track.Title, track.Artist, track.Bpm > 0 ? track.Bpm : null, null, null)
        {
            AudioFeatures = track.AudioFeatures,
            IsFileAvailable = track.CanLoadToDeck,
            DurationSeconds = track.DurationSeconds > 0 ? track.DurationSeconds : null,
            MetadataVersion = track.AnalysisVersion
        };

    public void Dispose()
    {
        _catalog.Dispose();
        _personalizationStore.Dispose();
        _setMemoryStore.Dispose();
        _personalizationGate.Dispose();
    }
}
