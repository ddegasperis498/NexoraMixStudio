using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public enum NoraSetPhase
{
    WarmUp,
    ProgressiveBuild,
    Sustain,
    Peak,
    SecondPeak,
    ControlledBreak,
    Recovery,
    Closing,
    AfterParty,
    Free
}

public enum NoraSetSessionStatus
{
    Active,
    Paused,
    Closed
}

public sealed record NoraSetTrajectory
{
    public double? CurrentEnergy { get; init; }
    public double? TargetEnergy { get; init; }
    public int? TargetMinutes { get; init; }
    public NoraSetPhase TargetPhase { get; init; } = NoraSetPhase.Free;
    public double? MaximumBpmIncreasePerTrack { get; init; }
    public bool? AllowEnergyDrop { get; init; }
}

public sealed record NoraSetSession(
    string Id,
    string ProfileId,
    NoraSetSessionStatus Status,
    NoraSetPhase Phase,
    NoraSetTrajectory Trajectory,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? PausedAtUtc = null,
    DateTimeOffset? ResumedAtUtc = null,
    DateTimeOffset? ClosedAtUtc = null);

public sealed record NoraSetSessionTrack(
    string Id,
    string SessionId,
    string TrackId,
    string Title,
    string Artist,
    string? Genre,
    int Sequence,
    DateTimeOffset? LoadedAtUtc,
    DateTimeOffset? PlayedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string? DeckId,
    double? EffectiveDurationSeconds,
    double? EffectiveBpm,
    double? PitchPercent,
    double? Energy,
    string? CamelotKey,
    VocalPresence? VocalPresence,
    bool? WasNoraSuggested,
    double? EntryTimeSeconds,
    double? ExitTimeSeconds,
    string? AudibleDeckId,
    double? CrossfaderPosition,
    double? DeckVolume,
    bool? CueActive);

public sealed record NoraSetTransition(
    string Id,
    string SessionId,
    string? OutgoingTrackId,
    string? IncomingTrackId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? TransitionType,
    int? DurationBeats,
    double? PitchPercent,
    int? BassSwapBeat,
    string? VocalRisk,
    bool? Succeeded);

public sealed record NoraSetRecommendation(
    string Id,
    string SessionId,
    string CurrentTrackId,
    string CandidateTrackId,
    int Position,
    double Score,
    bool? WasLoaded,
    bool? WasPlayed,
    DateTimeOffset CreatedAtUtc);

public sealed record NoraSetFeedback(
    string Id,
    string SessionId,
    string? RecommendationId,
    NoraFeedbackType Type,
    bool IsExplicit,
    double Confidence,
    DateTimeOffset CreatedAtUtc);

public sealed record NoraSetMemorySnapshot(
    NoraSetSession Session,
    IReadOnlyList<NoraSetSessionTrack> RecentTracks,
    IReadOnlyList<NoraSetTransition> RecentTransitions,
    IReadOnlyList<NoraSetRecommendation> RecentRecommendations,
    IReadOnlySet<string> RecentTrackIds,
    IReadOnlySet<string> RecentArtists,
    int ConsecutiveVocalTracks,
    double? CurrentEnergy,
    double? TargetEnergy);

public static class NoraSetMemory
{
    public static NoraSetMemorySnapshot CreateSnapshot(
        NoraSetSession session,
        IEnumerable<NoraSetSessionTrack> tracks,
        IEnumerable<NoraSetTransition>? transitions = null,
        IEnumerable<NoraSetRecommendation>? recommendations = null,
        int recentLimit = 20)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tracks);
        var recent = tracks
            .Where(track => track.PlayedAtUtc.HasValue)
            .OrderByDescending(track => track.PlayedAtUtc)
            .ThenByDescending(track => track.Sequence)
            .Take(Math.Max(1, recentLimit))
            .ToArray();
        var vocalStreak = 0;
        foreach (var track in recent)
        {
            if (track.VocalPresence is VocalPresence.Occasional or VocalPresence.Predominant) vocalStreak++;
            else break;
        }
        return new(
            session,
            recent,
            (transitions ?? []).OrderByDescending(item => item.StartedAtUtc).Take(recentLimit).ToArray(),
            (recommendations ?? []).OrderByDescending(item => item.CreatedAtUtc).Take(recentLimit).ToArray(),
            recent.Select(track => track.TrackId).ToHashSet(StringComparer.OrdinalIgnoreCase),
            recent.Where(track => !string.IsNullOrWhiteSpace(track.Artist)).Select(track => track.Artist)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            vocalStreak,
            recent.Select(track => track.Energy).FirstOrDefault(energy => energy.HasValue),
            session.Trajectory.TargetEnergy);
    }
}
