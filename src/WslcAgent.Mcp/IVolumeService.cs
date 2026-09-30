using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>Volume operations the MCP tools need from the host. Implemented by the server.</summary>
public interface IVolumeService
{
    /// <summary>Managed volumes on the selected session, with usage.</summary>
    Task<VolumeListResponse> ListAsync(CancellationToken cancellationToken = default);

    /// <summary><c>wslc volume create</c> with the request's driver, size, labels and options.</summary>
    Task CreateAsync(CreateVolumeRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>wslc volume inspect NAME</c>: the raw JSON, indented.</summary>
    Task<VolumeInspect> InspectAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>The containers (any state) that mount the volume, with where and whether read-only.</summary>
    Task<VolumeUsers> UsersAsync(string name, CancellationToken cancellationToken = default);

    /// <summary><c>wslc volume remove NAME</c>. Destructive: callers confirm with the user first.</summary>
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);

    /// <summary><c>wslc volume prune -a</c>: every volume no container uses. Destructive.</summary>
    Task<CommandOutput> PruneAsync(CancellationToken cancellationToken = default);
}
