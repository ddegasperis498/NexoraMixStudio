namespace NexoraMix.Core.Services;

public sealed record NoraSetPlannerOptions
{
    public int PlanLength { get; init; } = 3;
    public int BeamWidth { get; init; } = 5;
    public int CandidatesPerLevel { get; init; } = 10;
    public int AlternativeCount { get; init; } = 2;
}

public sealed record NoraSetPlanStep(
    int Position,
    NoraTrackProfile Track,
    double PairScore,
    double? TargetEnergy,
    double Risk,
    IReadOnlyList<string> Reasons);

public sealed record NoraSetPlanAlternative(
    int Position,
    NoraTrackProfile Track,
    double Score,
    string Reason);

public sealed record NoraSetPlan(
    IReadOnlyList<NoraSetPlanStep> Sequence,
    double Score,
    double Risk,
    IReadOnlyList<NoraSetPlanAlternative> Alternatives,
    IReadOnlyList<string> Reasons,
    int ExpandedNodes,
    NoraSetPhase Phase);

public sealed class NoraSetPlanner
{
    public NoraSetPlan CreatePlan(
        NoraTrackProfile current,
        IEnumerable<NoraTrackProfile> candidates,
        NoraSetTrajectory trajectory,
        NoraRankingContext? context = null,
        NoraSetPlannerOptions? options = null,
        NoraRankingWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(trajectory);
        context ??= new NoraRankingContext();
        options ??= new NoraSetPlannerOptions();
        weights ??= new NoraRankingWeights();
        if (options.PlanLength is < 3 or > 5) throw new ArgumentOutOfRangeException(nameof(options), "Il piano deve contenere da 3 a 5 tracce.");
        if (options.BeamWidth is < 1 or > 50 || options.CandidatesPerLevel is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options), "Beam width o pool candidati non valido.");

