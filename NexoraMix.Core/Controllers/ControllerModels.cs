using NexoraMix.Core.Models;

namespace NexoraMix.Core.Controllers;

public enum ControllerProtocol
{
    Midi,
    Hid,
    VendorSdk
}

public enum ControllerVerificationStatus
{
    BuiltInVerified,
    BuiltInExperimental,
    UserCreated,
    Community,
    GenericMidi,
    Disabled
}

public enum ControllerTarget
{
    Global,
    DeckA,
    DeckB,
    DeckC,
    DeckD,
    Mixer,
    Sampler,
    Effects,
    Library,
    Master
}

public enum ControllerMessageKind
{
    Note,
    ControlChange,
    PitchBend,
    ProgramChange,
    Aftertouch,
    SystemExclusive
}

public enum ControlMappingKind
{
    Button,
    Toggle,
    Momentary,
    AbsoluteKnob,
    RelativeEncoder,
    Fader,
    PitchFader,
    JogWheel,
    VelocityPad,
    Aftertouch,
    PitchBend,
    ProgramChange,
    Modifier,
    ShiftLayer
}

public sealed record ControlMapping(
    string Command,
    ControllerMessageKind MessageKind,
    int Channel,
    int Data1,
    ControllerTarget Target,
    double Minimum = 0,
    double Maximum = 1,
    bool Invert = false,
    bool Momentary = false,
    ControlMappingKind MappingKind = ControlMappingKind.Button,
    double DeadZone = 0,
    double Sensitivity = 1,
    bool SoftTakeover = false,
    string Layer = "Default",
    string? ModifierCommand = null);

public sealed record FeedbackMapping(
    string State,
    ControllerMessageKind MessageKind,
    int Channel,
    int Data1,
    int OffValue = 0,
    int OnValue = 127);

public sealed record ControllerProfile(
    string Id,
    string DisplayName,
    string Manufacturer,
    string ProductMatch,
    ControllerProtocol Protocol,
    ControllerVerificationStatus VerificationStatus,
    IReadOnlyList<ControlMapping> Mappings,
    IReadOnlyList<FeedbackMapping> FeedbackMappings);

public sealed record ControllerDeviceInfo(
    int? InputDeviceNumber,
    int? OutputDeviceNumber,
    string ProductName,
    string StableId)
{
    public bool HasInput => InputDeviceNumber.HasValue;
    public bool HasOutput => OutputDeviceNumber.HasValue;
    public string CapabilityText => HasInput && HasOutput ? "IN/OUT" : HasInput ? "IN" : "OUT";
}

public sealed record ControllerInputEvent(
    DateTimeOffset Timestamp,
    ControllerMessageKind Kind,
    int Channel,
    int Data1,
    int Data2,
    string RawDescription);

public sealed record ControllerConnection(
    string ConnectionId,
    ControllerDeviceInfo Device,
    ControllerProfile Profile,
    DateTimeOffset ConnectedAtUtc);

public sealed record ControllerFeedbackEvent(
    string ConnectionId,
    string State,
    ControllerTarget Target,
    int Value,
    DateTimeOffset Timestamp);

public interface IControllerManager
{
    IReadOnlyList<ControllerDeviceInfo> Devices { get; }
    IReadOnlyList<ControllerConnection> Connections { get; }

    Task RefreshAsync(CancellationToken cancellationToken);

    Task<ControllerConnection> ConnectAsync(
        ControllerDeviceInfo device,
        ControllerProfile profile,
        CancellationToken cancellationToken);

    Task DisconnectAsync(
        string connectionId,
        CancellationToken cancellationToken);
}

public interface IControllerInputRouter
{
    void Route(ControllerInputEvent inputEvent);
}

public interface IControllerFeedbackRouter
{
    ValueTask SendAsync(
        ControllerFeedbackEvent feedback,
        CancellationToken cancellationToken);
}
