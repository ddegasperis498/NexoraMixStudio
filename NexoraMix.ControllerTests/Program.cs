using System.Diagnostics;
using Microsoft.Data.Sqlite;
using NexoraMix.App.Services;
using NexoraMix.Core.Controllers;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;

var failures = new List<string>();

void Check(bool condition, string message)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {message}");
    if (!condition) failures.Add(message);
}

Console.WriteLine("Nexora Mix Studio V7 - controller model tests");
var generic = ControllerProfileCatalog.Match("Generic USB MIDI Controller");
Check(generic.VerificationStatus == ControllerVerificationStatus.GenericMidi, "Fallback Generic MIDI");
Check(generic.Mappings.Count >= 29, "Mapping 4-deck completo presente");
Check(generic.Mappings.Count(mapping => mapping.MappingKind == ControlMappingKind.PitchFader) == 4, "Pitch fader per quattro deck presente");
Check(generic.Mappings.Any(mapping => mapping.Command == "Mixer.Crossfader" && mapping.SoftTakeover), "Soft takeover su crossfader generico");
Check(generic.FeedbackMappings.Count >= 5, "Feedback MIDI generico presente");
Check(Enum.IsDefined(ControllerVerificationStatus.UserCreated), "Stato profilo UserCreated disponibile");

var pioneer = ControllerProfileCatalog.Match("Pioneer DJ DDJ Test Device");
Check(pioneer.Manufacturer.Contains("Pioneer", StringComparison.OrdinalIgnoreCase), "Riconoscimento famiglia Pioneer/AlphaTheta");
Check(pioneer.VerificationStatus == ControllerVerificationStatus.BuiltInExperimental, "Profilo vendor marcato sperimentale");

var gemini = ControllerProfileCatalog.Match("Gemini G4V");
Check(gemini.Manufacturer == "Gemini", "Riconoscimento famiglia Gemini");

Check(ControllerProfileCatalog.BuiltIn.Any(profile => profile.Manufacturer.Contains("Denon", StringComparison.OrdinalIgnoreCase)), "Catalogo Denon presente");
Check(ControllerProfileCatalog.BuiltIn.Any(profile => profile.Manufacturer.Contains("Numark", StringComparison.OrdinalIgnoreCase)), "Catalogo Numark presente");
Check(ControllerProfileCatalog.BuiltIn.Any(profile => profile.Manufacturer.Contains("Hercules", StringComparison.OrdinalIgnoreCase)), "Catalogo Hercules presente");

var store = new ControllerProfileStore();
var customProfile = generic with
{
    Id = "user-test-profile",
    DisplayName = "User Test Profile",
    VerificationStatus = ControllerVerificationStatus.UserCreated
};
var validationErrors = ControllerProfileStore.Validate(customProfile);
Check(validationErrors.Count == 0, "Validazione profilo utente senza errori");

var profilePath = Path.Combine(Path.GetTempPath(), "NexoraMix-ControllerTests", Guid.NewGuid().ToString("N"), "profile.json");
await store.SaveAsync(profilePath, customProfile);
var loadedProfile = await store.LoadAsync(profilePath);
Check(loadedProfile.Id == customProfile.Id && loadedProfile.Mappings.Count == customProfile.Mappings.Count, "Import/export profilo controller conserva mapping");

var currentTrack = new NoraTrackProfile("current", "Current", "DJ", 124, 2001, "House");
var rankedTracks = NoraTrackCompatibility.Rank(currentTrack,
[
    new("best", "Best", "DJ", 124, 2001, "House"),
    new("half-time", "Half", "DJ", 62, 2008, "House"),
    new("far", "Far", "DJ", 90, 1970, "Metal")
]);
Check(rankedTracks[0].Track.Id == "best", "Nora ordina prima la traccia compatibile per BPM, anno e genere");
Check(rankedTracks[1].Track.Id == "half-time" && rankedTracks[1].BpmDifference == 0, "Nora riconosce la compatibilita BPM half-time/double-time");
Check(rankedTracks[0].Score > rankedTracks[^1].Score, "Nora penalizza candidati lontani e di genere diverso");

var advancedCurrent = new NoraTrackProfile("advanced-current", "Current Advanced", "Artist A", 124, 2020, "House")
{
    IsFileAvailable = true,
    DurationSeconds = 240,
    AudioFeatures = new TrackAudioFeatures
    {
        Status = AudioFeatureAnalysisStatus.Partial,
        Bpm = 124,
        BpmConfidence = 0.95,
        CamelotKey = "8A",
        KeyConfidence = 0.9,
        Energy = 0.62,
        OverallConfidence = 0.88,
        VocalPresence = VocalPresence.Predominant,
        VocalConfidence = 0.8
    }
};
var pitchCandidate = new NoraTrackProfile("pitch", "Pitch", "Artist B", 126, 2021, "House")
{
    IsFileAvailable = true,
    DurationSeconds = 220,
    AudioFeatures = advancedCurrent.AudioFeatures with
    {
        Bpm = 126,
        CamelotKey = "9A",
        Energy = 0.68,
        VocalPresence = VocalPresence.Instrumental
    }
};
var pitchMatch = NoraTrackCompatibility.Rank(advancedCurrent, [pitchCandidate], 1)[0];
Check(Math.Abs((pitchMatch.SuggestedPitchPercent ?? 0d) - -1.5873d) < 0.02d,
    "Nora calcola il pitch percentuale necessario");
Check(pitchMatch.Factors.Any(factor => factor.Name == "Compatibilità Camelot" && factor.RawValue == 0.85d),
    "Nora valuta il movimento Camelot adiacente");
Check(pitchMatch.Factors.Any(factor => factor.Name == "Traiettoria energetica" && factor.Contribution > 0d),
    "Nora applica lo scoring energia");
