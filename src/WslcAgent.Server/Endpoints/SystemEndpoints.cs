using WslcAgent.Mcp;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/system</c>: the host overview and its cleanups. See docs/api-v1.md.</summary>
public static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/system");

        group.MapGet("", (ISystemService system, CancellationToken ct) => system.GetAsync(ct))
            .WithName("SystemOverview");

        group.MapPost("/cleanup/{target}", (string target, ISystemService system, CancellationToken ct) => system.CleanupAsync(target, ct))
            .WithName("SystemCleanup");

        return api;
    }
}
