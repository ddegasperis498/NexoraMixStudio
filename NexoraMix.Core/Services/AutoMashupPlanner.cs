using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public sealed class AutoMashupPlanner
{
    private static readonly DeckId[] DeckOrder = { DeckId.A, DeckId.B, DeckId.C, DeckId.D };

    public MashupPlan CreatePlan(
        IEnumerable<AudioTrack> sourceTracks,
        MashupMode mode,
        double maximumTempoPercent = 16d,
        int phraseLengthBeats = 16)
    {
        ArgumentNullException.ThrowIfNull(sourceTracks);

        var tracks = sourceTracks
            .Where(track => track.CanLoadToDeck && track.Bpm > 0)
            .DistinctBy(track => track.Id)
            .Take(4)
            .ToList();

        if (tracks.Count < 2)
            throw new InvalidOperationException("Auto Mashup richiede almeno due file locali analizzati.");

        var master = tracks
            .OrderByDescending(track => track.AnalysisConfidence + track.PhaseConfidence)
            .ThenByDescending(track => track.DurationSeconds)
            .First();
        var orderedTracks = tracks
            .Select((track, index) => new { Track = track, Index = index })
            .OrderBy(item => item.Track.Id == master.Id ? 0 : 1)
            .ThenBy(item => item.Index)
            .Select(item => item.Track)
            .ToList();

        var masterBpm = master.Bpm;
        var plans = new List<MashupDeckPlan>(tracks.Count);
        var roles = CreateRoles(orderedTracks.Count, mode);

        for (var index = 0; index < orderedTracks.Count; index++)
        {
            var track = orderedTracks[index];
            var ratio = SyncMath.CalculateTempoRatio(masterBpm, track.Bpm);
            if (!SyncMath.IsTempoRatioSupported(ratio, maximumTempoPercent))
            {
                throw new InvalidOperationException(
                    $"{track.Title}: variazione tempo {(ratio - 1d) * 100d:+0.0;-0.0;0.0}% oltre il limite ±{maximumTempoPercent:0}%.");
            }

            var role = roles[index];
            var settings = GetMixSettings(role, mode);
            plans.Add(new MashupDeckPlan(
                DeckOrder[index],
                track.Id,
                role,
                track.Bpm,
                masterBpm,
                ratio,
                EntryBar: index == 0 ? 0 : 1 + (index - 1) * Math.Max(1, phraseLengthBeats / 4),
                settings.Gain,
                settings.Low,
                settings.Mid,
                settings.High));
        }

        return new MashupPlan(
            masterBpm,
            plans[0].Deck,
            Math.Clamp(phraseLengthBeats, 4, 64),
            mode,
            plans,
            $"{plans.Count} deck · master {masterBpm:0.00} BPM · modalità {mode}");
    }

    private static IReadOnlyList<MashupRole> CreateRoles(int count, MashupMode mode)
    {
        var roles = mode switch
        {
            MashupMode.Safe => new[] { MashupRole.Foundation, MashupRole.Support, MashupRole.Texture, MashupRole.Percussion },
            MashupMode.Balanced => new[] { MashupRole.Foundation, MashupRole.Support, MashupRole.Vocals, MashupRole.Percussion },
            MashupMode.Creative => new[] { MashupRole.Foundation, MashupRole.Bass, MashupRole.Vocals, MashupRole.Texture },
            MashupMode.Aggressive => new[] { MashupRole.FullMix, MashupRole.Drums, MashupRole.Vocals, MashupRole.FxLayer },
            _ => new[] { MashupRole.Foundation, MashupRole.Support, MashupRole.Texture, MashupRole.Percussion }
        };
        return roles.Take(count).ToArray();
    }

    private static (double Gain, double Low, double Mid, double High) GetMixSettings(MashupRole role, MashupMode mode)
    {
        var creativeBoost = mode is MashupMode.Creative or MashupMode.Aggressive ? 0.04d : 0d;
        return role switch
        {
            MashupRole.Foundation => (0.72d, 0d, 0d, 0d),
            MashupRole.Support => (0.52d + creativeBoost, -12d, -3d, -2d),
            MashupRole.Drums => (0.54d + creativeBoost, -8d, -4d, 1d),
            MashupRole.Bass => (0.48d, -2d, -12d, -18d),
            MashupRole.Vocals => (0.48d + creativeBoost, -24d, 1d, 0d),
            MashupRole.Instrumental => (0.52d, -14d, -2d, -3d),
            MashupRole.Texture => (0.38d + creativeBoost, -24d, -8d, -1d),
            MashupRole.Percussion => (0.42d + creativeBoost, -18d, -7d, 1d),
            MashupRole.FxLayer => (0.34d + creativeBoost, -24d, -10d, -3d),
            MashupRole.FullMix => (0.55d, -8d, -3d, -3d),
            _ => (0.45d, -12d, -4d, -3d)
        };
    }
}
