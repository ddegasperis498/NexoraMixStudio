using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexoraMix.Core.Services;
using IODirectory = System.IO.Directory;
using IOInvalidDataException = System.IO.InvalidDataException;
using IOPath = System.IO.Path;

namespace NexoraMix.App.Services;

/// <summary>Persistenza SQLite locale dei feedback. Non memorizza percorsi, audio o segreti.</summary>
public sealed class NoraPersonalizationStore : IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _databasePath;
    private readonly NoraPersonalizationService _learning = new();
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public NoraPersonalizationStore(string? databasePath = null)
    {
        _databasePath = IOPath.GetFullPath(databasePath ?? IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexoraMix", "Nora", "nora-personalization.db"));
    }

    public string DatabasePath => _databasePath;

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            IODirectory.CreateDirectory(IOPath.GetDirectoryName(_databasePath)!);
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = SchemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM NoraSchemaMigrations;";
            var version = Convert.ToInt32(
                await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (version > CurrentSchemaVersion)
                throw new InvalidOperationException($"Schema personalizzazione Nora {version} non supportato.");
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<NoraDjPreferenceProfile> GetOrCreateProfileAsync(
        string profileId,
        string displayName = "DJ principale",
        NoraPersonalizationSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO NoraDjPreferenceProfiles
                    (Id, DisplayName, SettingsJson, FeedbackCount, SnapshotAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ($id, $name, $settings, 0, $now, $now, $now)
                ON CONFLICT(Id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$id", profileId);
            insert.Parameters.AddWithValue("$name", displayName);
            insert.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(settings ?? new NoraPersonalizationSettings()));
            insert.Parameters.AddWithValue("$now", DatabaseTime(now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return await ReadProfileAsync(connection, profileId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Profilo Nora non disponibile dopo la creazione.");
    }

    public async Task SaveProfileAsync(
        NoraDjPreferenceProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfileId(profile.Id);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO NoraDjPreferenceProfiles
                (Id, DisplayName, SettingsJson, FeedbackCount, SnapshotAtUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $name, $settings, 0, $updated, $created, $updated)
            ON CONFLICT(Id) DO UPDATE SET DisplayName=excluded.DisplayName,
                SettingsJson=excluded.SettingsJson, UpdatedAtUtc=excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.DisplayName);
        command.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(profile.Settings));
        command.Parameters.AddWithValue("$created", DatabaseTime(profile.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", DatabaseTime(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoraPersonalizationSnapshot> RecordFeedbackAsync(
        NoraFeedbackEvent feedback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ValidateProfileId(feedback.ProfileId);
        if (string.IsNullOrWhiteSpace(feedback.Id)) throw new ArgumentException("ID feedback richiesto.", nameof(feedback));
        var previous = await LoadSnapshotAsync(feedback.ProfileId, cancellationToken).ConfigureAwait(false);
        var profile = await GetOrCreateProfileAsync(feedback.ProfileId, cancellationToken: cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO NoraFeedbackEvents
                    (Id, ProfileId, FeedbackType, IsExplicit, Confidence,
                     SourceTrackId, SourceArtist, SourceGenre, CandidateTrackId, CandidateArtist, CandidateGenre,
                     ContextJson, CreatedAtUtc)
                VALUES ($id,$profile,$type,$explicit,$confidence,$sourceId,$sourceArtist,$sourceGenre,
                        $candidateId,$candidateArtist,$candidateGenre,$context,$created);
                """;
            BindFeedback(command, feedback);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = _learning.UpdateSnapshot(profile, previous, feedback, DateTimeOffset.UtcNow);
        await PersistSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<NoraPersonalizationSnapshot> LoadSnapshotAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateProfileAsync(profileId, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProfileId, FeedbackType, IsExplicit, Confidence,
                   SourceTrackId, SourceArtist, SourceGenre, CandidateTrackId, CandidateArtist, CandidateGenre,
                   ContextJson, CreatedAtUtc
            FROM NoraFeedbackEvents WHERE ProfileId=$profile ORDER BY CreatedAtUtc, Id;
            """;
        command.Parameters.AddWithValue("$profile", profileId);
        var events = new List<NoraFeedbackEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) events.Add(ReadFeedback(reader));
        var snapshot = _learning.BuildSnapshot(profile, events, DateTimeOffset.UtcNow);
        await PersistSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public async Task ResetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var table in new[]
                 {
                     "NoraFeedbackEvents", "NoraLearnedWeights", "NoraArtistPreferences", "NoraCombinationPreferences"
                 })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = $"DELETE FROM {table} WHERE ProfileId=$profile;";
            command.Parameters.AddWithValue("$profile", profileId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE NoraDjPreferenceProfiles SET FeedbackCount=0, SnapshotAtUtc=$now, UpdatedAtUtc=$now
                WHERE Id=$profile;
                """;
            update.Parameters.AddWithValue("$profile", profileId);
            update.Parameters.AddWithValue("$now", DatabaseTime(DateTimeOffset.UtcNow));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExportProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateProfileAsync(profileId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var events = await ReadEventsAsync(profileId, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new ExportPackage(CurrentSchemaVersion, profile, events),
            new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task ImportProfileAsync(string json, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Esportazione Nora vuota.", nameof(json));
        var package = JsonSerializer.Deserialize<ExportPackage>(json)
            ?? throw new IOInvalidDataException("Esportazione Nora non valida.");
        if (package.SchemaVersion != CurrentSchemaVersion)
            throw new IOInvalidDataException($"Versione esportazione Nora {package.SchemaVersion} non supportata.");
        ValidateProfileId(package.Profile.Id);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await SaveProfileAsync(package.Profile, cancellationToken).ConfigureAwait(false);
        await ResetProfileAsync(package.Profile.Id, cancellationToken).ConfigureAwait(false);
        foreach (var feedback in package.Events.OrderBy(item => item.CreatedAtUtc))
            await RecordFeedbackAsync(feedback with { ProfileId = package.Profile.Id }, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistSnapshotAsync(NoraPersonalizationSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var table in new[] { "NoraLearnedWeights", "NoraArtistPreferences", "NoraCombinationPreferences" })
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = $"DELETE FROM {table} WHERE ProfileId=$profile;";
            delete.Parameters.AddWithValue("$profile", snapshot.ProfileId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (var preference in snapshot.Weights.Values)
        {
            await InsertWeightAsync(connection, (SqliteTransaction)transaction, snapshot.ProfileId, preference, cancellationToken)
                .ConfigureAwait(false);
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE NoraDjPreferenceProfiles SET FeedbackCount=$count, SnapshotAtUtc=$at, UpdatedAtUtc=$at
                WHERE Id=$profile;
                """;
            update.Parameters.AddWithValue("$count", snapshot.FeedbackCount);
            update.Parameters.AddWithValue("$at", DatabaseTime(snapshot.GeneratedAtUtc));
            update.Parameters.AddWithValue("$profile", snapshot.ProfileId);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertWeightAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string profileId,
        NoraLearnedPreference preference,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO NoraLearnedWeights (ProfileId,FactorKey,Value,EvidenceCount,UpdatedAtUtc)
                VALUES ($profile,$key,$value,$evidence,$updated);
                """;
            BindPreference(command, profileId, preference);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        if (preference.Key.StartsWith("artist:", StringComparison.OrdinalIgnoreCase))
        {
            await using var artist = connection.CreateCommand();
            artist.Transaction = transaction;
            artist.CommandText = """
                INSERT INTO NoraArtistPreferences (ProfileId,Artist,Value,EvidenceCount,UpdatedAtUtc)
                VALUES ($profile,$name,$value,$evidence,$updated);
                """;
            BindPreference(artist, profileId, preference);
            artist.Parameters.AddWithValue("$name", preference.Key["artist:".Length..]);
            await artist.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (preference.Key.StartsWith("combination:", StringComparison.OrdinalIgnoreCase))
        {
            var names = preference.Key["combination:".Length..].Split("=>", 2, StringSplitOptions.None);
            if (names.Length != 2) return;
            await using var combination = connection.CreateCommand();
            combination.Transaction = transaction;
            combination.CommandText = """
                INSERT INTO NoraCombinationPreferences
                    (ProfileId,SourceArtist,CandidateArtist,Value,EvidenceCount,UpdatedAtUtc)
                VALUES ($profile,$source,$candidate,$value,$evidence,$updated);
                """;
            BindPreference(combination, profileId, preference);
            combination.Parameters.AddWithValue("$source", names[0]);
            combination.Parameters.AddWithValue("$candidate", names[1]);
            await combination.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void BindPreference(
        SqliteCommand command, string profileId, NoraLearnedPreference preference)
    {
        command.Parameters.AddWithValue("$profile", profileId);
        command.Parameters.AddWithValue("$key", preference.Key);
        command.Parameters.AddWithValue("$value", preference.Value);
        command.Parameters.AddWithValue("$evidence", preference.EvidenceCount);
        command.Parameters.AddWithValue("$updated", DatabaseTime(preference.UpdatedAtUtc));
    }

    private async Task<IReadOnlyList<NoraFeedbackEvent>> ReadEventsAsync(
        string profileId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,ProfileId,FeedbackType,IsExplicit,Confidence,SourceTrackId,SourceArtist,SourceGenre,
                   CandidateTrackId,CandidateArtist,CandidateGenre,ContextJson,CreatedAtUtc
            FROM NoraFeedbackEvents WHERE ProfileId=$profile ORDER BY CreatedAtUtc,Id;
            """;
        command.Parameters.AddWithValue("$profile", profileId);
        var events = new List<NoraFeedbackEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) events.Add(ReadFeedback(reader));
        return events;
    }

    private static NoraFeedbackEvent ReadFeedback(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), Enum.Parse<NoraFeedbackType>(reader.GetString(2)),
        reader.GetInt32(3) == 1, reader.GetDouble(4), reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        JsonSerializer.Deserialize<NoraFeedbackContext>(reader.GetString(11)) ?? new NoraFeedbackContext(),
        DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture));

    private static void BindFeedback(SqliteCommand command, NoraFeedbackEvent feedback)
    {
        command.Parameters.AddWithValue("$id", feedback.Id);
        command.Parameters.AddWithValue("$profile", feedback.ProfileId);
        command.Parameters.AddWithValue("$type", feedback.Type.ToString());
        command.Parameters.AddWithValue("$explicit", feedback.IsExplicit ? 1 : 0);
        command.Parameters.AddWithValue("$confidence", Math.Clamp(feedback.Confidence, 0d, 1d));
        command.Parameters.AddWithValue("$sourceId", feedback.SourceTrackId);
        command.Parameters.AddWithValue("$sourceArtist", (object?)feedback.SourceArtist ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceGenre", (object?)feedback.SourceGenre ?? DBNull.Value);
        command.Parameters.AddWithValue("$candidateId", feedback.CandidateTrackId);
        command.Parameters.AddWithValue("$candidateArtist", (object?)feedback.CandidateArtist ?? DBNull.Value);
        command.Parameters.AddWithValue("$candidateGenre", (object?)feedback.CandidateGenre ?? DBNull.Value);
        command.Parameters.AddWithValue("$context", JsonSerializer.Serialize(feedback.Context));
        command.Parameters.AddWithValue("$created", DatabaseTime(feedback.CreatedAtUtc));
    }

    private static async Task<NoraDjPreferenceProfile?> ReadProfileAsync(
        SqliteConnection connection, string profileId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,DisplayName,SettingsJson,CreatedAtUtc,UpdatedAtUtc
            FROM NoraDjPreferenceProfiles WHERE Id=$id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(reader.GetString(0), reader.GetString(1),
            JsonSerializer.Deserialize<NoraPersonalizationSettings>(reader.GetString(2)) ?? new(),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static void ValidateProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || profileId.Length > 128)
            throw new ArgumentException("ID profilo Nora non valido.", nameof(profileId));
    }

    private static string DatabaseTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initializationGate.Dispose();
    }

    private sealed record ExportPackage(
        int SchemaVersion,
        NoraDjPreferenceProfile Profile,
        IReadOnlyList<NoraFeedbackEvent> Events);

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS NoraSchemaMigrations (
            Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAtUtc TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS NoraDjPreferenceProfiles (
            Id TEXT PRIMARY KEY, DisplayName TEXT NOT NULL, SettingsJson TEXT NOT NULL,
            FeedbackCount INTEGER NOT NULL DEFAULT 0, SnapshotAtUtc TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS NoraFeedbackEvents (
            Id TEXT PRIMARY KEY, ProfileId TEXT NOT NULL REFERENCES NoraDjPreferenceProfiles(Id) ON DELETE CASCADE,
            FeedbackType TEXT NOT NULL, IsExplicit INTEGER NOT NULL, Confidence REAL NOT NULL CHECK(Confidence BETWEEN 0 AND 1),
            SourceTrackId TEXT NOT NULL, SourceArtist TEXT, SourceGenre TEXT,
            CandidateTrackId TEXT NOT NULL, CandidateArtist TEXT, CandidateGenre TEXT,
            ContextJson TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS NoraLearnedWeights (
            ProfileId TEXT NOT NULL REFERENCES NoraDjPreferenceProfiles(Id) ON DELETE CASCADE,
            FactorKey TEXT NOT NULL, Value REAL NOT NULL, EvidenceCount INTEGER NOT NULL, UpdatedAtUtc TEXT NOT NULL,
            PRIMARY KEY(ProfileId,FactorKey));
        CREATE TABLE IF NOT EXISTS NoraArtistPreferences (
            ProfileId TEXT NOT NULL REFERENCES NoraDjPreferenceProfiles(Id) ON DELETE CASCADE,
            Artist TEXT NOT NULL, Value REAL NOT NULL, EvidenceCount INTEGER NOT NULL, UpdatedAtUtc TEXT NOT NULL,
            PRIMARY KEY(ProfileId,Artist));
        CREATE TABLE IF NOT EXISTS NoraCombinationPreferences (
            ProfileId TEXT NOT NULL REFERENCES NoraDjPreferenceProfiles(Id) ON DELETE CASCADE,
            SourceArtist TEXT NOT NULL, CandidateArtist TEXT NOT NULL,
            Value REAL NOT NULL, EvidenceCount INTEGER NOT NULL, UpdatedAtUtc TEXT NOT NULL,
            PRIMARY KEY(ProfileId,SourceArtist,CandidateArtist));
        CREATE INDEX IF NOT EXISTS IX_NoraFeedback_ProfileCreated
            ON NoraFeedbackEvents(ProfileId,CreatedAtUtc);
        INSERT OR IGNORE INTO NoraSchemaMigrations (Version,Name,AppliedAtUtc)
            VALUES (1,'initial-personalization',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
        """;
}
