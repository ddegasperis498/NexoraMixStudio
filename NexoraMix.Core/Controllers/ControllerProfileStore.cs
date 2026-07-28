using System.Text.Json;

namespace NexoraMix.Core.Controllers;

public sealed class ControllerProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task SaveAsync(
        string path,
        ControllerProfile profile,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(profile);
        if (errors.Count > 0)
            throw new InvalidOperationException("Profilo controller non valido: " + string.Join("; ", errors));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<ControllerProfile> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var profile = JsonSerializer.Deserialize<ControllerProfile>(json, JsonOptions)
                      ?? throw new InvalidOperationException("File profilo controller vuoto o non valido.");

        var errors = Validate(profile);
        if (errors.Count > 0)
            throw new InvalidOperationException("Profilo controller non valido: " + string.Join("; ", errors));

        return profile;
    }

    public static IReadOnlyList<string> Validate(ControllerProfile profile)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Id)) errors.Add("Id mancante");
        if (string.IsNullOrWhiteSpace(profile.DisplayName)) errors.Add("Nome visualizzato mancante");
        if (string.IsNullOrWhiteSpace(profile.Manufacturer)) errors.Add("Produttore mancante");
        if (profile.Protocol is ControllerProtocol.Hid or ControllerProtocol.VendorSdk &&
            profile.VerificationStatus == ControllerVerificationStatus.GenericMidi)
            errors.Add("Un profilo HID/vendor non puo essere marcato GenericMidi");

        var duplicateCommands = profile.Mappings
            .GroupBy(mapping => $"{mapping.Command}\u001f{mapping.Layer}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Replace('\u001f', '@'))
            .ToList();
        if (duplicateCommands.Count > 0)
            errors.Add("Comandi duplicati: " + string.Join(", ", duplicateCommands));

        foreach (var mapping in profile.Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Command)) errors.Add("Mapping con comando vuoto");
            if (mapping.Channel is < 1 or > 16) errors.Add($"{mapping.Command}: canale MIDI fuori range");
            if (mapping.Data1 is < 0 or > 127) errors.Add($"{mapping.Command}: Data1 fuori range");
            if (mapping.Minimum >= mapping.Maximum) errors.Add($"{mapping.Command}: range non valido");
            if (mapping.DeadZone is < 0 or > 1) errors.Add($"{mapping.Command}: dead zone fuori range");
            if (mapping.Sensitivity <= 0) errors.Add($"{mapping.Command}: sensibilita non valida");
        }

        foreach (var feedback in profile.FeedbackMappings)
        {
            if (string.IsNullOrWhiteSpace(feedback.State)) errors.Add("Feedback con stato vuoto");
            if (feedback.Channel is < 1 or > 16) errors.Add($"{feedback.State}: canale MIDI fuori range");
            if (feedback.Data1 is < 0 or > 127) errors.Add($"{feedback.State}: Data1 fuori range");
            if (feedback.OffValue is < 0 or > 127 || feedback.OnValue is < 0 or > 127)
                errors.Add($"{feedback.State}: valore feedback fuori range");
        }

        return errors;
    }
}
