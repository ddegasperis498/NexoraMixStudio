using System.IO;
using NAudio.Wave;
using NexoraMix.Audio.Analysis;
using NexoraMix.Audio.Playback;
using NexoraMix.Audio.Timecode;
using NexoraMix.App.Services;
using NexoraMix.Core.Services;
using NexoraMix.Core.Models;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (condition)
    {
        Console.WriteLine($"PASS  {message}");
        return;
    }

    failures.Add(message);
    Console.WriteLine($"FAIL  {message}");
}

Console.WriteLine("Nexora Mix Studio V7 Pro DJ - deterministic self test");
Console.WriteLine(new string('=', 64));

var leftOnly = SyncMath.EqualPowerCrossfade(-1d);
var center = SyncMath.EqualPowerCrossfade(0d);
var rightOnly = SyncMath.EqualPowerCrossfade(1d);
Check(Math.Abs(leftOnly.Left - 1d) < 0.0001d && Math.Abs(leftOnly.Right) < 0.0001d, "Crossfader equal-power sul Deck A");
Check(Math.Abs(center.Left - Math.Sqrt(0.5d)) < 0.0001d, "Crossfader equal-power al centro (left)");
Check(Math.Abs(center.Right - Math.Sqrt(0.5d)) < 0.0001d, "Crossfader equal-power al centro (right)");
Check(Math.Abs(rightOnly.Right - 1d) < 0.0001d && Math.Abs(rightOnly.Left) < 0.0001d, "Crossfader equal-power sul Deck B");

var ratio = SyncMath.CalculateTempoRatio(118d, 124d);
Check(Math.Abs(ratio - 118d / 124d) < 0.000001d, "Calcolo tempo ratio 124 -> 118 BPM");
Check(SyncMath.IsTempoRatioSupported(ratio, 16d), "Tempo ratio entro limite ±16%");
Check(!SyncMath.IsTempoRatioSupported(0.80d, 16d), "Rifiuto tempo ratio oltre limite");

var nextBeat = BeatGridMath.QuantizeForward(1.2d, 0d, 120d, 1);
var nextBar = BeatGridMath.QuantizeForward(1.2d, 0d, 120d, 4);
var nextPhrase = BeatGridMath.QuantizeForwardPhrase(1.2d, 0d, 120d, 4, 4);
Check(Math.Abs(nextBeat - 1.5d) < 0.0001d, "Quantizzazione alla battuta successiva");
Check(Math.Abs(nextBar - 2d) < 0.0001d, "Quantizzazione alla misura successiva");
Check(Math.Abs(nextPhrase - 8d) < 0.0001d, "Quantizzazione alla frase successiva");

var phaseError = BeatGridMath.PhaseErrorMilliseconds(10d, 0d, 120d, 1d, 10.01d, 0d, 120d, 1d);
Check(Math.Abs(phaseError - 10d) < 0.2d, "Misura errore fase in millisecondi");

Check(CamelotCompatibilityService.ToCamelot("A", MusicalMode.Minor) == "8A", "Camelot converte A minor in 8A");
Check(CamelotCompatibilityService.ToCamelot("C", MusicalMode.Major) == "8B", "Camelot converte C major in 8B");
Check(CamelotCompatibilityService.ToCamelot("Db", MusicalMode.Major) == "3B", "Camelot normalizza le tonalità bemolli");
Check(CamelotCompatibilityService.ToCamelot(null, MusicalMode.Unknown) is null, "Camelot conserva la tonalità mancante come Unknown");
Check(CamelotCompatibilityService.Compare("8A", "8A").Relation == CamelotRelation.SameKey, "Camelot riconosce la stessa chiave");
Check(CamelotCompatibilityService.Compare("8A", "9A").Relation == CamelotRelation.Adjacent, "Camelot riconosce il movimento adiacente");
Check(CamelotCompatibilityService.Compare("8A", "8B").Relation == CamelotRelation.RelativeMajorMinor, "Camelot riconosce maggiore/minore compatibile");
Check(CamelotCompatibilityService.Compare("8A", "2B").Relation == CamelotRelation.Incompatible, "Camelot segnala una combinazione incompatibile");
var unknownFeatures = new TrackAudioFeatures();
Check(unknownFeatures.Status == AudioFeatureAnalysisStatus.Unknown && unknownFeatures.Energy is null, "Feature mancanti restano Unknown e nullable");
Check(TrackAudioFeatures.CurrentAnalysisVersion == 4, "Versione analisi avanzata esplicita per invalidare cache precedenti");

