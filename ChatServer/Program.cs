using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var clients = new ConcurrentDictionary<Guid, WebSocket>();

app.Map("/chat", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        await HandleRequest(webSocket, clients);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

async Task HandleRequest(WebSocket webSocket, ConcurrentDictionary<Guid, WebSocket> clients)
{
    var clientId = Guid.NewGuid();
    clients.TryAdd(clientId, webSocket);

    while (webSocket.State == WebSocketState.Open)
    {
        var (payload, messageType) = await Read(webSocket);
        if (messageType == WebSocketMessageType.Close)
        {
            await webSocket.CloseAsync(webSocket.CloseStatus!.Value, webSocket.CloseStatusDescription, CancellationToken.None);
            break;
        }
        if (messageType == WebSocketMessageType.Text)
        {
            await Broadcast(payload, clientId, clients);
        }
    }

    webSocket.Dispose();
    clients.TryRemove(clientId, out _);
}

async Task<(List<byte>, WebSocketMessageType)> Read(WebSocket webSocket)
{
    var buffer = new byte[1024 * 4]; // 4 KB
    var payload = new List<byte>(1024 * 4);
    WebSocketReceiveResult? result;
    do
    {
        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        payload.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
    }
    while (result.EndOfMessage == false);
    return (payload, result.MessageType);
}

async Task Broadcast(List<byte> payload, Guid clientId, ConcurrentDictionary<Guid, WebSocket> clients)
{
    var receivedMessage = Encoding.UTF8.GetString(payload.ToArray());

    foreach (var key in clients.Keys)
    {
        var message = Encoding.UTF8.GetBytes($"{clientId} says {receivedMessage}");
        await clients[key].SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, CancellationToken.None);
    }
}

app.Run();