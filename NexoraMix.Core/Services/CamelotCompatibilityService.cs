using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public enum CamelotRelation
{
    Unknown,
    SameKey,
    Adjacent,
    RelativeMajorMinor,
    Incompatible
}

public sealed record CamelotCompatibility(double Score, CamelotRelation Relation);

public static class CamelotCompatibilityService
{
    private static readonly IReadOnlyDictionary<string, string> MajorKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["B"] = "1B", ["F#"] = "2B", ["C#"] = "3B", ["G#"] = "4B",
            ["D#"] = "5B", ["A#"] = "6B", ["F"] = "7B", ["C"] = "8B",
            ["G"] = "9B", ["D"] = "10B", ["A"] = "11B", ["E"] = "12B"
        };

    private static readonly IReadOnlyDictionary<string, string> MinorKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["G#"] = "1A", ["D#"] = "2A", ["A#"] = "3A", ["F"] = "4A",
            ["C"] = "5A", ["G"] = "6A", ["D"] = "7A", ["A"] = "8A",
            ["E"] = "9A", ["B"] = "10A", ["F#"] = "11A", ["C#"] = "12A"
        };

    public static string? ToCamelot(string? musicalKey, MusicalMode mode)
    {
        var normalized = NormalizeKey(musicalKey);
        if (normalized is null) return null;
        var map = mode == MusicalMode.Major ? MajorKeys : mode == MusicalMode.Minor ? MinorKeys : null;
        return map is not null && map.TryGetValue(normalized, out var camelot) ? camelot : null;
    }

    public static CamelotCompatibility Compare(string? current, string? candidate)
    {
        if (!TryParse(current, out var currentNumber, out var currentMode) ||
            !TryParse(candidate, out var candidateNumber, out var candidateMode))
            return new(0d, CamelotRelation.Unknown);

        if (currentNumber == candidateNumber && currentMode == candidateMode)
            return new(1d, CamelotRelation.SameKey);
        if (currentNumber == candidateNumber)
            return new(0.9d, CamelotRelation.RelativeMajorMinor);

        var circularDistance = Math.Min(
            Math.Abs(currentNumber - candidateNumber),
            12 - Math.Abs(currentNumber - candidateNumber));
        return circularDistance == 1 && currentMode == candidateMode
            ? new(0.85d, CamelotRelation.Adjacent)
            : new(0d, CamelotRelation.Incompatible);
    }

    private static string? NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var key = value.Trim().ToUpperInvariant()
            .Replace(" MAJOR", string.Empty, StringComparison.Ordinal)
            .Replace(" MINOR", string.Empty, StringComparison.Ordinal)
            .Replace("♯", "#", StringComparison.Ordinal)
            .Replace("♭", "B", StringComparison.Ordinal);
        return key switch
        {
            "DB" => "C#", "EB" => "D#", "GB" => "F#", "AB" => "G#", "BB" => "A#",
            "CB" => "B", "E#" => "F", "FB" => "E", "B#" => "C",
            _ => key
        };
    }

    private static bool TryParse(string? value, out int number, out char mode)
    {
        number = 0;
        mode = '\0';
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 3 || !int.TryParse(normalized[..^1], out number)) return false;
        mode = normalized[^1];
        return number is >= 1 and <= 12 && mode is 'A' or 'B';
    }
}