var analyzer = new BpmAnalyzer();
var demoFiles = DemoAudioFactory.EnsureDemoFiles();
var expectedDemoBpms = new[] { 118d, 124d };

for (var index = 0; index < Math.Min(demoFiles.Count, expectedDemoBpms.Length); index++)
{
    var result = analyzer.Analyze(demoFiles[index]);
    var error = Math.Abs(result.Bpm - expectedDemoBpms[index]);
    Console.WriteLine($"INFO  {Path.GetFileName(demoFiles[index])}: {result.Bpm:0.00} BPM, errore {error:0.00}, confidenza {result.Confidence:P0}");
    Check(error <= 1.0d, $"Rilevamento BPM demo {expectedDemoBpms[index]:0}");
}

using (var previewEngine = new MasterAudioEngine(enableHardwareOutput: false))
{
    var previewTrack = new AudioTrack
    {
        Title = "Preview isolata",
        Artist = "SelfTest",
        FilePath = demoFiles[0],
        SourceKind = TrackSourceKind.LocalFile,
        Bpm = 118d
    };
    const double cueSeconds = 0.2d;
    previewEngine.LoadPreview(previewTrack, cueSeconds);
    Check(previewEngine.PreviewState == PreviewPlaybackState.Ready, "Preview locale pronta al cue richiesto");
    Check(previewEngine.Decks.Values.All(deck => deck.Track is null && !deck.IsPlaying), "Preview non carica e non avvia alcun deck");

    previewEngine.PlayPreview(startCueOutput: false);
    var previewBuffer = new float[previewEngine.SampleRate];
    var cueRendered = previewEngine.RenderCueOffline(previewBuffer, 0, previewBuffer.Length);
    var previewPeak = previewBuffer.Take(cueRendered).Select(Math.Abs).DefaultIfEmpty(0f).Max();
    Check(previewPeak > 0.001f, "Preview produce audio esclusivamente nel render cuffia");

    Array.Clear(previewBuffer);
    var masterRendered = previewEngine.RenderOffline(previewBuffer, 0, previewBuffer.Length);
    var isolatedMasterPeak = previewBuffer.Take(masterRendered).Select(Math.Abs).DefaultIfEmpty(0f).Max();
    Check(isolatedMasterPeak < 0.000001f, "Preview non entra mai nel master");
    Check(previewEngine.Decks.Values.All(deck => deck.Track is null && !deck.IsPlaying), "Preview resta indipendente dai quattro deck");

    previewEngine.StopPreview();
    Array.Clear(previewBuffer);
    previewEngine.RenderCueOffline(previewBuffer, 0, previewBuffer.Length);
    Check(previewEngine.PreviewState == PreviewPlaybackState.Ready && previewBuffer.All(sample => sample == 0f), "Stop preview silenzia la cuffia e torna al cue");
    previewEngine.UnloadPreview();
    Check(previewEngine.PreviewState == PreviewPlaybackState.Empty && previewEngine.PreviewTrack is null, "Unload preview rilascia il file locale");
}

