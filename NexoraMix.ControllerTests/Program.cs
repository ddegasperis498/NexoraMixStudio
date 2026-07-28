using NexoraMix.Core.Controllers;
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

return failures.Count == 0 ? 0 : 1;
