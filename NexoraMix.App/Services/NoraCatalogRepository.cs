using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexoraMix.Core.Models;
using IOFile = System.IO.File;
using IOFileInfo = System.IO.FileInfo;
using IOPath = System.IO.Path;

namespace NexoraMix.App.Services;

public sealed record NoraCatalogTrack(
    string Id,
    string Title,
    string Artist,
    double? Bpm,
    int? Year,
    string? Genre)
{
    public TrackAudioFeatures? AudioFeatures { get; init; }
    public string? FilePath { get; init; }
    public bool IsFileAvailable { get; init; }
    public double? DurationSeconds { get; init; }
}

public sealed record NoraCatalogQuery(
    string ExcludedTrackId,
    double? Bpm,
    int? Year,
    string? Genre,
    int Limit = 1000);

public sealed record NoraCatalogMetrics(
    TimeSpan QueryDuration,
    int CandidateCount,
    int ReturnedCount,
    bool CacheHit,
    int CacheEntryCount);

public sealed record NoraCatalogQueryResult(
    IReadOnlyList<NoraCatalogTrack> Tracks,
    NoraCatalogMetrics Metrics);

public sealed record NoraCatalogLocalFile(
    string Path,
    string TrackId,
    double? Bpm,
    double? BpmConfidence,
    TrackAudioFeatures? Features);