using (var engine = new MasterAudioEngine(enableHardwareOutput: false))
{
    var offlineTracks = new Dictionary<DeckId, AudioTrack>
    {
        [DeckId.A] = new() { Title = "Offline A", Artist = "SelfTest", FilePath = demoFiles[0], SourceKind = TrackSourceKind.LocalFile, Bpm = 118d },
        [DeckId.B] = new() { Title = "Offline B", Artist = "SelfTest", FilePath = demoFiles[1], SourceKind = TrackSourceKind.LocalFile, Bpm = 124d },
        [DeckId.C] = new() { Title = "Offline C", Artist = "SelfTest", FilePath = demoFiles[0], SourceKind = TrackSourceKind.LocalFile, Bpm = 118d },
        [DeckId.D] = new() { Title = "Offline D", Artist = "SelfTest", FilePath = demoFiles[1], SourceKind = TrackSourceKind.LocalFile, Bpm = 124d }
    };

    engine.SetCrossfader(0d);
    foreach (var pair in offlineTracks)
    {
        engine.Load(pair.Key, pair.Value);
        engine.GetDeck(pair.Key).Play();
    }

    var renderBuffer = new float[engine.WaveFormat.SampleRate * engine.WaveFormat.Channels / 2];
    var rendered = engine.RenderOffline(renderBuffer, 0, renderBuffer.Length);
    var peak = renderBuffer.Take(rendered).Select(Math.Abs).DefaultIfEmpty(0f).Max();
    Check(rendered == renderBuffer.Length, "Render offline master 4 deck produce il numero di campioni richiesto");
    Check(peak > 0.001f, "Render offline master 4 deck contiene audio");
    Check(engine.Decks.Values.All(deck => deck.IsPlaying), "Quattro deck restano attivi nello stesso clock");
    Check(engine.Decks.Values.All(deck => deck.MeterPeak > 0), "Meter dei quattro deck aggiornati dal mixer condiviso");
    var cueBuffer = new float[renderBuffer.Length / 2];
    var cueRead = engine.GetDeck(DeckId.A).ReadCue(cueBuffer, 0, cueBuffer.Length);
    var cuePeak = cueBuffer.Take(cueRead).Select(Math.Abs).DefaultIfEmpty(0f).Max();
    Check(cuePeak > 0.001f, "Preascolto deck produce audio separato dal master");
}

using (var syncEngine = new MasterAudioEngine(enableHardwareOutput: false))
{
    var masterTrack = new AudioTrack
    {
        Title = "Sync Master",
        Artist = "SelfTest",
        FilePath = demoFiles[0],
        SourceKind = TrackSourceKind.LocalFile,
        Bpm = 118d,
        BeatOffsetSeconds = 0d,
        PhaseConfidence = 0.8d
    };
    var followerTrack = new AudioTrack
    {
        Title = "Sync Follower",
        Artist = "SelfTest",
        FilePath = demoFiles[1],
        SourceKind = TrackSourceKind.LocalFile,
        Bpm = 124d,
        BeatOffsetSeconds = 0d,
        PhaseConfidence = 0.8d
    };

    syncEngine.Load(DeckId.A, masterTrack);
    syncEngine.Load(DeckId.B, followerTrack);
    syncEngine.GetDeck(DeckId.A).Play();
    var arm = syncEngine.ArmBeatSync(DeckId.B, DeckId.A, alignToNextBar: false, maximumTempoPercent: 16d);
    Check(arm.Success, "Sync offline arma il follower senza errori");

    var syncBuffer = new float[syncEngine.WaveFormat.SampleRate * syncEngine.WaveFormat.Channels / 10];
    var finite = true;
    var activeBlocks = 0;
    for (var block = 0; block < 80; block++)
    {
        syncEngine.UpdateSync(DeckId.B, DeckId.A, 16d);
        Array.Clear(syncBuffer);
        syncEngine.RenderOffline(syncBuffer, 0, syncBuffer.Length);
        finite &= syncBuffer.All(sample => !float.IsNaN(sample) && !float.IsInfinity(sample));
        if (syncBuffer.Select(Math.Abs).DefaultIfEmpty(0f).Max() > 0.001f) activeBlocks++;
    }

    Check(finite, "Sync offline produce solo campioni audio validi");
    Check(activeBlocks > 20, "Sync offline mantiene audio continuo dopo l'aggancio");
}

