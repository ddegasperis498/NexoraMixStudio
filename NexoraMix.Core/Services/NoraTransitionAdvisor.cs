using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public enum NoraTransitionRisk
{
    Low,
    Medium,
    High
}

public sealed record NoraTrackTiming(
    double PositionSeconds,
    double DurationSeconds,
    double BeatOffsetSeconds,
    int BeatsPerBar = 4);

public sealed record NoraTransitionAdvice(
    DeckId? RecommendedDeck,
    double? SuggestedPitchPercent,
    bool PitchWithinRange,
    bool SyncRecommended,
    double EntryTimeSeconds,
    int? EntryBar,
    TrackSectionType EntrySection,
    double ExitTimeSeconds,
    int? ExitBar,
    TrackSectionType ExitSection,
    int TransitionBeats,
    int BassSwapBeat,
    string BassSwapAdvice,
    string VocalAdvice,
    NoraTransitionRisk Risk,
    double Confidence,
    IReadOnlyList<string> Reasons,
    bool RequiresExplicitDjAction = true);

public sealed class NoraTransitionAdvisor
{
    private static readonly int[] TransitionLengths = { 64, 32, 16, 8 };

    public NoraTransitionAdvice Advise(
        NoraTrackProfile current,
        NoraTrackProfile candidate,
        NoraDeckState deckState,
        NoraTrackTiming currentTiming,
        NoraTrackTiming candidateTiming,
        double maximumPitchPercent = 16d)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(deckState);
        if (maximumPitchPercent is <= 0d or > 50d) throw new ArgumentOutOfRangeException(nameof(maximumPitchPercent));

        var pitch = CalculatePitch(current.Bpm, candidate.Bpm);
        var pitchWithinRange = pitch.HasValue && Math.Abs(pitch.Value) <= maximumPitchPercent;
        var syncRecommended = pitchWithinRange && current.Bpm is > 0d && candidate.Bpm is > 0d;
        var entrySection = BestSection(candidate.AudioFeatures?.Sections, TrackSectionType.Intro, candidateTiming.PositionSeconds);
        var exitSection = BestSection(current.AudioFeatures?.Sections, TrackSectionType.Outro, currentTiming.PositionSeconds);
        var structureKnown = entrySection is not null && exitSection is not null;
        var desiredBeats = structureKnown ? 32 : 16;
        var remainingSeconds = Math.Max(0d, currentTiming.DurationSeconds - currentTiming.PositionSeconds);
        var transitionBeats = SelectTransitionBeats(desiredBeats, remainingSeconds, current.Bpm);
        var transitionSeconds = current.Bpm is > 0d
            ? transitionBeats * 60d / current.Bpm.Value
            : Math.Min(remainingSeconds, 8d);

        var entryTime = Math.Clamp(
            entrySection?.StartTimeSeconds ?? Math.Max(candidateTiming.BeatOffsetSeconds, candidateTiming.PositionSeconds),
            0d,
            Math.Max(0d, candidateTiming.DurationSeconds));
        var latestExitStart = Math.Max(currentTiming.PositionSeconds,
            currentTiming.DurationSeconds - Math.Max(0d, transitionSeconds));
        var exitTime = Math.Clamp(
            exitSection?.StartTimeSeconds ?? latestExitStart,
            currentTiming.PositionSeconds,
            Math.Max(currentTiming.PositionSeconds, currentTiming.DurationSeconds));
        if (exitTime + transitionSeconds > currentTiming.DurationSeconds)
            exitTime = Math.Max(currentTiming.PositionSeconds, currentTiming.DurationSeconds - transitionSeconds);

