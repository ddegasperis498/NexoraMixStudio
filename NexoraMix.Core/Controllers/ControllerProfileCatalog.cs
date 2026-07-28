using System.Text.RegularExpressions;

namespace NexoraMix.Core.Controllers;

public static class ControllerProfileCatalog
{
    public static IReadOnlyList<ControllerProfile> BuiltIn { get; } = CreateProfiles();

    public static ControllerProfile Match(string productName)
    {
        var matched = BuiltIn.FirstOrDefault(profile =>
            !string.IsNullOrWhiteSpace(profile.ProductMatch) &&
            Regex.IsMatch(productName ?? string.Empty, profile.ProductMatch, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        return matched ?? BuiltIn.First(profile => profile.Id == "generic-midi-4deck");
    }

    private static IReadOnlyList<ControllerProfile> CreateProfiles()
    {
        var genericMappings = new List<ControlMapping>
        {
            new("DeckA.Play", ControllerMessageKind.Note, 1, 36, ControllerTarget.DeckA, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckA.Cue", ControllerMessageKind.Note, 1, 37, ControllerTarget.DeckA, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckB.Play", ControllerMessageKind.Note, 1, 38, ControllerTarget.DeckB, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckB.Cue", ControllerMessageKind.Note, 1, 39, ControllerTarget.DeckB, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckC.Play", ControllerMessageKind.Note, 1, 40, ControllerTarget.DeckC, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckC.Cue", ControllerMessageKind.Note, 1, 41, ControllerTarget.DeckC, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckD.Play", ControllerMessageKind.Note, 1, 42, ControllerTarget.DeckD, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckD.Cue", ControllerMessageKind.Note, 1, 43, ControllerTarget.DeckD, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckA.Sync", ControllerMessageKind.Note, 1, 44, ControllerTarget.DeckA, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckB.Sync", ControllerMessageKind.Note, 1, 45, ControllerTarget.DeckB, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckC.Sync", ControllerMessageKind.Note, 1, 46, ControllerTarget.DeckC, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckD.Sync", ControllerMessageKind.Note, 1, 47, ControllerTarget.DeckD, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckA.Master", ControllerMessageKind.Note, 1, 48, ControllerTarget.DeckA, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckB.Master", ControllerMessageKind.Note, 1, 49, ControllerTarget.DeckB, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckC.Master", ControllerMessageKind.Note, 1, 50, ControllerTarget.DeckC, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckD.Master", ControllerMessageKind.Note, 1, 51, ControllerTarget.DeckD, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("Mashup.StartStop", ControllerMessageKind.Note, 1, 52, ControllerTarget.Global, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("Transport.StopAll", ControllerMessageKind.Note, 1, 53, ControllerTarget.Global, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("Recording.StartStop", ControllerMessageKind.Note, 1, 54, ControllerTarget.Master, Momentary: true, MappingKind: ControlMappingKind.Momentary),
            new("DeckA.Volume", ControllerMessageKind.ControlChange, 1, 16, ControllerTarget.DeckA, MappingKind: ControlMappingKind.Fader, SoftTakeover: true),
            new("DeckB.Volume", ControllerMessageKind.ControlChange, 1, 17, ControllerTarget.DeckB, MappingKind: ControlMappingKind.Fader, SoftTakeover: true),
            new("DeckC.Volume", ControllerMessageKind.ControlChange, 1, 18, ControllerTarget.DeckC, MappingKind: ControlMappingKind.Fader, SoftTakeover: true),
            new("DeckD.Volume", ControllerMessageKind.ControlChange, 1, 19, ControllerTarget.DeckD, MappingKind: ControlMappingKind.Fader, SoftTakeover: true),
            new("Mixer.Crossfader", ControllerMessageKind.ControlChange, 1, 20, ControllerTarget.Mixer, MappingKind: ControlMappingKind.Fader, SoftTakeover: true),
            new("Master.Gain", ControllerMessageKind.ControlChange, 1, 21, ControllerTarget.Master, MappingKind: ControlMappingKind.Fader, SoftTakeover: true),
            new("DeckA.Tempo", ControllerMessageKind.PitchBend, 1, 0, ControllerTarget.DeckA, Minimum: -16, Maximum: 16, MappingKind: ControlMappingKind.PitchFader, DeadZone: 0.002, SoftTakeover: true),
            new("DeckB.Tempo", ControllerMessageKind.PitchBend, 2, 0, ControllerTarget.DeckB, Minimum: -16, Maximum: 16, MappingKind: ControlMappingKind.PitchFader, DeadZone: 0.002, SoftTakeover: true),
            new("DeckC.Tempo", ControllerMessageKind.PitchBend, 3, 0, ControllerTarget.DeckC, Minimum: -16, Maximum: 16, MappingKind: ControlMappingKind.PitchFader, DeadZone: 0.002, SoftTakeover: true),
            new("DeckD.Tempo", ControllerMessageKind.PitchBend, 4, 0, ControllerTarget.DeckD, Minimum: -16, Maximum: 16, MappingKind: ControlMappingKind.PitchFader, DeadZone: 0.002, SoftTakeover: true)
        };

        var genericFeedback = new List<FeedbackMapping>
        {
            new("DeckA.Playing", ControllerMessageKind.Note, 1, 36),
            new("DeckB.Playing", ControllerMessageKind.Note, 1, 38),
            new("DeckC.Playing", ControllerMessageKind.Note, 1, 40),
            new("DeckD.Playing", ControllerMessageKind.Note, 1, 42),
            new("Recording.Active", ControllerMessageKind.Note, 1, 54)
        };

        ControllerProfile Experimental(string id, string name, string manufacturer, string pattern) =>
            new(id, name, manufacturer, pattern, ControllerProtocol.Midi,
                ControllerVerificationStatus.BuiltInExperimental, genericMappings, genericFeedback);

        return new[]
        {
            new ControllerProfile(
                "generic-midi-4deck",
                "Generic MIDI 4 Deck",
                "Generic",
                string.Empty,
                ControllerProtocol.Midi,
                ControllerVerificationStatus.GenericMidi,
                genericMappings,
                genericFeedback),
            Experimental("pioneer-alpha-midi", "Pioneer DJ / AlphaTheta Generic MIDI", "Pioneer DJ / AlphaTheta", "Pioneer|AlphaTheta|DDJ|XDJ|CDJ|DJM"),
            Experimental("gemini-midi", "Gemini Generic MIDI", "Gemini", "Gemini"),
            Experimental("denon-midi", "Denon DJ Generic MIDI", "Denon DJ", "Denon|Prime|MCX"),
            Experimental("numark-midi", "Numark Generic MIDI", "Numark", "Numark|Mixtrack|NS[0-9]"),
            Experimental("hercules-midi", "Hercules Generic MIDI", "Hercules", "Hercules|DJControl|Inpulse"),
            Experimental("reloop-midi", "Reloop Generic MIDI", "Reloop", "Reloop"),
            Experimental("traktor-midi", "Traktor Kontrol Generic MIDI", "Native Instruments", "Traktor|Kontrol|Native Instruments"),
            Experimental("roland-midi", "Roland DJ Generic MIDI", "Roland", "Roland|DJ-202|DJ-505|DJ-808")
        };
    }
}
