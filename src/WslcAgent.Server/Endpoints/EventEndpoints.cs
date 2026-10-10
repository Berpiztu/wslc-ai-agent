using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Endpoints;

/// <summary>
/// <c>/api/v1/events</c>: what changed, as the agent hears it from
/// <c>wslc events</c>, so a screen reads its list when something happened
/// instead of every few seconds. See docs/api-v1.md.
/// </summary>
public static class EventEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapEventEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/events");

        group.MapGet("/status", (WslcEvents events) => new EventStatus(events.Live))
            .WithName("EventStatus");

        // What happened, newest first: the events the agent heard since it began
        // listening to the session it targets (RecentWslcEvents)
        group.MapGet("/recent", (RecentWslcEvents recent, int? limit) => recent.Recent(limit ?? 50))
            .WithName("RecentEvents");

        // One notice per message, the first of them a full one: a client that
        // has just arrived has missed whatever came before it.
        group.Map("/stream", async (HttpContext http, WslcEvents events) =>
            {
                if (!http.WebSockets.IsWebSocketRequest)
                {
                    return Results.Problem("This endpoint is a WebSocket.", statusCode: StatusCodes.Status400BadRequest, title: "Not a WebSocket request");
                }

                using var socket = await http.WebSockets.AcceptWebSocketAsync();
                using var listener = events.Listen();
                try
                {
                    await foreach (var notice in listener.ReadAllAsync(http.RequestAborted))
                    {
                        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notice, Json));
                        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, http.RequestAborted);
                    }
                }
                catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
                {
                    // The client left, which is the ordinary way this ends.
                }

                return Results.Empty;
            })
            .WithName("EventStream");

        return api;
    }
}
