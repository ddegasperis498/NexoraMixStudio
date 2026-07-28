using System.IO;
using System.Text.Json;
using NexoraMix.Core.Models;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;

namespace NexoraMix.App.Services;

public sealed class MusicImportIndexStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly Dictionary<string, MusicImportRecord> _records;

    public MusicImportIndexStore()
    {
        _records = LoadRecords()
            .GroupBy(record => record.Fingerprint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(record => record.ImportedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public bool IsKnown(string path)
    {
        if (!IOFile.Exists(path)) return false;
        lock (_gate) return _records.ContainsKey(CreateFingerprint(path));
    }

    public AudioTrack? TryCreateTrack(string path)
    {
        if (!IOFile.Exists(path)) return null;
        lock (_gate)
        {
            return _records.TryGetValue(CreateFingerprint(path), out var record)
                ? record.ToTrack(path)
                : null;
        }
    }

    public void SaveTrack(AudioTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.FilePath) || !IOFile.Exists(track.FilePath)) return;
        var fingerprint = CreateFingerprint(track.FilePath);
        lock (_gate)
        {
            _records[fingerprint] = MusicImportRecord.FromTrack(fingerprint, track);
            SaveRecords();
        }
    }

    private static string IndexPath
    {
        get
        {
            var folder = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexoraMix",
                "Library");
            IODirectory.CreateDirectory(folder);
            return IOPath.Combine(folder, "music-import-index.json");
        }
    }

    private static string CreateFingerprint(string path)
    {
        var info = new FileInfo(path);
        return string.Join(
            "|",
            info.FullName.ToUpperInvariant(),
            info.Length,
            info.LastWriteTimeUtc.Ticks);
    }

    private static List<MusicImportRecord> LoadRecords()
    {
        try
        {
            if (!IOFile.Exists(IndexPath)) return new List<MusicImportRecord>();
            return JsonSerializer.Deserialize<List<MusicImportRecord>>(IOFile.ReadAllText(IndexPath), JsonOptions)
                   ?? new List<MusicImportRecord>();
        }
        catch (JsonException)
        {
            return new List<MusicImportRecord>();
        }
        catch (IOException)
        {
            return new List<MusicImportRecord>();
        }
        catch (UnauthorizedAccessException)
        {
            return new List<MusicImportRecord>();
        }
    }

    private void SaveRecords()
    {
        var temporary = IndexPath + ".tmp";
        var records = _records.Values.OrderBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
        try
        {
            IOFile.WriteAllText(temporary, JsonSerializer.Serialize(records, JsonOptions));
            IOFile.Move(temporary, IndexPath, overwrite: true);
        }
        finally
        {
            if (IOFile.Exists(temporary)) IOFile.Delete(temporary);
        }
    }

    private sealed record MusicImportRecord(
        string Fingerprint,
        string FilePath,
        string Title,
        string Artist,
        double DurationSeconds,
        double Bpm,
        double BeatOffsetSeconds,
        double AnalysisConfidence,
        double PhaseConfidence,
        int BeatsPerBar,
        int PhraseLengthBars,
        int DetectedBeatCount,
        int EstimatedBarCount,
        double AverageBeatIntervalSeconds,
        DateTimeOffset ImportedAtUtc)
    {
        public static MusicImportRecord FromTrack(string fingerprint, AudioTrack track) =>
            new(
                fingerprint,
                track.FilePath ?? string.Empty,
                track.Title,
                track.Artist,
                track.DurationSeconds,
                track.Bpm,
                track.BeatOffsetSeconds,
                track.AnalysisConfidence,
                track.PhaseConfidence,
                track.BeatsPerBar,
                track.PhraseLengthBars,
                track.DetectedBeatCount,
                track.EstimatedBarCount,
                track.AverageBeatIntervalSeconds,
                DateTimeOffset.UtcNow);

        public AudioTrack ToTrack(string currentPath) =>
            new()
            {
                Title = Title,
                Artist = Artist,
                FilePath = currentPath,
                SourceKind = TrackSourceKind.LocalFile,
                DurationSeconds = DurationSeconds,
                Bpm = Bpm,
                BeatOffsetSeconds = BeatOffsetSeconds,
                AnalysisConfidence = AnalysisConfidence,
                PhaseConfidence = PhaseConfidence,
                BeatsPerBar = BeatsPerBar,
                PhraseLengthBars = PhraseLengthBars,
                DetectedBeatCount = DetectedBeatCount,
                EstimatedBarCount = EstimatedBarCount,
                AverageBeatIntervalSeconds = AverageBeatIntervalSeconds,
                IsAnalyzed = Bpm > 0 || DurationSeconds > 0,
                AnalysisVersion = 3,
                AnalysisStatus = Bpm > 0
                    ? $"Gia riconosciuta · {Bpm:0.0} BPM · {DetectedBeatCount} battute"
                    : "Gia riconosciuta · analisi disponibile al caricamento"
            };
    }
}
