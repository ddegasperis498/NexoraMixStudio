using IOFile = System.IO.File;

namespace NexoraMix.Core.Models;

public enum StemPart
{
    Vocals,
    Drums,
    Bass,
    Other
}

public sealed record StemFile(StemPart Part, string Path, double Gain = 1d, bool Muted = false, bool Solo = false);

public sealed class StemBundle
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required IReadOnlyList<StemFile> Stems { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Title)) errors.Add("Titolo mancante");
        if (string.IsNullOrWhiteSpace(Artist)) errors.Add("Artista mancante");
        if (Stems.Count == 0) errors.Add("Nessuno stem locale configurato");

        foreach (var duplicate in Stems.GroupBy(stem => stem.Part).Where(group => group.Count() > 1))
            errors.Add($"Stem duplicato: {duplicate.Key}");

        foreach (var stem in Stems)
        {
            if (string.IsNullOrWhiteSpace(stem.Path)) errors.Add($"{stem.Part}: percorso mancante");
            else if (!IOFile.Exists(stem.Path)) errors.Add($"{stem.Part}: file non trovato");
            if (stem.Gain is < 0d or > 2d) errors.Add($"{stem.Part}: gain fuori range");
        }

        return errors;
    }

    public bool IsUsable => Validate().Count == 0;
}
