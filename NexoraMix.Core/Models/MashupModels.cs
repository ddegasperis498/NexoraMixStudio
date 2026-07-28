namespace NexoraMix.Core.Models;

public enum MashupMode
{
    Safe,
    Balanced,
    Creative,
    Aggressive
}

public enum MashupRole
{
    Foundation,
    Support,
    Drums,
    Bass,
    Vocals,
    Instrumental,
    Texture,
    Percussion,
    FxLayer,
    FullMix
}

public sealed record MashupDeckPlan(
    DeckId Deck,
    Guid TrackId,
    MashupRole Role,
    double SourceBpm,
    double TargetBpm,
    double TempoRatio,
    int EntryBar,
    double ChannelGain,
    double LowEqDb,
    double MidEqDb,
    double HighEqDb);

public sealed record MashupPlan(
    double MasterBpm,
    DeckId MasterDeck,
    int PhraseLengthBeats,
    MashupMode Mode,
    IReadOnlyList<MashupDeckPlan> Decks,
    string Summary);
