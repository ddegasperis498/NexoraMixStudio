using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NexoraMix.Audio.Controllers;
using NexoraMix.Core.Controllers;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;

namespace NexoraMix.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AutoMashupPlanner _mashupPlanner = new();
    private readonly GenericMidiControllerService _midiController = new();
    private ControllerDeviceInfo? _selectedController;
    private MashupMode _selectedMashupMode = MashupMode.Safe;
    private bool _isMashupRunning;
    private string _mashupSummary = "Auto Mashup pronto · seleziona almeno 2 file locali analizzati";
    private string _controllerStatus = "Nessun controller MIDI collegato";
    private string _lastMidiMessage = "—";
    private bool _isRecording;

    public ObservableCollection<ControllerDeviceInfo> ControllerDevices { get; } = new();
    public IReadOnlyList<MashupMode> MashupModes { get; } = Enum.GetValues<MashupMode>();
    public IReadOnlyList<int> TransitionBeatOptions { get; } = new[] { 4, 8, 16, 32 };

    public ControllerDeviceInfo? SelectedController
    {
        get => _selectedController;
        set
        {
            if (!SetProperty(ref _selectedController, value)) return;
            if (ConnectControllerCommand is RelayCommand command) command.RaiseCanExecuteChanged();
        }
    }

    public MashupMode SelectedMashupMode
    {
        get => _selectedMashupMode;
        set => SetProperty(ref _selectedMashupMode, value);
    }

    public bool IsMashupRunning
    {
        get => _isMashupRunning;
        private set
        {
            if (!SetProperty(ref _isMashupRunning, value)) return;
            RaisePropertyChanged(nameof(MashupButtonText));
            if (StartAutoMashupCommand is AsyncRelayCommand start) start.RaiseCanExecuteChanged();
            if (StopAutoMashupCommand is RelayCommand stop) stop.RaiseCanExecuteChanged();
        }
    }

    public string MashupButtonText => IsMashupRunning ? "MASHUP ATTIVO" : "AVVIA AUTO MASHUP";
    public string MashupSummary { get => _mashupSummary; private set => SetProperty(ref _mashupSummary, value); }
    public string ControllerStatus { get => _controllerStatus; private set => SetProperty(ref _controllerStatus, value); }
    public string LastMidiMessage { get => _lastMidiMessage; private set => SetProperty(ref _lastMidiMessage, value); }
    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (!SetProperty(ref _isRecording, value)) return;
            RaisePropertyChanged(nameof(RecordingButtonText));
        }
    }
    public string RecordingButtonText => IsRecording ? "STOP REGISTRAZIONE" : "REGISTRA MASTER";

    public ICommand LoadDeckCCommand { get; private set; } = null!;
    public ICommand LoadDeckDCommand { get; private set; } = null!;
    public ICommand StartAutoMashupCommand { get; private set; } = null!;
    public ICommand StopAutoMashupCommand { get; private set; } = null!;
    public ICommand StartStopRecordingCommand { get; private set; } = null!;
    public ICommand RefreshControllersCommand { get; private set; } = null!;
    public ICommand ConnectControllerCommand { get; private set; } = null!;
    public ICommand DisconnectControllerCommand { get; private set; } = null!;

    private void InitializeV7()
    {
        LoadDeckCCommand = new RelayCommand(
            parameter => LoadTrackToDeck(parameter as AudioTrack ?? SelectedTrack, DeckC),
            parameter => (parameter as AudioTrack ?? SelectedTrack)?.CanLoadToDeck == true);
        LoadDeckDCommand = new RelayCommand(
            parameter => LoadTrackToDeck(parameter as AudioTrack ?? SelectedTrack, DeckD),
            parameter => (parameter as AudioTrack ?? SelectedTrack)?.CanLoadToDeck == true);
        StartAutoMashupCommand = new AsyncRelayCommand(
            StartAutoMashupAsync,
            () => !IsMashupRunning && Library.Count(track => track.CanLoadToDeck && track.Bpm > 0) >= 2);
        StopAutoMashupCommand = new RelayCommand(
            StopAutoMashup,
            () => IsMashupRunning);
        StartStopRecordingCommand = new RelayCommand(StartStopRecording);
        RefreshControllersCommand = new RelayCommand(RefreshControllers);
        ConnectControllerCommand = new RelayCommand(ConnectSelectedController, () => SelectedController is not null);
        DisconnectControllerCommand = new RelayCommand(DisconnectController);

        _midiController.InputReceived += OnMidiInputReceived;
        _midiController.ConnectionChanged += OnMidiConnectionChanged;
        RefreshControllers();
    }

    private async Task StartAutoMashupAsync()
    {
        var candidates = Library
            .Where(track => track.IsMashupSelected && track.CanLoadToDeck && track.Bpm > 0)
            .Take(4)
            .ToList();
        if (candidates.Count < 2)
        {
            candidates.Clear();
            if (SelectedTrack?.CanLoadToDeck == true && SelectedTrack.Bpm > 0) candidates.Add(SelectedTrack);
            candidates.AddRange(Library.Where(track => track.CanLoadToDeck && track.Bpm > 0 && candidates.All(item => item.Id != track.Id)));
            candidates = candidates.Take(4).ToList();
        }

        MashupPlan plan;
        try
        {
            plan = _mashupPlanner.CreatePlan(candidates, SelectedMashupMode, MaximumSyncPercent, TransitionBeats);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            MessageBox.Show(ex.Message, "Auto Mashup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StopAll();
        AutoMixEnabled = false;
        Crossfader = 0;
        _audioEngine.ResetStatistics();

        foreach (var deckPlan in plan.Decks)
        {
            var track = candidates.First(item => item.Id == deckPlan.TrackId);
            var deck = AllDecks.First(item => item.Id == deckPlan.Deck);
            deck.Load(track);
            deck.Volume = deckPlan.ChannelGain;
            deck.LowEq = deckPlan.LowEqDb;
            deck.MidEq = deckPlan.MidEqDb;
            deck.HighEq = deckPlan.HighEqDb;
            deck.TargetBpm = deckPlan.TargetBpm;
        }

        var master = AllDecks.First(deck => deck.Id == plan.MasterDeck);
        SetMaster(master);
        master.Play();

        foreach (var deckPlan in plan.Decks.Where(item => item.Deck != plan.MasterDeck))
        {
            var deck = AllDecks.First(item => item.Id == deckPlan.Deck);
            var result = _audioEngine.ArmBeatSync(
                deck.Id,
                master.Id,
                alignToNextBar: true,
                MaximumSyncPercent,
                additionalBars: deckPlan.EntryBar,
                alignToPhrase: true);

            if (!result.Success)
            {
                StopAll();
                Status = $"Auto Mashup interrotto: {result.Message}";
                MessageBox.Show(result.Message, "Auto Mashup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            deck.EnableSync(new SyncSnapshot(
                SyncState.Armed,
                result.TempoRatio,
                result.TempoRatio,
                0,
                0,
                $"{deckPlan.Role} · ingresso misura {deckPlan.EntryBar + 1}"));
        }

        IsMashupRunning = true;
        MashupSummary = plan.Summary + " · " + string.Join(" · ", plan.Decks.Select(item => $"{item.Deck}:{item.Role}"));
        SyncHealth = $"AUTO MASHUP · {plan.Decks.Count} DECK";
        Status = "Auto Mashup avviato: i layer entrano su misure successive, con BPM, fase, gain ed EQ controllati.";
        await Task.CompletedTask;
    }

    private void StopAutoMashup()
    {
        IsMashupRunning = false;
        MashupSummary = "Auto Mashup arrestato";
        StopAll();
    }

    private void StartStopRecording()
    {
        if (IsRecording)
        {
            var path = _audioEngine.StopRecording();
            IsRecording = false;
            Status = string.IsNullOrWhiteSpace(path) ? "Registrazione arrestata." : $"Registrazione salvata: {path}";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Registra master Nexora Mix",
            Filter = "Wave 32-bit float|*.wav",
            DefaultExt = ".wav",
            FileName = $"NexoraMix-{DateTime.Now:yyyyMMdd-HHmmss}.wav"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            _audioEngine.StartRecording(dialog.FileName);
            IsRecording = true;
            Status = $"Registrazione master attiva: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Status = $"Registrazione non avviata: {ex.Message}";
            MessageBox.Show(ex.Message, "Registrazione", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshControllers()
    {
        try
        {
            var currentId = SelectedController?.StableId;
            ControllerDevices.Clear();
            foreach (var device in _midiController.EnumerateDevices()) ControllerDevices.Add(device);
            SelectedController = ControllerDevices.FirstOrDefault(device => device.StableId == currentId)
                                 ?? ControllerDevices.FirstOrDefault();
            ControllerStatus = ControllerDevices.Count == 0
                ? "Nessun controller MIDI rilevato"
                : $"{ControllerDevices.Count} controller MIDI rilevati";
        }
        catch (Exception ex)
        {
            ControllerStatus = $"Enumerazione MIDI fallita: {ex.Message}";
        }
    }

    private void ConnectSelectedController()
    {
        if (SelectedController is null) return;
        try
        {
            _midiController.Disconnect();
            if (SelectedController.InputDeviceNumber is int input) _midiController.ConnectInput(input);
            if (SelectedController.OutputDeviceNumber is int output) _midiController.ConnectOutput(output);
            var profile = ControllerProfileCatalog.Match(SelectedController.ProductName);
            ControllerStatus = $"Collegato: {SelectedController.ProductName} ({SelectedController.CapabilityText}) · {profile.DisplayName} · {profile.VerificationStatus}";
        }
        catch (Exception ex)
        {
            ControllerStatus = $"Connessione controller fallita: {ex.Message}";
            MessageBox.Show(ex.Message, "Controller MIDI", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DisconnectController()
    {
        _midiController.Disconnect();
        ControllerStatus = "Controller MIDI scollegato";
    }

    private void OnMidiConnectionChanged(object? sender, string message)
    {
        Application.Current?.Dispatcher.InvokeAsync(() => ControllerStatus = message);
    }

    private void OnMidiInputReceived(object? sender, ControllerInputEvent input)
    {
        Application.Current?.Dispatcher.InvokeAsync(() => ApplyMidiInput(input));
    }

    private void ApplyMidiInput(ControllerInputEvent input)
    {
        LastMidiMessage = $"{input.Kind} CH{input.Channel} {input.Data1}/{input.Data2} · {input.RawDescription}";
        var decks = AllDecks;

        if (input.Kind == ControllerMessageKind.Note && input.Data2 > 0)
        {
            switch (input.Data1)
            {
                case 36: decks[0].TogglePlayPause(); break;
                case 37: ExecuteIfPossible(decks[0].CueCommand); break;
                case 38: decks[1].TogglePlayPause(); break;
                case 39: ExecuteIfPossible(decks[1].CueCommand); break;
                case 40: decks[2].TogglePlayPause(); break;
                case 41: ExecuteIfPossible(decks[2].CueCommand); break;
                case 42: decks[3].TogglePlayPause(); break;
                case 43: ExecuteIfPossible(decks[3].CueCommand); break;
                case 44: ToggleSync(decks[0]); break;
                case 45: ToggleSync(decks[1]); break;
                case 46: ToggleSync(decks[2]); break;
                case 47: ToggleSync(decks[3]); break;
                case 48: if (decks[0].HasLocalAudio) SetMaster(decks[0]); break;
                case 49: if (decks[1].HasLocalAudio) SetMaster(decks[1]); break;
                case 50: if (decks[2].HasLocalAudio) SetMaster(decks[2]); break;
                case 51: if (decks[3].HasLocalAudio) SetMaster(decks[3]); break;
                case 52: ExecuteIfPossible(StartAutoMashupCommand); break;
                case 53: StopAll(); break;
                case 54: StartStopRecording(); break;
            }
            return;
        }

        if (input.Kind == ControllerMessageKind.ControlChange)
        {
            var normalized = Math.Clamp(input.Data2 / 127d, 0d, 1d);
            switch (input.Data1)
            {
                case 16: decks[0].Volume = normalized; break;
                case 17: decks[1].Volume = normalized; break;
                case 18: decks[2].Volume = normalized; break;
                case 19: decks[3].Volume = normalized; break;
                case 20: Crossfader = normalized * 2d - 1d; break;
                case 21: _audioEngine.SetMasterGain(normalized); break;
            }
            return;
        }

        if (input.Kind == ControllerMessageKind.PitchBend)
        {
            var value14 = input.Data1 | (input.Data2 << 7);
            var normalized = (value14 - 8192d) / 8192d;
            var deckIndex = Math.Clamp(input.Channel - 1, 0, 3);
            decks[deckIndex].TempoPercent = Math.Clamp(normalized * 16d, -16d, 16d);
        }
    }

    private static void ExecuteIfPossible(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void DisposeV7()
    {
        if (IsRecording) _audioEngine.StopRecording();
        _midiController.InputReceived -= OnMidiInputReceived;
        _midiController.ConnectionChanged -= OnMidiConnectionChanged;
        _midiController.Dispose();
    }
}