using (var liveSyncEngine = new MasterAudioEngine(enableHardwareOutput: false))
{
    var masterTrack = new AudioTrack
    {
        Title = "Live Master",
        Artist = "SelfTest",
        FilePath = demoFiles[0],
        SourceKind = TrackSourceKind.LocalFile,
        Bpm = 118d,
        BeatOffsetSeconds = 0d,
        PhaseConfidence = 0.8d
    };
    var followerTrack = new AudioTrack
    {
        Title = "Live Follower",
        Artist = "SelfTest",
        FilePath = demoFiles[1],
        SourceKind = TrackSourceKind.LocalFile,
        Bpm = 124d,
        BeatOffsetSeconds = 0d,
        PhaseConfidence = 0.8d
    };

    liveSyncEngine.Load(DeckId.A, masterTrack);
    liveSyncEngine.Load(DeckId.B, followerTrack);
    liveSyncEngine.GetDeck(DeckId.A).Seek(4d);
    liveSyncEngine.GetDeck(DeckId.B).Seek(7d);
    liveSyncEngine.GetDeck(DeckId.A).Play();
    liveSyncEngine.GetDeck(DeckId.B).Play();
    var beforeLiveSync = liveSyncEngine.GetDeck(DeckId.B).PositionSeconds;
    var live = liveSyncEngine.EngageLiveBeatSync(DeckId.B, DeckId.A, 16d);
    var afterLiveSync = liveSyncEngine.GetDeck(DeckId.B).PositionSeconds;
    Check(live.Success, "Sync live si aggancia su due tracce già in play");
    Check(Math.Abs(afterLiveSync - beforeLiveSync) < 0.05d, "Sync live non riporta il follower all'inizio");
}

var samplerFolder = Path.Combine(Path.GetTempPath(), "NexoraMix-SamplerTest", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(samplerFolder);
try
{
    var samplePath = Path.Combine(samplerFolder, "one-shot.wav");
    WriteSineSample(samplePath, 220d, 0.2d);
    using var samplerEngine = new MasterAudioEngine(enableHardwareOutput: false);
    samplerEngine.Sampler.LoadSlot(0, samplePath, gain: 0.8d, choke: true);
    Check(samplerEngine.Sampler.LoadedSlots.Contains(0), "Sampler carica sample locale nello slot");
    Check(samplerEngine.Sampler.Trigger(0), "Sampler triggera sample caricato");
    var samplerBuffer = new float[samplerEngine.WaveFormat.SampleRate * samplerEngine.WaveFormat.Channels / 4];
    samplerEngine.RenderOffline(samplerBuffer, 0, samplerBuffer.Length);
    var samplerPeak = samplerBuffer.Select(Math.Abs).DefaultIfEmpty(0f).Max();
    Check(samplerPeak > 0.01f, "Sampler produce audio nel master offline");

    Array.Clear(samplerBuffer);
    samplerEngine.Sampler.Sequencer.Configure(120d, 16);
    samplerEngine.Sampler.Sequencer.SetStep(0, 0, 1d);
    samplerEngine.Sampler.Sequencer.SetStep(4, 0, 0.6d);
    samplerEngine.Sampler.Sequencer.Start();
    samplerEngine.RenderOffline(samplerBuffer, 0, samplerBuffer.Length);
    var sequencerPeak = samplerBuffer.Select(Math.Abs).DefaultIfEmpty(0f).Max();
    Check(sequencerPeak > 0.01f, "Step sequencer triggera sample sul clock master");

    var stemBundle = new StemBundle
    {
        Artist = "SelfTest",
        Title = "Stem Bundle",
        Stems = new[]
        {
            new StemFile(StemPart.Drums, samplePath, Gain: 0.8d),
            new StemFile(StemPart.Bass, samplePath, Gain: 0.7d, Muted: true),
            new StemFile(StemPart.Vocals, samplePath, Gain: 0.6d, Solo: true),
            new StemFile(StemPart.Other, samplePath, Gain: 0.5d)
        }
    };
    var stemPlanner = new StemBundlePlanner();
    Check(stemBundle.IsUsable, "Stem bundle locale pre-separato valido");
    var audibleStems = stemPlanner.CreateAudibleMix(stemBundle);
    Check(audibleStems.Count == 1 && audibleStems[0].Part == StemPart.Vocals, "Stem planner rispetta solo/mute senza separazione finta");
}
finally
{
    try { Directory.Delete(samplerFolder, recursive: true); }
    catch (IOException) { Console.WriteLine("WARN  Cartella sampler temporanea non eliminata."); }
    catch (UnauthorizedAccessException) { Console.WriteLine("WARN  Cartella sampler temporanea non eliminata: accesso negato."); }
}

var fxFormat = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);
var drivenFx = new DeckFxSampleProvider(new ConstantSampleProvider(fxFormat, 0.85f, 4096))
{
    Saturation = 0.8d,
    Compressor = 1d
};
var drivenBuffer = new float[4096];
var drivenRead = drivenFx.Read(drivenBuffer, 0, drivenBuffer.Length);
var drivenPeak = drivenBuffer.Take(drivenRead).Select(Math.Abs).DefaultIfEmpty(0f).Max();
Check(drivenPeak is > 0.1f and <= 1.0f, "DSP saturazione/compressore produce segnale limitato");

