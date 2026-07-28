using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoraMix.App.Remote;
using NexoraMix.App.Services;
using QRCoder;

namespace NexoraMix.App.ViewModels;

public sealed partial class MainViewModel
{
    private TabletRemoteHost? _tabletRemoteHost;
    private AsyncRelayCommand? _startStopTabletRemoteCommand;
    private RelayCommand? _copyTabletUrlCommand;
    private RelayCommand? _openTabletConsoleCommand;
    private bool _tabletRemoteRunning;
    private string _tabletUrl = "Server tablet non avviato";
    private string _tabletPairingCode = "—";
    private string _tabletConnectionStatus = "Avvia la console tablet, poi apri l'indirizzo dal tablet collegato alla stessa rete Wi-Fi.";
    private ImageSource? _tabletQrCodeImage;
    private string _tabletQrCodeText = "Avvia la console tablet per generare il QR di collegamento.";

    public bool TabletRemoteRunning
    {
        get => _tabletRemoteRunning;
        private set
        {
            if (!SetProperty(ref _tabletRemoteRunning, value)) return;
            RaisePropertyChanged(nameof(TabletRemoteButtonText));
        }
    }

    public string TabletRemoteButtonText => TabletRemoteRunning ? "ARRESTA CONSOLE TABLET" : "AVVIA CONSOLE TABLET";
    public string TabletUrl { get => _tabletUrl; private set => SetProperty(ref _tabletUrl, value); }
    public string TabletPairingCode { get => _tabletPairingCode; private set => SetProperty(ref _tabletPairingCode, value); }
    public string TabletConnectionStatus { get => _tabletConnectionStatus; private set => SetProperty(ref _tabletConnectionStatus, value); }
    public ImageSource? TabletQrCodeImage { get => _tabletQrCodeImage; private set => SetProperty(ref _tabletQrCodeImage, value); }
    public string TabletQrCodeText { get => _tabletQrCodeText; private set => SetProperty(ref _tabletQrCodeText, value); }

    public ICommand StartStopTabletRemoteCommand => _startStopTabletRemoteCommand ??= new AsyncRelayCommand(ToggleTabletRemoteAsync);
    public ICommand CopyTabletUrlCommand => _copyTabletUrlCommand ??= new RelayCommand(CopyTabletUrl, () => TabletRemoteRunning);
    public ICommand OpenTabletConsoleCommand => _openTabletConsoleCommand ??= new RelayCommand(OpenTabletConsole, () => TabletRemoteRunning);

