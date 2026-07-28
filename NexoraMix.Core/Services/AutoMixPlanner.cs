using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public static class AutoMixPlanner
{
    public static IReadOnlyList<AudioTrack> OrderByBpmContinuity(IEnumerable<AudioTrack> source)
    {
        var remaining = source.ToList();
        if (remaining.Count <= 2) return remaining;

        var analyzed = remaining.Where(t => t.Bpm > 0).OrderBy(t => t.Bpm).ToList();
        var unknown = remaining.Where(t => t.Bpm <= 0).ToList();
        if (analyzed.Count == 0) return remaining;

        var result = new List<AudioTrack>(remaining.Count);
        var current = analyzed[0];
        result.Add(current);
        analyzed.RemoveAt(0);

        while (analyzed.Count > 0)
        {
            var next = analyzed
                .OrderBy(t => HarmonicDistance(current.Bpm, t.Bpm))
                .ThenBy(t => Math.Abs(t.Bpm - current.Bpm))
                .First();
            result.Add(next);
            analyzed.Remove(next);
            current = next;
        }

        result.AddRange(unknown);
        return result;
    }

    public static double HarmonicDistance(double a, double b)
    {
        if (a <= 0 || b <= 0) return double.MaxValue;
        var candidates = new[] { b, b / 2d, b * 2d };
        return candidates.Min(value => Math.Abs(a - value));
    }
}