var gatedFx = new DeckFxSampleProvider(new ConstantSampleProvider(fxFormat, 0.001f, 4096))
{
    Gate = 1d
};
var gatedBuffer = new float[4096];
var gatedRead = gatedFx.Read(gatedBuffer, 0, gatedBuffer.Length);
var gatedPeak = gatedBuffer.Take(gatedRead).Select(Math.Abs).DefaultIfEmpty(0f).Max();
Check(gatedPeak < 0.0002f, "DSP gate attenua segnale sotto soglia");

var rollFx = new DeckFxSampleProvider(new ConstantSampleProvider(fxFormat, 0.4f, 44_100))
{
    Roll = 0.7d
};
var rollBuffer = new float[4096];
rollFx.Read(rollBuffer, 0, rollBuffer.Length);
var rollPeak = rollBuffer.Select(Math.Abs).DefaultIfEmpty(0f).Max();
Check(rollPeak > 0.01f, "DSP roll produce buffer audio ripetuto");

var brakeFx = new DeckFxSampleProvider(new ConstantSampleProvider(fxFormat, 0.6f, 44_100))
{
    Brake = 1d
};
var brakeFirst = new float[4096];
var brakeSecond = new float[4096];
brakeFx.Read(brakeFirst, 0, brakeFirst.Length);
brakeFx.Read(brakeSecond, 0, brakeSecond.Length);
var firstAverage = brakeFirst.Select(Math.Abs).Average();
var secondAverage = brakeSecond.Select(Math.Abs).Average();
Check(secondAverage < firstAverage, "DSP brake riduce energia nel tempo");

var dvsDecoder = new DvsTimecodeDecoder(44_100);
var noSignal = dvsDecoder.Decode(new float[2048], 0, 2048);
Check(!noSignal.HasSignal, "DVS decoder rileva assenza segnale");
var forwardSignal = BuildQuadratureSignal(44_100, 0d, 0.15d);
var forwardFrame = dvsDecoder.Decode(forwardSignal, 0, forwardSignal.Length);
var forwardFrame2 = dvsDecoder.Decode(BuildQuadratureSignal(44_100, 0.15d, 0.30d), 0, forwardSignal.Length);
Check(forwardFrame.HasSignal && forwardFrame2.Direction == DvsDirection.Forward, "DVS decoder rileva movimento forward sintetico");
var reverseFrame = dvsDecoder.Decode(BuildQuadratureSignal(44_100, 0.30d, 0.15d), 0, forwardSignal.Length);
Check(reverseFrame.Direction == DvsDirection.Reverse, "DVS decoder rileva movimento reverse sintetico");


