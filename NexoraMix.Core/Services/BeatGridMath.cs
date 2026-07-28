namespace NexoraMix.Core.Services;

public static class BeatGridMath
{
    public static double BeatLengthSeconds(double bpm) => bpm > 0 ? 60d / bpm : 0;

    public static double QuantizeNearest(double positionSeconds, double firstBeatSeconds, double bpm, int beatsPerBoundary = 1)
    {
        var boundary = BoundaryLengthSeconds(bpm, beatsPerBoundary);
        if (boundary <= 0) return Math.Max(0, positionSeconds);

        var index = Math.Round((positionSeconds - firstBeatSeconds) / boundary);
        return Math.Max(0, firstBeatSeconds + index * boundary);
    }

    public static double QuantizeForward(double positionSeconds, double firstBeatSeconds, double bpm, int beatsPerBoundary = 1)
    {
        var boundary = BoundaryLengthSeconds(bpm, beatsPerBoundary);
        if (boundary <= 0) return Math.Max(0, positionSeconds);

        var relative = (positionSeconds - firstBeatSeconds) / boundary;
        var index = Math.Ceiling(relative - 0.000001d);
        var result = firstBeatSeconds + index * boundary;
        if (result < positionSeconds + 0.001d) result += boundary;
        return Math.Max(0, result);
    }

    public static double SecondsUntilNextBoundary(
        double positionSeconds,
        double firstBeatSeconds,
        double bpm,
        int beatsPerBoundary = 1)
    {
        var next = QuantizeForward(positionSeconds, firstBeatSeconds, bpm, beatsPerBoundary);
        return Math.Max(0, next - positionSeconds);
    }

    public static int PhraseLengthBeats(int beatsPerBar, int phraseLengthBars) =>
        Math.Max(1, beatsPerBar) * Math.Max(1, phraseLengthBars);

    public static double QuantizeForwardPhrase(
        double positionSeconds,
        double firstBeatSeconds,
        double bpm,
        int beatsPerBar,
        int phraseLengthBars) =>
        QuantizeForward(positionSeconds, firstBeatSeconds, bpm, PhraseLengthBeats(beatsPerBar, phraseLengthBars));

    public static double SecondsUntilNextPhrase(
        double positionSeconds,
        double firstBeatSeconds,
        double bpm,
        int beatsPerBar,
        int phraseLengthBars)
    {
        var next = QuantizeForwardPhrase(positionSeconds, firstBeatSeconds, bpm, beatsPerBar, phraseLengthBars);
        return Math.Max(0, next - positionSeconds);
    }

    public static double PhaseErrorMilliseconds(
        double masterPositionSeconds,
        double masterFirstBeatSeconds,
        double masterBpm,
        double masterTempoRatio,
        double followerPositionSeconds,
        double followerFirstBeatSeconds,
        double followerBpm,
        double followerTempoRatio)
    {
        if (masterBpm <= 0 || followerBpm <= 0 || masterTempoRatio <= 0 || followerTempoRatio <= 0)
            return 0;

        var masterPhase = PositiveModulo((masterPositionSeconds - masterFirstBeatSeconds) / BeatLengthSeconds(masterBpm), 1);
        var followerPhase = PositiveModulo((followerPositionSeconds - followerFirstBeatSeconds) / BeatLengthSeconds(followerBpm), 1);
        var cycleDifference = followerPhase - masterPhase;

        if (cycleDifference > 0.5d) cycleDifference -= 1d;
        if (cycleDifference < -0.5d) cycleDifference += 1d;

        var effectiveMasterBpm = masterBpm * masterTempoRatio;
        var beatMilliseconds = 60_000d / effectiveMasterBpm;
        return cycleDifference * beatMilliseconds;
    }

    public static double SmoothStep(double edge0, double edge1, double value)
    {
        if (Math.Abs(edge1 - edge0) < double.Epsilon) return value >= edge1 ? 1 : 0;
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3d - 2d * t);
    }

    private static double BoundaryLengthSeconds(double bpm, int beatsPerBoundary)
    {
        var beat = BeatLengthSeconds(bpm);
        return beat <= 0 ? 0 : beat * Math.Max(1, beatsPerBoundary);
    }

    private static double PositiveModulo(double value, double modulo)
    {
        var result = value % modulo;
        return result < 0 ? result + modulo : result;
    }
}
