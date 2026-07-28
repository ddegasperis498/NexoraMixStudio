using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetFormUrlEncodedContent = System.Net.Http.FormUrlEncodedContent;
using NetHttpClient = System.Net.Http.HttpClient;
using NetHttpMethod = System.Net.Http.HttpMethod;
using NetHttpRequestMessage = System.Net.Http.HttpRequestMessage;

namespace NexoraMix.App.Services;

public sealed class SpotifyAuthService
{
    private const string RedirectUri = "http://127.0.0.1:5543/callback/";
    private readonly NetHttpClient _http = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _clientId;
    private string? _refreshToken;

    public string? AccessToken { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsConnected => !string.IsNullOrWhiteSpace(AccessToken) &&
                               (ExpiresAt > DateTimeOffset.Now.AddSeconds(20) || !string.IsNullOrWhiteSpace(_refreshToken));

    public async Task LoginAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Inserisci prima il Client ID Spotify nelle impostazioni.");

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var scopes = Uri.EscapeDataString("playlist-read-private playlist-read-collaborative user-read-playback-state user-modify-playback-state");
        var authorizeUrl = "https://accounts.spotify.com/authorize" +
                           $"?client_id={Uri.EscapeDataString(clientId)}" +
                           "&response_type=code" +
                           $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                           $"&scope={scopes}" +
                           $"&code_challenge_method=S256&code_challenge={challenge}&state={state}";

        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        Process.Start(new ProcessStartInfo(authorizeUrl)
        {
            UseShellExecute = true
        });

        using var registration = cancellationToken.Register(() =>
        {
            if (listener.IsListening)
                listener.Stop();
        });

        var context = await listener.GetContextAsync();
        var query = context.Request.QueryString;
        var returnedState = query["state"];
        var code = query["code"];
        var error = query["error"];

        var html = string.IsNullOrWhiteSpace(error)
            ? "<html><body style='background:#090b12;color:white;font-family:Segoe UI;padding:40px'><h2>Nexora Mix connesso</h2><p>Puoi chiudere questa finestra e tornare all'app.</p></body></html>"
            : "<html><body><h2>Accesso annullato</h2></body></html>";

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();

        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"Spotify ha rifiutato l'accesso: {error}");

        if (!string.Equals(state, returnedState, StringComparison.Ordinal))
            throw new InvalidOperationException("Risposta Spotify non valida: state OAuth differente.");

        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Spotify non ha restituito il codice di autorizzazione.");

        _clientId = clientId;

        using var request = new NetHttpRequestMessage(
            NetHttpMethod.Post,
            "https://accounts.spotify.com/api/token")
        {
            Content = new NetFormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = verifier
            })
        };

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(payload);
        ApplyTokenResponse(json.RootElement);
    }

    public async Task<NetHttpRequestMessage> CreateAuthorizedRequestAsync(
        NetHttpMethod method,
        string url,
        CancellationToken cancellationToken = default)
    {
        await EnsureValidAccessTokenAsync(cancellationToken);
        var request = new NetHttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return request;
    }

    private async Task EnsureValidAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(AccessToken) && ExpiresAt > DateTimeOffset.Now.AddSeconds(45)) return;
        if (string.IsNullOrWhiteSpace(_refreshToken) || string.IsNullOrWhiteSpace(_clientId))
            throw new InvalidOperationException("La sessione Spotify è scaduta. Esegui nuovamente CONNETTI.");

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(AccessToken) && ExpiresAt > DateTimeOffset.Now.AddSeconds(45)) return;

            using var request = new NetHttpRequestMessage(NetHttpMethod.Post, "https://accounts.spotify.com/api/token")
            {
                Content = new NetFormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _clientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = _refreshToken
                })
            };

            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(payload);
            ApplyTokenResponse(json.RootElement);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void ApplyTokenResponse(JsonElement root)
    {
        AccessToken = root.GetProperty("access_token").GetString();
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        ExpiresAt = DateTimeOffset.Now.AddSeconds(expiresIn);
        if (root.TryGetProperty("refresh_token", out var refreshTokenNode) &&
            refreshTokenNode.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(refreshTokenNode.GetString()))
        {
            _refreshToken = refreshTokenNode.GetString();
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
