namespace NexoraMix.Core.Models;

public sealed class CuePoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Cue";
    public double PositionSeconds { get; set; }
}
