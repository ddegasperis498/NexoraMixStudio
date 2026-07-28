using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using NexoraMix.App.Services;
using NexoraMix.Core.Models;

namespace NexoraMix.App.ViewModels;

public sealed partial class MainViewModel
{
    private NoraDjAssistantService? _noraAssistant;
    private CancellationTokenSource? _noraRefreshCancellation;
    private string _noraStatus = "Nora AI si collegherà al catalogo musicale condiviso.";
    private string _noraCurrentTrack = "Nessuna traccia attiva";
    private bool _isNoraBusy;

    public ObservableCollection<NoraDjRecommendation> NoraRecommendations { get; } = new();
    public ICommand RefreshNoraCommand { get; private set; } = null!;
    public ICommand LoadNoraSuggestionCommand { get; private set; } = null!;
    public string NoraStatus { get => _noraStatus; private set => SetProperty(ref _noraStatus, value); }
    public string NoraCurrentTrack { get => _noraCurrentTrack; private set => SetProperty(ref _noraCurrentTrack, value); }
    public bool IsNoraBusy { get => _isNoraBusy; private set => SetProperty(ref _isNoraBusy, value); }

    private void InitializeNora()
    {
        _noraAssistant = new NoraDjAssistantService();
        RefreshNoraCommand = new AsyncRelayCommand(RefreshNoraRecommendationsAsync);
        LoadNoraSuggestionCommand = new RelayCommand(LoadNoraSuggestion);
        foreach (var deck in AllDecks) deck.PropertyChanged += OnNoraDeckPropertyChanged;
    }

    private void OnNoraDeckPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeckViewModel.Track) or nameof(DeckViewModel.IsPlaying))
            ScheduleNoraRefresh();
    }

    private void ScheduleNoraRefresh()
    {
        if (_noraAssistant is null) return;
        _noraRefreshCancellation?.Cancel();
        _noraRefreshCancellation?.Dispose();
        _noraRefreshCancellation = new CancellationTokenSource();
        _ = RefreshNoraRecommendationsAsync(_noraRefreshCancellation.Token);
    }

    private Task RefreshNoraRecommendationsAsync() =>
        RefreshNoraRecommendationsAsync(CancellationToken.None);

    private async Task RefreshNoraRecommendationsAsync(CancellationToken cancellationToken)
    {
        if (_noraAssistant is null) return;
        var current = _masterDeck.Track
                      ?? AllDecks.FirstOrDefault(deck => deck.IsPlaying && deck.Track is not null)?.Track;
        if (current is null)
        {
            NoraCurrentTrack = "Nessuna traccia attiva";
            NoraStatus = "Carica una traccia su un deck: Nora proporrà subito il seguito più coerente.";
            NoraRecommendations.Clear();
            return;
        }

        IsNoraBusy = true;
        NoraCurrentTrack = $"In ascolto: {current.Artist} — {current.Title} · {current.BpmText} BPM";
        NoraStatus = "Nora sta confrontando BPM, anno e genere…";
        try
        {
            var result = await _noraAssistant.RecommendAsync(current, Library.ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            NoraRecommendations.Clear();
            foreach (var recommendation in result.Recommendations) NoraRecommendations.Add(recommendation);
            NoraStatus = result.CatalogTrackCount == 0
                ? "Catalogo PuliziaSpazioDev non ancora popolato: suggerimenti basati sui BPM della libreria Mix Studio."
                : $"Nora connessa · {result.CatalogTrackCount} tracce note · {result.SharedTeachingCount} insegnamenti condivisi · {NoraRecommendations.Count} suggerimenti";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Nora DJ Assistant");
            NoraStatus = $"Nora non riesce a leggere il catalogo condiviso: {ex.Message}";
        }
        finally
        {
            IsNoraBusy = false;
        }
    }

    private void LoadNoraSuggestion(object? parameter)
    {
        if (parameter is not NoraDjRecommendation recommendation) return;
        if (recommendation.LocalTrack?.CanLoadToDeck != true)
        {
            Status = $"{recommendation.Title} è nota a Nora ma non è ancora presente nella libreria locale di Mix Studio.";
            return;
        }

        SelectedTrack = recommendation.LocalTrack;
        LoadSelectedToAvailableDeck();
        Status = $"Scelta di Nora caricata: {recommendation.Artist} — {recommendation.Title} ({recommendation.Compatibility}).";
    }

    private void DisposeNora()
    {
        foreach (var deck in AllDecks) deck.PropertyChanged -= OnNoraDeckPropertyChanged;
        _noraRefreshCancellation?.Cancel();
        _noraRefreshCancellation?.Dispose();
        _noraAssistant?.Dispose();
    }
}
