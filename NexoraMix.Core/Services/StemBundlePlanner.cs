using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public sealed class StemBundlePlanner
{
    public IReadOnlyList<StemFile> CreateAudibleMix(StemBundle bundle)
    {
        var errors = bundle.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException("Stem bundle non valido: " + string.Join("; ", errors));

        var hasSolo = bundle.Stems.Any(stem => stem.Solo);
        return bundle.Stems
            .Where(stem => hasSolo ? stem.Solo : !stem.Muted)
            .Select(stem => stem with { Gain = Math.Clamp(stem.Gain, 0d, 2d) })
            .ToArray();
    }
}
