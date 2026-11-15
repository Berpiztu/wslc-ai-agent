using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Sessions;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/agent-start</c>: Settings → Security → Session at agent start. See docs/api-v1.md.</summary>
public static class AgentStartEndpoints
{
    public static RouteGroupBuilder MapAgentStartEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/agent-start", (AgentStartSettingsStore store) => store.Get())
            .WithName("GetAgentStartSettings");

        api.MapPut("/agent-start", (AgentStartSettings settings, AgentStartSettingsStore store) => store.Set(settings))
            .WithName("SetAgentStartSettings");

        return api;
    }
}