        var vocal = VocalRisk(current.AudioFeatures?.VocalPresence, candidate.AudioFeatures?.VocalPresence);
        var risk = !pitchWithinRange || deckState.RecommendedLoadDeck is null
            ? NoraTransitionRisk.High
            : vocal.Risk == NoraTransitionRisk.High || !structureKnown || deckState.IsAmbiguous
                ? NoraTransitionRisk.Medium
                : NoraTransitionRisk.Low;
        var featureConfidence = new[]
            {
                current.AudioFeatures?.OverallConfidence,
                candidate.AudioFeatures?.OverallConfidence,
                entrySection?.Confidence,
                exitSection?.Confidence
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(structureKnown ? 0.55d : 0.35d)
            .Average();
        var confidence = Math.Clamp(deckState.Confidence * 0.45d + featureConfidence * 0.55d, 0d, 1d);
        var reasons = new List<string>
        {
            deckState.RecommendedLoadDeck is DeckId deck
                ? $"Deck {deck} libero e consigliato"
                : "nessun deck libero confermato",
            pitch.HasValue
                ? $"pitch {pitch:+0.00;-0.00;0.00}% · limite ±{maximumPitchPercent:0.#}%"
                : "BPM insufficienti per calcolare pitch e sync",
            structureKnown
                ? $"struttura nota: {exitSection!.SectionType} → {entrySection!.SectionType}"
                : "struttura incompleta: transizione conservativa",
            vocal.Advice
        };

        return new(
            deckState.RecommendedLoadDeck,
            pitch,
            pitchWithinRange,
            syncRecommended,
            entryTime,
            CalculateBar(entryTime, candidateTiming.BeatOffsetSeconds, candidate.Bpm, candidateTiming.BeatsPerBar),
            entrySection?.SectionType ?? TrackSectionType.Unknown,
            exitTime,
            CalculateBar(exitTime, currentTiming.BeatOffsetSeconds, current.Bpm, currentTiming.BeatsPerBar),
            exitSection?.SectionType ?? TrackSectionType.Unknown,
            transitionBeats,
            transitionBeats / 2 + 1,
            $"Bass swap alla battuta {transitionBeats / 2 + 1}: riduci gradualmente il deck in uscita e apri quello in ingresso.",
            vocal.Advice,
            risk,
            confidence,
            reasons,
            RequiresExplicitDjAction: true);
    }

    private static double? CalculatePitch(double? currentBpm, double? candidateBpm)
    {
        if (currentBpm is not > 0d || candidateBpm is not > 0d) return null;
        var compatible = new[] { candidateBpm.Value / 2d, candidateBpm.Value, candidateBpm.Value * 2d }
            .OrderBy(value => Math.Abs(value - currentBpm.Value))
            .First();
        return (currentBpm.Value / compatible - 1d) * 100d;
    }

    private static TrackAudioSection? BestSection(
        IReadOnlyList<TrackAudioSection>? sections,
        TrackSectionType desired,
        double minimumTime) => sections?
        .Where(section => section.SectionType == desired && section.EndTimeSeconds > minimumTime)
        .OrderByDescending(section => section.Confidence)
        .ThenBy(section => section.StartTimeSeconds)
        .FirstOrDefault();

    private static int SelectTransitionBeats(int desired, double remainingSeconds, double? bpm)
    {
        if (bpm is not > 0d) return 8;
        return TransitionLengths
            .Where(beats => beats <= desired)
            .FirstOrDefault(beats => beats * 60d / bpm.Value <= remainingSeconds, 8);
    }

    private static int? CalculateBar(double seconds, double offset, double? bpm, int beatsPerBar)
    {
        if (bpm is not > 0d) return null;
        var beatLength = 60d / bpm.Value;
        var beatIndex = Math.Max(0d, (seconds - Math.Max(0d, offset)) / beatLength);
        return 1 + (int)Math.Floor(beatIndex / Math.Max(1, beatsPerBar));
    }

    private static (NoraTransitionRisk Risk, string Advice) VocalRisk(
        VocalPresence? current,
        VocalPresence? candidate)
    {
        if (current is null or VocalPresence.Unknown || candidate is null or VocalPresence.Unknown)
            return (NoraTransitionRisk.Medium, "Voce non determinata: usa 16 battute, verifica in cuffia e non sovrapporre strofe.");
        if (current == VocalPresence.Predominant && candidate == VocalPresence.Predominant)
            return (NoraTransitionRisk.High, "Rischio voce su voce: attendi la fine della strofa o entra da una sezione strumentale.");
        if (current == VocalPresence.Instrumental || candidate == VocalPresence.Instrumental)
            return (NoraTransitionRisk.Low, "Compatibilità vocale favorevole: almeno una delle due sezioni è strumentale.");
        return (NoraTransitionRisk.Medium, "Mantieni breve la sovrapposizione vocale e verifica il ritornello in cuffia.");
    }
}
