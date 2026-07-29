namespace NexoraMix.Core.Models;

public enum AudioFeatureAnalysisStatus
{
    Unknown,
    Analyzed,
    Partial,
    Failed
}

public enum MusicalMode
{
    Unknown,
    Major,
    Minor
}

public enum VocalPresence
{
    Unknown,
    Instrumental,
    Occasional,
    Predominant
}

public enum TrackSectionType
{
    Unknown,
    Intro,
    Rhythm,
    Break,
    BuildUp,
    Drop,
    Bridge,
    Outro
}

public sealed record TrackAudioSection(
    double StartTimeSeconds,
    double EndTimeSeconds,
    TrackSectionType SectionType,
    double Confidence);

/// <summary>
/// Feature audio persistibili. I valori non disponibili restano null/Unknown:
/// zero non viene usato per rappresentare un'analisi mancante.
/// </summary>
public sealed record TrackAudioFeatures
{
    public const int CurrentAnalysisVersion = 4;

    public AudioFeatureAnalysisStatus Status { get; init; } = AudioFeatureAnalysisStatus.Unknown;
    public int AnalysisVersion { get; init; } = CurrentAnalysisVersion;
    public string Algorithm { get; init; } = "nexoramix-local-dsp-v1";
    public DateTimeOffset? AnalyzedAtUtc { get; init; }
    public string? AnalysisError { get; init; }
    public double? OverallConfidence { get; init; }

    public double? Bpm { get; init; }
    public double? BpmConfidence { get; init; }
    public string? MusicalKey { get; init; }
    public MusicalMode MusicalMode { get; init; } = MusicalMode.Unknown;
    public string? CamelotKey { get; init; }
    public double? KeyConfidence { get; init; }

    // Stima locale non certificata EBU R128/BS.1770.
    public double? EstimatedIntegratedLufs { get; init; }
    public double? SamplePeakDbFs { get; init; }
    public double? EstimatedTruePeakDbFs { get; init; }
    public double? Energy { get; init; }
    public double? Danceability { get; init; }
    public double? SpectralCentroidHz { get; init; }
    public double? BassIntensity { get; init; }

    public VocalPresence VocalPresence { get; init; } = VocalPresence.Unknown;
    public double? VocalConfidence { get; init; }
    public IReadOnlyList<TrackAudioSection> Sections { get; init; } = Array.Empty<TrackAudioSection>();
}
