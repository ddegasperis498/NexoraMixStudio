using System.IO;
using System.Text.Json;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;

namespace NexoraMix.App.Services;

public sealed class TrackMappingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _mappings;

    public TrackMappingStore()
    {
        _mappings = LoadMappings();
    }

    public string FilePath
    {
        get
        {
            var folder = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexoraMix",
                "Mappings");
            IODirectory.CreateDirectory(folder);
            return IOPath.Combine(folder, "spotify-local.json");
        }
    }

    public bool TryResolve(string? spotifyId, out string localPath)
    {
        localPath = string.Empty;
        if (string.IsNullOrWhiteSpace(spotifyId)) return false;

        lock (_gate)
        {
            if (!_mappings.TryGetValue(spotifyId, out var candidate) || !IOFile.Exists(candidate)) return false;
            localPath = candidate;
            return true;
        }
    }

    public async Task SaveAsync(string spotifyId, string localPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyId)) throw new ArgumentException("Spotify ID mancante.", nameof(spotifyId));
        if (!IOFile.Exists(localPath)) throw new FileNotFoundException("File locale non trovato.", localPath);

        Dictionary<string, string> snapshot;
        lock (_gate)
        {
            _mappings[spotifyId] = IOPath.GetFullPath(localPath);
            snapshot = new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase);
        }

        var target = FilePath;
        var temporary = target + ".tmp";
        var backup = target + ".bak";

        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 8 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (IOFile.Exists(target))
            {
                try
                {
                    IOFile.Replace(temporary, target, backup, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    IOFile.Copy(target, backup, overwrite: true);
                    IOFile.Move(temporary, target, overwrite: true);
                }
                catch (IOException)
                {
                    IOFile.Copy(target, backup, overwrite: true);
                    IOFile.Move(temporary, target, overwrite: true);
                }
            }
            else
            {
                IOFile.Move(temporary, target);
            }
        }
        finally
        {
            if (IOFile.Exists(temporary)) IOFile.Delete(temporary);
        }
    }

    private Dictionary<string, string> LoadMappings()
    {
        var path = FilePath;
        if (!IOFile.Exists(path)) return NewMappingDictionary();

        try
        {
            var json = IOFile.ReadAllText(path);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return values is null
                ? NewMappingDictionary()
                : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            PreserveCorruptFile(path);
            return NewMappingDictionary();
        }
        catch (IOException)
        {
            return NewMappingDictionary();
        }
        catch (UnauthorizedAccessException)
        {
            return NewMappingDictionary();
        }
    }

    private static Dictionary<string, string> NewMappingDictionary() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static void PreserveCorruptFile(string path)
    {
        try
        {
            IOFile.Copy(path, path + $".corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}", overwrite: false);
        }
        catch (IOException)
        {
            // Il file originale resta disponibile per una diagnosi manuale.
        }
        catch (UnauthorizedAccessException)
        {
            // Il file originale resta disponibile per una diagnosi manuale.
        }
    }
}
