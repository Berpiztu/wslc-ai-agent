using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Updates;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/agent/update</c>: Settings → Agent update, and the countdown every client shows. See docs/api-v1.md.</summary>
public static class AgentUpdateEndpoints
{
    public static RouteGroupBuilder MapAgentUpdateEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/agent/update");

        group.MapGet("", (AgentUpdater updater) => updater.Status())
            .WithName("GetAgentUpdate");

        group.MapPut("/settings", (AgentUpdateSettings settings, AgentUpdater updater) => updater.Save(settings))
            .WithName("SetAgentUpdateSettings");

        // Update now: 409 with the reason when there is nothing newer to install.
        group.MapPost("", (AgentUpdater updater) => updater.Request())
            .WithName("UpdateAgent");

        group.MapPost("/cancel", (AgentUpdater updater) => updater.Cancel())
            .WithName("CancelAgentUpdate");

        return api;
    }
}