var planner = new AutoMashupPlanner();
var mashupTracks = new[]
{
    new AudioTrack { Title = "Foundation", Artist = "Test", FilePath = demoFiles[0], SourceKind = TrackSourceKind.LocalFile, Bpm = 118d, DurationSeconds = 120d, AnalysisConfidence = 0.9d, PhaseConfidence = 0.8d },
    new AudioTrack { Title = "Support", Artist = "Test", FilePath = demoFiles[1], SourceKind = TrackSourceKind.LocalFile, Bpm = 124d, DurationSeconds = 120d, AnalysisConfidence = 0.8d, PhaseConfidence = 0.7d }
};
var mashupPlan = planner.CreatePlan(mashupTracks, MashupMode.Safe, maximumTempoPercent: 16d, phraseLengthBeats: 16);
Check(mashupPlan.Decks.Count == 2, "Auto Mashup crea un piano a 2 deck");
Check(mashupPlan.Decks.All(deck => deck.TempoRatio > 0), "Auto Mashup assegna tempo ratio validi");
Check(mashupPlan.Decks.Select(deck => deck.Deck).Distinct().Count() == 2, "Auto Mashup assegna deck distinti");

var fourDeckMashupTracks = new[]
{
    new AudioTrack { Title = "Layer 1", Artist = "Test", FilePath = demoFiles[0], SourceKind = TrackSourceKind.LocalFile, Bpm = 118d, DurationSeconds = 90d, AnalysisConfidence = 0.4d, PhaseConfidence = 0.4d },
    new AudioTrack { Title = "Layer 2", Artist = "Test", FilePath = demoFiles[1], SourceKind = TrackSourceKind.LocalFile, Bpm = 124d, DurationSeconds = 90d, AnalysisConfidence = 0.5d, PhaseConfidence = 0.4d },
    new AudioTrack { Title = "Best Master", Artist = "Test", FilePath = demoFiles[0], SourceKind = TrackSourceKind.LocalFile, Bpm = 120d, DurationSeconds = 120d, AnalysisConfidence = 0.95d, PhaseConfidence = 0.9d },
    new AudioTrack { Title = "Layer 4", Artist = "Test", FilePath = demoFiles[1], SourceKind = TrackSourceKind.LocalFile, Bpm = 126d, DurationSeconds = 90d, AnalysisConfidence = 0.5d, PhaseConfidence = 0.5d }
};
var fourDeckPlan = planner.CreatePlan(fourDeckMashupTracks, MashupMode.Balanced, maximumTempoPercent: 16d, phraseLengthBeats: 16);
Check(fourDeckPlan.Decks.Count == 4, "Auto Mashup crea un piano a 4 deck");
Check(fourDeckPlan.Decks[0].TrackId == fourDeckMashupTracks[2].Id, "Auto Mashup mette il master scelto al primo ingresso");
Check(fourDeckPlan.Decks[0].EntryBar == 0, "Auto Mashup assegna ingresso immediato al master");
Check(fourDeckPlan.Decks.Select(deck => deck.Role).Contains(MashupRole.Vocals), "Auto Mashup 4 deck assegna ruolo vocale in modalità Balanced");

