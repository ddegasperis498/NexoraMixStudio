using System.Net.Http;
using System.Text;
using System.Text.Json;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;

namespace NexoraMix.App.Services;

public sealed class SpotifyApiService
{
    private readonly HttpClient _http = new();
    private readonly SpotifyAuthService _auth;

    public SpotifyApiService(SpotifyAuthService auth) => _auth = auth;

    public async Task<IReadOnlyList<AudioTrack>> ImportAsync(string link, CancellationToken cancellationToken = default)
    {
        if (!SpotifyLinkParser.TryParse(link, out var resource))
            throw new InvalidOperationException("Il testo non contiene un link Spotify valido a traccia, album o playlist.");

        if (!_auth.IsConnected)
            return new[] { await ImportViaOEmbedAsync(resource, cancellationToken) };

        return resource.Type switch
        {
            SpotifyResourceType.Track => new[] { await GetTrackAsync(resource.Id, cancellationToken) },
            SpotifyResourceType.Album => await GetAlbumTracksAsync(resource.Id, cancellationToken),
            SpotifyResourceType.Playlist => await GetPlaylistTracksAsync(resource.Id, cancellationToken),
            _ => throw new NotSupportedException("Tipo di collegamento Spotify non supportato.")
        };
    }

    public async Task StartPlaybackAsync(AudioTrack track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (string.IsNullOrWhiteSpace(track.SpotifyId))
            throw new InvalidOperationException("La traccia non contiene un identificativo Spotify valido.");

        using var request = await _auth.CreateAuthorizedRequestAsync(
            HttpMethod.Put,
            "https://api.spotify.com/v1/me/player/play",
            cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            uris = new[] { $"spotify:track:{track.SpotifyId}" },
            position_ms = 0
        });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsurePlaybackResponseAsync(response, "avviare la riproduzione", cancellationToken);
    }

    public async Task PausePlaybackAsync(CancellationToken cancellationToken = default)
    {
        using var request = await _auth.CreateAuthorizedRequestAsync(
            HttpMethod.Put,
            "https://api.spotify.com/v1/me/player/pause",
            cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsurePlaybackResponseAsync(response, "mettere in pausa la riproduzione", cancellationToken);
    }

    private static async Task EnsurePlaybackResponseAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = string.IsNullOrWhiteSpace(payload) ? string.Empty : $" Dettaglio: {payload}";

        throw response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => new InvalidOperationException(
                $"Spotify non trova un dispositivo attivo per {operation}. Apri Spotify sul PC o sul telefono, avvia una traccia e riprova.{detail}"),
            System.Net.HttpStatusCode.Forbidden => new InvalidOperationException(
                $"Spotify ha rifiutato il comando. Verifica Premium, permessi dell'app e nuovo accesso OAuth.{detail}"),
            System.Net.HttpStatusCode.Unauthorized => new InvalidOperationException(
                $"La sessione Spotify non è più valida. Disconnetti e connetti nuovamente l'account.{detail}"),
            System.Net.HttpStatusCode.TooManyRequests => new InvalidOperationException(
                $"Spotify ha limitato temporaneamente le richieste. Attendi alcuni secondi e riprova.{detail}"),
            _ => new InvalidOperationException(
                $"Impossibile {operation} su Spotify (HTTP {(int)response.StatusCode}).{detail}")
        };
    }

    private async Task<AudioTrack> ImportViaOEmbedAsync(SpotifyResource resource, CancellationToken cancellationToken)
    {
        var url = $"https://open.spotify.com/oembed?url={Uri.EscapeDataString(resource.CanonicalUrl)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var title = json.RootElement.TryGetProperty("title", out var titleNode) ? titleNode.GetString() : "Contenuto Spotify";
        return new AudioTrack
        {
            SourceKind = TrackSourceKind.SpotifyReference,
            SpotifyId = resource.Type == SpotifyResourceType.Track ? resource.Id : null,
            Title = title ?? "Contenuto Spotify",
            Artist = resource.Type.ToString(),
            ExternalUrl = resource.CanonicalUrl,
            AnalysisStatus = resource.Type == SpotifyResourceType.Track
                ? "Spotify esterno: caricabile nel deck; sync, EQ e crossfader non disponibili"
                : "Connetti Spotify per espandere album o playlist nelle singole tracce"
        };
    }

    private async Task<AudioTrack> GetTrackAsync(string id, CancellationToken cancellationToken)
    {
        using var request = await _auth.CreateAuthorizedRequestAsync(HttpMethod.Get, $"https://api.spotify.com/v1/tracks/{id}", cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(payload);
        return MapTrack(json.RootElement);
    }

    private async Task<IReadOnlyList<AudioTrack>> GetAlbumTracksAsync(string id, CancellationToken cancellationToken)
    {
        var result = new List<AudioTrack>();
        var url = $"https://api.spotify.com/v1/albums/{id}/tracks?limit=50";
        while (!string.IsNullOrWhiteSpace(url))
        {
            using var request = await _auth.CreateAuthorizedRequestAsync(HttpMethod.Get, url, cancellationToken);
            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(payload);
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray()) result.Add(MapTrack(item));
            url = json.RootElement.TryGetProperty("next", out var next) && next.ValueKind != JsonValueKind.Null ? next.GetString() ?? string.Empty : string.Empty;
        }
        return result;
    }

    private async Task<IReadOnlyList<AudioTrack>> GetPlaylistTracksAsync(string id, CancellationToken cancellationToken)
    {
        var result = new List<AudioTrack>();
        var url = $"https://api.spotify.com/v1/playlists/{id}/items?limit=50";
        while (!string.IsNullOrWhiteSpace(url))
        {
            using var request = await _auth.CreateAuthorizedRequestAsync(HttpMethod.Get, url, cancellationToken);
            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Spotify non consente di leggere questa playlist ({(int)response.StatusCode}). In Developer Mode potrebbe dover essere tua o condivisa con te.");

            using var json = JsonDocument.Parse(payload);
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                if (item.TryGetProperty("item", out var track) && track.ValueKind == JsonValueKind.Object)
                    result.Add(MapTrack(track));
                else if (item.TryGetProperty("track", out track) && track.ValueKind == JsonValueKind.Object)
                    result.Add(MapTrack(track));
            }
            url = json.RootElement.TryGetProperty("next", out var next) && next.ValueKind != JsonValueKind.Null ? next.GetString() ?? string.Empty : string.Empty;
        }
        return result;
    }

    private static AudioTrack MapTrack(JsonElement item)
    {
        var title = item.TryGetProperty("name", out var name) ? name.GetString() ?? "Traccia Spotify" : "Traccia Spotify";
        var artist = "Spotify";
        if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
            artist = string.Join(", ", artists.EnumerateArray().Select(a => a.GetProperty("name").GetString()).Where(n => !string.IsNullOrWhiteSpace(n)));

        var id = item.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
        var duration = item.TryGetProperty("duration_ms", out var durationNode) ? durationNode.GetInt32() / 1000d : 0;
        var externalUrl = id is null ? null : $"https://open.spotify.com/track/{id}";
        if (item.TryGetProperty("external_urls", out var external) && external.TryGetProperty("spotify", out var spotifyUrl))
            externalUrl = spotifyUrl.GetString();

        string? albumName = null;
        string? artworkUrl = null;
        if (item.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
        {
            if (album.TryGetProperty("name", out var albumNameNode)) albumName = albumNameNode.GetString();
            if (album.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                var image = images.EnumerateArray().FirstOrDefault();
                if (image.ValueKind == JsonValueKind.Object && image.TryGetProperty("url", out var imageUrl))
                    artworkUrl = imageUrl.GetString();
            }
        }

        string? isrc = null;
        if (item.TryGetProperty("external_ids", out var externalIds) &&
            externalIds.ValueKind == JsonValueKind.Object &&
            externalIds.TryGetProperty("isrc", out var isrcNode))
            isrc = isrcNode.GetString();

        return new AudioTrack
        {
            SourceKind = TrackSourceKind.SpotifyReference,
            SpotifyId = id,
            Title = title,
            Artist = artist,
            Album = albumName,
            ArtworkUrl = artworkUrl,
            Isrc = isrc,
            DurationSeconds = duration,
            ExternalUrl = externalUrl,
            AnalysisStatus = "Spotify esterno: PLAY controlla il dispositivo Spotify; collega un file locale per il mix completo"
        };
    }
}