    private async Task ToggleTabletRemoteAsync()
    {
        if (_tabletRemoteHost is not null)
        {
            await StopTabletRemoteAsync();
            return;
        }

        var webRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "RemoteUi");
        var host = new TabletRemoteHost(CreateRemoteSnapshot, ExecuteRemoteCommandAsync, webRoot);
        try
        {
            TabletConnectionStatus = "Avvio del server tablet…";
            await host.StartAsync();
            _tabletRemoteHost = host;
            TabletRemoteRunning = true;
            TabletUrl = host.LocalUrl;
            TabletPairingCode = host.PairingCode;
            UpdateTabletQrCode();
            TabletConnectionStatus = "Console attiva. Tablet e PC devono essere sulla stessa rete. Inserisci il codice di abbinamento nell'app.";
            RaiseRemoteCommandStates();
        }
        catch (Exception ex)
        {
            await host.DisposeAsync();
            TabletConnectionStatus = $"Avvio fallito: {ex.Message}";
            AppLog.Error(ex, "Avvio console tablet");
            MessageBox.Show(
                "Non è stato possibile avviare la console tablet. Verifica che la porta 17840 non sia occupata e autorizza Nexora Mix nel firewall di Windows.\n\n" + ex.Message,
                "Console tablet",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task StopTabletRemoteAsync()
    {
        if (_tabletRemoteHost is null) return;
        try
        {
            await _tabletRemoteHost.StopAsync();
        }
        finally
        {
            await _tabletRemoteHost.DisposeAsync();
            _tabletRemoteHost = null;
            TabletRemoteRunning = false;
            TabletUrl = "Server tablet non avviato";
            TabletPairingCode = "—";
            TabletQrCodeImage = null;
            TabletQrCodeText = "Avvia la console tablet per generare il QR di collegamento.";
            TabletConnectionStatus = "Console tablet arrestata.";
            RaiseRemoteCommandStates();
        }
    }

    private void CopyTabletUrl()
    {
        if (!TabletRemoteRunning) return;
        try
        {
            Clipboard.SetText(TabletUrl);
            TabletConnectionStatus = "Indirizzo copiato. Aprilo sul tablet e inserisci il codice di abbinamento.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Copia indirizzo tablet");
            TabletConnectionStatus = "Non è stato possibile copiare l'indirizzo.";
        }
    }

    private void OpenTabletConsole()
    {
        if (!TabletRemoteRunning) return;
        try
        {
            Process.Start(new ProcessStartInfo(TabletUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Apertura console tablet nel browser");
        }
    }

    private void UpdateTabletQrCode()
    {
        if (!TabletRemoteRunning || string.IsNullOrWhiteSpace(TabletUrl) || !TabletPairingCode.All(char.IsDigit))
        {
            TabletQrCodeImage = null;
            TabletQrCodeText = "Avvia la console tablet per generare il QR di collegamento.";
            return;
        }

        var launchUrl = $"{TabletUrl}?pair={Uri.EscapeDataString(TabletPairingCode)}";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(launchUrl, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        var bytes = qrCode.GetGraphic(12, [18, 24, 35], [247, 251, 255]);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        image.EndInit();
        image.Freeze();
        TabletQrCodeImage = image;
        TabletQrCodeText = "Scansiona questo QR dal tablet: apre la console e inserisce automaticamente il codice.";
    }

    private void RaiseRemoteCommandStates()
    {
        _copyTabletUrlCommand?.RaiseCanExecuteChanged();
        _openTabletConsoleCommand?.RaiseCanExecuteChanged();
    }

    private RemoteMixerSnapshot CreateRemoteSnapshot()
    {
        var decks = AllDecks.Select(deck => new RemoteDeckSnapshot(
            deck.Name,
            deck.TrackTitle,
            deck.Artist,
            deck.HasLocalAudio,
            deck.IsPlaying,
            deck.IsMaster,
            deck.SyncEnabled,
            deck.CueMonitorEnabled,
            deck.Bpm,
            deck.EffectiveBpm,
            deck.TempoPercent,
            deck.PositionSeconds,
            deck.DurationSeconds,
            deck.BeatOffsetSeconds,
            deck.BeatsPerBar,
            deck.Volume,
            deck.MeterPeak,
            deck.LoopBeats,
            deck.LowEq,
            deck.MidEq,
            deck.HighEq,
            deck.Filter,
            deck.EchoMix,
            deck.BitCrush,
            deck.Saturation,
            deck.Gate,
            deck.Compressor,
            deck.Roll,
            deck.Brake,
            deck.CurrentBeatNumber,
            deck.CurrentBarNumber,
            deck.BeatInBar,
            DownsampleWaveform(deck.Waveform, 240)))
            .ToArray();

        return new RemoteMixerSnapshot(
            "8.0",
            DateTimeOffset.UtcNow,
            _masterDeck.Name,
            Crossfader,
            Status,
            SyncHealth,
            MasterPeakText,
            ClippingText,
            IsMashupRunning,
            IsRecording,
            CueVolume,
            MasterCueEnabled,
            decks);
    }

    private Task ExecuteRemoteCommandAsync(RemoteCommand command)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) return ExecuteRemoteCommandOnUiAsync(command);
        return dispatcher.InvokeAsync(() => ExecuteRemoteCommandOnUiAsync(command)).Task.Unwrap();
    }

    private async Task ExecuteRemoteCommandOnUiAsync(RemoteCommand command)
    {
        if (command.Action.StartsWith("deck.", StringComparison.OrdinalIgnoreCase))
        {
            var deck = AllDecks.FirstOrDefault(item =>
                string.Equals(item.Name, command.Deck, StringComparison.OrdinalIgnoreCase));
            if (deck is null) return;

            switch (command.Action.ToLowerInvariant())
            {
                case "deck.playpause": await deck.TogglePlayPauseAsync(); break;
                case "deck.cutdown": deck.PressCut(); break;
                case "deck.cutup": deck.ReleaseCut(); break;
                case "deck.cue": ExecuteIfPossible(deck.CueCommand); break;
                case "deck.setcue": deck.SetCue(deck.PositionSeconds); break;
                case "deck.stop": ExecuteIfPossible(deck.StopCommand); break;
                case "deck.sync": ExecuteIfPossible(deck.SyncCommand); break;
                case "deck.cuemonitor": deck.CueMonitorEnabled = !deck.CueMonitorEnabled; break;
                case "deck.master": ExecuteIfPossible(deck.MasterCommand); break;
                case "deck.loop":
                    if (command.IntValue is int beats) deck.LoopBeats = Math.Clamp(beats, 1, 32);
                    ExecuteIfPossible(deck.LoopCommand);
                    break;
                case "deck.loopsize":
                    if (command.IntValue is int loopBeats) deck.LoopBeats = Math.Clamp(loopBeats, 1, 32);
                    break;
                case "deck.hotcue":
                    if (command.IntValue is int cueIndex) deck.TriggerOrSetHotCue(cueIndex);
                    break;
                case "deck.tempo": if (command.Value is double tempo) deck.TempoPercent = tempo; break;
                case "deck.targetbpm": if (command.Value is double targetBpm) deck.TargetBpm = targetBpm; break;
                case "deck.tapbpm": ExecuteIfPossible(deck.TapBpmCommand); break;
                case "deck.volume": if (command.Value is double volume) deck.Volume = volume; break;
                case "deck.low": if (command.Value is double low) deck.LowEq = low; break;
                case "deck.mid": if (command.Value is double mid) deck.MidEq = mid; break;
                case "deck.high": if (command.Value is double high) deck.HighEq = high; break;
                case "deck.filter": if (command.Value is double filter) deck.Filter = filter; break;
                case "deck.echo": if (command.Value is double echo) deck.EchoMix = echo; break;
                case "deck.crush": if (command.Value is double crush) deck.BitCrush = crush; break;
                case "deck.saturation": if (command.Value is double saturation) deck.Saturation = saturation; break;
                case "deck.gate": if (command.Value is double gate) deck.Gate = gate; break;
                case "deck.compressor": if (command.Value is double compressor) deck.Compressor = compressor; break;
                case "deck.roll": if (command.Value is double roll) deck.Roll = roll; break;
                case "deck.brake": if (command.Value is double brake) deck.Brake = brake; break;
                case "deck.seek": if (command.Value is double seek) deck.Seek(seek); break;
                case "deck.seekrelative":
                    if (command.Value is double delta)
                        deck.Seek(Math.Clamp(deck.PositionSeconds + delta, 0d, deck.DurationSeconds));
                    break;
            }
            return;
        }

        switch (command.Action.ToLowerInvariant())
        {
            case "global.crossfader": if (command.Value is double crossfader) Crossfader = crossfader; break;
            case "global.center": Crossfader = 0d; break;
            case "global.cuevolume": if (command.Value is double cueVolume) CueVolume = cueVolume; break;
            case "global.mastercue": MasterCueEnabled = !MasterCueEnabled; break;
            case "global.stopall": ExecuteIfPossible(StopAllCommand); break;
            case "global.mashup": ExecuteIfPossible(StartAutoMashupCommand); break;
            case "global.stopmashup": ExecuteIfPossible(StopAutoMashupCommand); break;
            case "global.record": ExecuteIfPossible(StartStopRecordingCommand); break;
        }
    }

    private static float[] DownsampleWaveform(float[] source, int maximumSamples)
    {
        if (source.Length <= maximumSamples) return source.ToArray();
        var result = new float[maximumSamples];
        var step = source.Length / (double)maximumSamples;
        for (var i = 0; i < result.Length; i++)
        {
            var start = (int)Math.Floor(i * step);
            var end = Math.Min(source.Length, (int)Math.Ceiling((i + 1) * step));
            var peak = 0f;
            for (var sample = start; sample < end; sample++)
                peak = Math.Max(peak, Math.Abs(source[sample]));
            result[i] = peak;
        }
        return result;
    }

    private void DisposeRemoteConsole()
    {
        if (_tabletRemoteHost is null) return;
        try
        {
            _tabletRemoteHost.StopAsync().GetAwaiter().GetResult();
            _tabletRemoteHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Arresto console tablet");
        }
        finally
        {
            _tabletRemoteHost = null;
        }
    }
}
