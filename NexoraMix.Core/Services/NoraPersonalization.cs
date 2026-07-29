namespace NexoraMix.Core.Services;

public enum NoraFeedbackType
{
    ExcellentSuggestion,
    NotSuitable,
    TooEnergetic,
    TooSlow,
    AvoidArtist,
    PreferArtist,
    PreferCombination,
    NeverSuggestCombination,
    TransitionSucceeded,
    TransitionFailed,
    SuggestionIgnored,
    TrackLoaded,
    TrackPlayed,
    HeadphonePreviewed,
    TrackRemoved,
    TrackSkipped,
    PitchAdjustedSignificantly,
    DifferentTrackChosen,
    RecommendationUnused,
    TrackStoppedEarly
}

public sealed record NoraFeedbackContext
{
    public double? SourceBpm { get; init; }
    public double? CandidateBpm { get; init; }
    public double? SourceEnergy { get; init; }
    public double? CandidateEnergy { get; init; }
    public string? SetPhase { get; init; }
    public int? LocalHour { get; init; }
}

public sealed record NoraFeedbackEvent(
    string Id,
    string ProfileId,
    NoraFeedbackType Type,
    bool IsExplicit,
    double Confidence,
    string SourceTrackId,
    string? SourceArtist,
    string? SourceGenre,
    string CandidateTrackId,
    string? CandidateArtist,
    string? CandidateGenre,
    NoraFeedbackContext Context,
    DateTimeOffset CreatedAtUtc);

public sealed record NoraPersonalizationSettings
{
    public bool LearningEnabled { get; init; } = true;
    public bool NeutralMode { get; init; }
    public int MinimumFeedbackCount { get; init; } = 3;
    public double DecayHalfLifeDays { get; init; } = 90d;
    public double LearningRate { get; init; } = 0.35d;
    public double MaximumPreferenceMagnitude { get; init; } = 1d;
    public double MaximumScoreAdjustment { get; init; } = 18d;
}

