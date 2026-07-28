using System.Text.Json.Serialization;
using IOFile = System.IO.File;

namespace NexoraMix.Core.Models;

public sealed class AudioTrack : ObservableObject
{
    private string _title = "Senza titolo";
    private string _artist = "Artista sconosciuto";
    private string? _filePath;
    private string? _externalUrl;
    private double _durationSeconds;
    private double _bpm;
    private double _beatOffsetSeconds;
    private double _cueInSeconds;
    private double? _cueOutSeconds;
    private bool _isAnalyzed;
    private bool _isAnalyzing;
    private string _analysisStatus = "Non analizzata";
    private float[] _waveform = Array.Empty<float>();
    private double _analysisConfidence;
    private double _phaseConfidence;
    private int _beatsPerBar = 4;
    private TrackSourceKind _sourceKind = TrackSourceKind.LocalFile;

    public Guid Id { get; set; } = Guid.NewGuid();
    public TrackSourceKind SourceKind
    {
        get => _sourceKind;
        set
        {
            if (!SetProperty(ref _sourceKind, value)) return;
            RaisePropertyChanged(nameof(CanLoadToDeck));
            RaisePropertyChanged(nameof(CanOpenExternally));
            RaisePropertyChanged(nameof(CanAssignToDeck));
            RaisePropertyChanged(nameof(IsExternalOnly));
            RaisePropertyChanged(nameof(SourceDisplay));
        }
    }
    public string? SpotifyId { get; set; }
    public string? Album { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? Isrc { get; set; }
    public int AnalysisVersion { get; set; } = 2;

    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Artist { get => _artist; set => SetProperty(ref _artist, value); }
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (!SetProperty(ref _filePath, value)) return;
            RaisePropertyChanged(nameof(CanLoadToDeck));
            RaisePropertyChanged(nameof(CanOpenExternally));
            RaisePropertyChanged(nameof(CanAssignToDeck));
            RaisePropertyChanged(nameof(IsExternalOnly));
            RaisePropertyChanged(nameof(SourceDisplay));
        }
    }
    public string? ExternalUrl { get => _externalUrl; set => SetProperty(ref _externalUrl, value); }
    public double DurationSeconds { get => _durationSeconds; set { if (SetProperty(ref _durationSeconds, value)) RaisePropertyChanged(nameof(DurationText)); } }
    public double Bpm { get => _bpm; set { if (SetProperty(ref _bpm, value)) RaisePropertyChanged(nameof(BpmText)); } }
    public double BeatOffsetSeconds { get => _beatOffsetSeconds; set => SetProperty(ref _beatOffsetSeconds, value); }
    public double CueInSeconds { get => _cueInSeconds; set { if (SetProperty(ref _cueInSeconds, value)) RaisePropertyChanged(nameof(CueText)); } }
    public double? CueOutSeconds { get => _cueOutSeconds; set => SetProperty(ref _cueOutSeconds, value); }
    public bool IsAnalyzed { get => _isAnalyzed; set => SetProperty(ref _isAnalyzed, value); }
    public bool IsAnalyzing { get => _isAnalyzing; set => SetProperty(ref _isAnalyzing, value); }
    public string AnalysisStatus { get => _analysisStatus; set => SetProperty(ref _analysisStatus, value); }
    public double AnalysisConfidence { get => _analysisConfidence; set => SetProperty(ref _analysisConfidence, Math.Clamp(value, 0, 1)); }
    public double PhaseConfidence { get => _phaseConfidence; set => SetProperty(ref _phaseConfidence, Math.Clamp(value, 0, 1)); }
    public int BeatsPerBar { get => _beatsPerBar; set => SetProperty(ref _beatsPerBar, Math.Clamp(value, 1, 16)); }

    [JsonIgnore]
    public float[] Waveform { get => _waveform; set => SetProperty(ref _waveform, value ?? Array.Empty<float>()); }

    public List<CuePoint> CuePoints { get; set; } = new();

    [JsonIgnore]
    public bool CanLoadToDeck => SourceKind == TrackSourceKind.LocalFile && !string.IsNullOrWhiteSpace(FilePath) && IOFile.Exists(FilePath);

    [JsonIgnore]
    public bool CanOpenExternally => SourceKind == TrackSourceKind.SpotifyReference &&
                                     !string.IsNullOrWhiteSpace(SpotifyId);

    [JsonIgnore]
    public bool CanAssignToDeck => CanLoadToDeck || CanOpenExternally;

    [JsonIgnore]
    public bool IsExternalOnly => CanOpenExternally && !CanLoadToDeck;

    [JsonIgnore]
    public string DurationText => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    [JsonIgnore]
    public string BpmText => Bpm > 0 ? $"{Bpm:0.0}" : "—";

    [JsonIgnore]
    public string CueText => TimeSpan.FromSeconds(Math.Max(0, CueInSeconds)).ToString(@"m\:ss\.fff");

    [JsonIgnore]
    public string SourceDisplay => CanLoadToDeck
        ? "File locale · mixer interno"
        : IsExternalOnly
            ? "Spotify esterno · non mixabile"
            : SourceKind.ToString();

    public override string ToString() => $"{Artist} - {Title}";
}