Check(pitchMatch.Factors.Any(factor => factor.Name == "Compatibilità vocale" && factor.RawValue == 1d),
    "Nora preferisce voce predominante verso ingresso strumentale");
Check(Math.Abs(pitchMatch.Factors.Sum(factor => factor.Contribution) - pitchMatch.Score) < 0.11d,
    "La spiegazione strutturata somma al punteggio finale");
Check(!string.IsNullOrWhiteSpace(pitchMatch.Explanation) && pitchMatch.RankingVersion == NoraRankingWeights.CurrentVersion,
    "Nora conserva spiegazione leggibile e versione ranking");

var energyLow = pitchCandidate with
{
    Id = "energy-low",
    AudioFeatures = pitchCandidate.AudioFeatures! with { Energy = 0.20 }
};
var energyTarget = new NoraRankingContext { TargetEnergy = 0.70 };
var energyRanking = NoraTrackCompatibility.Rank(advancedCurrent, [energyLow, pitchCandidate], energyTarget, limit: 2);
Check(energyRanking[0].Track.Id == "pitch", "Nora segue la traiettoria energetica configurata");

var vocalConflict = pitchCandidate with
{
    Id = "vocal-conflict",
    AudioFeatures = pitchCandidate.AudioFeatures! with { VocalPresence = VocalPresence.Predominant }
};
var vocalRanking = NoraTrackCompatibility.Rank(advancedCurrent, [vocalConflict, pitchCandidate], 2);
Check(vocalRanking[0].Track.Id == "pitch", "Nora penalizza la sovrapposizione di due voci predominanti");

var missingCandidate = new NoraTrackProfile("missing", "Missing", "Unknown", null, null, null);
var missingMatch = NoraTrackCompatibility.Rank(advancedCurrent, [missingCandidate], 1)[0];
Check(!missingMatch.Factors.Any(factor => factor.Name is "BPM e pitch" or "Genere" or "Epoca") &&
      missingMatch.Factors.Any(factor => factor.Name == "Incertezza dati" && factor.Contribution < 0d),
    "Feature mancanti non diventano falsi zeri e generano penalità d'incertezza esplicita");

var available = pitchCandidate with { Id = "available", IsFileAvailable = true };
var unavailable = pitchCandidate with { Id = "unavailable", IsFileAvailable = false };
var availabilityRanking = NoraTrackCompatibility.Rank(advancedCurrent, [unavailable, available], 2);
Check(availabilityRanking[0].Track.Id == "available" &&
      availabilityRanking[^1].Factors.Any(factor => factor.Name == "Disponibilità file" && factor.RawValue == 0d),
    "Nora preferisce il file disponibile e spiega l'indisponibilità");

var neutralMatch = NoraTrackCompatibility.Rank(advancedCurrent, [pitchCandidate], new NoraRankingContext(), limit: 1)[0];
var penalizedMatch = NoraTrackCompatibility.Rank(advancedCurrent, [pitchCandidate], new NoraRankingContext
{
    RecentTrackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pitchCandidate.Id },
    RecentArtists = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pitchCandidate.Artist }
}, limit: 1)[0];
Check(penalizedMatch.Score < neutralMatch.Score &&
      penalizedMatch.Factors.Any(factor => factor.Name == "Traccia recente") &&
      penalizedMatch.Factors.Any(factor => factor.Name == "Affaticamento artista"),
    "Nora applica penalità traccia recente e affaticamento artista");

var deterministicTie = NoraTrackCompatibility.Rank(advancedCurrent,
[
    pitchCandidate with { Id = "tie-b", Title = "Tie" },
    pitchCandidate with { Id = "tie-a", Title = "Tie" }
], 2);
Check(deterministicTie[0].Track.Id == "tie-a", "Nora risolve i pareggi in modo deterministico");

