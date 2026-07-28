namespace NexoraMix.App.Remote;

public sealed record RemoteMixerSnapshot(
    string Version,
    DateTimeOffset TimestampUtc,
    string MasterDeck,
    double Crossfader,
    string Status,
    string SyncHealth,
    string MasterPeak,
    string Clipping,
    bool MashupRunning,
    bool Recording,
    double CueVolume,
    bool MasterCue,
    IReadOnlyList<RemoteDeckSnapshot> Decks);

public sealed record RemoteDeckSnapshot(
    string Id,
    string Title,
    string Artist,
    bool Loaded,
    bool Playing,
    bool Master,
    bool Sync,
    bool Cue,
    double Bpm,
    double EffectiveBpm,
    double TempoPercent,
    double Position,
    double Duration,
    double BeatOffset,
    int BeatsPerBar,
    double Volume,
    double Meter,
    int LoopBeats,
    double LowEq,
    double MidEq,
    double HighEq,
    double Filter,
    double Echo,
    double Crush,
    double Saturation,
    double Gate,
    double Compressor,
    double Roll,
    double Brake,
    int Beat,
    int Bar,
    int BeatInBar,
    float[] Waveform);

public sealed record RemoteCommand
{
    public string Action { get; init; } = string.Empty;
    public string? Deck { get; init; }
    public double? Value { get; init; }
    public int? IntValue { get; init; }
    public string? Text { get; init; }
}
