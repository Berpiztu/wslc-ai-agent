using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>Network operations the MCP tools need from the host. Implemented by the server.</summary>
public interface INetworkService
{
    /// <summary>Networks on the selected session, with subnet, gateway and usage.</summary>
    Task<NetworkListResponse> ListAsync(CancellationToken cancellationToken = default);

    /// <summary><c>wslc network create</c> with the request's driver, IPAM values, labels and options.</summary>
    Task CreateAsync(CreateNetworkRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>wslc network connect [--ip IP] NETWORK CONTAINER</c>; the static ip only on user-defined networks.</summary>
    Task ConnectAsync(string network, string container, string ip, CancellationToken cancellationToken = default);

    /// <summary><c>wslc network disconnect NETWORK CONTAINER</c>.</summary>
    Task DisconnectAsync(string network, string container, CancellationToken cancellationToken = default);

    /// <summary><c>wslc network remove NAME</c>. Destructive: callers confirm with the user first.</summary>
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>View &amp; edit: the inspect facts, attached containers and the create-time settings as a form.</summary>
    Task<NetworkDetails> DetailsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Replaces <paramref name="source"/> with a network created from <paramref name="request"/> and reconnects its containers. Destructive.</summary>
    Task<RecreateNetworkResult> RecreateAsync(string source, CreateNetworkRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>wslc network prune</c>: every network no container uses. Destructive.</summary>
    Task<CommandOutput> PruneAsync(CancellationToken cancellationToken = default);
}