var planningCurrent = PlanTrack("plan-current", "Opening", "Resident", 120, 0.40, "8A", VocalPresence.Instrumental);
var planningCandidates = new[]
{
    PlanTrack("plan-1", "Warm Lift", "Artist 1", 121, 0.48, "8A", VocalPresence.Occasional),
    PlanTrack("plan-2", "Build One", "Artist 2", 122, 0.58, "9A", VocalPresence.Instrumental),
    PlanTrack("plan-3", "Build Two", "Artist 3", 123, 0.67, "10A", VocalPresence.Occasional),
    PlanTrack("plan-4", "Near Peak", "Artist 4", 124, 0.76, "11A", VocalPresence.Instrumental),
    PlanTrack("plan-5", "Alternative", "Artist 5", 121.5, 0.54, "8B", VocalPresence.Predominant),
    PlanTrack("recent-track", "Already Played", "Artist 6", 122, 0.62, "9A", VocalPresence.Instrumental)
};
var plannerOptions = new NoraSetPlannerOptions { PlanLength = 3, BeamWidth = 4, CandidatesPerLevel = 5, AlternativeCount = 1 };
var plannerContext = new NoraRankingContext
{
    RecentTrackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "recent-track" }
};
var trajectory = new NoraSetTrajectory
{
    CurrentEnergy = 0.40,
    TargetEnergy = 0.76,
    TargetPhase = NoraSetPhase.ProgressiveBuild,
    AllowEnergyDrop = false,
    MaximumBpmIncreasePerTrack = 4
};
var setPlanner = new NoraSetPlanner();
var setPlan = setPlanner.CreatePlan(planningCurrent, planningCandidates, trajectory, plannerContext, plannerOptions);
Check(setPlan.Sequence.Count == 3, "Set planner genera almeno tre tracce");
Check(setPlan.Sequence.Select(step => step.Track.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3 &&
      setPlan.Sequence.All(step => step.Track.Id is not "plan-current" and not "recent-track"),
    "Set planner evita duplicati, traccia corrente e tracce recenti");
Check(setPlan.Sequence.Zip(setPlan.Sequence.Skip(1), (left, right) =>
        right.Track.AudioFeatures!.Energy >= left.Track.AudioFeatures!.Energy - 0.035d).All(value => value),
    "Set planner rispetta la crescita energetica senza cali non autorizzati");
var maximumExpanded = plannerOptions.CandidatesPerLevel *
                      (1 + plannerOptions.BeamWidth * (plannerOptions.PlanLength - 1));
Check(setPlan.ExpandedNodes <= maximumExpanded && setPlan.Alternatives.Count > 0,
    "Beam search resta limitato e produce alternative");
var repeatedPlan = setPlanner.CreatePlan(planningCurrent, planningCandidates, trajectory, plannerContext, plannerOptions);
Check(setPlan.Sequence.Select(step => step.Track.Id).SequenceEqual(repeatedPlan.Sequence.Select(step => step.Track.Id)),
    "Set planner è deterministico a parità di input");
var insufficientPlanRejected = false;
try
{
    setPlanner.CreatePlan(planningCurrent, planningCandidates.Take(2), trajectory, plannerContext, plannerOptions);
}
catch (InvalidOperationException exception) when (exception.Message.Contains("almeno 3", StringComparison.OrdinalIgnoreCase))
{
    insufficientPlanRejected = true;
}
Check(insufficientPlanRejected, "Set planner segnala esplicitamente candidati insufficienti invece di creare un piano incompleto");

var transitionCurrent = planningCurrent with
{
    AudioFeatures = planningCurrent.AudioFeatures! with
    {
        VocalPresence = VocalPresence.Instrumental,
        Sections = [new TrackAudioSection(180, 240, TrackSectionType.Outro, 0.9)]
    }
};
var transitionCandidate = planningCandidates[0] with
{
    Bpm = 126,
    AudioFeatures = planningCandidates[0].AudioFeatures! with
    {
        VocalPresence = VocalPresence.Occasional,
        Sections = [new TrackAudioSection(0, 24, TrackSectionType.Intro, 0.88)]
    }
};
var transitionDeckState = new NoraDeckState(
    DeckId.A, DeckId.A, null, null, DeckId.B, 0.9, false, "Deck A udibile, Deck B libero");
var transitionAdvisor = new NoraTransitionAdvisor();
var transitionAdvice = transitionAdvisor.Advise(
    transitionCurrent,
    transitionCandidate,
    transitionDeckState,
    new NoraTrackTiming(150, 240, 0, 4),
    new NoraTrackTiming(0, 220, 0, 4));
Check(transitionAdvice.RecommendedDeck == DeckId.B && transitionAdvice.PitchWithinRange && transitionAdvice.SyncRecommended,
    "Transition advisor sceglie deck libero e calcola pitch/sync eseguibile");
Check(Math.Abs((transitionAdvice.SuggestedPitchPercent ?? 0d) - -4.7619d) < 0.02d,
    "Transition advisor calcola il pitch candidato verso master");
Check(transitionAdvice.EntryTimeSeconds is >= 0 and <= 220 && transitionAdvice.ExitTimeSeconds is >= 150 and <= 240 &&
      transitionAdvice.EntryBar is > 0 && transitionAdvice.ExitBar is > 0,
    "Transition advisor mantiene cue e battute nei limiti delle tracce");
Check(transitionAdvice.TransitionBeats == 32 && transitionAdvice.BassSwapBeat == 17 &&
      transitionAdvice.EntrySection == TrackSectionType.Intro && transitionAdvice.ExitSection == TrackSectionType.Outro,
    "Transition advisor usa struttura reale, 32 battute e bass swap alla 17");
Check(transitionAdvice.Risk == NoraTransitionRisk.Low && transitionAdvice.VocalAdvice.Contains("favorevole", StringComparison.OrdinalIgnoreCase),
    "Transition advisor riconosce una transizione vocale a basso rischio");

var vocalRiskAdvice = transitionAdvisor.Advise(
    transitionCurrent with { AudioFeatures = transitionCurrent.AudioFeatures! with { VocalPresence = VocalPresence.Predominant } },
    transitionCandidate with { AudioFeatures = transitionCandidate.AudioFeatures! with { VocalPresence = VocalPresence.Predominant } },
    transitionDeckState,
    new NoraTrackTiming(150, 240, 0, 4),
    new NoraTrackTiming(0, 220, 0, 4));
Check(vocalRiskAdvice.VocalAdvice.Contains("voce su voce", StringComparison.OrdinalIgnoreCase),
    "Transition advisor segnala il rischio di due voci predominanti");

var unknownTransition = transitionAdvisor.Advise(
    new NoraTrackProfile("unknown-current", "Unknown Current", "DJ", 124, null, null),
    new NoraTrackProfile("unknown-next", "Unknown Next", "DJ", 125, null, null),
    transitionDeckState,
    new NoraTrackTiming(100, 180, 0, 4),
    new NoraTrackTiming(0, 180, 0, 4));
Check(unknownTransition.TransitionBeats == 16 && unknownTransition.EntrySection == TrackSectionType.Unknown &&
      unknownTransition.ExitSection == TrackSectionType.Unknown && unknownTransition.Risk == NoraTransitionRisk.Medium,
    "Transition advisor usa fallback conservativo con feature sconosciute");
Check(unknownTransition.RequiresExplicitDjAction,
    "Transition advisor produce solo un consiglio e richiede sempre azione esplicita del DJ");

var leftDeckState = NoraDeckStateProvider.Evaluate([
    Deck(DeckId.A, DeckSide.Left, loaded: true, playing: true, master: true, meter: 0.5),
    Deck(DeckId.B, DeckSide.Right, loaded: true, playing: true, meter: 0.5),
    Deck(DeckId.C, DeckSide.Left),
    Deck(DeckId.D, DeckSide.Right)
], -1d);
Check(leftDeckState.PrimaryAudibleDeck == DeckId.A && !leftDeckState.IsAmbiguous,
    "Nora riconosce il Deck A udibile con crossfader a sinistra");

var rightDeckState = NoraDeckStateProvider.Evaluate([
    Deck(DeckId.A, DeckSide.Left, loaded: true, playing: true, master: true, meter: 0.5),
    Deck(DeckId.B, DeckSide.Right, loaded: true, playing: true, meter: 0.5),
    Deck(DeckId.C, DeckSide.Left),
    Deck(DeckId.D, DeckSide.Right)
], 1d);
Check(rightDeckState.PrimaryAudibleDeck == DeckId.B && !rightDeckState.IsAmbiguous,
    "Nora riconosce il Deck B udibile con crossfader a destra");

var centerDeckState = NoraDeckStateProvider.Evaluate([
    Deck(DeckId.A, DeckSide.Left, loaded: true, playing: true, master: true, meter: 0.35),
    Deck(DeckId.B, DeckSide.Right, loaded: true, playing: true, meter: 0.35),
    Deck(DeckId.C, DeckSide.Left),
    Deck(DeckId.D, DeckSide.Right)
], 0d);
Check(centerDeckState.PrimaryAudibleDeck is null && centerDeckState.IsAmbiguous &&
      centerDeckState.OutgoingDeck == DeckId.A && centerDeckState.IncomingDeck == DeckId.B,
    "Nora dichiara l'ambiguita al centro e distingue uscita/ingresso tramite il master");

var stoppedMasterState = NoraDeckStateProvider.Evaluate([
    Deck(DeckId.A, DeckSide.Left, loaded: true, master: true),
    Deck(DeckId.B, DeckSide.Right, loaded: true, playing: true, meter: 0.45),
    Deck(DeckId.C, DeckSide.Left),
    Deck(DeckId.D, DeckSide.Right)
], 1d);
Check(stoppedMasterState.PrimaryAudibleDeck == DeckId.B,
    "Nora ignora il clock master fermo e segue il deck realmente udibile");

var previewState = NoraDeckStateProvider.Evaluate([
    Deck(DeckId.A, DeckSide.Left, loaded: true, playing: true, master: true, meter: 0.45),
    Deck(DeckId.B, DeckSide.Right, loaded: true, cue: true),
    Deck(DeckId.C, DeckSide.Left),
    Deck(DeckId.D, DeckSide.Right)
], -1d);
Check(previewState.PreviewDeck == DeckId.B,
    "Nora distingue il deck in preascolto non udibile sul master");
Check(previewState.RecommendedLoadDeck == DeckId.D,
    "Nora consiglia prima un deck vuoto sul lato escluso dal crossfader");

var noFreeDeckState = NoraDeckStateProvider.Evaluate([
    Deck(DeckId.A, DeckSide.Left, loaded: true, playing: true, master: true, meter: 0.45),
    Deck(DeckId.B, DeckSide.Right, loaded: true),
    Deck(DeckId.C, DeckSide.Left, loaded: true),
    Deck(DeckId.D, DeckSide.Right, loaded: true)
], -1d);
Check(noFreeDeckState.RecommendedLoadDeck is null,
    "La policy LoadOnly non sovrascrive deck gia preparati quando nessun deck e libero");

var catalogDirectory = Path.Combine(Path.GetTempPath(), "NexoraMix-CatalogTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(catalogDirectory);
var catalogPath = Path.Combine(catalogDirectory, "catalog.db");
var playablePath = Path.Combine(catalogDirectory, "candidate.wav");
await File.WriteAllBytesAsync(playablePath, [0x52, 0x49, 0x46, 0x46]);
var playableTrackId = NoraCatalogRepository.ComputePathId(playablePath);
await CreateCatalogSchemaAsync(catalogPath);
await using (var seed = await OpenCatalogAsync(catalogPath))
{
    await InsertTrackAsync(seed, playableTrackId, "Playable", "Local DJ", 126, 2004, "House", "2026-01-04T00:00:00Z");
    await InsertTrackAsync(seed, "normal", "Normal Match", "DJ One", 125, 2002, "House", "2026-01-03T00:00:00Z");
    await InsertTrackAsync(seed, "half", "Half Match", "DJ Two", 63, 2001, "House", "2026-01-02T00:00:00Z");
    await InsertTrackAsync(seed, "double", "Double Match", "DJ Three", 250, 2000, "House", "2026-01-01T00:00:00Z");
    await InsertTrackAsync(seed, "duplicate", "Normal Match", "DJ One", 125, 2002, "House", "2025-01-01T00:00:00Z");
}

using var catalog = new NoraCatalogRepository(catalogPath);
Check(await catalog.EnsureInitializedAsync(), "Repository Nora valida lo schema SQLite reale");
await using (var verifyIndexes = await OpenCatalogAsync(catalogPath))
{
    var indexCount = await ScalarIntAsync(verifyIndexes, """
        SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN
        ('IX_Tracks_Bpm','IX_Tracks_UpdatedAtUtc','IX_FileInstances_AvailableTrack');
        """);
    Check(indexCount == 3, "Migrazione Nora crea soltanto i tre indici richiesti");
}
Check(catalog.LastBackupPath is not null && File.Exists(catalog.LastBackupPath), "Migrazione indici crea un backup best-effort");

var persistedFeatures = new TrackAudioFeatures
{
    Status = AudioFeatureAnalysisStatus.Analyzed,
    AnalysisVersion = TrackAudioFeatures.CurrentAnalysisVersion,
    Bpm = 126.25,
    BpmConfidence = 0.91,
    CamelotKey = "8A",
    Energy = 0.68,
    OverallConfidence = 0.82,
    AnalyzedAtUtc = DateTimeOffset.UtcNow
};
var changedRows = await catalog.UpsertLocalFilesAsync([
    new(playablePath, playableTrackId, 126.25, 0.91, persistedFeatures)
]);
Check(changedRows >= 2, "Upsert incrementale persiste FileInstance e TechnicalAudioFeatures");
await using (var verifyFile = await OpenCatalogAsync(catalogPath))
{
    Check(await ScalarIntAsync(verifyFile, "SELECT COUNT(*) FROM FileInstances WHERE TrackId=$id AND IsAvailable=1;", playableTrackId) == 1,
        "FileInstances collega il percorso reale alla traccia esistente");
    Check(await ScalarIntAsync(verifyFile, "SELECT COUNT(*) FROM TechnicalAudioFeatures WHERE FeaturesJson LIKE '%8A%';") == 1,
        "Feature audio persistite con JSON e versione algoritmo");
}
Check(string.Equals(await catalog.ResolveBestFileAsync(playableTrackId), playablePath, StringComparison.OrdinalIgnoreCase),
    "Resolver restituisce il migliore file fisicamente esistente e supportato");

var candidateQuery = new NoraCatalogQuery("current", 125, 2002, "House", 1000);
var firstCandidates = await catalog.QueryCandidatesAsync(candidateQuery);
var secondCandidates = await catalog.QueryCandidatesAsync(candidateQuery);
Check(firstCandidates.Tracks.Any(track => track.Id == "normal") &&
      firstCandidates.Tracks.Any(track => track.Id == "half") &&
      firstCandidates.Tracks.Any(track => track.Id == "double"),
    "Query Nora prefiltra BPM normale, half-time e double-time");
Check(firstCandidates.Tracks.Count(track => track.Title == "Normal Match" && track.Artist == "DJ One") == 1,
    "Query Nora deduplica titolo e artista esatti");
Check(firstCandidates.Tracks.Count <= 1000 && firstCandidates.Metrics.CandidateCount >= firstCandidates.Tracks.Count,
    "Query Nora applica il limite massimo e pubblica metriche candidati");
var playableCandidate = firstCandidates.Tracks.Single(track => track.Id == playableTrackId);
Check(playableCandidate.AudioFeatures?.AnalysisVersion == TrackAudioFeatures.CurrentAnalysisVersion &&
      playableCandidate.AudioFeatures.CamelotKey == "8A",
    "Query Nora espone le feature JSON v4 complete nel pool limitato");
Check(playableCandidate.IsFileAvailable &&
      string.Equals(playableCandidate.FilePath, playablePath, StringComparison.OrdinalIgnoreCase),
    "Query Nora espone soltanto FileInstances realmente disponibili sul disco");
Check(!firstCandidates.Metrics.CacheHit && secondCandidates.Metrics.CacheHit,
    "Cache candidati evita una seconda query SQLite identica");

using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    var cancellationObserved = false;
    try { await catalog.QueryCandidatesAsync(candidateQuery, cancelled.Token); }
    catch (OperationCanceledException) { cancellationObserved = true; }
    Check(cancellationObserved, "Repository Nora rispetta la cancellazione di richieste obsolete");
}

await using (var bulk = await OpenCatalogAsync(catalogPath))
await using (var transaction = await bulk.BeginTransactionAsync())
await using (var insert = bulk.CreateCommand())
{
    insert.Transaction = (SqliteTransaction)transaction;
    insert.CommandText = """
        INSERT INTO Tracks (Id,Title,Artist,Year,IdentificationStatus,CreatedAtUtc,UpdatedAtUtc,Genre,Bpm)
        VALUES ($id,$title,$artist,$year,'Identified',$updated,$updated,$genre,$bpm);
        """;
    var id = insert.Parameters.Add("$id", SqliteType.Text);
    var title = insert.Parameters.Add("$title", SqliteType.Text);
    var artist = insert.Parameters.Add("$artist", SqliteType.Text);
    var year = insert.Parameters.Add("$year", SqliteType.Integer);
    var updated = insert.Parameters.Add("$updated", SqliteType.Text);
    var genre = insert.Parameters.Add("$genre", SqliteType.Text);
    var bpm = insert.Parameters.Add("$bpm", SqliteType.Integer);
    for (var index = 0; index < 50_000; index++)
    {
        id.Value = "bulk-" + index;
        title.Value = "Performance Track " + index;
        artist.Value = "Artist " + index % 500;
        year.Value = 1980 + index % 47;
        updated.Value = $"2026-02-{1 + index % 28:00}T00:00:00Z";
        genre.Value = index % 2 == 0 ? "House" : "Techno";
        bpm.Value = 60 + index % 241;
        await insert.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
}

var benchmarkWatch = Stopwatch.StartNew();
var benchmark = await catalog.QueryCandidatesAsync(new("benchmark-current", 128, 2005, "House", 1000));
benchmarkWatch.Stop();
Console.WriteLine($"INFO  Nora SQLite 50.000: {benchmarkWatch.Elapsed.TotalMilliseconds:0.0} ms, " +
                  $"pool {benchmark.Metrics.CandidateCount}, restituiti {benchmark.Tracks.Count}; target operativo <500 ms (informativo, non flaky)");
Check(benchmark.Tracks.Count is > 0 and <= 1000, "Benchmark SQLite reale 50.000 usa un pool limitato senza caricare l'intero catalogo");

var personalizationPath = Path.Combine(catalogDirectory, "nora-personalization.db");
var personalizationEngine = new NoraPersonalizationService();
var personalizationCurrent = new NoraTrackProfile("source", "Current", "Source DJ", 125, 2002, "House");
var personalizationCandidates = new[]
{
    new NoraTrackProfile("alpha", "Alpha", "Artist Alpha", 125, 2002, "House"),
    new NoraTrackProfile("beta", "Beta", "Artist Beta", 125, 2002, "House")
};
var neutralRanking = NoraTrackCompatibility.Rank(
    personalizationCurrent, personalizationCandidates, context: null, weights: null, limit: 2);
Check(neutralRanking[0].Track.Id == "alpha", "Ranking neutro di riferimento deterministico");

var profileNow = DateTimeOffset.UtcNow;
var adaptiveProfile = new NoraDjPreferenceProfile(
    "adaptive", "DJ adattivo",
    new NoraPersonalizationSettings
    {
        MinimumFeedbackCount = 1,
        DecayHalfLifeDays = 30,
        LearningRate = 0.5,
        MaximumPreferenceMagnitude = 1,
        MaximumScoreAdjustment = 18
    }, profileNow, profileNow);
NoraPersonalizationSnapshot learnedSnapshot;
using (var personalizationStore = new NoraPersonalizationStore(personalizationPath))
{
    await personalizationStore.SaveProfileAsync(adaptiveProfile);
    learnedSnapshot = await personalizationStore.RecordFeedbackAsync(
        CreateFeedback("feedback-not-suitable", "adaptive", NoraFeedbackType.NotSuitable, profileNow));
    var adaptedContext = personalizationEngine.CreateRankingContext(learnedSnapshot, personalizationCurrent.Artist);
    var adaptedRanking = NoraTrackCompatibility.Rank(
        personalizationCurrent, personalizationCandidates, adaptedContext, weights: null, limit: 2);
    Check(adaptedRanking[0].Track.Id == "beta" && adaptedRanking[1].Score < neutralRanking[0].Score,
        "Feedback NonAdatta modifica concretamente punteggio e ordine del ranking");
}

using (var restartedStore = new NoraPersonalizationStore(personalizationPath))
{
    var persisted = await restartedStore.LoadSnapshotAsync("adaptive");
    var restartedRanking = NoraTrackCompatibility.Rank(
        personalizationCurrent, personalizationCandidates,
        personalizationEngine.CreateRankingContext(persisted, personalizationCurrent.Artist), weights: null, limit: 2);
    Check(persisted.FeedbackCount == 1 && restartedRanking[0].Track.Id == "beta",
        "Apprendimento Nora persiste dopo la ricreazione dello store");

    var minimumProfile = adaptiveProfile with
    {
        Id = "minimum-two",
        Settings = adaptiveProfile.Settings with { MinimumFeedbackCount = 2 }
    };
    await restartedStore.SaveProfileAsync(minimumProfile);
    var belowMinimum = await restartedStore.RecordFeedbackAsync(
        CreateFeedback("feedback-minimum", minimumProfile.Id, NoraFeedbackType.PreferArtist, profileNow));
    Check(!belowMinimum.IsActive && belowMinimum.FeedbackCount == 1,
        "Soglia minima impedisce sovra-apprendimento da un singolo feedback");

    var recentEvent = CreateFeedback("recent", "decay", NoraFeedbackType.PreferArtist, profileNow);
    var decayProfile = adaptiveProfile with { Id = "decay" };
    var recentSnapshot = personalizationEngine.BuildSnapshot(decayProfile, [recentEvent], profileNow);
    var oldSnapshot = personalizationEngine.BuildSnapshot(decayProfile,
        [recentEvent with { Id = "old", CreatedAtUtc = profileNow.AddDays(-90) }], profileNow);
    var artistKey = NoraPersonalizationService.ArtistKey("Artist Alpha");
    Check(Math.Abs(oldSnapshot.Weights[artistKey].Value) < Math.Abs(recentSnapshot.Weights[artistKey].Value),
        "Decay temporale riduce il peso dei feedback vecchi");

    var otherProfile = adaptiveProfile with { Id = "other-dj", DisplayName = "Secondo DJ" };
    await restartedStore.SaveProfileAsync(otherProfile);
    await restartedStore.RecordFeedbackAsync(
        CreateFeedback("other-feedback", otherProfile.Id, NoraFeedbackType.PreferArtist, profileNow));
    await restartedStore.ResetProfileAsync("adaptive");
    var reset = await restartedStore.LoadSnapshotAsync("adaptive");
    var other = await restartedStore.LoadSnapshotAsync(otherProfile.Id);
    Check(reset.FeedbackCount == 0 && reset.Weights.Count == 0,
        "Reset profilo elimina eventi e pesi appresi");
    Check(other.FeedbackCount == 1 && other.Weights.Count > 0,
        "Profili DJ separati non vengono confusi dal reset");

    var exported = await restartedStore.ExportProfileAsync(otherProfile.Id);
    await restartedStore.ResetProfileAsync(otherProfile.Id);
    await restartedStore.ImportProfileAsync(exported);
    var imported = await restartedStore.LoadSnapshotAsync(otherProfile.Id);
    Check(imported.FeedbackCount == 1 && imported.Weights.Count > 0,
        "Export/import conserva eventi origine e preferenze ricostruibili");
}

var setMemoryPath = Path.Combine(catalogDirectory, "nora-set-memory.db");
var setNow = DateTimeOffset.UtcNow;
NoraSetSession firstSet;
using (var setStore = new NoraSetMemoryStore(setMemoryPath))
{
    firstSet = await setStore.StartSessionAsync("default", NoraSetPhase.ProgressiveBuild,
        new NoraSetTrajectory
        {
            CurrentEnergy = 0.45,
            TargetEnergy = 0.80,
            TargetMinutes = 15,
            TargetPhase = NoraSetPhase.Peak,
            MaximumBpmIncreasePerTrack = 4,
            AllowEnergyDrop = false
        });
    var loaded = CreateSetTrack(firstSet.Id, "set-track-a", "Artist A", 1, setNow, playedAt: null, 0.55,
        VocalPresence.Predominant);
    await setStore.RecordLoadedTrackAsync(loaded);
    await setStore.RecordPlayedTrackAsync(loaded with
    {
        PlayedAtUtc = setNow.AddSeconds(5),
        EffectiveBpm = 124,
        PitchPercent = 0.8,
        AudibleDeckId = "A",
        CrossfaderPosition = -1,
        DeckVolume = 0.9,
        CueActive = false
    });
    var recommendation = new NoraSetRecommendation(
        "set-rec-1", firstSet.Id, "set-track-a", "set-track-b", 1, 91, true, true, setNow.AddSeconds(10));
    await setStore.RecordRecommendationAsync(recommendation);
    await setStore.RecordFeedbackAsync(new(
        "set-feedback-1", firstSet.Id, recommendation.Id, NoraFeedbackType.ExcellentSuggestion,
        true, 1, setNow.AddSeconds(11)));
    await setStore.RecordTransitionAsync(new(
        "set-transition-1", firstSet.Id, "set-track-a", "set-track-b", setNow.AddSeconds(12),
        setNow.AddSeconds(40), "Blend", 32, 0.8, 17, "Basso", true));
    await setStore.PauseSessionAsync(firstSet.Id);
    var paused = await setStore.GetActiveSnapshotAsync("default");
    Check(paused?.Session.Status == NoraSetSessionStatus.Paused && paused.RecentTracks.Count == 1,
        "Memoria set registra tracce e sospende la sessione attiva");
}

using (var reopenedSetStore = new NoraSetMemoryStore(setMemoryPath))
{
    var reopenedPaused = await reopenedSetStore.GetActiveSnapshotAsync("default");
    Check(reopenedPaused?.Session.Id == firstSet.Id && reopenedPaused.Session.Status == NoraSetSessionStatus.Paused,
        "Sessione sospesa sopravvive alla riapertura dello store");
    await reopenedSetStore.ResumeSessionAsync(firstSet.Id);
    await reopenedSetStore.RecordPlayedTrackAsync(CreateSetTrack(
        firstSet.Id, "set-track-b", "Artist B", 2, setNow.AddMinutes(4), setNow.AddMinutes(4), 0.65,
        VocalPresence.Predominant));
    var activeSet = await reopenedSetStore.GetActiveSnapshotAsync("default");
    Check(activeSet is not null && activeSet.RecentTrackIds.SetEquals(["set-track-a", "set-track-b"]) &&
          activeSet.RecentArtists.SetEquals(["Artist A", "Artist B"]),
        "Snapshot espone ID e artisti recenti senza confondere caricamenti non suonati");
    Check(activeSet!.ConsecutiveVocalTracks == 2 && activeSet.CurrentEnergy == 0.65 && activeSet.TargetEnergy == 0.80,
        "Snapshot calcola sequenza vocale, energia corrente e traiettoria target");

    var setRankingCurrent = new NoraTrackProfile("now", "Current", "Current Artist", 124, 2000, "House")
    {
        AudioFeatures = new TrackAudioFeatures { Status = AudioFeatureAnalysisStatus.Analyzed, Energy = 0.65 }
    };
    var setRanking = NoraTrackCompatibility.Rank(setRankingCurrent,
    [
        new NoraTrackProfile("set-track-a", "Recent", "Artist A", 124, 2000, "House")
        {
            AudioFeatures = new TrackAudioFeatures { Status = AudioFeatureAnalysisStatus.Analyzed, Energy = 0.80 }
        },
        new NoraTrackProfile("fresh-track", "Fresh", "Fresh Artist", 124, 2000, "House")
        {
            AudioFeatures = new TrackAudioFeatures { Status = AudioFeatureAnalysisStatus.Analyzed, Energy = 0.80 }
        }
    ], new NoraRankingContext
    {
        RecentTrackIds = activeSet.RecentTrackIds,
        RecentArtists = activeSet.RecentArtists,
        TargetEnergy = activeSet.TargetEnergy
    }, limit: 2);
    Check(setRanking[0].Track.Id == "fresh-track" &&
          setRanking[1].Factors.Any(factor => factor.Name == "Traccia recente"),
        "Memoria set applica penalità recenti al ranking");

    await reopenedSetStore.CloseSessionAsync(firstSet.Id);
    Check(await reopenedSetStore.GetActiveSnapshotAsync("default") is null,
        "Chiusura set rimuove la sessione dall'attivo");
    var secondSet = await reopenedSetStore.StartSessionAsync("default", NoraSetPhase.Closing);
    var secondSnapshot = await reopenedSetStore.GetActiveSnapshotAsync("default");
    Check(secondSnapshot?.Session.Id == secondSet.Id && secondSnapshot.RecentTracks.Count == 0,
        "Nuovo set resta separato dalla memoria della sessione precedente");
    var closedFirst = await reopenedSetStore.LoadSnapshotAsync(firstSet.Id);
    Check(closedFirst.RecentTrackIds.Count == 2,
        "Storico del set chiuso resta consultabile separatamente");
    await reopenedSetStore.CloseSessionAsync(secondSet.Id);
}

SqliteConnection.ClearAllPools();
try { Directory.Delete(catalogDirectory, recursive: true); }
catch (System.IO.IOException exception) { Console.WriteLine($"INFO  Pulizia database temporaneo rinviata: {exception.Message}"); }

return failures.Count == 0 ? 0 : 1;

static async Task CreateCatalogSchemaAsync(string path)
{
    await using var connection = await OpenCatalogAsync(path);
    await using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE Tracks (
            Id TEXT PRIMARY KEY, Title TEXT, Artist TEXT, Album TEXT, Year INTEGER, TrackNumber INTEGER,
            IdentificationStatus TEXT NOT NULL DEFAULT 'Unidentified', CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL,
            AlbumArtist TEXT, Genre TEXT, DiscNumber INTEGER, Bpm INTEGER);
        CREATE TABLE FileInstances (
            Id TEXT PRIMARY KEY, TrackId TEXT REFERENCES Tracks(Id) ON DELETE SET NULL, Path TEXT NOT NULL UNIQUE,
            Sha256 TEXT, SizeBytes INTEGER NOT NULL, LastWriteTimeUtc TEXT NOT NULL, IsAvailable INTEGER NOT NULL DEFAULT 1,
            CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);
        CREATE TABLE TechnicalAudioFeatures (
            Id TEXT PRIMARY KEY, FileInstanceId TEXT NOT NULL UNIQUE REFERENCES FileInstances(Id) ON DELETE CASCADE,
            Format TEXT, DurationSeconds REAL, Bitrate INTEGER, SampleRate INTEGER, Channels INTEGER, BitsPerSample INTEGER,
            Bpm REAL, BpmConfidence REAL, FeaturesJson TEXT NOT NULL DEFAULT '{}', CreatedAtUtc TEXT NOT NULL);
        CREATE INDEX IX_FileInstances_TrackId ON FileInstances(TrackId);
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task<SqliteConnection> OpenCatalogAsync(string path)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false
    }.ToString());
    await connection.OpenAsync();
    await using var pragma = connection.CreateCommand();
    pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;";
    await pragma.ExecuteNonQueryAsync();
    return connection;
}

