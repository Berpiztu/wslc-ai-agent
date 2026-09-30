using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Host;
using WslcAgent.Server.Overview;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/terminal</c>: the host shell, its native window and its jobs. See docs/api-v1.md.</summary>
public static class TerminalEndpoints
{
    public static RouteGroupBuilder MapTerminalEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/terminal");

        group.MapGet("/status", (HttpContext http) => new TerminalStatus("local", HostShell.Label, LocalClient.IsLocal(http)))
            .WithName("TerminalStatus");

        group.Map("/stream", async (HttpContext http, ExecTerminals terminals) =>
            {
                if (!http.WebSockets.IsWebSocketRequest)
                {
                    return Results.Problem("This endpoint is a WebSocket.", statusCode: StatusCodes.Status400BadRequest, title: "Not a WebSocket request");
                }

                using var socket = await http.WebSockets.AcceptWebSocketAsync();
                await terminals.RunHostAsync(socket, http.RequestAborted);
                return Results.Empty;
            })
            .WithName("HostTerminalStream");

        // A window on the agent's desktop: only for someone sitting at it.
        group.MapPost("/open-native", Results<Ok<NativeTerminalResult>, ProblemHttpResult> (HttpContext http, OpenTerminalRequest? request, NativeTerminals terminals) =>
            {
                if (!LocalClient.IsLocal(http))
                {
                    return TypedResults.Problem(
                        "Native terminals open on the server desktop and can only be requested from the server itself. Use the client terminal instead.",
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Server-local action");
                }

                return TypedResults.Ok(terminals.OpenHost(request?.Command));
            })
            .WithName("OpenHostTerminal");

        group.MapPost("/jobs/{job}", (string job, TerminalJobs jobs, CancellationToken ct) => jobs.PrepareAsync(job, ct))
            .WithName("PrepareTerminalJob");

        return api;
    }
}
