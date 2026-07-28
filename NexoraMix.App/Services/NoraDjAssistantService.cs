using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Nexora.AI.Embedded;
using Nexora.MusicIntelligence.Catalog;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;

namespace NexoraMix.App.Services;

public sealed record NoraDjRecommendation(
    NoraTrackMatch Match,
    AudioTrack? LocalTrack)
{
    public int Position { get; init; }
    public string Title => Match.Track.Title;
    public string Artist => Match.Track.Artist;
    public string Bpm => Match.Track.Bpm is > 0 ? $"{Match.Track.Bpm:0.0}" : "—";
    public string Year => Match.Track.Year?.ToString() ?? "—";
    public string Genre => string.IsNullOrWhiteSpace(Match.Track.Genre) ? "—" : Match.Track.Genre;
    public string Compatibility => $"{Match.Score:0}%";
    public string Explanation => Match.Explanation;
    public bool IsAvailableLocally => LocalTrack?.CanLoadToDeck == true;
    public string Availability => IsAvailableLocally ? "PRONTA" : "NEL CATALOGO";
}

public sealed record NoraDjRecommendationResult(
    IReadOnlyList<NoraDjRecommendation> Recommendations,
    int CatalogTrackCount,
    int SharedTeachingCount,
    string CatalogPath);

public sealed class NoraDjAssistantService : IDisposable
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private NexoraEmbeddedRuntime? _runtime;

    public async Task<NoraDjRecommendationResult> RecommendAsync(
        AudioTrack currentTrack,
        IReadOnlyCollection<AudioTrack> localLibrary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentTrack);
        ArgumentNullException.ThrowIfNull(localLibrary);

        var catalogPath = MusicCatalogOptions.CreateDefault().DatabasePath;
        var catalogTracks = await ReadCatalogAsync(catalogPath, cancellationToken).ConfigureAwait(false);
        var localById = localLibrary
            .Where(track => !string.IsNullOrWhiteSpace(track.FilePath))
            .GroupBy(track => TrackId(track.FilePath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var currentId = !string.IsNullOrWhiteSpace(currentTrack.FilePath)
            ? TrackId(currentTrack.FilePath)
            : "mix:" + currentTrack.Id.ToString("N");
        var catalogCurrent = catalogTracks.FirstOrDefault(track =>
            string.Equals(track.Id, currentId, StringComparison.OrdinalIgnoreCase));
        var current = catalogCurrent ?? ToProfile(currentTrack, currentId);

        var candidates = catalogTracks.ToList();
        var catalogIds = candidates.Select(track => track.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        candidates.AddRange(localLibrary
            .Where(track => !ReferenceEquals(track, currentTrack))
            .Select(track => ToProfile(track, !string.IsNullOrWhiteSpace(track.FilePath)
                ? TrackId(track.FilePath)
                : "mix:" + track.Id.ToString("N")))
            .Where(track => !catalogIds.Contains(track.Id)));

        var distinctCandidates = candidates.Where(candidate =>
            !string.Equals(candidate.Title.Trim(), current.Title.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.Artist.Trim(), current.Artist.Trim(), StringComparison.OrdinalIgnoreCase));
        var recommendations = NoraTrackCompatibility.Rank(current, distinctCandidates, 8)
            .Select((match, index) => new NoraDjRecommendation(
                match,
                localById.GetValueOrDefault(match.Track.Id))
            {
                Position = index + 1
            })
            .ToArray();

        var runtime = await GetRuntimeAsync(cancellationToken).ConfigureAwait(false);
        var teachings = await runtime.SharedLearning.ListAsync("music", 200, cancellationToken).ConfigureAwait(false);
        return new(recommendations, catalogTracks.Count, teachings.Count, catalogPath);
    }

    private async Task<NexoraEmbeddedRuntime> GetRuntimeAsync(CancellationToken cancellationToken)
    {
        if (_runtime is not null) return _runtime;
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runtime is not null) return _runtime;
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexora", "AI");
            _runtime = await NexoraEmbeddedRuntime.CreateAsync(root, cancellationToken).ConfigureAwait(false);
            return _runtime;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task<IReadOnlyList<NoraTrackProfile>> ReadCatalogAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath)) return [];
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Artist, Bpm, Year, Genre
            FROM Tracks
            WHERE NULLIF(TRIM(Title), '') IS NOT NULL
            ORDER BY UpdatedAtUtc DESC;
            """;
        var tracks = new List<NoraTrackProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tracks.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "Artista sconosciuto" : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return tracks;
    }

    private static NoraTrackProfile ToProfile(AudioTrack track, string id) =>
        new(id, track.Title, track.Artist, track.Bpm > 0 ? track.Bpm : null, null, null);

    private static string TrackId(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(path).ToUpperInvariant())));

    public void Dispose()
    {
        _runtime?.Dispose();
        _initializationGate.Dispose();
    }
}
