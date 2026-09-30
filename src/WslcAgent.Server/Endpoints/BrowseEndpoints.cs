using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Browse;
using WslcAgent.Server.Host;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/containers/{container}/browse…</c>: a container's published port in a browser on the host, for a client that cannot reach it. See docs/api-v1.md.</summary>
public static class BrowseEndpoints
{
    public static RouteGroupBuilder MapBrowseEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/containers/{container}");

        group.MapGet("/browse-sessions", (string container, HostBrowserSessions sessions) => new BrowseSessionList(sessions.List(container)))
            .WithName("ListBrowseSessions");

        group.Map("/browse/stream", async (HttpContext http, string container, BrowseStream stream, string? hostPort, string? viewerId, string? sessionId) =>
            {
                if (!http.WebSockets.IsWebSocketRequest)
                {
                    return Results.Problem("This endpoint is a WebSocket.", statusCode: StatusCodes.Status400BadRequest, title: "Not a WebSocket request");
                }

                using var socket = await http.WebSockets.AcceptWebSocketAsync();
                var caller = new BrowseStream.Caller(ClientLabel(http), http.Connection.LocalPort);
                await stream.RunAsync(socket, caller, container, hostPort ?? "", viewerId, sessionId, http.RequestAborted);
                return Results.Empty;
            })
            .WithName("BrowseStream");

        return api;
    }

    /// <summary>What View browser sessions shows as the opener: <c>local</c> at the agent's machine, else the address the request came from.</summary>
    private static string ClientLabel(HttpContext http) =>
        LocalClient.IsLocal(http)
            ? "local"
            : http.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() is { Length: > 0 } forwarded
                ? forwarded
                : http.Connection.RemoteIpAddress?.ToString() ?? "remote";
}
