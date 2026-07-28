namespace NexoraMix.Audio.Sync;

public sealed record SyncArmResult(
    bool Success,
    string Message,
    long ScheduledFrame,
    double TempoRatio,
    double StartPositionSeconds)
{
    public static SyncArmResult Failed(string message) => new(false, message, 0, 1, 0);
}