        var pool = candidates
            .Where(candidate => !string.Equals(candidate.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => !context.RecentTrackIds.Contains(candidate.Id))
            .Where(candidate => candidate.IsFileAvailable != false)
            .DistinctBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (pool.Length < options.PlanLength)
            throw new InvalidOperationException($"Servono almeno {options.PlanLength} candidati distinti e utilizzabili.");

        var initial = new BeamState(current, [], 0d, 0d, []);
        var beam = new[] { initial };
        var expanded = 0;
        for (var depth = 0; depth < options.PlanLength; depth++)
        {
            var targetEnergy = TargetEnergy(current, trajectory, depth + 1, options.PlanLength);
            var nextBeam = new List<BeamState>();
            foreach (var state in beam)
            {
                var usedIds = state.Sequence.Select(step => step.Track.Id)
                    .Append(current.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var recentIds = context.RecentTrackIds.Concat(usedIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var recentArtists = context.RecentArtists.Concat(state.Sequence.Select(step => step.Track.Artist))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var pairContext = context with
                {
                    RecentTrackIds = recentIds,
                    RecentArtists = recentArtists,
                    TargetEnergy = targetEnergy
                };
                var available = pool.Where(candidate => !usedIds.Contains(candidate.Id));
                var ranked = NoraTrackCompatibility.Rank(
                    state.Last,
                    available,
                    pairContext,
                    weights,
                    options.CandidatesPerLevel);
                foreach (var match in ranked)
                {
                    if (!TrajectoryAllows(state.Last, match.Track, trajectory)) continue;
                    expanded++;
                    var reasons = match.Factors
                        .Where(factor => Math.Abs(factor.Contribution) >= 2d)
                        .OrderByDescending(factor => Math.Abs(factor.Contribution))
                        .Take(4)
                        .Select(factor => factor.Reason)
                        .ToArray();
                    var step = new NoraSetPlanStep(
                        depth + 1,
                        match.Track,
                        match.Score,
                        targetEnergy,
                        Math.Round(100d - match.Score, 1),
                        reasons);
                    nextBeam.Add(new(
                        match.Track,
                        state.Sequence.Append(step).ToArray(),
                        state.TotalScore + match.Score,
                        state.TotalRisk + step.Risk,
                        state.Alternatives));
                }
            }

            beam = nextBeam
                .OrderByDescending(state => state.TotalScore - state.TotalRisk * 0.15d)
                .ThenBy(state => string.Join('|', state.Sequence.Select(step => step.Track.Id)), StringComparer.OrdinalIgnoreCase)
                .Take(options.BeamWidth)
                .ToArray();
            if (beam.Length == 0) throw new InvalidOperationException("Nessuna sequenza rispetta la traiettoria configurata.");
        }

        var best = beam[0];
        var alternatives = BuildAlternatives(best, pool, current, trajectory, context, weights, options);
        var averageScore = best.Sequence.Average(step => step.PairScore);
        var averageRisk = best.Sequence.Average(step => step.Risk);
        return new(
            best.Sequence,
            Math.Round(averageScore, 1),
            Math.Round(averageRisk, 1),
            alternatives,
            [
                $"Piano {options.PlanLength} tracce · fase {trajectory.TargetPhase}",
                trajectory.TargetEnergy.HasValue ? $"Obiettivo energia {trajectory.TargetEnergy:P0}" : "Traiettoria energia libera",
                $"Ricerca limitata: beam {options.BeamWidth}, pool {options.CandidatesPerLevel}"
            ],
            expanded,
            trajectory.TargetPhase);
    }

    private static IReadOnlyList<NoraSetPlanAlternative> BuildAlternatives(
        BeamState best,
        IReadOnlyList<NoraTrackProfile> pool,
        NoraTrackProfile current,
        NoraSetTrajectory trajectory,
        NoraRankingContext context,
        NoraRankingWeights weights,
        NoraSetPlannerOptions options)
    {
        var result = new List<NoraSetPlanAlternative>();
        var previous = current;
        foreach (var step in best.Sequence)
        {
            var excluded = best.Sequence.Select(item => item.Track.Id).Append(current.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ranked = NoraTrackCompatibility.Rank(previous,
                pool.Where(candidate => !excluded.Contains(candidate.Id)),
                context with { TargetEnergy = step.TargetEnergy }, weights, options.AlternativeCount);
            result.AddRange(ranked.Select(match => new NoraSetPlanAlternative(
                step.Position, match.Track, match.Score, match.Explanation)));
            previous = step.Track;
        }
        return result;
    }

    private static double? TargetEnergy(
        NoraTrackProfile current,
        NoraSetTrajectory trajectory,
        int position,
        int length)
    {
        var start = trajectory.CurrentEnergy ?? current.AudioFeatures?.Energy;
        var target = trajectory.TargetEnergy ?? DefaultTarget(trajectory.TargetPhase, start);
        if (!start.HasValue || !target.HasValue) return target;
        return Math.Clamp(start.Value + (target.Value - start.Value) * position / length, 0d, 1d);
    }

    private static double? DefaultTarget(NoraSetPhase phase, double? current) => phase switch
    {
        NoraSetPhase.WarmUp => 0.45d,
        NoraSetPhase.ProgressiveBuild => Math.Min(0.8d, (current ?? 0.5d) + 0.18d),
        NoraSetPhase.Sustain => current,
        NoraSetPhase.Peak => 0.92d,
        NoraSetPhase.SecondPeak => 0.88d,
        NoraSetPhase.ControlledBreak => 0.48d,
        NoraSetPhase.Recovery => 0.65d,
        NoraSetPhase.Closing => 0.42d,
        NoraSetPhase.AfterParty => 0.72d,
        _ => null
    };

    private static bool TrajectoryAllows(
        NoraTrackProfile previous,
        NoraTrackProfile candidate,
        NoraSetTrajectory trajectory)
    {
        if (trajectory.AllowEnergyDrop == false &&
            previous.AudioFeatures?.Energy is double previousEnergy &&
            candidate.AudioFeatures?.Energy is double candidateEnergy &&
            candidateEnergy < previousEnergy - 0.035d)
            return false;
        if (trajectory.MaximumBpmIncreasePerTrack is double maximumIncrease &&
            previous.Bpm is > 0 && candidate.Bpm is > 0 &&
            new[] { candidate.Bpm.Value / 2d, candidate.Bpm.Value, candidate.Bpm.Value * 2d }
                .OrderBy(value => Math.Abs(value - previous.Bpm.Value))
                .First() - previous.Bpm.Value > maximumIncrease)
            return false;
        return true;
    }

    private sealed record BeamState(
        NoraTrackProfile Last,
        IReadOnlyList<NoraSetPlanStep> Sequence,
        double TotalScore,
        double TotalRisk,
        IReadOnlyList<NoraSetPlanAlternative> Alternatives);
}
