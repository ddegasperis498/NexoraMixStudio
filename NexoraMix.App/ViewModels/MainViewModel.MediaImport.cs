using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using NexoraMix.App.Services;
using NexoraMix.App.Views;

namespace NexoraMix.App.ViewModels;

public sealed partial class MainViewModel
{
    private FileSystemWatcher? _musicInboxWatcher;
    private RelayCommand? _openMusicFolderCommand;
    private AsyncRelayCommand? _importMusicInboxCommand;
    private RelayCommand? _openYouTubeLinkCommand;

    public string MusicRootPath { get; private set; } = string.Empty;
    public string MusicInboxPath { get; private set; } = string.Empty;
    public string YouTubeImportNotice =>
        "Nexora non estrae audio da YouTube e non automatizza servizi di ripping. Incolla il link per aprirlo, poi importa nella cartella Musica un file che possiedi o che sei autorizzato a usare.";

    public ICommand OpenMusicFolderCommand => _openMusicFolderCommand ??= new RelayCommand(OpenMusicFolder);
    public ICommand ImportMusicInboxCommand => _importMusicInboxCommand ??= new AsyncRelayCommand(ImportMusicInboxAsync);
    public ICommand OpenYouTubeLinkCommand => _openYouTubeLinkCommand ??= new RelayCommand(OpenYouTubeLink);

    private void InitializeMediaImport()
    {
        MusicRootPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrWhiteSpace(MusicRootPath))
            MusicRootPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Music");
        MusicInboxPath = System.IO.Path.Combine(MusicRootPath, "Inbox");
        Directory.CreateDirectory(MusicRootPath);
        Directory.CreateDirectory(MusicInboxPath);

        _musicInboxWatcher = new FileSystemWatcher(MusicRootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _musicInboxWatcher.Created += OnMusicInboxFile;
        _musicInboxWatcher.Renamed += OnMusicInboxFile;
    }

    private void OpenMusicFolder()
    {
        try
        {
            Directory.CreateDirectory(MusicInboxPath);
            Process.Start(new ProcessStartInfo(MusicRootPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Apertura cartella Musica");
            MessageBox.Show(ex.Message, "Cartella Musica", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenYouTubeLink()
    {
        var prompt = new TextPromptWindow(
            "Apri link YouTube",
            "Incolla un link YouTube. Nexora aprirà il video nel browser. Per il mixer importa successivamente un file audio ottenuto legalmente e autorizzato.");
        if (prompt.ShowDialog() != true) return;

        if (!Uri.TryCreate(prompt.Value.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Inserisci un link youtube.com o youtu.be valido.", "Link YouTube", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            Status = "Link YouTube aperto. Salva nella cartella Musica soltanto contenuti che puoi legalmente utilizzare.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Apertura link YouTube");
            MessageBox.Show(ex.Message, "Link YouTube", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ImportMusicInboxAsync()
    {
        Directory.CreateDirectory(MusicRootPath);
        Directory.CreateDirectory(MusicInboxPath);
        var files = await Task.Run(EnumerateNewMusicFolderFiles);

        if (files.Length == 0)
        {
            MessageBox.Show(
                $"Non ci sono tracce nuove da importare.\n\nCartella controllata:\n{MusicRootPath}",
                "Cartella Musica",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await ImportFilePathsAsync(files);
        Status = $"Importati {files.Length} file dalla cartella Musica.";
    }

    private void ImportMusicFolderAtStartup()
    {
        _ = ImportMusicFolderAtStartupAsync();
    }

    private async Task ImportMusicFolderAtStartupAsync()
    {
        try
        {
            await Task.Delay(800);
            Status = $"Controllo cartella Musica in background: {MusicRootPath}";

            var files = await Task.Run(EnumerateMusicFolderFiles);
            if (files.Length == 0)
            {
                Status = $"Cartella Musica pronta: {MusicRootPath}";
                return;
            }

            var knownFiles = await Task.Run(() => files.Where(path => _musicImportIndex.IsKnown(path)).ToArray());
            var newFiles = await Task.Run(() => files.Where(path => !_musicImportIndex.IsKnown(path)).ToArray());

            await RestoreKnownMusicFolderTracksAsync(knownFiles);

            if (newFiles.Length == 0)
            {
                Status = "Cartella Musica riconosciuta: nessuna traccia nuova da importare.";
                return;
            }

            await ImportFilePathsAsync(newFiles);
            Status = $"Cartella Musica aggiornata: {newFiles.Length} nuove tracce importate.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Caricamento automatico cartella Musica");
            Status = "Caricamento automatico cartella Musica non riuscito.";
        }
    }

    private string[] EnumerateMusicFolderFiles()
    {
        Directory.CreateDirectory(MusicRootPath);
        Directory.CreateDirectory(MusicInboxPath);
        return Directory.EnumerateFiles(MusicRootPath, "*.*", SearchOption.AllDirectories)
            .Where(path => SupportedExtensions.Contains(System.IO.Path.GetExtension(path)))
            .ToArray();
    }

    private string[] EnumerateNewMusicFolderFiles() =>
        EnumerateMusicFolderFiles()
            .Where(path => !_musicImportIndex.IsKnown(path))
            .ToArray();

    private async Task RestoreKnownMusicFolderTracksAsync(IEnumerable<string> files)
    {
        var restored = 0;
        foreach (var path in files)
        {
            if (Library.Any(track => string.Equals(track.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            var track = _musicImportIndex.TryCreateTrack(path);
            if (track is null) continue;
            var cached = await _analysisCache.TryLoadAsync(path);
            if (cached is not null)
            {
                track.DurationSeconds = cached.DurationSeconds;
                track.Bpm = cached.Bpm;
                track.BeatOffsetSeconds = cached.BeatOffsetSeconds;
                track.Waveform = cached.Waveform;
                track.AnalysisConfidence = cached.Confidence;
                track.PhaseConfidence = cached.PhaseConfidence;
                track.DetectedBeatCount = cached.DetectedBeatCount;
                track.EstimatedBarCount = cached.EstimatedBarCount;
                track.AverageBeatIntervalSeconds = cached.AverageBeatIntervalSeconds;
                track.IsAnalyzed = true;
                track.AnalysisStatus = cached.Bpm > 0
                    ? $"Gia riconosciuta da cache · {cached.Bpm:0.0} BPM · {cached.DetectedBeatCount} battute"
                    : "Gia riconosciuta da cache · BPM da correggere o usare TAP BPM";
            }

            Library.Add(track);
            SelectedTrack ??= track;
            restored++;

            if (restored % 25 != 0) continue;
            Status = $"Ripristino libreria Musica: {restored} tracce riconosciute.";
            await Application.Current.Dispatcher.InvokeAsync(
                () => { },
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnMusicInboxFile(object sender, FileSystemEventArgs e)
    {
        if (!SupportedExtensions.Contains(System.IO.Path.GetExtension(e.FullPath))) return;
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                // Attendi che il browser/file manager completi la scrittura.
                await Task.Delay(900);
                if (File.Exists(e.FullPath) && !_musicImportIndex.IsKnown(e.FullPath))
                    await ImportFilePathsAsync(new[] { e.FullPath });
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "Importazione automatica cartella Musica");
            }
        });
    }

    private void DisposeMediaImport()
    {
        if (_musicInboxWatcher is null) return;
        _musicInboxWatcher.EnableRaisingEvents = false;
        _musicInboxWatcher.Created -= OnMusicInboxFile;
        _musicInboxWatcher.Renamed -= OnMusicInboxFile;
        _musicInboxWatcher.Dispose();
        _musicInboxWatcher = null;
    }
}
