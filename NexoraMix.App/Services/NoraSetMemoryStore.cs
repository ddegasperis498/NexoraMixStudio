using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexoraMix.Core.Services;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace NexoraMix.App.Services;

public sealed class NoraSetMemoryStore : IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public NoraSetMemoryStore(string? databasePath = null)
    {
        _databasePath = IOPath.GetFullPath(databasePath ?? IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexoraMix", "Nora", "nora-set-memory.db"));
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
            var version = await UserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version > CurrentSchemaVersion)
                throw new InvalidOperationException($"Schema memoria set Nora {version} non supportato.");
            if (version < 1)
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = SchemaVersion1;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<NoraSetSession> StartSessionAsync(
        string profileId,
        NoraSetPhase phase,
        NoraSetTrajectory? trajectory = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("Profilo DJ richiesto.", nameof(profileId));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var session = new NoraSetSession(
            Guid.NewGuid().ToString("N"), profileId, NoraSetSessionStatus.Active, phase,
            trajectory ?? new NoraSetTrajectory { TargetPhase = phase }, DateTimeOffset.UtcNow);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = (SqliteTransaction)transaction;
            check.CommandText = """
                SELECT COUNT(*) FROM SetSession
                WHERE ProfileId=$profile AND Status IN ('Active','Paused');
                """;
            check.Parameters.AddWithValue("$profile", profileId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException("Esiste già un set attivo o sospeso per questo profilo.");
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO SetSession
                    (Id,ProfileId,Status,Phase,TrajectoryJson,StartedAtUtc,PausedAtUtc,ResumedAtUtc,ClosedAtUtc)
                VALUES ($id,$profile,$status,$phase,$trajectory,$started,NULL,NULL,NULL);
                """;
            BindSession(insert, session);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public Task PauseSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(sessionId, NoraSetSessionStatus.Active, NoraSetSessionStatus.Paused, "PausedAtUtc", cancellationToken);

    public Task ResumeSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(sessionId, NoraSetSessionStatus.Paused, NoraSetSessionStatus.Active, "ResumedAtUtc", cancellationToken);

    public async Task CloseSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SetSession SET Status='Closed', ClosedAtUtc=$now
            WHERE Id=$id AND Status IN ('Active','Paused');
            """;
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$now", DatabaseTime(DateTimeOffset.UtcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Set non attivo, sospeso o inesistente.");
    }

    public Task RecordLoadedTrackAsync(NoraSetSessionTrack track, CancellationToken cancellationToken = default) =>
        UpsertTrackAsync(track, requirePlayed: false, cancellationToken);

    public Task RecordPlayedTrackAsync(NoraSetSessionTrack track, CancellationToken cancellationToken = default) =>
        UpsertTrackAsync(track, requirePlayed: true, cancellationToken);

    public async Task RecordTransitionAsync(NoraSetTransition transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await RequireOpenSessionAsync(connection, (SqliteTransaction)transaction, transition.SessionId, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO SetTransition
                (Id,SessionId,OutgoingTrackId,IncomingTrackId,StartedAtUtc,CompletedAtUtc,
                 TransitionType,DurationBeats,PitchPercent,BassSwapBeat,VocalRisk,Succeeded)
            VALUES ($id,$session,$outgoing,$incoming,$started,$completed,$type,$beats,$pitch,$bass,$vocal,$succeeded);
            """;
        command.Parameters.AddWithValue("$id", transition.Id);
        command.Parameters.AddWithValue("$session", transition.SessionId);
        command.Parameters.AddWithValue("$outgoing", DbValue(transition.OutgoingTrackId));
        command.Parameters.AddWithValue("$incoming", DbValue(transition.IncomingTrackId));
        command.Parameters.AddWithValue("$started", DatabaseTime(transition.StartedAtUtc));
        command.Parameters.AddWithValue("$completed", DbTime(transition.CompletedAtUtc));
        command.Parameters.AddWithValue("$type", DbValue(transition.TransitionType));
        command.Parameters.AddWithValue("$beats", DbValue(transition.DurationBeats));
        command.Parameters.AddWithValue("$pitch", DbValue(transition.PitchPercent));
        command.Parameters.AddWithValue("$bass", DbValue(transition.BassSwapBeat));
        command.Parameters.AddWithValue("$vocal", DbValue(transition.VocalRisk));
        command.Parameters.AddWithValue("$succeeded", DbBool(transition.Succeeded));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task RecordRecommendationAsync(
        NoraSetRecommendation recommendation, CancellationToken cancellationToken = default) =>
        InsertSimpleAsync(recommendation.SessionId,
            """
            INSERT INTO SetRecommendation
                (Id,SessionId,CurrentTrackId,CandidateTrackId,Position,Score,WasLoaded,WasPlayed,CreatedAtUtc)
            VALUES ($id,$session,$current,$candidate,$position,$score,$loaded,$played,$created);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", recommendation.Id);
                command.Parameters.AddWithValue("$session", recommendation.SessionId);
                command.Parameters.AddWithValue("$current", recommendation.CurrentTrackId);
                command.Parameters.AddWithValue("$candidate", recommendation.CandidateTrackId);
                command.Parameters.AddWithValue("$position", recommendation.Position);
                command.Parameters.AddWithValue("$score", recommendation.Score);
                command.Parameters.AddWithValue("$loaded", DbBool(recommendation.WasLoaded));
                command.Parameters.AddWithValue("$played", DbBool(recommendation.WasPlayed));
                command.Parameters.AddWithValue("$created", DatabaseTime(recommendation.CreatedAtUtc));
            }, cancellationToken);

    public Task RecordFeedbackAsync(NoraSetFeedback feedback, CancellationToken cancellationToken = default) =>
        InsertSimpleAsync(feedback.SessionId,
            """
            INSERT INTO SetFeedback
                (Id,SessionId,RecommendationId,FeedbackType,IsExplicit,Confidence,CreatedAtUtc)
            VALUES ($id,$session,$recommendation,$type,$explicit,$confidence,$created);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", feedback.Id);
                command.Parameters.AddWithValue("$session", feedback.SessionId);
                command.Parameters.AddWithValue("$recommendation", DbValue(feedback.RecommendationId));
                command.Parameters.AddWithValue("$type", feedback.Type.ToString());
                command.Parameters.AddWithValue("$explicit", feedback.IsExplicit ? 1 : 0);
                command.Parameters.AddWithValue("$confidence", Math.Clamp(feedback.Confidence, 0d, 1d));
                command.Parameters.AddWithValue("$created", DatabaseTime(feedback.CreatedAtUtc));
            }, cancellationToken);

    public async Task<NoraSetMemorySnapshot?> GetActiveSnapshotAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id FROM SetSession WHERE ProfileId=$profile AND Status IN ('Active','Paused')
            ORDER BY StartedAtUtc DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profile", profileId);
        var id = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(id) ? null : await LoadSnapshotAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoraSetMemorySnapshot> LoadSnapshotAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var session = await ReadSessionAsync(connection, sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Sessione DJ non trovata.");
        var tracks = await ReadTracksAsync(connection, sessionId, cancellationToken).ConfigureAwait(false);
        var transitions = await ReadTransitionsAsync(connection, sessionId, cancellationToken).ConfigureAwait(false);
        var recommendations = await ReadRecommendationsAsync(connection, sessionId, cancellationToken).ConfigureAwait(false);
        return NoraSetMemory.CreateSnapshot(session, tracks, transitions, recommendations);
    }

    private async Task UpsertTrackAsync(
        NoraSetSessionTrack track,
        bool requirePlayed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (requirePlayed && !track.PlayedAtUtc.HasValue)
            throw new ArgumentException("PlayedAtUtc richiesto per una traccia suonata.", nameof(track));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await RequireOpenSessionAsync(connection, (SqliteTransaction)transaction, track.SessionId, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = TrackUpsertSql;
        BindTrack(command, track);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertSimpleAsync(
        string sessionId,
        string sql,
        Action<SqliteCommand> bind,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await RequireOpenSessionAsync(connection, (SqliteTransaction)transaction, sessionId, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        bind(command);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ChangeStatusAsync(
        string sessionId,
        NoraSetSessionStatus expected,
        NoraSetSessionStatus next,
        string timestampColumn,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE SetSession SET Status=$next, {timestampColumn}=$now WHERE Id=$id AND Status=$expected;";
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$expected", expected.ToString());
        command.Parameters.AddWithValue("$next", next.ToString());
        command.Parameters.AddWithValue("$now", DatabaseTime(DateTimeOffset.UtcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException($"Transizione sessione {expected} → {next} non valida.");
    }

    private static async Task RequireOpenSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Status FROM SetSession WHERE Id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", sessionId);
        var status = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (!string.Equals(status, NoraSetSessionStatus.Active.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("La sessione deve essere attiva per registrare eventi.");
    }

    private static async Task<NoraSetSession?> ReadSessionAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,ProfileId,Status,Phase,TrajectoryJson,StartedAtUtc,PausedAtUtc,ResumedAtUtc,ClosedAtUtc
            FROM SetSession WHERE Id=$id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(
            reader.GetString(0), reader.GetString(1), Enum.Parse<NoraSetSessionStatus>(reader.GetString(2)),
            Enum.Parse<NoraSetPhase>(reader.GetString(3)),
            JsonSerializer.Deserialize<NoraSetTrajectory>(reader.GetString(4)) ?? new(),
            ParseTime(reader.GetString(5)), NullableTime(reader, 6), NullableTime(reader, 7), NullableTime(reader, 8));
    }

    private static async Task<IReadOnlyList<NoraSetSessionTrack>> ReadTracksAsync(
        SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = TrackSelectSql + " WHERE SessionId=$session ORDER BY Sequence DESC, PlayedAtUtc DESC LIMIT 100;";
        command.Parameters.AddWithValue("$session", sessionId);
        var result = new List<NoraSetSessionTrack>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadTrack(reader));
        return result;
    }

    private static async Task<IReadOnlyList<NoraSetTransition>> ReadTransitionsAsync(
        SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,SessionId,OutgoingTrackId,IncomingTrackId,StartedAtUtc,CompletedAtUtc,
                   TransitionType,DurationBeats,PitchPercent,BassSwapBeat,VocalRisk,Succeeded
            FROM SetTransition WHERE SessionId=$session ORDER BY StartedAtUtc DESC LIMIT 100;
            """;
        command.Parameters.AddWithValue("$session", sessionId);
        var result = new List<NoraSetTransition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), reader.GetString(1), Text(reader, 2), Text(reader, 3),
                ParseTime(reader.GetString(4)), NullableTime(reader, 5), Text(reader, 6), Integer(reader, 7),
                Number(reader, 8), Integer(reader, 9), Text(reader, 10), Boolean(reader, 11)));
        return result;
    }

    private static async Task<IReadOnlyList<NoraSetRecommendation>> ReadRecommendationsAsync(
        SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,SessionId,CurrentTrackId,CandidateTrackId,Position,Score,WasLoaded,WasPlayed,CreatedAtUtc
            FROM SetRecommendation WHERE SessionId=$session ORDER BY CreatedAtUtc DESC LIMIT 100;
            """;
        command.Parameters.AddWithValue("$session", sessionId);
        var result = new List<NoraSetRecommendation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetDouble(5), Boolean(reader, 6), Boolean(reader, 7), ParseTime(reader.GetString(8))));
        return result;
    }

    private static NoraSetSessionTrack ReadTrack(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        Text(reader, 5), reader.GetInt32(6), NullableTime(reader, 7), NullableTime(reader, 8), NullableTime(reader, 9),
        Text(reader, 10), Number(reader, 11), Number(reader, 12), Number(reader, 13), Number(reader, 14), Text(reader, 15),
        reader.IsDBNull(16) ? null : Enum.Parse<NexoraMix.Core.Models.VocalPresence>(reader.GetString(16)),
        Boolean(reader, 17), Number(reader, 18), Number(reader, 19), Text(reader, 20), Number(reader, 21), Number(reader, 22),
        Boolean(reader, 23));

    private static void BindSession(SqliteCommand command, NoraSetSession session)
    {
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$profile", session.ProfileId);
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$phase", session.Phase.ToString());
        command.Parameters.AddWithValue("$trajectory", JsonSerializer.Serialize(session.Trajectory));
        command.Parameters.AddWithValue("$started", DatabaseTime(session.StartedAtUtc));
    }

    private static void BindTrack(SqliteCommand command, NoraSetSessionTrack track)
    {
        command.Parameters.AddWithValue("$id", track.Id);
        command.Parameters.AddWithValue("$session", track.SessionId);
        command.Parameters.AddWithValue("$track", track.TrackId);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$genre", DbValue(track.Genre));
        command.Parameters.AddWithValue("$sequence", track.Sequence);
        command.Parameters.AddWithValue("$loaded", DbTime(track.LoadedAtUtc));
        command.Parameters.AddWithValue("$played", DbTime(track.PlayedAtUtc));
        command.Parameters.AddWithValue("$stopped", DbTime(track.StoppedAtUtc));
        command.Parameters.AddWithValue("$deck", DbValue(track.DeckId));
        command.Parameters.AddWithValue("$duration", DbValue(track.EffectiveDurationSeconds));
        command.Parameters.AddWithValue("$bpm", DbValue(track.EffectiveBpm));
        command.Parameters.AddWithValue("$pitch", DbValue(track.PitchPercent));
        command.Parameters.AddWithValue("$energy", DbValue(track.Energy));
        command.Parameters.AddWithValue("$camelot", DbValue(track.CamelotKey));
        command.Parameters.AddWithValue("$vocal", DbValue(track.VocalPresence?.ToString()));
        command.Parameters.AddWithValue("$suggested", DbBool(track.WasNoraSuggested));
        command.Parameters.AddWithValue("$entry", DbValue(track.EntryTimeSeconds));
        command.Parameters.AddWithValue("$exit", DbValue(track.ExitTimeSeconds));
        command.Parameters.AddWithValue("$audible", DbValue(track.AudibleDeckId));
        command.Parameters.AddWithValue("$crossfader", DbValue(track.CrossfaderPosition));
        command.Parameters.AddWithValue("$volume", DbValue(track.DeckVolume));
        command.Parameters.AddWithValue("$cue", DbBool(track.CueActive));
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

    private static async Task<int> UserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static string DatabaseTime(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static object DbTime(DateTimeOffset? value) => value.HasValue ? DatabaseTime(value.Value) : DBNull.Value;
    private static object DbValue(object? value) => value ?? DBNull.Value;
    private static object DbBool(bool? value) => value.HasValue ? value.Value ? 1 : 0 : DBNull.Value;
    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static DateTimeOffset? NullableTime(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseTime(reader.GetString(ordinal));
    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static double? Number(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    private static int? Integer(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static bool? Boolean(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal) == 1;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initializationGate.Dispose();
    }

    private const string TrackSelectSql = """
        SELECT Id,SessionId,TrackId,Title,Artist,Genre,Sequence,LoadedAtUtc,PlayedAtUtc,StoppedAtUtc,
               DeckId,EffectiveDurationSeconds,EffectiveBpm,PitchPercent,Energy,CamelotKey,VocalPresence,
               WasNoraSuggested,EntryTimeSeconds,ExitTimeSeconds,AudibleDeckId,CrossfaderPosition,DeckVolume,CueActive
        FROM SetSessionTrack
        """;

    private const string TrackUpsertSql = """
        INSERT INTO SetSessionTrack
            (Id,SessionId,TrackId,Title,Artist,Genre,Sequence,LoadedAtUtc,PlayedAtUtc,StoppedAtUtc,
             DeckId,EffectiveDurationSeconds,EffectiveBpm,PitchPercent,Energy,CamelotKey,VocalPresence,
             WasNoraSuggested,EntryTimeSeconds,ExitTimeSeconds,AudibleDeckId,CrossfaderPosition,DeckVolume,CueActive)
        VALUES ($id,$session,$track,$title,$artist,$genre,$sequence,$loaded,$played,$stopped,$deck,$duration,$bpm,
                $pitch,$energy,$camelot,$vocal,$suggested,$entry,$exit,$audible,$crossfader,$volume,$cue)
        ON CONFLICT(Id) DO UPDATE SET
            PlayedAtUtc=COALESCE(excluded.PlayedAtUtc,SetSessionTrack.PlayedAtUtc),
            StoppedAtUtc=COALESCE(excluded.StoppedAtUtc,SetSessionTrack.StoppedAtUtc),
            DeckId=COALESCE(excluded.DeckId,SetSessionTrack.DeckId),
            EffectiveDurationSeconds=COALESCE(excluded.EffectiveDurationSeconds,SetSessionTrack.EffectiveDurationSeconds),
            EffectiveBpm=COALESCE(excluded.EffectiveBpm,SetSessionTrack.EffectiveBpm),
            PitchPercent=COALESCE(excluded.PitchPercent,SetSessionTrack.PitchPercent),
            Energy=COALESCE(excluded.Energy,SetSessionTrack.Energy), CamelotKey=COALESCE(excluded.CamelotKey,SetSessionTrack.CamelotKey),
            VocalPresence=COALESCE(excluded.VocalPresence,SetSessionTrack.VocalPresence),
            WasNoraSuggested=COALESCE(excluded.WasNoraSuggested,SetSessionTrack.WasNoraSuggested),
            EntryTimeSeconds=COALESCE(excluded.EntryTimeSeconds,SetSessionTrack.EntryTimeSeconds),
            ExitTimeSeconds=COALESCE(excluded.ExitTimeSeconds,SetSessionTrack.ExitTimeSeconds),
            AudibleDeckId=COALESCE(excluded.AudibleDeckId,SetSessionTrack.AudibleDeckId),
            CrossfaderPosition=COALESCE(excluded.CrossfaderPosition,SetSessionTrack.CrossfaderPosition),
            DeckVolume=COALESCE(excluded.DeckVolume,SetSessionTrack.DeckVolume), CueActive=COALESCE(excluded.CueActive,SetSessionTrack.CueActive);
        """;

    private const string SchemaVersion1 = """
        CREATE TABLE SetSession (
            Id TEXT PRIMARY KEY, ProfileId TEXT NOT NULL, Status TEXT NOT NULL, Phase TEXT NOT NULL,
            TrajectoryJson TEXT NOT NULL, StartedAtUtc TEXT NOT NULL, PausedAtUtc TEXT, ResumedAtUtc TEXT, ClosedAtUtc TEXT);
        CREATE TABLE SetSessionTrack (
            Id TEXT PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES SetSession(Id) ON DELETE CASCADE,
            TrackId TEXT NOT NULL, Title TEXT NOT NULL, Artist TEXT NOT NULL, Genre TEXT, Sequence INTEGER NOT NULL,
            LoadedAtUtc TEXT, PlayedAtUtc TEXT, StoppedAtUtc TEXT, DeckId TEXT, EffectiveDurationSeconds REAL,
            EffectiveBpm REAL, PitchPercent REAL, Energy REAL, CamelotKey TEXT, VocalPresence TEXT,
            WasNoraSuggested INTEGER, EntryTimeSeconds REAL, ExitTimeSeconds REAL, AudibleDeckId TEXT,
            CrossfaderPosition REAL, DeckVolume REAL, CueActive INTEGER);
        CREATE TABLE SetTransition (
            Id TEXT PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES SetSession(Id) ON DELETE CASCADE,
            OutgoingTrackId TEXT, IncomingTrackId TEXT, StartedAtUtc TEXT NOT NULL, CompletedAtUtc TEXT,
            TransitionType TEXT, DurationBeats INTEGER, PitchPercent REAL, BassSwapBeat INTEGER, VocalRisk TEXT, Succeeded INTEGER);
        CREATE TABLE SetRecommendation (
            Id TEXT PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES SetSession(Id) ON DELETE CASCADE,
            CurrentTrackId TEXT NOT NULL, CandidateTrackId TEXT NOT NULL, Position INTEGER NOT NULL,
            Score REAL NOT NULL, WasLoaded INTEGER, WasPlayed INTEGER, CreatedAtUtc TEXT NOT NULL);
        CREATE TABLE SetFeedback (
            Id TEXT PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES SetSession(Id) ON DELETE CASCADE,
            RecommendationId TEXT, FeedbackType TEXT NOT NULL, IsExplicit INTEGER NOT NULL,
            Confidence REAL NOT NULL CHECK(Confidence BETWEEN 0 AND 1), CreatedAtUtc TEXT NOT NULL);
        CREATE INDEX IX_SetSession_ProfileStatus ON SetSession(ProfileId,Status,StartedAtUtc DESC);
        CREATE INDEX IX_SetSessionTrack_SessionPlayed ON SetSessionTrack(SessionId,PlayedAtUtc DESC,Sequence DESC);
        CREATE INDEX IX_SetTransition_SessionStarted ON SetTransition(SessionId,StartedAtUtc DESC);
        CREATE INDEX IX_SetRecommendation_SessionCreated ON SetRecommendation(SessionId,CreatedAtUtc DESC);
        CREATE INDEX IX_SetFeedback_SessionCreated ON SetFeedback(SessionId,CreatedAtUtc DESC);
        PRAGMA user_version=1;
        """;
}