var syntheticFolder = Path.Combine(Path.GetTempPath(), "NexoraMix-SelfTest", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(syntheticFolder);
try
{
    var cacheStore = new AnalysisCacheStore(Path.Combine(syntheticFolder, "analysis-cache"));
    var libraryIndexPath = Path.Combine(syntheticFolder, "library", "music-import-index.json");
    var cacheLifecycleTested = false;
    foreach (var expectedBpm in new[] { 90d, 100d, 118d, 120d, 124d, 128d, 140d })
    {
        var path = Path.Combine(syntheticFolder, $"click-{expectedBpm:0}.wav");
        WriteClickTrack(path, expectedBpm, durationSeconds: 18d);
        var result = analyzer.Analyze(path);
        var error = Math.Abs(result.Bpm - expectedBpm);
        var expectedBeats = 1 + (int)Math.Floor(18d / (60d / expectedBpm));
        Console.WriteLine($"INFO  sintetico {expectedBpm:0} BPM: rilevato {result.Bpm:0.00}, errore {error:0.00}, battute {result.DetectedBeatCount}, misure {result.EstimatedBarCount}");
        Check(error <= 0.55d, $"BPM sintetico {expectedBpm:0} entro ±0,55 BPM");
        Check(Math.Abs(result.DetectedBeatCount - expectedBeats) <= 2, $"Conteggio battute {expectedBpm:0} BPM entro ±2");
        Check(result.EstimatedBarCount == (int)Math.Ceiling(result.DetectedBeatCount / 4d), $"Conteggio misure {expectedBpm:0} BPM coerente");
        Check(result.Features.Status == AudioFeatureAnalysisStatus.Partial, $"Feature sintetiche {expectedBpm:0} dichiarano analisi parziale");
        Check(result.Features.AnalysisVersion == TrackAudioFeatures.CurrentAnalysisVersion, $"Feature sintetiche {expectedBpm:0} usano la versione corrente");
        Check(result.Features.Energy is >= 0d and <= 1d, $"Energia sintetica {expectedBpm:0} normalizzata");
        Check(result.Features.Danceability is >= 0d and <= 1d, $"Danceability sintetica {expectedBpm:0} normalizzata");
        Check(result.Features.BassIntensity is >= 0d and <= 1d, $"Intensità basse sintetica {expectedBpm:0} normalizzata");
        Check(result.Features.SpectralCentroidHz is > 0d, $"Centroide spettrale sintetico {expectedBpm:0} disponibile");
        Check(result.Features.EstimatedIntegratedLufs is < 0d, $"Stima loudness sintetica {expectedBpm:0} espressa in dB");
        Check(result.Features.EstimatedTruePeakDbFs >= result.Features.SamplePeakDbFs,
            $"True peak stimato {expectedBpm:0} non inferiore al sample peak");
        Check(result.Features.VocalPresence == VocalPresence.Unknown && result.Features.VocalConfidence is null,
            $"Voce sintetica {expectedBpm:0} non viene inventata");
        Check(result.Features.MusicalKey is null || result.Features.CamelotKey is not null,
            $"Tonalità sintetica {expectedBpm:0} ha Camelot coerente o fallback Unknown");

        if (!cacheLifecycleTested)
        {
            await cacheStore.SaveAsync(path, result);
            var cacheHit = await cacheStore.TryLoadAsync(path);
            Check(cacheHit?.Features.AnalysisVersion == TrackAudioFeatures.CurrentAnalysisVersion,
                "Cache reale restituisce un hit con feature v4");
            Check(cacheHit?.Features.Energy == result.Features.Energy,
                "Cache reale conserva le feature audio avanzate");

            var persistedTrack = new AudioTrack
            {
                FilePath = path,
                SourceKind = TrackSourceKind.LocalFile,
                Title = "Cache Track",
                Artist = "SelfTest",
                DurationSeconds = result.DurationSeconds,
                Bpm = result.Bpm,
                AudioFeatures = result.Features,
                AnalysisVersion = result.Features.AnalysisVersion,
                IsAnalyzed = true
            };
            new MusicImportIndexStore(libraryIndexPath).SaveTrack(persistedTrack);
            var restoredTrack = new MusicImportIndexStore(libraryIndexPath).TryCreateTrack(path);
            Check(restoredTrack is not null &&
                  restoredTrack.AudioFeatures.Energy == result.Features.Energy &&
                  restoredTrack.AnalysisVersion == TrackAudioFeatures.CurrentAnalysisVersion,
                "Indice libreria conserva e ripristina TrackAudioFeatures v4");

            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
            Check(await cacheStore.TryLoadAsync(path) is null,
                "Cache reale invalida il file modificato");

            var staleResult = result with
            {
                Features = result.Features with { AnalysisVersion = TrackAudioFeatures.CurrentAnalysisVersion - 1 }
            };
            await cacheStore.SaveAsync(path, staleResult);
            Check(await cacheStore.TryLoadAsync(path) is null,
                "Cache reale rifiuta feature di una versione precedente");
            cacheLifecycleTested = true;
        }
    }
}
finally
{
    try { Directory.Delete(syntheticFolder, recursive: true); }
    catch (IOException)
    {
        Console.WriteLine("WARN  Cartella temporanea non eliminata: file ancora in uso.");
    }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine("WARN  Cartella temporanea non eliminata: accesso negato.");
    }
}

