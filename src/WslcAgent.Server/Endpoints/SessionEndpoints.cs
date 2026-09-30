using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/sessions</c>: list, select, start and stop. See docs/api-v1.md.</summary>
public static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/sessions");

        group.MapGet("", (ISessionService sessions, CancellationToken ct) => sessions.ListAsync(ct))
            .WithName("ListSessions");

        group.MapPost("/select", NoContent (SelectSessionRequest request, ISessionService sessions) =>
            {
                sessions.Select(request.Name);
                return TypedResults.NoContent();
            })
            .WithName("SelectSession");

        group.MapPost("/start", (SessionActionRequest request, ISessionService sessions, CancellationToken ct) =>
                sessions.StartAsync(request.Name, ct))
            .WithName("StartSession");

        group.MapPost("/stop", (SessionActionRequest request, ISessionService sessions, CancellationToken ct) =>
                sessions.StopAsync(request.Name, ct))
            .WithName("StopSession");

        return api;
    }
}