/// <summary>
/// Accesso ristretto al catalogo condiviso di PuliziaSpazioDev. Non crea lo schema
/// proprietario: lo valida, aggiunge soltanto indici compatibili e usa query limitate.
/// </summary>
public sealed class NoraCatalogRepository : IDisposable
{
    private const int MaximumCandidates = 1000;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(3);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".aiff", ".aif", ".wma", ".m4a", ".aac", ".flac"
    };

    private readonly string _databasePath;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CacheEntry> _queryCache = new(StringComparer.Ordinal);
    private bool _initialized;
    private bool _disposed;

    public NoraCatalogRepository(string? databasePath = null)
    {
        _databasePath = IOPath.GetFullPath(databasePath ?? DefaultDatabasePath());
    }

    public string DatabasePath => _databasePath;
    public string? LastBackupPath { get; private set; }
    public int CatalogTrackCount { get; private set; }

    public static string DefaultDatabasePath() => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nexora", "PuliziaSpazioDev", "Data", "nexora-music.db");

    public static string ComputePathId(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            IOPath.GetFullPath(path).ToUpperInvariant())));

    public async Task<bool> EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return true;
        if (!IOFile.Exists(_databasePath)) return false;

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return true;
            await using var connection = await OpenAsync(readOnly: false, cancellationToken).ConfigureAwait(false);
            await ValidateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            if (await HasMissingIndexesAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                LastBackupPath = await TryBackupAsync(connection, cancellationToken).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    CREATE INDEX IF NOT EXISTS IX_Tracks_Bpm ON Tracks(Bpm);
                    CREATE INDEX IF NOT EXISTS IX_Tracks_UpdatedAtUtc ON Tracks(UpdatedAtUtc DESC);
                    CREATE INDEX IF NOT EXISTS IX_FileInstances_AvailableTrack
                        ON FileInstances(TrackId, UpdatedAtUtc DESC) WHERE IsAvailable = 1;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = "SELECT COUNT(*) FROM Tracks;";
                CatalogTrackCount = Convert.ToInt32(
                    await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
            }
            _initialized = true;
            return true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<int> UpsertLocalFilesAsync(
        IEnumerable<NoraCatalogLocalFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false)) return 0;

        var candidates = files
            .Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .Select(file => file with { Path = IOPath.GetFullPath(file.Path) })
            .Where(file => IOFile.Exists(file.Path) && IsSupported(file.Path))
            .GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (candidates.Length == 0) return 0;

        var changed = 0;
        await using var connection = await OpenAsync(readOnly: false, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var local in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new IOFileInfo(local.Path);
            var fileId = ComputePathId(local.Path);
            var trackId = await TrackExistsAsync(connection, (SqliteTransaction)transaction, local.TrackId, cancellationToken)
                .ConfigureAwait(false) ? local.TrackId : null;

            await using (var fileCommand = connection.CreateCommand())
            {
                fileCommand.Transaction = (SqliteTransaction)transaction;
                fileCommand.CommandText = """
                    INSERT INTO FileInstances
                        (Id, TrackId, Path, Sha256, SizeBytes, LastWriteTimeUtc, IsAvailable, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ($id, $trackId, $path, NULL, $size, $lastWrite, 1, $now, $now)
                    ON CONFLICT(Path) DO UPDATE SET
                        TrackId = COALESCE(excluded.TrackId, FileInstances.TrackId),
                        SizeBytes = excluded.SizeBytes,
                        LastWriteTimeUtc = excluded.LastWriteTimeUtc,
                        IsAvailable = 1,
                        UpdatedAtUtc = excluded.UpdatedAtUtc
                    WHERE FileInstances.TrackId IS NOT COALESCE(excluded.TrackId, FileInstances.TrackId)
                       OR FileInstances.SizeBytes <> excluded.SizeBytes
                       OR FileInstances.LastWriteTimeUtc <> excluded.LastWriteTimeUtc
                       OR FileInstances.IsAvailable <> 1;
                    """;
                fileCommand.Parameters.AddWithValue("$id", fileId);
                fileCommand.Parameters.AddWithValue("$trackId", (object?)trackId ?? DBNull.Value);
                fileCommand.Parameters.AddWithValue("$path", local.Path);
                fileCommand.Parameters.AddWithValue("$size", info.Length);
                fileCommand.Parameters.AddWithValue("$lastWrite", info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
                fileCommand.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                changed += await fileCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var persistedFileId = await GetFileInstanceIdAsync(
                connection, (SqliteTransaction)transaction, local.Path, cancellationToken).ConfigureAwait(false);
            if (local.Features is not null || local.Bpm is > 0)
            {
                var features = local.Features ?? new TrackAudioFeatures
                {
                    Status = AudioFeatureAnalysisStatus.Partial,
                    Bpm = local.Bpm,
                    BpmConfidence = local.BpmConfidence
                };
                var json = JsonSerializer.Serialize(features);
                await using var featureCommand = connection.CreateCommand();
                featureCommand.Transaction = (SqliteTransaction)transaction;
                featureCommand.CommandText = """
                    INSERT INTO TechnicalAudioFeatures
                        (Id, FileInstanceId, Format, DurationSeconds, Bitrate, SampleRate, Channels, BitsPerSample,
                         Bpm, BpmConfidence, FeaturesJson, CreatedAtUtc)
                    VALUES ($id, $fileId, $format, NULL, NULL, NULL, NULL, NULL, $bpm, $confidence, $json, $now)
                    ON CONFLICT(FileInstanceId) DO UPDATE SET
                        Format = excluded.Format,
                        Bpm = excluded.Bpm,
                        BpmConfidence = excluded.BpmConfidence,
                        FeaturesJson = excluded.FeaturesJson,
                        CreatedAtUtc = excluded.CreatedAtUtc
                    WHERE TechnicalAudioFeatures.Format IS NOT excluded.Format
                       OR TechnicalAudioFeatures.Bpm IS NOT excluded.Bpm
                       OR TechnicalAudioFeatures.BpmConfidence IS NOT excluded.BpmConfidence
                       OR TechnicalAudioFeatures.FeaturesJson <> excluded.FeaturesJson;
                    """;
                featureCommand.Parameters.AddWithValue("$id", "features-" + persistedFileId);
                featureCommand.Parameters.AddWithValue("$fileId", persistedFileId);
                featureCommand.Parameters.AddWithValue("$format", IOPath.GetExtension(local.Path).TrimStart('.').ToLowerInvariant());
                featureCommand.Parameters.AddWithValue("$bpm", (object?)(features.Bpm ?? local.Bpm) ?? DBNull.Value);
                featureCommand.Parameters.AddWithValue("$confidence", (object?)(features.BpmConfidence ?? local.BpmConfidence) ?? DBNull.Value);
                featureCommand.Parameters.AddWithValue("$json", json);
                featureCommand.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                changed += await featureCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (changed > 0) ClearCache();
        return changed;
    }

    public async Task<NoraCatalogTrack?> FindTrackAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            !await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false)) return null;

        await using var connection = await OpenAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.Id, t.Title, t.Artist,
                   COALESCE(CASE WHEN t.Bpm BETWEEN 40 AND 300 THEN t.Bpm END,
                       (SELECT taf.Bpm FROM FileInstances fi
                        INNER JOIN TechnicalAudioFeatures taf ON taf.FileInstanceId = fi.Id
                        WHERE fi.TrackId = t.Id AND fi.IsAvailable = 1 AND taf.Bpm BETWEEN 40 AND 300
                        ORDER BY fi.UpdatedAtUtc DESC LIMIT 1)),
                   t.Year, t.Genre,
                   (SELECT taf.FeaturesJson FROM FileInstances fi
                    INNER JOIN TechnicalAudioFeatures taf ON taf.FileInstanceId = fi.Id
                    WHERE fi.TrackId = t.Id AND fi.IsAvailable = 1
                    ORDER BY fi.UpdatedAtUtc DESC LIMIT 1),
                   (SELECT fi.Path FROM FileInstances fi
                    WHERE fi.TrackId = t.Id AND fi.IsAvailable = 1
                    ORDER BY fi.UpdatedAtUtc DESC LIMIT 1),
                   (SELECT taf.DurationSeconds FROM FileInstances fi
                    INNER JOIN TechnicalAudioFeatures taf ON taf.FileInstanceId = fi.Id
                    WHERE fi.TrackId = t.Id AND fi.IsAvailable = 1
                    ORDER BY fi.UpdatedAtUtc DESC LIMIT 1)
            FROM Tracks t WHERE t.Id = $id AND NULLIF(TRIM(t.Title), '') IS NOT NULL LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTrack(reader) : null;
    }

    public async Task<NoraCatalogQueryResult> QueryCandidatesAsync(
        NoraCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false))
            return new([], new(TimeSpan.Zero, 0, 0, false, 0));

        var normalized = query with { Limit = Math.Clamp(query.Limit, 1, MaximumCandidates) };
        var stamp = DatabaseStamp();
        var cacheKey = string.Join('|', stamp, normalized.ExcludedTrackId,
            normalized.Bpm?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-",
            normalized.Year?.ToString(CultureInfo.InvariantCulture) ?? "-",
            NormalizeGenre(normalized.Genre), normalized.Limit);
        lock (_cacheGate)
        {
            if (_queryCache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAtUtc <= CacheLifetime)
            {
                return cached.Result with
                {
                    Metrics = cached.Result.Metrics with { CacheHit = true, CacheEntryCount = _queryCache.Count }
                };
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var tracks = new List<NoraCatalogTrack>(normalized.Limit);
        var candidateCount = 0;
        await using var connection = await OpenAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = CandidateSql;
        BindCandidateParameters(command, normalized);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            candidateCount = reader.GetInt32(9);
            tracks.Add(ReadTrack(reader));
        }
        stopwatch.Stop();

        NoraCatalogQueryResult result;
        lock (_cacheGate)
        {
            TrimCache();
            result = new(tracks, new(stopwatch.Elapsed, candidateCount, tracks.Count, false, _queryCache.Count + 1));
            _queryCache[cacheKey] = new(result, DateTimeOffset.UtcNow);
        }
        return result;
    }

    public async Task<string?> ResolveBestFileAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId) ||
            !await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false)) return null;

        await using var connection = await OpenAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fi.Path
            FROM FileInstances fi
            LEFT JOIN TechnicalAudioFeatures taf ON taf.FileInstanceId = fi.Id
            WHERE fi.TrackId = $trackId AND fi.IsAvailable = 1
            ORDER BY CASE WHEN taf.Bitrate IS NULL THEN 1 ELSE 0 END,
                     taf.Bitrate DESC, taf.SampleRate DESC, fi.UpdatedAtUtc DESC;
            """;
        command.Parameters.AddWithValue("$trackId", trackId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = reader.GetString(0);
            if (IsSupported(path) && IOFile.Exists(path)) return path;
        }
        return null;
    }

    private async Task<SqliteConnection> OpenAsync(bool readOnly, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ValidateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var required = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tracks"] = ["Id", "Title", "Artist", "Bpm", "Year", "Genre", "UpdatedAtUtc"],
            ["FileInstances"] = ["Id", "TrackId", "Path", "SizeBytes", "LastWriteTimeUtc", "IsAvailable", "CreatedAtUtc", "UpdatedAtUtc"],
            ["TechnicalAudioFeatures"] = ["Id", "FileInstanceId", "Bpm", "BpmConfidence", "FeaturesJson"]
        };
        foreach (var pair in required)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{pair.Key}\");";
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) actual.Add(reader.GetString(1));
            var missing = pair.Value.Where(column => !actual.Contains(column)).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException($"Schema catalogo non compatibile: {pair.Key} manca {string.Join(", ", missing)}.");
        }
    }

    private static async Task<bool> HasMissingIndexesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name IN
                ('IX_Tracks_Bpm', 'IX_Tracks_UpdatedAtUtc', 'IX_FileInstances_AvailableTrack');
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) < 3;
    }

    private async Task<string?> TryBackupAsync(SqliteConnection source, CancellationToken cancellationToken)
    {
        try
        {
            var path = $"{_databasePath}.nora-indexes.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.bak";
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
            source.BackupDatabase(destination);
            return path;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    private static async Task<bool> TrackExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Tracks WHERE Id = $id);";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<string> GetFileInstanceIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string path,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM FileInstances WHERE Path = $path LIMIT 1;";
        command.Parameters.AddWithValue("$path", path);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException("FileInstance non disponibile dopo l'upsert.");
    }

    private static NoraCatalogTrack ReadTrack(SqliteDataReader reader)
    {
        TrackAudioFeatures? features = null;
        if (reader.FieldCount > 6 && !reader.IsDBNull(6))
        {
            try { features = JsonSerializer.Deserialize<TrackAudioFeatures>(reader.GetString(6)); }
            catch (JsonException exception)
            {
                Debug.WriteLine($"Nora ignora FeaturesJson non valido: {exception.Message}");
            }
        }
        var storedPath = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : null;
        var availablePath = !string.IsNullOrWhiteSpace(storedPath) && IsSupported(storedPath) && IOFile.Exists(storedPath)
            ? IOPath.GetFullPath(storedPath)
            : null;
        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "Artista sconosciuto" : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5))
        {
            AudioFeatures = features,
            FilePath = availablePath,
            IsFileAvailable = availablePath is not null,
            DurationSeconds = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetDouble(8) : null
        };
    }

    private static void BindCandidateParameters(SqliteCommand command, NoraCatalogQuery query)
    {
        var bpm = query.Bpm is >= 40 and <= 300 ? query.Bpm.Value : 0d;
        var genre = NormalizeGenre(query.Genre);
        command.Parameters.AddWithValue("$excluded", query.ExcludedTrackId ?? string.Empty);
        command.Parameters.AddWithValue("$hasBpm", bpm > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$low", Math.Max(40, bpm - 14));
        command.Parameters.AddWithValue("$high", Math.Min(300, bpm + 14));
        command.Parameters.AddWithValue("$halfLow", Math.Max(40, (bpm - 14) / 2));
        command.Parameters.AddWithValue("$halfHigh", Math.Min(300, (bpm + 14) / 2));
        command.Parameters.AddWithValue("$doubleLow", Math.Max(40, (bpm - 14) * 2));
        command.Parameters.AddWithValue("$doubleHigh", Math.Min(300, (bpm + 14) * 2));
        command.Parameters.AddWithValue("$bpm", bpm);
        command.Parameters.AddWithValue("$hasGenre", genre.Length > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$genre", genre);
        command.Parameters.AddWithValue("$hasYear", query.Year is >= 1900 and <= 2100 ? 1 : 0);
        command.Parameters.AddWithValue("$year", query.Year is >= 1900 and <= 2100 ? query.Year.Value : 0);
        command.Parameters.AddWithValue("$poolLimit", MaximumCandidates);
        command.Parameters.AddWithValue("$limit", query.Limit);
    }

    private static string NormalizeGenre(string? genre) => string.IsNullOrWhiteSpace(genre)
        ? string.Empty
        : genre.Split([',', ';', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

    private string DatabaseStamp()
    {
        var database = new IOFileInfo(_databasePath);
        var wal = new IOFileInfo(_databasePath + "-wal");
        return string.Create(CultureInfo.InvariantCulture,
            $"{database.Length}:{database.LastWriteTimeUtc.Ticks}:{(wal.Exists ? wal.Length : 0)}:{(wal.Exists ? wal.LastWriteTimeUtc.Ticks : 0)}");
    }

    private static bool IsSupported(string path) => SupportedExtensions.Contains(IOPath.GetExtension(path));

    private void ClearCache()
    {
        lock (_cacheGate) _queryCache.Clear();
    }

    private void TrimCache()
    {
        var expired = _queryCache.Where(pair => DateTimeOffset.UtcNow - pair.Value.CreatedAtUtc > CacheLifetime)
            .Select(pair => pair.Key).ToArray();
        foreach (var key in expired) _queryCache.Remove(key);
        while (_queryCache.Count >= 32) _queryCache.Remove(_queryCache.First().Key);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initializationGate.Dispose();
        ClearCache();
    }

    private sealed record CacheEntry(NoraCatalogQueryResult Result, DateTimeOffset CreatedAtUtc);

    private const string CandidateSql = """
        WITH bpm_pool AS (
            SELECT t.Id, t.Title, t.Artist, CAST(t.Bpm AS REAL) AS EffectiveBpm, t.Year, t.Genre,
                   t.UpdatedAtUtc, 0 AS Priority
            FROM Tracks t INDEXED BY IX_Tracks_Bpm
            WHERE $hasBpm = 1 AND t.Id <> $excluded
              AND NULLIF(TRIM(t.Title), '') IS NOT NULL
              AND t.Bpm BETWEEN 40 AND 300
              AND ((t.Bpm BETWEEN $low AND $high)
                OR (t.Bpm BETWEEN $halfLow AND $halfHigh)
                OR (t.Bpm BETWEEN $doubleLow AND $doubleHigh))
            ORDER BY MIN(ABS(t.Bpm - $bpm), ABS(t.Bpm * 2.0 - $bpm), ABS(t.Bpm / 2.0 - $bpm)),
                     t.UpdatedAtUtc DESC
            LIMIT $poolLimit
        ), feature_pool AS (
            SELECT t.Id, t.Title, t.Artist, taf.Bpm AS EffectiveBpm, t.Year, t.Genre,
                   t.UpdatedAtUtc, 1 AS Priority
            FROM TechnicalAudioFeatures taf
            INNER JOIN FileInstances fi ON fi.Id = taf.FileInstanceId AND fi.IsAvailable = 1
            INNER JOIN Tracks t ON t.Id = fi.TrackId
            WHERE $hasBpm = 1 AND t.Id <> $excluded
              AND NULLIF(TRIM(t.Title), '') IS NOT NULL
              AND (t.Bpm IS NULL OR t.Bpm NOT BETWEEN 40 AND 300)
              AND taf.Bpm BETWEEN 40 AND 300
              AND ((taf.Bpm BETWEEN $low AND $high)
                OR (taf.Bpm BETWEEN $halfLow AND $halfHigh)
                OR (taf.Bpm BETWEEN $doubleLow AND $doubleHigh))
            ORDER BY MIN(ABS(taf.Bpm - $bpm), ABS(taf.Bpm * 2.0 - $bpm), ABS(taf.Bpm / 2.0 - $bpm)),
                     t.UpdatedAtUtc DESC
            LIMIT $poolLimit
        ), fallback_pool AS (
            SELECT t.Id, t.Title, t.Artist,
                   CASE WHEN t.Bpm BETWEEN 40 AND 300 THEN CAST(t.Bpm AS REAL) END AS EffectiveBpm,
                   t.Year, t.Genre, t.UpdatedAtUtc, 2 AS Priority
            FROM Tracks t INDEXED BY IX_Tracks_UpdatedAtUtc
            WHERE t.Id <> $excluded AND NULLIF(TRIM(t.Title), '') IS NOT NULL
              AND (($hasGenre = 1 AND INSTR(LOWER(COALESCE(t.Genre, '')), $genre) > 0)
                OR ($hasYear = 1 AND t.Year BETWEEN $year - 20 AND $year + 20))
            ORDER BY t.UpdatedAtUtc DESC
            LIMIT $poolLimit
        ), combined AS (
            SELECT * FROM bpm_pool
            UNION ALL SELECT * FROM feature_pool
            UNION ALL SELECT * FROM fallback_pool
        ), ranked AS (
            SELECT *, ROW_NUMBER() OVER (
                PARTITION BY LOWER(TRIM(Title)), LOWER(TRIM(COALESCE(Artist, '')))
                ORDER BY Priority, UpdatedAtUtc DESC, Id) AS DuplicateRank
            FROM combined
        ), deduplicated AS (
            SELECT * FROM ranked WHERE DuplicateRank = 1
        )
        , enriched AS (
            SELECT d.*,
                   (SELECT taf.FeaturesJson FROM FileInstances fi
                    INNER JOIN TechnicalAudioFeatures taf ON taf.FileInstanceId = fi.Id
                    WHERE fi.TrackId = d.Id AND fi.IsAvailable = 1
                    ORDER BY fi.UpdatedAtUtc DESC LIMIT 1) AS FeaturesJson,
                   (SELECT fi.Path FROM FileInstances fi
                    WHERE fi.TrackId = d.Id AND fi.IsAvailable = 1
                    ORDER BY fi.UpdatedAtUtc DESC LIMIT 1) AS FilePath,
                   (SELECT taf.DurationSeconds FROM FileInstances fi
                    INNER JOIN TechnicalAudioFeatures taf ON taf.FileInstanceId = fi.Id
                    WHERE fi.TrackId = d.Id AND fi.IsAvailable = 1
                    ORDER BY fi.UpdatedAtUtc DESC LIMIT 1) AS DurationSeconds
            FROM deduplicated d
        )
        SELECT Id, Title, Artist, EffectiveBpm, Year, Genre,
               FeaturesJson, FilePath, DurationSeconds, COUNT(*) OVER()
        FROM enriched
        ORDER BY Priority, UpdatedAtUtc DESC, Title COLLATE NOCASE
        LIMIT $limit;
        """;
}
