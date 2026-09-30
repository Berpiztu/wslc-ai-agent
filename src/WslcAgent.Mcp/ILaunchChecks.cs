using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>The launch form's check before a run, create or recreate: what would stop it and what looks wrong. Implemented by the server.</summary>
public interface ILaunchChecks
{
    /// <summary>Errors and warnings by form field; <paramref name="source"/> is the container being recreated, whose own name and ports are not conflicts.</summary>
    Task<LaunchCheck> CheckAsync(ContainerLaunchRequest request, string source = "", CancellationToken cancellationToken = default);
}