static async Task InsertTrackAsync(
    SqliteConnection connection, string id, string title, string artist, int bpm, int year, string genre, string updated)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO Tracks (Id,Title,Artist,Year,IdentificationStatus,CreatedAtUtc,UpdatedAtUtc,Genre,Bpm)
        VALUES ($id,$title,$artist,$year,'Identified',$updated,$updated,$genre,$bpm);
        """;
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$title", title);
    command.Parameters.AddWithValue("$artist", artist);
    command.Parameters.AddWithValue("$year", year);
    command.Parameters.AddWithValue("$updated", updated);
    command.Parameters.AddWithValue("$genre", genre);
    command.Parameters.AddWithValue("$bpm", bpm);
    await command.ExecuteNonQueryAsync();
}

static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql, string? id = null)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    if (id is not null) command.Parameters.AddWithValue("$id", id);
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

static NoraFeedbackEvent CreateFeedback(
    string id, string profileId, NoraFeedbackType type, DateTimeOffset createdAt) => new(
        id,
        profileId,
        type,
        IsExplicit: true,
        Confidence: 1,
        SourceTrackId: "source",
        SourceArtist: "Source DJ",
        SourceGenre: "House",
        CandidateTrackId: "alpha",
        CandidateArtist: "Artist Alpha",
        CandidateGenre: "House",
        Context: new NoraFeedbackContext { SourceBpm = 125, CandidateBpm = 125, LocalHour = 23 },
        CreatedAtUtc: createdAt);

static NoraSetSessionTrack CreateSetTrack(
    string sessionId,
    string trackId,
    string artist,
    int sequence,
    DateTimeOffset loadedAt,
    DateTimeOffset? playedAt,
    double? energy,
    VocalPresence? vocalPresence) => new(
        Id: sessionId + "-" + trackId,
        SessionId: sessionId,
        TrackId: trackId,
        Title: "Track " + trackId,
        Artist: artist,
        Genre: "House",
        Sequence: sequence,
        LoadedAtUtc: loadedAt,
        PlayedAtUtc: playedAt,
        StoppedAtUtc: null,
        DeckId: sequence % 2 == 0 ? "B" : "A",
        EffectiveDurationSeconds: null,
        EffectiveBpm: 124,
        PitchPercent: null,
        Energy: energy,
        CamelotKey: "8A",
        VocalPresence: vocalPresence,
        WasNoraSuggested: true,
        EntryTimeSeconds: null,
        ExitTimeSeconds: null,
        AudibleDeckId: null,
        CrossfaderPosition: null,
        DeckVolume: null,
        CueActive: null);

static NoraDeckSnapshot Deck(
    DeckId id,
    DeckSide side,
    bool loaded = false,
    bool playing = false,
    bool master = false,
    bool cue = false,
    double volume = 1d,
    double meter = 0d) =>
    new(id, side, loaded, playing, master, cue, volume, meter);

static NoraTrackProfile PlanTrack(
    string id,
    string title,
    string artist,
    double bpm,
    double energy,
    string camelot,
    VocalPresence vocal) => new(id, title, artist, bpm, 2024, "House")
{
    IsFileAvailable = true,
    DurationSeconds = 220,
    AudioFeatures = new TrackAudioFeatures
    {
        Status = AudioFeatureAnalysisStatus.Partial,
        Bpm = bpm,
        BpmConfidence = 0.9,
        Energy = energy,
        CamelotKey = camelot,
        KeyConfidence = 0.85,
        VocalPresence = vocal,
        VocalConfidence = 0.8,
        OverallConfidence = 0.85
    }
};
