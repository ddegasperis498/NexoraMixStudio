using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexoraMix.Audio.Analysis;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;

namespace NexoraMix.App.Services;

public sealed class AnalysisCacheStore
{
    private const int AnalyzerVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string CacheFolder
    {
        get
        {
            var folder = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexoraMix",
                "AnalysisCache");
            IODirectory.CreateDirectory(folder);
            return folder;
        }
    }

    public async Task<AudioAnalysisResult?> TryLoadAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        if (!IOFile.Exists(audioPath)) return null;
        var cachePath = GetCachePath(audioPath);
        if (!IOFile.Exists(cachePath)) return null;

        try
        {
            await using var stream = IOFile.OpenRead(cachePath);
            var entry = await JsonSerializer.DeserializeAsync<CacheEntry>(stream, JsonOptions, cancellationToken);
            return entry is not null && entry.Fingerprint == CreateFingerprint(audioPath)
                ? entry.Analysis
                : null;
        }
        catch (JsonException)
        {
            TryDelete(cachePath);
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        string audioPath,
        AudioAnalysisResult analysis,
        CancellationToken cancellationToken = default)
    {
        if (!IOFile.Exists(audioPath)) return;
        var target = GetCachePath(audioPath);
        var temporary = target + ".tmp";
        var entry = new CacheEntry(CreateFingerprint(audioPath), DateTimeOffset.UtcNow, analysis);

        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            IOFile.Move(temporary, target, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static string GetCachePath(string audioPath)
    {
        var fingerprint = CreateFingerprint(audioPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        return IOPath.Combine(CacheFolder, hash + ".json");
    }

    private static string CreateFingerprint(string audioPath)
    {
        var info = new FileInfo(audioPath);
        return string.Join(
            "|",
            AnalyzerVersion,
            info.FullName.ToUpperInvariant(),
            info.Length,
            info.LastWriteTimeUtc.Ticks);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (IOFile.Exists(path)) IOFile.Delete(path);
        }
        catch (IOException)
        {
            // La cache può essere rigenerata in una sessione successiva.
        }
        catch (UnauthorizedAccessException)
        {
            // La cache può essere rigenerata in una sessione successiva.
        }
    }

    private sealed record CacheEntry(
        string Fingerprint,
        DateTimeOffset CreatedAtUtc,
        AudioAnalysisResult Analysis);
}
