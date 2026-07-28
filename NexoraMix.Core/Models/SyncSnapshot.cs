namespace NexoraMix.Core.Models;

public sealed record SyncSnapshot(
    SyncState State,
    double TargetTempoRatio,
    double AppliedTempoRatio,
    double PhaseErrorMilliseconds,
    double DriftMilliseconds,
    string Message)
{
    public static SyncSnapshot Off { get; } = new(
        SyncState.Off,
        1,
        1,
        0,
        0,
        "Sync disattivato");
}
