namespace NexoraMix.Core.Services;

public static class SyncMath
{
    public static double CalculateTempoRatio(double masterEffectiveBpm, double followerOriginalBpm)
    {
        if (masterEffectiveBpm <= 0) throw new ArgumentOutOfRangeException(nameof(masterEffectiveBpm));
        if (followerOriginalBpm <= 0) throw new ArgumentOutOfRangeException(nameof(followerOriginalBpm));
        return masterEffectiveBpm / followerOriginalBpm;
    }

    public static bool IsTempoRatioSupported(double ratio, double maximumPercent = 16)
    {
        var limit = Math.Max(0, maximumPercent) / 100d;
        return ratio >= 1d - limit && ratio <= 1d + limit;
    }

    public static (double Left, double Right) EqualPowerCrossfade(double position)
    {
        var normalized = (Math.Clamp(position, -1, 1) + 1d) / 2d;
        return (
            Math.Cos(normalized * Math.PI / 2d),
            Math.Sin(normalized * Math.PI / 2d));
    }
}
