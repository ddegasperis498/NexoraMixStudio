namespace NexoraMix.Audio.Analysis;

public sealed record AudioAnalysisResult(
    double DurationSeconds,
    double Bpm,
    double BeatOffsetSeconds,
    float[] Waveform,
    double Confidence,
    double PhaseConfidence,
    double Rms,
    double Peak,
    int DetectedBeatCount,
    int EstimatedBarCount,
    double AverageBeatIntervalSeconds);
