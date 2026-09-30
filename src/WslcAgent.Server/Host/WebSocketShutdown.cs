namespace WslcAgent.Server.Host;

/// <summary>
/// Ends every open WebSocket the moment the agent starts to stop. The host waits for the requests under way before it
/// exits, and a socket — a client's change stream, a terminal, a browser pane
/// — is a request that lasts until its client goes: stopping the agent sat
/// there until every client had reloaded or closed its window. Aborting the
/// connection raises the request's own abort, which every socket endpoint
/// already reads as its client leaving, so each ends the way it always does
/// and the process exits. The clients notice the agent gone and wait for it,
/// as they do when it crashes.
/// </summary>
public static class WebSocketShutdown
{
    /// <summary>After <c>UseWebSockets</c>, which is what tells a socket request apart.</summary>
    public static IApplicationBuilder UseWebSocketShutdown(this IApplicationBuilder app)
    {
        var stopping = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        return app.Use(async (http, next) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                await next(http);
                return;
            }

            using var abort = stopping.Register(http.Abort);
            await next(http);
        });
    }
}
