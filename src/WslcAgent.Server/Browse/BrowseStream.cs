using System.Buffers;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Browse;

/// <summary>
/// One pane's WebSocket to a host browser, in protocol 2.
/// <code>
/// pane  → {"type":"start","protocol":2,"width":W,"height":H,"viewerId":"…","sessionId":"…"}
///         {"type":"pointer","action":"click|down|move|up|wheel","x":…,"y":…,"button":"left","clickCount":1,"deltaX":…,"deltaY":…}
///         {"type":"key","text":"…"} {"type":"key","key":"Enter|Control+a"} {"type":"navigate","url":"…"}
///         {"type":"back"} {"type":"forward"} {"type":"reload"} {"type":"resize","width":W,"height":H}
///         {"type":"zoom","percent":50…150} {"type":"selection","op":"get|anchor|extend","handle":"start|end","x":…,"y":…}
///         {"type":"copy"} {"type":"ping"} {"type":"pong"} {"type":"detach"} {"type":"close"}
/// agent → {"type":"ready","url":"…","resumed":b,"joined":b,"owner":b,"sessionId":"…","zoom":100}
///         binary: u32 big-endian header length, JSON header {"type":"frame","mime","url","width","height"}, JPEG
///         {"type":"url","href":"…"} {"type":"focus","editable":b,"inputType":"…"} {"type":"clipboard","text":"…"}
///         {"type":"zoom","percent":N} {"type":"selection",…} {"type":"ping"} {"type":"pong"} {"type":"error","message":"…"}
/// </code>
/// Closing the socket or <c>detach</c> leaves the browser on its page for the
/// next open; <c>close</c> from the owner ends it for everyone attached.
/// </summary>
public sealed class BrowseStream(
    HostBrowserSessions sessions,
    IWslcRunner wslc,
    IOptionsMonitor<ExecTerminalOptions> limits,
    ILogger<BrowseStream> logger)
{
    /// <summary>Under a proxy's 60-second read timeout: an unchanged page sends no frames and would look like a dead socket.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(25);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Who is asking: <c>local</c> or the remote address, and the port the agent listens on (never browsable).</summary>
    public sealed record Caller(string Client, int AgentPort);

    public async Task RunAsync(WebSocket socket, Caller caller, string container, string hostPort, string? viewerQuery, string? sessionQuery, CancellationToken cancellationToken)
    {
        var channel = new Channel(socket);
        HostBrowserSessions.Attached? attached = null;
        var destroy = false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? pump = null;
        Task? heartbeat = null;
        try
        {
            if (await WaitForStartAsync(channel, stop.Token) is not { } start)
            {
                return;
            }

            var width = BrowseTarget.Width(BrowseMessage.Integer(start, "width"), 960);
            var height = BrowseTarget.Height(BrowseMessage.Integer(start, "height"), 640);
            var viewerId = BrowseTarget.NormalizeViewerId(FirstText(start, "viewerId", viewerQuery));
            var joinId = FirstText(start, "sessionId", sessionQuery).Trim();
            var joined = joinId.Length > 0;
            if (joined)
            {
                attached = await sessions.JoinAsync(joinId, container, width, height, stop.Token);
            }
            else
            {
                if (hostPort.Trim().Length == 0)
                {
                    throw new BrowseException("hostPort required");
                }

                var key = BrowseTarget.SessionKey(container, hostPort, caller.Client, viewerId);
                var inspection = await InspectAsync(container, stop.Token);
                var url = BrowseTarget.Resolve(inspection.Binds, hostPort, caller.AgentPort);
                attached = await sessions.OpenAsync(key, container, caller.Client, viewerId, url, width, height, stop.Token);
            }

            var session = attached.Session;
            logger.LogInformation(
                "Browse session {Verb} key={Key} id={Id} url={Url} viewport={Width}x{Height}",
                joined ? "joined" : attached.Created ? "created" : "resumed", session.RegistryKey, session.Id, session.Url, width, height);
            await channel.SendAsync(new JsonObject
            {
                ["type"] = "ready",
                ["url"] = session.Url,
                ["resumed"] = !attached.Created && !joined,
                ["joined"] = joined,
                ["owner"] = !joined,
                ["sessionId"] = session.Id,
                ["zoom"] = session.ZoomPercent,
            });

            pump = PumpAsync(channel, session, attached.Viewer, stop.Token);
            heartbeat = HeartbeatAsync(channel, stop.Token);
            destroy = await ReadPaneAsync(channel, session, stop.Token) && !joined;
        }
        catch (Exception ex) when (ex is BrowseException or WslcException or WslcNotFoundException or ArgumentException)
        {
            await channel.SendAsync(Error(ex.Message));
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            logger.LogDebug("Browse socket ended: {Message}", ex.Message);
        }
        finally
        {
            await stop.CancelAsync();
            await Task.WhenAll(new[] { pump, heartbeat }.OfType<Task>());
            if (attached is { Session: var session, Viewer: var viewer })
            {
                if (destroy)
                {
                    logger.LogInformation("Browse session destroyed id={Id}", session.Id);
                    await sessions.DestroyAsync(session);
                }
                else
                {
                    var (sent, dropped) = session.Stats(viewer);
                    logger.LogInformation("Browse viewer detached id={Id} (Edge kept) frames sent={Sent} dropped={Dropped}", session.Id, sent, dropped);
                    await session.EndViewAsync(viewer);
                }
            }

            await channel.CloseAsync();
        }
    }

    /// <summary>The pane's opening message; null when it left (or said close or detach) before sending one.</summary>
    private async Task<JsonElement?> WaitForStartAsync(Channel channel, CancellationToken cancellationToken)
    {
        while (true)
        {
            var received = await channel.ReceiveAsync(limits.CurrentValue.MaxMessageBytes, cancellationToken);
            if (received.Closed)
            {
                return null;
            }

            switch (received.Message is { } message ? BrowseMessage.Text(message, "type") : "")
            {
                case "start" or "resize":
                    return received.Message;
                case "close" or "end" or "detach":
                    return null;
            }
        }
    }

    /// <summary>Pane messages until it leaves; true when it asked to close the browser.</summary>
    private async Task<bool> ReadPaneAsync(Channel channel, HostBrowserSession session, CancellationToken cancellationToken)
    {
        while (true)
        {
            var received = await channel.ReceiveAsync(limits.CurrentValue.MaxMessageBytes, cancellationToken);
            if (received.Closed)
            {
                return false;
            }

            if (received.TooLarge)
            {
                await channel.SendAsync(Error("message too large"));
                continue;
            }

            if (received.Message is not { } message)
            {
                await channel.SendAsync(Error("invalid json"));
                continue;
            }

            var type = BrowseMessage.Text(message, "type");
            switch (type)
            {
                case "ping":
                    await channel.SendAsync(new JsonObject { ["type"] = "pong" });
                    continue;
                case "pong" or "start":
                    continue;
                case "close" or "end":
                    return true;
                case "detach":
                    return false;
            }

            try
            {
                if (await session.HandleAsync(type, message) is { } reply)
                {
                    await channel.SendAsync(reply);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await channel.SendAsync(Error(ex.Message));
            }
        }
    }

    private static async Task PumpAsync(Channel channel, HostBrowserSession session, int viewer, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in session.EventsAsync(viewer, cancellationToken))
            {
                var sent = item is BrowseFrame frame ? await channel.SendFrameAsync(frame) : await channel.SendAsync((JsonObject)item);
                if (!sent)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The pane left.
        }
    }

    private static async Task HeartbeatAsync(Channel channel, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Heartbeat);
            while (await timer.WaitForNextTickAsync(cancellationToken) && await channel.SendAsync(new JsonObject { ["type"] = "ping" }))
            {
            }
        }
        catch (OperationCanceledException)
        {
            // The pane left.
        }
    }

    private async Task<ContainerInspection> InspectAsync(string container, CancellationToken cancellationToken)
    {
        var result = await wslc.RunAsync(["container", "inspect", WslcArgs.Require(container, "container"), "--format", "json"], cancellationToken: cancellationToken);
        return ContainerInspection.Parse(result);
    }

    private static string FirstText(JsonElement message, string property, string? query) =>
        BrowseMessage.Text(message, property) is { Length: > 0 } text ? text : query ?? "";

    private static JsonObject Error(string message) => new() { ["type"] = "error", ["message"] = message };

    /// <summary>The socket, with one writer at a time: frames, replies and the heartbeat all send.</summary>
    private sealed class Channel(WebSocket socket)
    {
        private readonly SemaphoreSlim _send = new(1, 1);

        public sealed record Received(JsonElement? Message, bool Closed, bool TooLarge);

        public Task<bool> SendAsync(JsonObject message) =>
            WriteAsync(JsonSerializer.SerializeToUtf8Bytes(message, Json), WebSocketMessageType.Text);

        /// <summary>u32 big-endian header length, the JSON header, then the JPEG: no base64 third on the wire.</summary>
        public Task<bool> SendFrameAsync(BrowseFrame frame)
        {
            var header = JsonSerializer.SerializeToUtf8Bytes(new { type = "frame", mime = "image/jpeg", url = frame.Url, width = frame.Width, height = frame.Height }, Json);
            var payload = new byte[4 + header.Length + frame.Jpeg.Length];
            BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)header.Length);
            header.CopyTo(payload, 4);
            frame.Jpeg.CopyTo(payload, 4 + header.Length);
            return WriteAsync(payload, WebSocketMessageType.Binary);
        }

        public async Task<Received> ReceiveAsync(int maxBytes, CancellationToken cancellationToken)
        {
            var message = new ArrayBufferWriter<byte>(4096);
            var chunk = new byte[4096];
            var tooLarge = false;
            while (true)
            {
                var received = await socket.ReceiveAsync(chunk, cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    return new Received(null, Closed: true, TooLarge: false);
                }

                tooLarge |= maxBytes > 0 && message.WrittenCount + received.Count > maxBytes;
                if (!tooLarge)
                {
                    message.Write(chunk.AsSpan(0, received.Count));
                }

                if (received.EndOfMessage)
                {
                    break;
                }
            }

            if (tooLarge)
            {
                return new Received(null, Closed: false, TooLarge: true);
            }

            try
            {
                var root = JsonDocument.Parse(message.WrittenMemory).RootElement;
                return new Received(root.ValueKind == JsonValueKind.Object ? root.Clone() : null, Closed: false, TooLarge: false);
            }
            catch (JsonException)
            {
                return new Received(null, Closed: false, TooLarge: false);
            }
        }

        private async Task<bool> WriteAsync(byte[] payload, WebSocketMessageType type)
        {
            await _send.WaitAsync();
            try
            {
                if (socket.State != WebSocketState.Open)
                {
                    return false;
                }

                await socket.SendAsync(payload, type, endOfMessage: true, CancellationToken.None);
                return true;
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                return false;
            }
            finally
            {
                _send.Release();
            }
        }

        public async Task CloseAsync()
        {
            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            {
                return;
            }

            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "browse ended", CancellationToken.None);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                // Already gone.
            }
        }
    }
}
