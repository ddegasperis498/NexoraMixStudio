namespace NexoraMix.Core.Services;

public sealed record NoraTrackProfile(
    string Id,
    string Title,
    string Artist,
    double? Bpm,
    int? Year,
    string? Genre);

public sealed record NoraTrackMatch(
    NoraTrackProfile Track,
    double Score,
    double? BpmDifference,
    int? YearDifference,
    bool GenreMatches,
    string Explanation);

public static class NoraTrackCompatibility
{
    public static IReadOnlyList<NoraTrackMatch> Rank(
        NoraTrackProfile current,
        IEnumerable<NoraTrackProfile> candidates,
        int limit = 5)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => !string.Equals(candidate.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => Score(current, candidate))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.BpmDifference ?? double.MaxValue)
            .ThenBy(match => match.YearDifference ?? int.MaxValue)
            .ThenBy(match => match.Track.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Max(0, limit))
            .ToArray();
    }

    private static NoraTrackMatch Score(NoraTrackProfile current, NoraTrackProfile candidate)
    {
        var bpmDifference = CompatibleBpmDifference(current.Bpm, candidate.Bpm);
        var bpmScore = bpmDifference switch
        {
            null => 0d,
            <= 1.5d => 55d,
            <= 4d => 50d,
            <= 8d => 40d,
            <= 14d => 22d,
            _ => 0d
        };

        var genreScore = GenreScore(current.Genre, candidate.Genre, out var genreMatches);
        int? yearDifference = current.Year.HasValue && candidate.Year.HasValue
            ? Math.Abs(current.Year.Value - candidate.Year.Value)
            : null;
        var yearScore = yearDifference switch
        {
            null => 0d,
            <= 2 => 15d,
            <= 5 => 12d,
            <= 10 => 8d,
            <= 20 => 4d,
            _ => 0d
        };

        var score = Math.Round(bpmScore + genreScore + yearScore, 1);
        var reasons = new List<string>(3);
        if (bpmDifference.HasValue) reasons.Add($"BPM Δ {bpmDifference:0.0}");
        if (genreMatches) reasons.Add($"genere {candidate.Genre}");
        else if (!string.IsNullOrWhiteSpace(candidate.Genre)) reasons.Add($"genere {candidate.Genre} diverso");
        if (yearDifference.HasValue) reasons.Add($"anno Δ {yearDifference}");
        if (reasons.Count == 0) reasons.Add("metadati insufficienti");

        return new NoraTrackMatch(
            candidate,
            score,
            bpmDifference,
            yearDifference,
            genreMatches,
            string.Join(" · ", reasons));
    }

    private static double? CompatibleBpmDifference(double? current, double? candidate)
    {
        if (current is not > 0 || candidate is not > 0) return null;
        return new[] { candidate.Value / 2d, candidate.Value, candidate.Value * 2d }
            .Min(value => Math.Abs(current.Value - value));
    }

    private static double GenreScore(string? current, string? candidate, out bool matches)
    {
        matches = false;
        var currentGenres = GenreTokens(current);
        var candidateGenres = GenreTokens(candidate);
        if (currentGenres.Count == 0 || candidateGenres.Count == 0) return 0d;

        if (currentGenres.SetEquals(candidateGenres))
        {
            matches = true;
            return 30d;
        }

        matches = currentGenres.Overlaps(candidateGenres);
        return matches ? 22d : 0d;
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
}
