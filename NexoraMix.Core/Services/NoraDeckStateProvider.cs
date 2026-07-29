using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public sealed record NoraDeckSnapshot(
    DeckId Id,
    DeckSide Side,
    bool TrackLoaded,
    bool IsPlaying,
    bool IsMaster,
    bool CueEnabled,
    double Volume,
    double MeterPeak);

public sealed record NoraDeckState(
    DeckId? PrimaryAudibleDeck,
    DeckId? OutgoingDeck,
    DeckId? IncomingDeck,
    DeckId? PreviewDeck,
    DeckId? RecommendedLoadDeck,
    double Confidence,
    bool IsAmbiguous,
    string Reason);

public static class NoraDeckStateProvider
{
    private const double MinimumAudibleGain = 0.025d;
    private const double AmbiguityRatio = 0.75d;

    public static NoraDeckState Evaluate(
        IReadOnlyCollection<NoraDeckSnapshot> decks,
        double crossfader)
    {
        ArgumentNullException.ThrowIfNull(decks);
        var ordered = decks.OrderBy(deck => deck.Id).ToArray();
        if (ordered.Length == 0)
            return new(null, null, null, null, null, 0d, false, "Nessun deck disponibile.");

        var gains = SyncMath.EqualPowerCrossfade(Math.Clamp(crossfader, -1d, 1d));
        var scored = ordered
            .Select(deck => new ScoredDeck(deck, AudibilityScore(deck, gains.Left, gains.Right)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Deck.Id)
            .ToArray();
        var audible = scored.Where(item => item.Score >= MinimumAudibleGain).ToArray();

        DeckId? primary = null;
        DeckId? outgoing = null;
        DeckId? incoming = null;
        var ambiguous = false;
        var confidence = 0d;
        string audibleReason;

        if (audible.Length == 0)
        {
            audibleReason = "Nessun deck in riproduzione raggiunge l'uscita master.";
        }
        else if (audible.Length == 1)
        {
            primary = audible[0].Deck.Id;
            outgoing = primary;
            confidence = Math.Clamp(0.55d + audible[0].Score * 0.45d, 0d, 1d);
            audibleReason = $"Deck {primary} chiaramente udibile sul master.";
        }
        else
        {
            var first = audible[0];
            var second = audible[1];
            var ratio = first.Score <= 0d ? 1d : second.Score / first.Score;
            ambiguous = ratio >= AmbiguityRatio;

            var master = audible.FirstOrDefault(item => item.Deck.IsMaster);
            if (master is not null)
            {
                outgoing = master.Deck.Id;
                incoming = audible.First(item => item.Deck.Id != master.Deck.Id).Deck.Id;
            }
            else if (!ambiguous)
            {
                outgoing = first.Deck.Id;
                incoming = second.Deck.Id;
            }

            if (!ambiguous)
                primary = first.Deck.Id;

            confidence = ambiguous
                ? Math.Clamp(0.45d - Math.Abs(first.Score - second.Score) * 0.25d, 0.15d, 0.45d)
                : Math.Clamp(0.55d + (first.Score - second.Score) * 0.45d, 0.5d, 0.95d);
            audibleReason = ambiguous
                ? outgoing is not null && incoming is not null
                    ? $"Mix ambiguo: Deck {outgoing} in uscita e Deck {incoming} in ingresso hanno livelli simili."
                    : "Mix ambiguo: più deck raggiungono il master con livelli simili."
                : $"Deck {primary} prevale sugli altri deck in riproduzione.";
        }

        var previewCandidates = scored
            .Where(item => item.Deck.TrackLoaded && item.Deck.CueEnabled && item.Score < MinimumAudibleGain)
            .ToArray();
        DeckId? preview = previewCandidates.Length == 1 ? previewCandidates[0].Deck.Id : null;

        var recommended = ordered
            .Where(deck => !deck.TrackLoaded && !deck.IsPlaying && !deck.CueEnabled)
            .OrderBy(deck => SideGain(deck.Side, gains.Left, gains.Right))
            .ThenBy(deck => deck.Id)
            .Select(deck => (DeckId?)deck.Id)
            .FirstOrDefault();

        var reason = previewCandidates.Length > 1
            ? audibleReason + " Più deck sono in preascolto: preview non univoca."
            : audibleReason;
        if (recommended is null)
            reason += " Nessun deck completamente libero per un caricamento sicuro.";

        return new(primary, outgoing, incoming, preview, recommended, confidence, ambiguous, reason);
    }

    private static double AudibilityScore(NoraDeckSnapshot deck, double leftGain, double rightGain)
    {
        if (!deck.TrackLoaded || !deck.IsPlaying) return 0d;
        var routeGain = SideGain(deck.Side, leftGain, rightGain) * Math.Clamp(deck.Volume, 0d, 1.25d);
        if (routeGain < MinimumAudibleGain) return 0d;

        var signal = Math.Clamp(deck.MeterPeak / 0.35d, 0d, 1d);
        return Math.Clamp(routeGain * 0.7d + signal * 0.3d, 0d, 1d);
    }

    private static double SideGain(DeckSide side, double leftGain, double rightGain) => side switch
    {
        DeckSide.Left => leftGain,
        DeckSide.Right => rightGain,
        DeckSide.Thru => 1d,
        _ => 1d
    };

    private sealed record ScoredDeck(NoraDeckSnapshot Deck, double Score);
}