public sealed record NoraDjPreferenceProfile(
    string Id,
    string DisplayName,
    NoraPersonalizationSettings Settings,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NoraLearnedPreference(
    string Key,
    double Value,
    int EvidenceCount,
    DateTimeOffset UpdatedAtUtc);

public sealed record NoraPersonalizationSnapshot(
    string ProfileId,
    NoraPersonalizationSettings Settings,
    int FeedbackCount,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, NoraLearnedPreference> Weights)
{
    public bool IsActive => Settings.LearningEnabled && !Settings.NeutralMode &&
                            FeedbackCount >= Settings.MinimumFeedbackCount;
}

/// <summary>Apprendimento locale, deterministico e ricostruibile dagli eventi originali.</summary>
public sealed class NoraPersonalizationService
{
    private const string ArtistPrefix = "artist:";
    private const string GenrePrefix = "genre:";
    private const string CombinationPrefix = "combination:";

    public NoraPersonalizationSnapshot BuildSnapshot(
        NoraDjPreferenceProfile profile,
        IEnumerable<NoraFeedbackEvent> events,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(events);
        ValidateSettings(profile.Settings);
        var generatedAt = now ?? DateTimeOffset.UtcNow;
        var relevant = events.Where(item => string.Equals(item.ProfileId, profile.Id, StringComparison.Ordinal))
            .OrderBy(item => item.CreatedAtUtc).ToArray();
        if (!profile.Settings.LearningEnabled || profile.Settings.NeutralMode)
            return new(profile.Id, profile.Settings, relevant.Length, generatedAt,
                new Dictionary<string, NoraLearnedPreference>(StringComparer.OrdinalIgnoreCase));

        var aggregates = new Dictionary<string, Aggregate>(StringComparer.OrdinalIgnoreCase);
        foreach (var feedback in relevant)
        {
            var signal = FeedbackSignal(feedback.Type) * Math.Clamp(feedback.Confidence, 0d, 1d);
            if (Math.Abs(signal) < 0.0001d) continue;
            var ageDays = Math.Max(0d, (generatedAt - feedback.CreatedAtUtc).TotalDays);
            var decay = Math.Pow(0.5d, ageDays / profile.Settings.DecayHalfLifeDays);
            var weightedSignal = signal * decay * profile.Settings.LearningRate;
            Add(aggregates, ArtistKey(feedback.CandidateArtist), weightedSignal, feedback.CreatedAtUtc);
            foreach (var genre in GenreTokens(feedback.CandidateGenre))
                Add(aggregates, GenrePrefix + genre, weightedSignal * 0.7d, feedback.CreatedAtUtc);
            Add(aggregates, CombinationKey(feedback.SourceArtist, feedback.CandidateArtist),
                weightedSignal * CombinationMultiplier(feedback.Type), feedback.CreatedAtUtc);

            if (feedback.Type == NoraFeedbackType.TooSlow)
                Add(aggregates, "tempo:slower", -Math.Abs(weightedSignal), feedback.CreatedAtUtc);
            if (feedback.Type == NoraFeedbackType.TooEnergetic)
                Add(aggregates, "energy:higher", -Math.Abs(weightedSignal), feedback.CreatedAtUtc);
        }

        var weights = aggregates.ToDictionary(
            pair => pair.Key,
            pair => new NoraLearnedPreference(
                pair.Key,
                Math.Clamp(pair.Value.Value, -profile.Settings.MaximumPreferenceMagnitude,
                    profile.Settings.MaximumPreferenceMagnitude),
                pair.Value.Count,
                pair.Value.UpdatedAtUtc),
            StringComparer.OrdinalIgnoreCase);
        return new(profile.Id, profile.Settings, relevant.Length, generatedAt, weights);
    }

    public NoraPersonalizationSnapshot UpdateSnapshot(
        NoraDjPreferenceProfile profile,
        NoraPersonalizationSnapshot previous,
        NoraFeedbackEvent feedback,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(feedback);
        var generatedAt = now ?? DateTimeOffset.UtcNow;
        var decayed = previous.Weights.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Value = pair.Value.Value * Math.Pow(0.5d,
                    Math.Max(0d, (generatedAt - previous.GeneratedAtUtc).TotalDays) / profile.Settings.DecayHalfLifeDays),
                UpdatedAtUtc = generatedAt
            }, StringComparer.OrdinalIgnoreCase);
        var one = BuildSnapshot(profile with { Settings = profile.Settings with { MinimumFeedbackCount = 1 } },
            [feedback], generatedAt);
        foreach (var pair in one.Weights)
        {
            if (decayed.TryGetValue(pair.Key, out var existing))
                decayed[pair.Key] = existing with
                {
                    Value = Math.Clamp(existing.Value + pair.Value.Value,
                        -profile.Settings.MaximumPreferenceMagnitude, profile.Settings.MaximumPreferenceMagnitude),
                    EvidenceCount = existing.EvidenceCount + pair.Value.EvidenceCount,
                    UpdatedAtUtc = generatedAt
                };
            else decayed[pair.Key] = pair.Value;
        }
        return new(profile.Id, profile.Settings, previous.FeedbackCount + 1, generatedAt, decayed);
    }

    public IReadOnlyList<NoraTrackMatch> Apply(
        NoraTrackProfile current,
        IEnumerable<NoraTrackMatch> matches,
        NoraPersonalizationSnapshot snapshot,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsActive) return matches.Take(Math.Max(0, limit)).ToArray();

        return matches.Select(match => Apply(current, match, snapshot))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Track.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(match => match.Track.Id, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit)).ToArray();
    }

    public NoraRankingContext CreateRankingContext(
        NoraPersonalizationSnapshot snapshot,
        string? sourceArtist = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsActive) return new NoraRankingContext();
        var artists = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var genres = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var preference in snapshot.Weights.Values)
        {
            if (preference.Key.StartsWith(ArtistPrefix, StringComparison.OrdinalIgnoreCase))
                artists[preference.Key[ArtistPrefix.Length..]] = preference.Value;
            else if (preference.Key.StartsWith(GenrePrefix, StringComparison.OrdinalIgnoreCase))
                genres[preference.Key[GenrePrefix.Length..]] = preference.Value;
        }
        if (!string.IsNullOrWhiteSpace(sourceArtist))
        {
            var prefix = CombinationPrefix + Normalize(sourceArtist) + "=>";
            foreach (var preference in snapshot.Weights.Values.Where(item =>
                         item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                var artist = preference.Key[prefix.Length..];
                artists[artist] = Math.Clamp(artists.GetValueOrDefault(artist) + preference.Value, -1d, 1d);
            }
        }
        return new NoraRankingContext { ArtistPreferences = artists, GenrePreferences = genres };
    }

    public static string ArtistKey(string? artist) => string.IsNullOrWhiteSpace(artist)
        ? string.Empty
        : ArtistPrefix + Normalize(artist);

    public static string CombinationKey(string? sourceArtist, string? candidateArtist) =>
        string.IsNullOrWhiteSpace(sourceArtist) || string.IsNullOrWhiteSpace(candidateArtist)
            ? string.Empty
            : CombinationPrefix + Normalize(sourceArtist) + "=>" + Normalize(candidateArtist);

    private static NoraTrackMatch Apply(
        NoraTrackProfile current,
        NoraTrackMatch match,
        NoraPersonalizationSnapshot snapshot)
    {
        var preferences = new List<NoraLearnedPreference>();
        AddPreference(preferences, snapshot, ArtistKey(match.Track.Artist));
        foreach (var genre in GenreTokens(match.Track.Genre))
            AddPreference(preferences, snapshot, GenrePrefix + genre);
        AddPreference(preferences, snapshot, CombinationKey(current.Artist, match.Track.Artist));
        if (preferences.Count == 0) return match;

        var raw = preferences.Sum(preference => preference.Value);
        var adjustment = Math.Round(Math.Clamp(raw * 12d,
            -snapshot.Settings.MaximumScoreAdjustment, snapshot.Settings.MaximumScoreAdjustment), 1);
        if (Math.Abs(adjustment) < 0.05d) return match;
        var score = Math.Round(Math.Clamp(match.Score + adjustment, 0d, 100d), 1);
        var actualAdjustment = score - match.Score;
        var reason = actualAdjustment >= 0d
            ? $"preferenze del profilo {snapshot.ProfileId}"
            : $"feedback precedenti del profilo {snapshot.ProfileId}";
        var factor = new NoraRankingFactor(
            "Personalizzazione DJ",
            raw,
            snapshot.Settings.MaximumScoreAdjustment,
            actualAdjustment,
            reason,
            Math.Clamp(preferences.Sum(item => item.EvidenceCount) / 10d, 0.2d, 1d),
            actualAdjustment >= 0d ? NoraRankingFactorKind.Bonus : NoraRankingFactorKind.Penalty);
        return match with
        {
            Score = score,
            Explanation = match.Explanation + $" · {actualAdjustment:+0.0;-0.0;0.0} {reason}",
            Factors = match.Factors.Concat([factor]).ToArray()
        };
    }

    private static void AddPreference(
        ICollection<NoraLearnedPreference> target,
        NoraPersonalizationSnapshot snapshot,
        string key)
    {
        if (key.Length > 0 && snapshot.Weights.TryGetValue(key, out var preference)) target.Add(preference);
    }

    private static void Add(IDictionary<string, Aggregate> values, string key, double value, DateTimeOffset updated)
    {
        if (key.Length == 0) return;
        if (values.TryGetValue(key, out var current))
            values[key] = new(current.Value + value, current.Count + 1,
                current.UpdatedAtUtc > updated ? current.UpdatedAtUtc : updated);
        else values[key] = new(value, 1, updated);
    }

    private static double FeedbackSignal(NoraFeedbackType type) => type switch
    {
        NoraFeedbackType.ExcellentSuggestion => 1d,
        NoraFeedbackType.NotSuitable => -1d,
        NoraFeedbackType.TooEnergetic => -0.8d,
        NoraFeedbackType.TooSlow => -0.8d,
        NoraFeedbackType.AvoidArtist => -1.4d,
        NoraFeedbackType.PreferArtist => 1.2d,
        NoraFeedbackType.PreferCombination => 1.3d,
        NoraFeedbackType.NeverSuggestCombination => -1.5d,
        NoraFeedbackType.TransitionSucceeded => 0.8d,
        NoraFeedbackType.TransitionFailed => -0.8d,
        NoraFeedbackType.TrackPlayed => 0.65d,
        NoraFeedbackType.TrackLoaded => 0.3d,
        NoraFeedbackType.HeadphonePreviewed => 0.15d,
        NoraFeedbackType.SuggestionIgnored => -0.15d,
        NoraFeedbackType.TrackRemoved => -0.35d,
        NoraFeedbackType.TrackSkipped => -0.5d,
        NoraFeedbackType.PitchAdjustedSignificantly => -0.25d,
        NoraFeedbackType.DifferentTrackChosen => -0.25d,
        NoraFeedbackType.RecommendationUnused => -0.1d,
        NoraFeedbackType.TrackStoppedEarly => -0.45d,
        _ => 0d
    };

    private static double CombinationMultiplier(NoraFeedbackType type) => type switch
    {
        NoraFeedbackType.PreferCombination or NoraFeedbackType.NeverSuggestCombination => 1.4d,
        NoraFeedbackType.TransitionSucceeded or NoraFeedbackType.TransitionFailed => 1.1d,
        _ => 0.6d
    };

    private static IEnumerable<string> GenreTokens(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split([',', ';', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static void ValidateSettings(NoraPersonalizationSettings settings)
    {
        if (settings.MinimumFeedbackCount < 1 || settings.DecayHalfLifeDays <= 0d ||
            settings.LearningRate <= 0d || settings.MaximumPreferenceMagnitude <= 0d ||
            settings.MaximumScoreAdjustment < 0d)
            throw new ArgumentOutOfRangeException(nameof(settings), "Impostazioni di apprendimento non valide.");
    }

    private sealed record Aggregate(double Value, int Count, DateTimeOffset UpdatedAtUtc);
}
