using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>Compose files, read into what the agent would do with them. Implemented by the server.</summary>
public interface IProjectService
{
    /// <summary>The plan of a Compose file, by its path on the agent's machine or by its text. Nothing is run.</summary>
    Task<ComposePlan> PlanAsync(ComposePlanRequest request, CancellationToken cancellationToken = default);
}
