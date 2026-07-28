using System.IO;
using System.Text.Json;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;
using IOInvalidDataException = System.IO.InvalidDataException;
using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public sealed class SessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task SaveAsync(string path, MixSession session, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(session);

        var fullPath = IOPath.GetFullPath(path);
        var directory = IOPath.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            IODirectory.CreateDirectory(directory);
        }

        session.SavedAt = DateTimeOffset.Now;

        var temporaryPath = fullPath + ".tmp";
        var backupPath = fullPath + ".bak";

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, session, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (IOFile.Exists(fullPath))
            {
                try
                {
                    IOFile.Replace(temporaryPath, fullPath, backupPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    IOFile.Copy(fullPath, backupPath, overwrite: true);
                    IOFile.Move(temporaryPath, fullPath, overwrite: true);
                }
                catch (IOException)
                {
                    IOFile.Copy(fullPath, backupPath, overwrite: true);
                    IOFile.Move(temporaryPath, fullPath, overwrite: true);
                }
            }
            else
            {
                IOFile.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            if (IOFile.Exists(temporaryPath))
            {
                IOFile.Delete(temporaryPath);
            }
        }
    }

    public async Task<MixSession> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = IOFile.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MixSession>(stream, Options, cancellationToken)
               ?? throw new IOInvalidDataException("Il file sessione non contiene dati validi.");
    }
}
