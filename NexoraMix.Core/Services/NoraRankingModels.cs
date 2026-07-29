using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public sealed record NoraTrackProfile(
    string Id,
    string Title,
    string Artist,
    double? Bpm,
    int? Year,
    string? Genre)
{
    public TrackAudioFeatures? AudioFeatures { get; init; }
    public bool? IsFileAvailable { get; init; }
    public double? DurationSeconds { get; init; }
    public int MetadataVersion { get; init; } = 1;
}

public enum NoraRankingFactorKind
{
    Bonus,
    Penalty
}

public sealed record NoraRankingFactor(
    string Name,
    double? RawValue,
    double Weight,
    double Contribution,
    string Reason,
    double Confidence,
    NoraRankingFactorKind Kind);

public sealed record NoraTrackMatch(
    NoraTrackProfile Track,
    double Score,
    double? BpmDifference,
    int? YearDifference,
    bool GenreMatches,
    string Explanation)
{
    public double? SuggestedPitchPercent { get; init; }
    public double Confidence { get; init; }
    public int RankingVersion { get; init; }
    public IReadOnlyList<NoraRankingFactor> Factors { get; init; } = Array.Empty<NoraRankingFactor>();
}

public sealed record NoraRankingWeights
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;
    public double Bpm { get; init; } = 18d;
    public double Harmonic { get; init; } = 16d;
    public double Energy { get; init; } = 14d;
    public double Genre { get; init; } = 10d;
    public double Era { get; init; } = 6d;
    public double Vocal { get; init; } = 8d;
    public double FileAvailability { get; init; } = 8d;
    public double Transition { get; init; } = 12d;
    public double Personalization { get; init; } = 8d;
    public double MaximumUncertaintyPenalty { get; init; } = 18d;
    public double RecentlyPlayedPenalty { get; init; } = 30d;
    public double ArtistFatiguePenalty { get; init; } = 12d;

    public double PositiveWeightTotal =>
        Bpm + Harmonic + Energy + Genre + Era + Vocal + FileAvailability + Transition + Personalization;
}

public sealed record NoraRankingContext
{
    public IReadOnlySet<string> RecentTrackIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> RecentArtists { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, double> ArtistPreferences { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, double> GenrePreferences { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    public double? TargetEnergy { get; init; }
    public double MaximumPitchPercent { get; init; } = 16d;
}
