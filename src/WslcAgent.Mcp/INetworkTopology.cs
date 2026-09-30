using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>
/// The network map: every network with the containers attached to it, and every
/// container with its addresses. Its own interface because the host reads it
/// with something that itself needs <see cref="INetworkService"/>, and one
/// interface asking for the other would be a circle.
/// </summary>
public interface INetworkTopology
{
    Task<NetworkTopology> TopologyAsync(CancellationToken cancellationToken = default);
}
