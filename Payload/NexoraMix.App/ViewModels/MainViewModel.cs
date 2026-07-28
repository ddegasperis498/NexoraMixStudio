using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using NexoraMix.App.Services;
using NexoraMix.App.Views;
using NexoraMix.Audio.Analysis;
using NexoraMix.Audio.Playback;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

namespace NexoraMix.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".aiff", ".aif", ".wma", ".m4a", ".aac", ".flac"
    };

    private readonly BpmAnalyzer _analyzer = new();
    private readonly SessionStore _sessionStore = new();
    private readonly SpotifyAuthService _spotifyAuth = new();
    private readonly SpotifyApiService _spotifyApi;
    private readonly DispatcherTimer _timer;
    private readonly AppSettings _settings;
    private readonly TrackMappingStore _mappingStore = new();
    private readonly AnalysisCacheStore _analysisCache = new();
    private readonly MasterAudioEngine _audioEngine = new();
    private AudioTrack? _selectedTrack;
    private DeckViewModel _masterDeck = null!;
    private double _crossfader;
    private bool _autoMixEnabled;
    private int _transitionBeats = 16;
    private double _maximumSyncPercent = 25d;
    private bool _bassSwapEnabled = true;
    private string _status = "Pronto. Importa file locali oppure avvia la demo sincronizzata.";
    private bool _transitionRunning;
    private long _transitionStartFrame;
    private long _transitionEndFrame;
    private DeckViewModel? _transitionOutgoing;
    private DeckViewModel? _transitionIncoming;
    private bool _isBusy;
    private string _busyMessage = "Preparazione in corso…";
    private double _phaseErrorMilliseconds;
    private double _driftMilliseconds;
    private string _syncHealth = "SYNC IN ATTESA";
    private Exception? _lastOutputError;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _transitionBeats = NormalizeTransitionBeats(_settings.TransitionBeats);
        _maximumSyncPercent = Math.Clamp(_settings.MaximumSyncPercent, 4d, 25d);
        _bassSwapEnabled = _settings.BassSwapEnabled;
        _spotifyApi = new SpotifyApiService(_spotifyAuth);

        DeckA = new DeckViewModel(
            "A", DeckId.A, _audioEngine, SetMaster, ToggleSync, ToggleSpotifyDeckAsync, StopSpotifyDeckAsync);
        DeckB = new DeckViewModel(
            "B", DeckId.B, _audioEngine, SetMaster, ToggleSync, ToggleSpotifyDeckAsync, StopSpotifyDeckAsync);
        SetMaster(DeckA);

        ImportFilesCommand = new AsyncRelayCommand(ImportFilesAsync);
        ImportSpotifyCommand = new AsyncRelayCommand(ImportSpotifyAsync);
        ConnectSpotifyCommand = new AsyncRelayCommand(ConnectSpotifyAsync);
        ConfigureSpotifyCommand = new RelayCommand(ConfigureSpotify);
        AnalyzeSelectedCommand = new AsyncRelayCommand(AnalyzeSelectedAsync, () => SelectedTrack?.CanLoadToDeck == true);
        AnalyzeAllCommand = new AsyncRelayCommand(AnalyzeAllAsync, () => Library.Any(track => track.CanLoadToDeck));
        LinkLocalFileCommand = new AsyncRelayCommand(LinkLocalFileAsync, () => SelectedTrack is not null);
        LoadDeckACommand = new RelayCommand(parameter => LoadTrackToDeck(parameter as AudioTrack ?? SelectedTrack, DeckA), parameter => (parameter as AudioTrack ?? SelectedTrack)?.CanAssignToDeck == true);
        LoadDeckBCommand = new RelayCommand(parameter => LoadTrackToDeck(parameter as AudioTrack ?? SelectedTrack, DeckB), parameter => (parameter as AudioTrack ?? SelectedTrack)?.CanAssignToDeck == true);
        SmartOrderCommand = new RelayCommand(SmartOrder, () => Library.Count > 1);
        SaveSessionCommand = new AsyncRelayCommand(SaveSessionAsync, () => Library.Count > 0);
        LoadSessionCommand = new AsyncRelayCommand(LoadSessionAsync);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedTrack is not null);
        OpenSpotifyCommand = new RelayCommand(OpenSelectedSpotify, () => !string.IsNullOrWhiteSpace(SelectedTrack?.ExternalUrl));
        LoadDemoCommand = new AsyncRelayCommand(LoadDemoAsync);
        StopAllCommand = new RelayCommand(StopAll);
        CenterCrossfaderCommand = new RelayCommand(() => Crossfader = 0);

        Library.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(LibrarySummary));
            RaiseCommandStates();
        };

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Crossfader = 0;
    }

    public ObservableCollection<AudioTrack> Library { get; } = new();
    public DeckViewModel DeckA { get; }
    public DeckViewModel DeckB { get; }

    public AudioTrack? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (!SetProperty(ref _selectedTrack, value)) return;
            RaiseCommandStates();
        }
    }

    public double Crossfader
    {
        get => _crossfader;
        set
        {
            if (!SetProperty(ref _crossfader, Math.Clamp(value, -1d, 1d))) return;
            _audioEngine.SetCrossfader(_crossfader);
        }
    }

    public bool AutoMixEnabled
    {
        get => _autoMixEnabled;
        set
        {
            if (!SetProperty(ref _autoMixEnabled, value)) return;
            Status = value
                ? "Smart AutoMix attivo: sync su misura, crossfade equal-power e bass swap."
                : "Smart AutoMix disattivato: controllo manuale dei deck.";
        }
    }

    public int TransitionBeats
    {
        get => _transitionBeats;
        set
        {
            if (!SetProperty(ref _transitionBeats, NormalizeTransitionBeats(value))) return;
            RaisePropertyChanged(nameof(TransitionText));
        }
    }

    public double MaximumSyncPercent
    {
        get => _maximumSyncPercent;
        set
        {
            if (!SetProperty(ref _maximumSyncPercent, Math.Clamp(value, 4d, 25d))) return;
            RaisePropertyChanged(nameof(MaximumSyncText));
        }
    }

    public bool BassSwapEnabled
    {
        get => _bassSwapEnabled;
        set => SetProperty(ref _bassSwapEnabled, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetProperty(ref _busyMessage, value);
    }

    public string TransitionText => $"{TransitionBeats} battute";
    public string MaximumSyncText => $"±{MaximumSyncPercent:0}%";
    public bool SpotifyConnected => _spotifyAuth.IsConnected;
    public string SpotifyStatus => SpotifyConnected ? "Spotify connesso" : "Spotify non connesso";
    public string LibrarySummary => Library.Count == 1 ? "1 elemento" : $"{Library.Count} elementi";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string MasterDeckText => _masterDeck.HasLocalAudio
        ? $"MASTER · DECK {_masterDeck.Name}"
        : "MASTER · NESSUN CLOCK LOCALE";
    public string PhaseErrorText => $"{_phaseErrorMilliseconds:+0.0;-0.0;0.0} ms";
    public string DriftText => $"{_driftMilliseconds:+0.0;-0.0;0.0} ms";
    public string MasterPeakText => $"{_audioEngine.PeakDbFs:0.0} dBFS";
    public string ClippingText => _audioEngine.ClippingCount.ToString();
    public string SyncHealth { get => _syncHealth; private set => SetProperty(ref _syncHealth, value); }
    public double SavedWindowWidth => Math.Max(1180d, _settings.WindowWidth);
    public double SavedWindowHeight => Math.Max(700d, _settings.WindowHeight);
    public double? SavedWindowLeft => _settings.WindowLeft;
    public double? SavedWindowTop => _settings.WindowTop;
    public bool SavedWindowMaximized => _settings.WindowMaximized;

    public void SaveWindowPlacement(double width, double height, double? left, double? top, bool maximized)
    {
        _settings.WindowWidth = Math.Max(1180d, width);
        _settings.WindowHeight = Math.Max(700d, height);
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowMaximized = maximized;
        SavePreferences();
    }

    public ICommand ImportFilesCommand { get; }
    public ICommand ImportSpotifyCommand { get; }
    public ICommand ConnectSpotifyCommand { get; }
    public ICommand ConfigureSpotifyCommand { get; }
    public ICommand AnalyzeSelectedCommand { get; }
    public ICommand AnalyzeAllCommand { get; }
    public ICommand LinkLocalFileCommand { get; }
    public ICommand LoadDeckACommand { get; }
    public ICommand LoadDeckBCommand { get; }
    public ICommand SmartOrderCommand { get; }
    public ICommand SaveSessionCommand { get; }
    public ICommand LoadSessionCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand OpenSpotifyCommand { get; }
    public ICommand LoadDemoCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand CenterCrossfaderCommand { get; }

    public void ToggleMasterPlayPause() => _masterDeck.TogglePlayPause();

    public void ToggleFollowerSync()
    {
        var follower = ReferenceEquals(_masterDeck, DeckA) ? DeckB : DeckA;
        ToggleSync(follower);
    }

    public void SwitchMaster()
    {
        var candidate = ReferenceEquals(_masterDeck, DeckA) ? DeckB : DeckA;
        if (candidate.HasLocalAudio) SetMaster(candidate);
    }

    public void SeekDeck(DeckViewModel deck, double position, bool setCue)
    {
        deck.Seek(position);
        if (!setCue) return;
        deck.SetCue(position);
        var cueTime = TimeSpan.FromSeconds(position).ToString(@"m\:ss\.fff");
        Status = $"Start cue salvato sul Deck {deck.Name}: {cueTime}";
    }

    public async Task ImportFilePathsAsync(IEnumerable<string> paths)
    {
        var validPaths = paths
            .Where(IOFile.Exists)
            .Where(path => SupportedExtensions.Contains(IOPath.GetExtension(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validPaths.Count == 0)
        {
            Status = "Nessun file audio supportato trovato.";
            return;
        }

        IsBusy = true;
        BusyMessage = $"Importazione di {validPaths.Count} tracce…";
        try
        {
            var added = new List<AudioTrack>();
            foreach (var path in validPaths)
            {
                if (Library.Any(track => string.Equals(track.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;
                var (artist, title) = ParseFileName(path);
                var track = new AudioTrack
                {
                    Title = title,
                    Artist = artist,
                    FilePath = path,
                    SourceKind = TrackSourceKind.LocalFile
                };
                Library.Add(track);
                added.Add(track);
            }

            foreach (var track in added)
            {
                BusyMessage = $"Analisi beat-grid · {track.Title}";
                await AnalyzeTrackAsync(track);
            }

            SelectedTrack ??= added.FirstOrDefault();
            Status = added.Count > 0
                ? $"Importate e analizzate {added.Count} tracce locali."
                : "Le tracce selezionate erano già presenti nella libreria.";
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    public void LoadSelectedToAvailableDeck()
    {
        if (SelectedTrack?.CanAssignToDeck != true) return;
        var target = DeckA.Track is null
            ? DeckA
            : DeckB.Track is null
                ? DeckB
                : DeckA.PositionSeconds <= DeckB.PositionSeconds ? DeckA : DeckB;
        LoadTrackToDeck(SelectedTrack, target);
    }

    private async Task ImportFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importa tracce audio locali",
            Filter = "Audio supportato|*.mp3;*.wav;*.flac;*.aiff;*.aif;*.wma;*.m4a;*.aac|Tutti i file|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true) await ImportFilePathsAsync(dialog.FileNames);
    }

    private async Task ImportSpotifyAsync()
    {
        var dialog = new TextPromptWindow(
            "Importa scaletta Spotify",
            "Incolla il link pubblico di una traccia, album o playlist. Il riferimento può essere caricato nel deck e riprodotto sul dispositivo Spotify attivo; per Sync, EQ e crossfader serve il file locale.",
            "https://open.spotify.com/");

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        var link = dialog.Value.Trim();

        if (!SpotifyLinkParser.TryParse(link, out var resource))
        {
            MessageBox.Show("Il link Spotify non è valido.", "Spotify", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if ((resource.Type is SpotifyResourceType.Playlist or SpotifyResourceType.Album) && !_spotifyAuth.IsConnected)
        {
            var answer = MessageBox.Show(
                "Per importare tutte le tracce di album e playlist devi connettere Spotify. Vuoi effettuare ora l'accesso?",
                "Connessione Spotify richiesta",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;
            await ConnectSpotifyAsync();
            if (!_spotifyAuth.IsConnected) return;
        }

        IsBusy = true;
        BusyMessage = "Importazione metadati Spotify…";
        Status = "Lettura della scaletta Spotify…";
        try
        {
            var tracks = await _spotifyApi.ImportAsync(link);
            var mappedTracks = new List<AudioTrack>();
            foreach (var track in tracks)
            {
                if (_mappingStore.TryResolve(track.SpotifyId, out var mappedPath))
                {
                    track.FilePath = mappedPath;
                    track.SourceKind = TrackSourceKind.LocalFile;
                    track.AnalysisStatus = "Mapping locale ripristinato: analisi in corso";
                    mappedTracks.Add(track);
                }

                if (!Library.Any(existing => existing.SpotifyId == track.SpotifyId && !string.IsNullOrWhiteSpace(track.SpotifyId)))
                    Library.Add(track);
            }

            foreach (var mappedTrack in mappedTracks)
            {
                BusyMessage = $"Analisi file collegato · {mappedTrack.Title}";
                await AnalyzeTrackAsync(mappedTrack);
            }

            SelectedTrack = tracks.FirstOrDefault();
            Status = mappedTracks.Count > 0
                ? $"Importati {tracks.Count} riferimenti Spotify; {mappedTracks.Count} file locali sono stati ricollegati automaticamente."
                : $"Importati {tracks.Count} riferimenti Spotify. Puoi caricarli nei deck come Spotify esterno oppure collegare i file locali per Sync e mix completo.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Importazione Spotify");
            Status = "Importazione Spotify non riuscita.";
            MessageBox.Show(ex.Message, "Spotify", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private async Task ConnectSpotifyAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.SpotifyClientId))
        {
            ConfigureSpotify();
            if (string.IsNullOrWhiteSpace(_settings.SpotifyClientId)) return;
        }

        Status = "Apertura dell'autorizzazione Spotify nel browser…";
        try
        {
            await _spotifyAuth.LoginAsync(_settings.SpotifyClientId);
            RaisePropertyChanged(nameof(SpotifyConnected));
            RaisePropertyChanged(nameof(SpotifyStatus));
            Status = "Spotify connesso. Puoi importare e controllare la riproduzione originale su un dispositivo Spotify attivo.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Connessione Spotify");
            Status = "Connessione Spotify non riuscita.";
            MessageBox.Show(ex.Message, "Spotify", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ConfigureSpotify()
    {
        var dialog = new TextPromptWindow(
            "Impostazioni Spotify",
            "Inserisci il Client ID della tua app Spotify Developer. Redirect URI: http://127.0.0.1:5543/callback/. La connessione richiede anche i permessi di controllo riproduzione.",
            _settings.SpotifyClientId);

        if (dialog.ShowDialog() != true) return;
        _settings.SpotifyClientId = dialog.Value.Trim();
        _settings.Save();
        Status = "Client ID Spotify salvato localmente.";
    }

    private async Task AnalyzeSelectedAsync()
    {
        if (SelectedTrack is not null) await AnalyzeTrackAsync(SelectedTrack);
    }

    private async Task AnalyzeAllAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var track in Library.Where(track => track.CanLoadToDeck))
            {
                BusyMessage = $"Analisi beat-grid · {track.Title}";
                await AnalyzeTrackAsync(track);
            }
            Status = "Analisi dell'intera libreria completata.";
        }
        finally { IsBusy = false; }
    }

    private async Task AnalyzeTrackAsync(AudioTrack track)
    {
        if (!track.CanLoadToDeck) return;
        track.IsAnalyzing = true;
        track.AnalysisStatus = "Analisi BPM, fase e waveform…";
        try
        {
            var result = await _analysisCache.TryLoadAsync(track.FilePath!);
            var loadedFromCache = result is not null;
            if (result is null)
            {
                result = await _analyzer.AnalyzeAsync(track.FilePath!);
                await _analysisCache.SaveAsync(track.FilePath!, result);
            }

            track.DurationSeconds = result.DurationSeconds;
            track.Bpm = result.Bpm;
            track.BeatOffsetSeconds = result.BeatOffsetSeconds;
            track.Waveform = result.Waveform;
            track.AnalysisConfidence = result.Confidence;
            track.PhaseConfidence = result.PhaseConfidence;
            track.IsAnalyzed = true;
            track.AnalysisVersion = 2;
            track.AnalysisStatus = result.Bpm > 0
                ? $"Pronta{(loadedFromCache ? " da cache" : string.Empty)} · BPM {result.Confidence:P0} · fase {result.PhaseConfidence:P0}"
                : "Waveform pronta · BPM da correggere";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Analisi {track.FilePath}");
            track.IsAnalyzed = false;
            track.AnalysisStatus = $"Errore decoder/analisi: {ex.Message}";
        }
        finally { track.IsAnalyzing = false; }
    }

    private async Task LinkLocalFileAsync()
    {
        if (SelectedTrack is null) return;
        var dialog = new OpenFileDialog
        {
            Title = $"Collega file locale a {SelectedTrack.Title}",
            Filter = "Audio|*.mp3;*.wav;*.flac;*.aiff;*.aif;*.wma;*.m4a;*.aac|Tutti i file|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        SelectedTrack.FilePath = dialog.FileName;
        SelectedTrack.SourceKind = TrackSourceKind.LocalFile;
        SelectedTrack.AnalysisStatus = "File locale collegato: analisi in corso";
        if (!string.IsNullOrWhiteSpace(SelectedTrack.SpotifyId))
            await _mappingStore.SaveAsync(SelectedTrack.SpotifyId, dialog.FileName);
        await AnalyzeTrackAsync(SelectedTrack);
        Status = "File locale collegato, analizzato e ora caricabile nei deck.";
        RaiseCommandStates();
    }

    private void LoadTrackToDeck(AudioTrack? track, DeckViewModel deck)
    {
        if (track?.CanAssignToDeck != true)
        {
            Status = "Questo elemento non dispone di un file locale né di un riferimento Spotify riproducibile.";
            return;
        }

        try
        {
            SelectedTrack = track;
            deck.Load(track);

            if (track.CanLoadToDeck && (!_masterDeck.HasLocalAudio || ReferenceEquals(deck, _masterDeck)))
                SetMaster(deck);
            else if (!track.CanLoadToDeck && ReferenceEquals(deck, _masterDeck))
            {
                var alternative = ReferenceEquals(deck, DeckA) ? DeckB : DeckA;
                if (alternative.HasLocalAudio) SetMaster(alternative);
                else
                {
                    DeckA.SetMaster(false);
                    DeckB.SetMaster(false);
                    RaisePropertyChanged(nameof(MasterDeckText));
                }
            }

            Status = track.CanLoadToDeck
                ? $"{track.Title} caricata sul Deck {deck.Name}: mixer interno, BPM manuale e Sync disponibili."
                : $"{track.Title} caricata sul Deck {deck.Name} in modalità Spotify esterna. PLAY controlla Spotify; crossfader, EQ e Sync non possono agire su questo audio.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Caricamento Deck {deck.Name}");
            Status = $"Impossibile caricare il Deck {deck.Name}.";
            MessageBox.Show(ex.Message, "Nexora Mix Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ToggleSpotifyDeckAsync(DeckViewModel deck)
    {
        var track = deck.Track;
        if (track?.IsExternalOnly != true)
            return;

        if (!_spotifyAuth.IsConnected)
        {
            Status = "Per riprodurre il riferimento nel deck devi connettere Spotify con i permessi di riproduzione.";
            await ConnectSpotifyAsync();
            if (!_spotifyAuth.IsConnected) return;
        }

        try
        {
            if (deck.IsPlaying)
            {
                await _spotifyApi.PausePlaybackAsync();
                deck.SetExternalPlaybackState(false);
                Status = $"Spotify in pausa dal Deck {deck.Name}.";
            }
            else
            {
                await _spotifyApi.StartPlaybackAsync(track);
                var other = ReferenceEquals(deck, DeckA) ? DeckB : DeckA;
                if (other.IsSpotifyExternal) other.SetExternalPlaybackState(false);
                deck.SetExternalPlaybackState(true, resetPosition: true);
                Status = $"{track.Title} avviata sul dispositivo Spotify attivo. L'audio resta esterno al mixer Nexora.";
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Riproduzione Spotify Deck {deck.Name}");
            deck.SetExternalPlaybackState(false);
            Status = ex.Message;
            MessageBox.Show(
                ex.Message + "\n\nApri Spotify sul PC o sul telefono, avvia una traccia per rendere attivo il dispositivo e riprova.",
                "Spotify esterno",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task StopSpotifyDeckAsync(DeckViewModel deck)
    {
        try
        {
            if (_spotifyAuth.IsConnected && deck.IsSpotifyExternal && deck.IsPlaying)
                await _spotifyApi.PausePlaybackAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Stop Spotify Deck {deck.Name}");
            Status = $"Spotify non ha confermato la pausa: {ex.Message}";
        }
        finally
        {
            deck.SetExternalPlaybackState(false, resetPosition: true);
        }
    }

    private void SetMaster(DeckViewModel deck)
    {
        if (!deck.HasLocalAudio && deck.Track is not null)
        {
            Status = "Un riferimento Spotify esterno non può diventare clock master. Usa un file locale.";
            return;
        }

        _masterDeck = deck;
        DeckA.SetMaster(ReferenceEquals(deck, DeckA));
        DeckB.SetMaster(ReferenceEquals(deck, DeckB));
        RaisePropertyChanged(nameof(MasterDeckText));
        Status = deck.HasLocalAudio
            ? $"Deck {deck.Name} impostato come clock master."
            : "Clock master in attesa di una traccia locale.";
    }

    private void ToggleSync(DeckViewModel deck)
    {
        if (deck.IsSpotifyExternal || _masterDeck.IsSpotifyExternal)
        {
            SyncHealth = "SYNC NON DISPONIBILE";
            Status = "Spotify esterno mantiene l'audio originale e non espone il flusso PCM: il Sync è disponibile solo tra due file locali.";
            MessageBox.Show(Status, "Smart Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (deck.IsMaster)
        {
            Status = "Il deck master genera il clock: seleziona SYNC sull'altro deck.";
            return;
        }

        if (deck.SyncEnabled)
        {
            deck.DisableSync();
            SyncHealth = "SYNC DISATTIVATO";
            Status = $"Sync disattivato sul Deck {deck.Name}.";
            return;
        }

        if (_masterDeck.Track?.CanLoadToDeck != true || deck.Track?.CanLoadToDeck != true)
        {
            SyncHealth = "SYNC IN ATTESA";
            Status = "Carica due file audio locali per usare Sync.";
            return;
        }

        if (_masterDeck.Bpm <= 0 || deck.Bpm <= 0)
        {
            SyncHealth = "BPM MANCANTE";
            Status = "Imposta o analizza il BPM su entrambi i deck. Puoi usare i pulsanti −0,1 / +0,1 o il campo BPM.";
            MessageBox.Show(Status, "Smart Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_masterDeck.IsPlaying)
        {
            _masterDeck.Play();
            Status = $"Deck {_masterDeck.Name} avviato automaticamente come master.";
        }

        var result = _audioEngine.ArmBeatSync(deck.Id, _masterDeck.Id, alignToNextBar: false, MaximumSyncPercent);
        if (!result.Success)
        {
            SyncHealth = "SYNC RIFIUTATO";
            Status = result.Message;
            MessageBox.Show(result.Message, "Smart Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        deck.EnableSync(new SyncSnapshot(
            SyncState.Armed,
            result.TempoRatio,
            result.TempoRatio,
            0,
            0,
            result.Message));
        SyncHealth = "SYNC ARMATO";
        Status = $"Deck {deck.Name}: {result.Message}. Tempo {(result.TempoRatio - 1d) * 100d:+0.0;-0.0;0.0}%.";
    }

    private void SmartOrder()
    {
        var ordered = AutoMixPlanner.OrderByBpmContinuity(Library).ToList();
        Library.Clear();
        foreach (var track in ordered) Library.Add(track);
        Status = "Coda riordinata per continuità BPM, half-time e double-time.";
    }

    private async Task SaveSessionAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salva sessione Nexora Mix",
            Filter = "Nexora Mix Session|*.nexmix",
            DefaultExt = ".nexmix"
        };
        if (dialog.ShowDialog() != true) return;

        var session = new MixSession
        {
            Name = IOPath.GetFileNameWithoutExtension(dialog.FileName),
            TransitionSeconds = TransitionBeats * 60d / Math.Max(1d, _masterDeck.EffectiveBpm),
            TransitionBeats = TransitionBeats,
            Crossfader = Crossfader,
            Tracks = Library.ToList(),
            DeckATrackId = DeckA.Track?.Id,
            DeckBTrackId = DeckB.Track?.Id,
            MasterDeck = _masterDeck.Id
        };
        await _sessionStore.SaveAsync(dialog.FileName, session);
        Status = $"Sessione salvata: {dialog.FileName}";
    }

    private async Task LoadSessionAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Apri sessione Nexora Mix",
            Filter = "Nexora Mix Session|*.nexmix"
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var session = await _sessionStore.LoadAsync(dialog.FileName);
            StopAll();
            Library.Clear();
            foreach (var track in session.Tracks)
            {
                Library.Add(track);
                if (!track.CanLoadToDeck) continue;
                BusyMessage = $"Ripristino analisi · {track.Title}";
                await AnalyzeTrackAsync(track);
            }

            TransitionBeats = session.TransitionBeats > 0 ? session.TransitionBeats : 16;
            Crossfader = session.Crossfader;
            var deckATrack = Library.FirstOrDefault(track => track.Id == session.DeckATrackId);
            var deckBTrack = Library.FirstOrDefault(track => track.Id == session.DeckBTrackId);
            if (deckATrack?.CanAssignToDeck == true) DeckA.Load(deckATrack);
            if (deckBTrack?.CanAssignToDeck == true) DeckB.Load(deckBTrack);
            SetMaster(session.MasterDeck == DeckId.B ? DeckB : DeckA);
            SelectedTrack = Library.FirstOrDefault();
            Status = $"Sessione caricata: {session.Name}";
            RaiseCommandStates();
        }
        finally { IsBusy = false; }
    }

    private void RemoveSelected()
    {
        if (SelectedTrack is null) return;
        Library.Remove(SelectedTrack);
        SelectedTrack = Library.FirstOrDefault();
        RaiseCommandStates();
    }

    private void OpenSelectedSpotify()
    {
        if (!string.IsNullOrWhiteSpace(SelectedTrack?.ExternalUrl))
            Process.Start(new ProcessStartInfo(SelectedTrack.ExternalUrl) { UseShellExecute = true });
    }

    private async Task LoadDemoAsync()
    {
        IsBusy = true;
        BusyMessage = "Creazione demo sincronizzata…";
        Status = "Preparazione della demo Advanced…";

        try
        {
            StopAll();
            _audioEngine.ResetStatistics();
            Crossfader = -1;
            TransitionBeats = 16;

            var paths = DemoAudioFactory.EnsureDemoFiles();
            var demoTracks = new List<AudioTrack>();
            foreach (var path in paths)
            {
                var track = Library.FirstOrDefault(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (track is null)
                {
                    var (artist, title) = ParseFileName(path);
                    track = new AudioTrack
                    {
                        Artist = artist,
                        Title = title,
                        FilePath = path,
                        SourceKind = TrackSourceKind.LocalFile
                    };
                    Library.Add(track);
                }

                BusyMessage = $"Analisi demo · {track.Title}";
                if (!track.IsAnalyzed || track.Waveform.Length == 0) await AnalyzeTrackAsync(track);
                demoTracks.Add(track);
            }

            if (demoTracks.Count < 2 || demoTracks.Any(track => !track.CanLoadToDeck || track.Bpm <= 0))
                throw new InvalidOperationException("Non è stato possibile preparare due tracce demo con BPM valido.");

            DeckA.Load(demoTracks[0]);
            DeckB.Load(demoTracks[1]);
            SetMaster(DeckA);
            SelectedTrack = demoTracks[0];
            AutoMixEnabled = true;
            Crossfader = -1;
            DeckA.Play();

            Status = "Demo Advanced avviata: 118 BPM master, 124 BPM follower sincronizzato automaticamente nel finale.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Avvio demo Advanced");
            StopAll();
            Status = "La demo Advanced non è partita.";
            MessageBox.Show(
                $"La demo non è riuscita ad avviarsi.\n\n{ex.Message}\n\nVerifica che Windows abbia un dispositivo audio attivo.\n\nLog: {AppLog.LogPath}",
                "Demo Nexora Mix",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void StopAll()
    {
        _transitionRunning = false;
        _transitionOutgoing = null;
        _transitionIncoming = null;
        var hadExternalPlayback = (DeckA.IsSpotifyExternal && DeckA.IsPlaying) || (DeckB.IsSpotifyExternal && DeckB.IsPlaying);
        DeckA.Stop();
        DeckB.Stop();
        if (hadExternalPlayback && _spotifyAuth.IsConnected)
            _ = PauseSpotifySilentlyAsync();
        DeckA.SetAutomixLowEq(0);
        DeckB.SetAutomixLowEq(0);
        Crossfader = 0;
        SyncHealth = "SYNC IN ATTESA";
        _phaseErrorMilliseconds = 0;
        _driftMilliseconds = 0;
        RaiseMetricProperties();
        Status = "Riproduzione arrestata.";
    }

    private async Task PauseSpotifySilentlyAsync()
    {
        try { await _spotifyApi.PausePlaybackAsync(); }
        catch (Exception ex) { AppLog.Error(ex, "Pausa Spotify durante Stop tutto"); }
    }

    private void Tick()
    {
        DeckA.Tick();
        DeckB.Tick();
        UpdateSyncMetrics();
        UpdateTransition();
        TryStartAutoMix();
        RaisePropertyChanged(nameof(MasterPeakText));
        RaisePropertyChanged(nameof(ClippingText));

        if (_audioEngine.LastOutputError is not null && !ReferenceEquals(_lastOutputError, _audioEngine.LastOutputError))
        {
            _lastOutputError = _audioEngine.LastOutputError;
            Status = $"Errore dispositivo audio: {_lastOutputError.Message}";
            AppLog.Error(_lastOutputError, "Output audio");
        }
    }

    private void UpdateSyncMetrics()
    {
        var follower = ReferenceEquals(_masterDeck, DeckA) ? DeckB : DeckA;
        if (!follower.SyncEnabled)
        {
            _phaseErrorMilliseconds = 0;
            _driftMilliseconds = 0;
            if (!_transitionRunning) SyncHealth = "SYNC IN ATTESA";
            RaiseMetricProperties();
            return;
        }

        var snapshot = _audioEngine.UpdateSync(follower.Id, _masterDeck.Id, MaximumSyncPercent);
        follower.ApplySyncSnapshot(snapshot);
        _phaseErrorMilliseconds = snapshot.PhaseErrorMilliseconds;
        _driftMilliseconds = snapshot.DriftMilliseconds;
        SyncHealth = snapshot.State switch
        {
            SyncState.Armed => "SYNC ARMATO",
            SyncState.Synchronized => "SYNC STABILE",
            SyncState.OutOfPhase => "CORREZIONE FASE",
            SyncState.Failed => "SYNC FALLITO",
            _ => "SYNC IN ATTESA"
        };
        RaiseMetricProperties();
    }

    private void TryStartAutoMix()
    {
        if (!AutoMixEnabled || _transitionRunning) return;

        var active = DeckA.IsPlaying && !DeckB.IsPlaying
            ? DeckA
            : DeckB.IsPlaying && !DeckA.IsPlaying
                ? DeckB
                : null;
        if (active?.Track is null || !active.HasLocalAudio || active.EffectiveBpm <= 0) return;

        var transitionSeconds = TransitionBeats * 60d / active.EffectiveBpm;
        var leadSeconds = active.BeatsPerBar * 60d / active.EffectiveBpm;
        var remaining = active.DurationSeconds - active.PositionSeconds;
        if (remaining > transitionSeconds + leadSeconds || remaining <= 0.2d) return;

        var inactive = ReferenceEquals(active, DeckA) ? DeckB : DeckA;
        var next = FindNextLocalTrack(active.Track);
        if (next is null)
        {
            Status = "AutoMix in attesa: non esiste una seconda traccia locale valida.";
            return;
        }

        try
        {
            if (inactive.Track?.Id != next.Id) inactive.Load(next);
            SetMaster(active);
            Crossfader = ReferenceEquals(active, DeckA) ? -1d : 1d;
            inactive.SetAutomixLowEq(BassSwapEnabled ? -24d : 0d);

            var arm = _audioEngine.ArmBeatSync(inactive.Id, active.Id, true, MaximumSyncPercent);
            if (!arm.Success)
            {
                Status = $"AutoMix rifiutato: {arm.Message}";
                inactive.SetAutomixLowEq(0);
                return;
            }

            inactive.EnableSync(new SyncSnapshot(
                SyncState.Armed,
                arm.TempoRatio,
                arm.TempoRatio,
                0,
                0,
                arm.Message));

            _transitionOutgoing = active;
            _transitionIncoming = inactive;
            _transitionStartFrame = arm.ScheduledFrame;
            var durationFrames = (long)Math.Ceiling(transitionSeconds * _audioEngine.SampleRate);
            _transitionEndFrame = _transitionStartFrame + Math.Max(1, durationFrames);
            _transitionRunning = true;
            SyncHealth = "AUTOMIX ARMATO";
            Status = $"Smart AutoMix armato: {active.Track.Title} → {next.Title}, {TransitionBeats} battute.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Smart AutoMix");
            _transitionRunning = false;
            Status = $"AutoMix non avviato: {ex.Message}";
        }
    }

    private void UpdateTransition()
    {
        if (!_transitionRunning || _transitionOutgoing is null || _transitionIncoming is null) return;
        var frame = _audioEngine.ClockFrame;
        if (frame < _transitionStartFrame) return;

        var totalFrames = Math.Max(1d, _transitionEndFrame - _transitionStartFrame);
        var progress = Math.Clamp((frame - _transitionStartFrame) / totalFrames, 0d, 1d);
        var eased = BeatGridMath.SmoothStep(0d, 1d, progress);
        var outgoingIsA = ReferenceEquals(_transitionOutgoing, DeckA);
        Crossfader = outgoingIsA ? -1d + 2d * eased : 1d - 2d * eased;

        if (BassSwapEnabled)
        {
            var bassProgress = BeatGridMath.SmoothStep(0.30d, 0.70d, progress);
            _transitionOutgoing.SetAutomixLowEq(-24d * bassProgress);
            _transitionIncoming.SetAutomixLowEq(-24d * (1d - bassProgress));
        }

        SyncHealth = $"AUTOMIX {progress:P0}";
        if (progress < 1d) return;

        var outgoing = _transitionOutgoing;
        var incoming = _transitionIncoming;
        outgoing.Pause();
        outgoing.SetAutomixLowEq(0);
        incoming.SetAutomixLowEq(0);
        SetMaster(incoming);
        Crossfader = ReferenceEquals(incoming, DeckA) ? -1d : 1d;
        _transitionRunning = false;
        _transitionOutgoing = null;
        _transitionIncoming = null;
        SyncHealth = "AUTOMIX COMPLETATO";
        Status = $"AutoMix completato. Deck {incoming.Name} è il nuovo master.";
    }

    private AudioTrack? FindNextLocalTrack(AudioTrack current)
    {
        var list = Library.Where(track => track.CanLoadToDeck && track.Bpm > 0).ToList();
        if (list.Count < 2) return null;
        var index = list.FindIndex(track => track.Id == current.Id);
        for (var offset = 1; offset <= list.Count; offset++)
        {
            var candidate = list[(Math.Max(index, -1) + offset) % list.Count];
            if (candidate.Id != current.Id) return candidate;
        }
        return null;
    }

    private void RaiseMetricProperties()
    {
        RaisePropertyChanged(nameof(PhaseErrorText));
        RaisePropertyChanged(nameof(DriftText));
        RaisePropertyChanged(nameof(MasterPeakText));
        RaisePropertyChanged(nameof(ClippingText));
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[]
                 {
                     AnalyzeSelectedCommand, LinkLocalFileCommand, LoadDeckACommand, LoadDeckBCommand,
                     RemoveSelectedCommand, OpenSpotifyCommand, AnalyzeAllCommand, SmartOrderCommand,
                     SaveSessionCommand
                 })
        {
            switch (command)
            {
                case RelayCommand relay: relay.RaiseCanExecuteChanged(); break;
                case AsyncRelayCommand asyncRelay: asyncRelay.RaiseCanExecuteChanged(); break;
            }
        }
    }

    private static int NormalizeTransitionBeats(int value)
    {
        var allowed = new[] { 4, 8, 16, 32 };
        return allowed.OrderBy(candidate => Math.Abs(candidate - value)).First();
    }

    private static (string Artist, string Title) ParseFileName(string path)
    {
        var name = IOPath.GetFileNameWithoutExtension(path);
        const string separator = " - ";
        var separatorIndex = name.IndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex + separator.Length >= name.Length)
            return ("File locale", name.Trim());

        var artist = name[..separatorIndex].Trim();
        var title = name[(separatorIndex + separator.Length)..].Trim();
        return (artist, title);
    }

    private void SavePreferences()
    {
        _settings.TransitionBeats = TransitionBeats;
        _settings.MaximumSyncPercent = MaximumSyncPercent;
        _settings.BassSwapEnabled = BassSwapEnabled;

        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Salvataggio impostazioni");
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        SavePreferences();
        DeckA.Dispose();
        DeckB.Dispose();
        _audioEngine.Dispose();
    }
}