Console.WriteLine(new string('-', 64));
if (failures.Count == 0)
{
    Console.WriteLine("SELF TEST COMPLETATO: TUTTI I CONTROLLI SONO PASSATI");
    return 0;
}

Console.WriteLine($"SELF TEST FALLITO: {failures.Count} controlli non superati");
foreach (var failure in failures) Console.WriteLine($" - {failure}");
return 1;

static void WriteClickTrack(string path, double bpm, double durationSeconds)
{
    const int sampleRate = 44_100;
    const int channels = 2;
    var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    var beatLength = 60d / bpm;
    var totalFrames = (int)Math.Ceiling(durationSeconds * sampleRate);

    using var writer = new WaveFileWriter(path, format);
    for (var frame = 0; frame < totalFrames; frame++)
    {
        var time = frame / (double)sampleRate;
        var beatIndex = (int)Math.Floor(time / beatLength);
        var phase = time - beatIndex * beatLength;
        var envelope = Math.Exp(-phase * 52d);
        var accent = beatIndex % 4 == 0 ? 1d : 0.72d;
        var click = Math.Sin(2d * Math.PI * 110d * phase) * envelope * accent * 0.85d;
        var sub = Math.Sin(2d * Math.PI * 55d * time) * envelope * 0.12d;
        var sample = (float)Math.Clamp(click + sub, -0.95d, 0.95d);
        writer.WriteSample(sample);
        writer.WriteSample(sample);
    }
}

static void WriteSineSample(string path, double frequency, double durationSeconds)
{
    const int sampleRate = 44_100;
    const int channels = 2;
    var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    var totalFrames = (int)Math.Ceiling(durationSeconds * sampleRate);
    using var writer = new WaveFileWriter(path, format);
    for (var frame = 0; frame < totalFrames; frame++)
    {
        var time = frame / (double)sampleRate;
        var fadeOut = Math.Clamp((durationSeconds - time) / 0.04d, 0d, 1d);
        var sample = (float)(Math.Sin(2d * Math.PI * frequency * time) * 0.6d * fadeOut);
        writer.WriteSample(sample);
        writer.WriteSample(sample);
    }
}

static float[] BuildQuadratureSignal(int sampleRate, double startPhaseTurns, double endPhaseTurns)
{
    var frames = 1024;
    var result = new float[frames * 2];
    for (var frame = 0; frame < frames; frame++)
    {
        var t = frame / (double)Math.Max(1, frames - 1);
        var phase = (startPhaseTurns + (endPhaseTurns - startPhaseTurns) * t) * Math.PI * 2d;
        result[frame * 2] = (float)(Math.Cos(phase) * 0.6d);
        result[frame * 2 + 1] = (float)(Math.Sin(phase) * 0.6d);
    }
    return result;
}

sealed class ConstantSampleProvider : ISampleProvider
{
    private readonly float _value;
    private int _remaining;

    public ConstantSampleProvider(WaveFormat waveFormat, float value, int sampleCount)
    {
        WaveFormat = waveFormat;
        _value = value;
        _remaining = sampleCount;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = Math.Min(count, _remaining);
        for (var i = 0; i < read; i++) buffer[offset + i] = _value;
        _remaining -= read;
        return read;
    }
}
