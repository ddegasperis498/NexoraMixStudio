using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace NexoraMix.App.Remote;

public sealed class TabletRemoteHost : IAsyncDisposable
{
    private readonly Func<RemoteMixerSnapshot> _snapshotProvider;
    private readonly Func<RemoteCommand, Task> _commandHandler;
    private readonly string _webRoot;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private WebApplication? _application;
    private CancellationTokenSource? _broadcastCancellation;
    private Task? _broadcastTask;

    public TabletRemoteHost(
        Func<RemoteMixerSnapshot> snapshotProvider,
        Func<RemoteCommand, Task> commandHandler,
        string webRoot,
        int port = 17840)
    {
        _snapshotProvider = snapshotProvider;
        _commandHandler = commandHandler;
        _webRoot = webRoot;
        Port = port;
        PairingCode = Random.Shared.Next(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public int Port { get; }
    public string PairingCode { get; }
    public bool IsRunning => _application is not null;
    public string LocalUrl => $"http://{GetPreferredLocalIp()}:{Port}/";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null) return;
        if (!Directory.Exists(_webRoot))
            throw new DirectoryNotFoundException($"Interfaccia tablet non trovata: {_webRoot}");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
        var app = builder.Build();
        var files = new PhysicalFileProvider(_webRoot);

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15)
        });
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files });

        app.MapGet("/api/health", () => Results.Json(new
        {
            ok = true,
            product = "Nexora Mix Studio",
            version = "8.0",
            requiresPairing = true
        }));

        app.MapGet("/api/state", (HttpContext context) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            return Results.Json(_snapshotProvider(), _jsonOptions);
        });

        app.MapPost("/api/command", async (HttpContext context) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            var command = await JsonSerializer.DeserializeAsync<RemoteCommand>(
                context.Request.Body,
                _jsonOptions,
                context.RequestAborted);
            if (command is null || string.IsNullOrWhiteSpace(command.Action))
                return Results.BadRequest(new { error = "Comando non valido" });

            await _commandHandler(command);
            return Results.Ok(new { ok = true });
        });

        app.Map("/ws", HandleWebSocketAsync);

        await app.StartAsync(cancellationToken);
        _application = app;
        _broadcastCancellation = new CancellationTokenSource();
        _broadcastTask = BroadcastLoopAsync(_broadcastCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_application is null) return;

        _broadcastCancellation?.Cancel();
        if (_broadcastTask is not null)
        {
            try { await _broadcastTask; }
            catch (OperationCanceledException)
            {
                // Arresto richiesto: il loop di broadcast termina normalmente.
            }
        }

        foreach (var socket in _clients.Values)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server arrestato", cancellationToken);
            }
            catch
            {
                // Una socket remota può essere già scomparsa durante lo shutdown.
            }
            socket.Dispose();
        }
        _clients.Clear();

        await _application.StopAsync(cancellationToken);
        await _application.DisposeAsync();
        _application = null;
        _broadcastCancellation?.Dispose();
        _broadcastCancellation = null;
        _broadcastTask = null;
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!IsAuthorized(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        _clients[id] = socket;

        try
        {
            await SendSnapshotAsync(socket, context.RequestAborted);
            var buffer = new byte[8192];
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text) continue;

                using var stream = new MemoryStream();
                stream.Write(buffer, 0, result.Count);
                while (!result.EndOfMessage)
                {
                    result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                    stream.Write(buffer, 0, result.Count);
                }

                stream.Position = 0;
                var command = await JsonSerializer.DeserializeAsync<RemoteCommand>(stream, _jsonOptions, context.RequestAborted);
                if (command is not null && !string.IsNullOrWhiteSpace(command.Action))
                {
                    await _commandHandler(command);
                    await SendSnapshotAsync(socket, context.RequestAborted);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normale quando il browser/tablet chiude la pagina.
        }
        catch (WebSocketException)
        {
            // La rete Wi-Fi può interrompersi senza un close frame.
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (_clients.IsEmpty) continue;
            var json = JsonSerializer.Serialize(_snapshotProvider(), _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            foreach (var pair in _clients.ToArray())
            {
                if (pair.Value.State != WebSocketState.Open)
                {
                    _clients.TryRemove(pair.Key, out _);
                    continue;
                }

                try
                {
                    await pair.Value.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
                }
                catch
                {
                    _clients.TryRemove(pair.Key, out _);
                }
            }
        }
    }

    private async Task SendSnapshotAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(_snapshotProvider(), _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private bool IsAuthorized(HttpContext context)
    {
        var supplied = context.Request.Query["token"].ToString();
        if (string.IsNullOrWhiteSpace(supplied) && context.Request.Headers.TryGetValue("X-Nexora-Pairing", out var header))
            supplied = header.ToString();
        return string.Equals(supplied, PairingCode, StringComparison.Ordinal);
    }

    private static string GetPreferredLocalIp()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                                  network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                                  !IPAddress.IsLoopback(address.Address) &&
                                  !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => address.Address.ToString())
                .ToList();
            return candidates.FirstOrDefault() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
