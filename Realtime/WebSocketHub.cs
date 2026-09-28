using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace PortfolioManager.Api.Realtime;

/// <summary>
/// Keeps track of connected WebSocket clients and pushes published messages to all of them.
/// Messages are queued and sent from a background loop so publishers (e.g. an HTTP request
/// saving to the database) never wait on slow clients.
/// </summary>
public sealed class WebSocketHub(ILogger<WebSocketHub> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly Channel<byte[]> _outbox =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Queues a <c>{ "type": ..., "data": ... }</c> message for every connected client.</summary>
    public void Publish(string type, object data)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { type, data }, JsonOptions);
        _outbox.Writer.TryWrite(payload);
    }

    /// <summary>Registers an accepted socket and keeps it open until the client disconnects.</summary>
    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        logger.LogInformation("WebSocket client {ClientId} connected ({Count} total)", id, _clients.Count);

        try
        {
            // Clients only listen; incoming messages are read and ignored so close frames are handled.
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _clients.TryRemove(id, out _);
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // Client went away without a clean close.
        }
        finally
        {
            _clients.TryRemove(id, out _);
            logger.LogInformation("WebSocket client {ClientId} disconnected ({Count} total)", id, _clients.Count);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var payload in _outbox.Reader.ReadAllAsync(stoppingToken))
        {
            await Task.WhenAll(_clients.Select(c => SendAsync(c.Key, c.Value, payload, stoppingToken)));
        }
    }

    private async Task SendAsync(Guid id, WebSocket socket, byte[] payload, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open)
            return;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SendTimeout);
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            logger.LogWarning("Dropping WebSocket client {ClientId}: send failed", id);
            _clients.TryRemove(id, out _);
            socket.Abort();
        }
    }
}
