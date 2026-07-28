namespace NexoraMix.Core.Models;

public sealed class MixSession
{
    public int SchemaVersion { get; set; } = 3;
    public string Name { get; set; } = "Nuova sessione";
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
    public double TransitionSeconds { get; set; } = 12;
    public int TransitionBeats { get; set; } = 16;
    public double Crossfader { get; set; }
    public List<AudioTrack> Tracks { get; set; } = new();
    public Guid? DeckATrackId { get; set; }
    public Guid? DeckBTrackId { get; set; }
    public Guid? DeckCTrackId { get; set; }
    public Guid? DeckDTrackId { get; set; }
    public DeckId MasterDeck { get; set; } = DeckId.A;
    public MashupMode MashupMode { get; set; } = MashupMode.Safe;
}
