using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public static class NoraTrackCompatibility
{
    public static IReadOnlyList<NoraTrackMatch> Rank(
        NoraTrackProfile current,
        IEnumerable<NoraTrackProfile> candidates,
        int limit = 5)
        => Rank(current, candidates, context: null, weights: null, limit);

    public static IReadOnlyList<NoraTrackMatch> Rank(
        NoraTrackProfile current,
        IEnumerable<NoraTrackProfile> candidates,
        NoraRankingContext? context,
        NoraRankingWeights? weights = null,
        int limit = 5)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);
        context ??= new NoraRankingContext();
        weights ??= new NoraRankingWeights();
        ValidateWeights(weights);

        return candidates
            .Where(candidate => !string.Equals(candidate.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => Score(current, candidate, context, weights))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.BpmDifference ?? double.MaxValue)
            .ThenBy(match => match.YearDifference ?? int.MaxValue)
            .ThenBy(match => match.Track.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(match => match.Track.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(match => match.Track.Id, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit))
            .ToArray();
    }

    private static NoraTrackMatch Score(
        NoraTrackProfile current,
        NoraTrackProfile candidate,
        NoraRankingContext context,
        NoraRankingWeights weights)
    {
        var factors = new List<FactorDraft>();
        var bpmDifference = CompatibleBpmDifference(current.Bpm, candidate.Bpm, out var pitchPercent);
        if (bpmDifference.HasValue && pitchPercent.HasValue)
        {
            var normalized = Math.Clamp(1d - Math.Abs(pitchPercent.Value) / Math.Max(1d, context.MaximumPitchPercent), 0d, 1d);
            Add(factors, "BPM e pitch", Math.Abs(pitchPercent.Value), weights.Bpm, normalized,
                $"pitch suggerito {pitchPercent:+0.00;-0.00;0.00}% · BPM Δ {bpmDifference:0.0}", BpmConfidence(current, candidate));
            var transition = Math.Abs(pitchPercent.Value) <= context.MaximumPitchPercent
                ? 1d - Math.Abs(pitchPercent.Value) / Math.Max(1d, context.MaximumPitchPercent) * 0.35d
                : 0d;
            if (candidate.DurationSeconds is > 0 and < 45) transition *= 0.7d;
            Add(factors, "Fattibilità transizione", Math.Abs(pitchPercent.Value), weights.Transition, transition,
                transition > 0d ? "variazione tempo eseguibile dal deck" : "pitch oltre il limite configurato", BpmConfidence(current, candidate));
        }

        var currentCamelot = current.AudioFeatures?.CamelotKey;
        var candidateCamelot = candidate.AudioFeatures?.CamelotKey;
        if (!string.IsNullOrWhiteSpace(currentCamelot) && !string.IsNullOrWhiteSpace(candidateCamelot))
        {
            var harmonic = CamelotCompatibilityService.Compare(currentCamelot, candidateCamelot);
            Add(factors, "Compatibilità Camelot", harmonic.Score, weights.Harmonic, harmonic.Score,
                $"Camelot {currentCamelot} → {candidateCamelot} · {harmonic.Relation}",
                MinimumConfidence(current.AudioFeatures?.KeyConfidence, candidate.AudioFeatures?.KeyConfidence));
        }

        var currentEnergy = current.AudioFeatures?.Energy;
        var candidateEnergy = candidate.AudioFeatures?.Energy;
        if (candidateEnergy.HasValue && (currentEnergy.HasValue || context.TargetEnergy.HasValue))
        {
            var target = context.TargetEnergy ?? currentEnergy!.Value;
            var difference = Math.Abs(candidateEnergy.Value - target);
            Add(factors, "Traiettoria energetica", difference, weights.Energy,
                Math.Clamp(1d - difference / 0.65d, 0d, 1d),
                context.TargetEnergy.HasValue
                    ? $"energia {candidateEnergy:P0} verso obiettivo {target:P0}"
                    : $"variazione energia {candidateEnergy.Value - currentEnergy!.Value:+0%;-0%;0%}",
                MinimumConfidence(current.AudioFeatures?.OverallConfidence, candidate.AudioFeatures?.OverallConfidence));
        }

        var genreValue = GenreCompatibility(current.Genre, candidate.Genre, out var genreMatches);
        if (genreValue.HasValue)
            Add(factors, "Genere", genreValue, weights.Genre, genreValue.Value,
                genreMatches ? $"genere compatibile · {candidate.Genre}" : $"genere differente · {candidate.Genre}", 1d);

        int? yearDifference = current.Year.HasValue && candidate.Year.HasValue
            ? Math.Abs(current.Year.Value - candidate.Year.Value)
            : null;
        if (yearDifference.HasValue)
        {
            var era = Math.Clamp(1d - yearDifference.Value / 30d, 0d, 1d);
            Add(factors, "Epoca", yearDifference, weights.Era, era, $"anno Δ {yearDifference}", 1d);
        }

        var vocal = VocalCompatibility(current.AudioFeatures?.VocalPresence, candidate.AudioFeatures?.VocalPresence);
        if (vocal.HasValue)
            Add(factors, "Compatibilità vocale", vocal, weights.Vocal, vocal.Value,
                vocal >= 0.8d ? "densità vocale compatibile" : "rischio di sovrapposizione vocale",
                MinimumConfidence(current.AudioFeatures?.VocalConfidence, candidate.AudioFeatures?.VocalConfidence));

        if (candidate.IsFileAvailable.HasValue)
            Add(factors, "Disponibilità file", candidate.IsFileAvailable.Value ? 1d : 0d, weights.FileAvailability,
                candidate.IsFileAvailable.Value ? 1d : 0d,
                candidate.IsFileAvailable.Value ? "file disponibile localmente" : "file non disponibile", 1d);

        var preference = PreferenceScore(candidate, context);
        if (preference.HasValue)
            Add(factors, "Preferenze DJ", preference, weights.Personalization, (preference.Value + 1d) / 2d,
                preference >= 0d ? "preferenza appresa positiva" : "preferenza appresa negativa", 1d);

        var availableWeight = factors.Sum(factor => factor.Weight);
        var scale = availableWeight > 0d ? 100d / availableWeight : 0d;
        var breakdown = factors.Select(factor => new NoraRankingFactor(
            factor.Name,
            factor.RawValue,
            factor.Weight,
            factor.Score * factor.Weight * factor.Confidence * scale,
            factor.Reason,
            factor.Confidence,
            NoraRankingFactorKind.Bonus)).ToList();
        var uncertaintyRatio = weights.PositiveWeightTotal > 0d
            ? Math.Clamp(1d - availableWeight / weights.PositiveWeightTotal, 0d, 1d)
            : 1d;
        var uncertaintyPenalty = uncertaintyRatio * weights.MaximumUncertaintyPenalty;
        breakdown.Add(new("Incertezza dati", uncertaintyRatio, weights.MaximumUncertaintyPenalty,
            -uncertaintyPenalty, $"{uncertaintyRatio:P0} dei fattori non disponibile", 1d, NoraRankingFactorKind.Penalty));

        if (context.RecentTrackIds.Contains(candidate.Id))
            breakdown.Add(new("Traccia recente", 1d, weights.RecentlyPlayedPenalty, -weights.RecentlyPlayedPenalty,
                "traccia già suonata recentemente", 1d, NoraRankingFactorKind.Penalty));
        if (!string.IsNullOrWhiteSpace(candidate.Artist) && context.RecentArtists.Contains(candidate.Artist))
            breakdown.Add(new("Affaticamento artista", 1d, weights.ArtistFatiguePenalty, -weights.ArtistFatiguePenalty,
                $"{candidate.Artist} già utilizzato recentemente", 1d, NoraRankingFactorKind.Penalty));

        var unclamped = breakdown.Sum(factor => factor.Contribution);
        var score = Math.Round(Math.Clamp(unclamped, 0d, 100d), 1);
        var clampAdjustment = score - unclamped;
        if (Math.Abs(clampAdjustment) > 0.0001d)
            breakdown.Add(new("Limite punteggio", unclamped, 0d, clampAdjustment,
                "punteggio riportato nel range 0–100", 1d,
                clampAdjustment < 0d ? NoraRankingFactorKind.Penalty : NoraRankingFactorKind.Bonus));

        var confidence = weights.PositiveWeightTotal > 0d
            ? Math.Clamp(availableWeight / weights.PositiveWeightTotal, 0d, 1d)
            : 0d;
        var explanation = breakdown.Count == 1
            ? "metadati insufficienti"
            : string.Join(" · ", breakdown
                .Where(factor => Math.Abs(factor.Contribution) >= 0.05d)
                .OrderByDescending(factor => Math.Abs(factor.Contribution))
                .Take(5)
                .Select(factor => $"{factor.Contribution:+0.0;-0.0;0.0} {factor.Reason}"));

        return new NoraTrackMatch(
            candidate,
            score,
            bpmDifference,
            yearDifference,
            genreMatches,
            explanation)
        {
            SuggestedPitchPercent = pitchPercent,
            Confidence = confidence,
            RankingVersion = weights.Version,
            Factors = breakdown
        };
    }

    private static double? CompatibleBpmDifference(double? current, double? candidate, out double? pitchPercent)
    {
        pitchPercent = null;
        if (current is not > 0 || candidate is not > 0) return null;
        var compatible = new[] { candidate.Value / 2d, candidate.Value, candidate.Value * 2d }
            .OrderBy(value => Math.Abs(current.Value - value))
            .First();
        pitchPercent = (current.Value / compatible - 1d) * 100d;
        return Math.Abs(current.Value - compatible);
    }

    private static double? GenreCompatibility(string? current, string? candidate, out bool matches)
    {
        matches = false;
        var currentGenres = GenreTokens(current);
        var candidateGenres = GenreTokens(candidate);
        if (currentGenres.Count == 0 || candidateGenres.Count == 0) return null;

        if (currentGenres.SetEquals(candidateGenres))
        {
            matches = true;
            return 1d;
        }

        matches = currentGenres.Overlaps(candidateGenres);
        return matches ? 0.75d : 0d;
    }

    private static double? VocalCompatibility(VocalPresence? current, VocalPresence? candidate)
    {
        if (current is null or VocalPresence.Unknown || candidate is null or VocalPresence.Unknown) return null;
        if (current == VocalPresence.Instrumental || candidate == VocalPresence.Instrumental) return 1d;
        if (current == VocalPresence.Predominant && candidate == VocalPresence.Predominant) return 0.15d;
        if (current == VocalPresence.Predominant || candidate == VocalPresence.Predominant) return 0.55d;
        return 0.8d;
    }

    private static double? PreferenceScore(NoraTrackProfile candidate, NoraRankingContext context)
    {
        var values = new List<double>();
        if (!string.IsNullOrWhiteSpace(candidate.Artist) && context.ArtistPreferences.TryGetValue(candidate.Artist, out var artist))
            values.Add(Math.Clamp(artist, -1d, 1d));
        foreach (var genre in GenreTokens(candidate.Genre))
            if (context.GenrePreferences.TryGetValue(genre, out var preference)) values.Add(Math.Clamp(preference, -1d, 1d));
        return values.Count > 0 ? values.Average() : null;
    }

    private static double BpmConfidence(NoraTrackProfile current, NoraTrackProfile candidate) =>
        MinimumConfidence(current.AudioFeatures?.BpmConfidence, candidate.AudioFeatures?.BpmConfidence);

    private static double MinimumConfidence(double? current, double? candidate) =>
        current.HasValue && candidate.HasValue
            ? Math.Clamp(Math.Min(current.Value, candidate.Value), 0d, 1d)
            : 1d;

    private static void Add(
        ICollection<FactorDraft> factors,
        string name,
        double? rawValue,
        double weight,
        double score,
        string reason,
        double confidence)
    {
        if (weight <= 0d) return;
        factors.Add(new(name, rawValue, weight, Math.Clamp(score, 0d, 1d), reason, Math.Clamp(confidence, 0d, 1d)));
    }

    private static void ValidateWeights(NoraRankingWeights weights)
    {
        var allWeights = new[]
        {
            weights.Bpm, weights.Harmonic, weights.Energy, weights.Genre, weights.Era,
            weights.Vocal, weights.FileAvailability, weights.Transition, weights.Personalization,
            weights.MaximumUncertaintyPenalty, weights.RecentlyPlayedPenalty, weights.ArtistFatiguePenalty
        };
        if (weights.PositiveWeightTotal <= 0d || allWeights.Any(value => value < 0d || double.IsNaN(value) || double.IsInfinity(value)))
            throw new ArgumentOutOfRangeException(nameof(weights), "I pesi devono essere non negativi e almeno un fattore deve essere attivo.");
    }

    private static HashSet<string> GenreTokens(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new(StringComparer.OrdinalIgnoreCase)
            : value.Split([',', ';', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeGenre)
                .Where(token => token.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeGenre(string value) =>
        string.Join(' ', value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed record FactorDraft(
        string Name,
        double? RawValue,
        double Weight,
        double Score,
        string Reason,
        double Confidence);
}
