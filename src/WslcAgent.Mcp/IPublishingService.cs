using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>
/// Publishing a container port on a public name, the third way of Open with
/// browser: a tab at the agent's machine, the host browser pane from anywhere,
/// and now the public name from anywhere. Implemented by the server, which
/// keeps the store, writes the proxy's map and restarts the proxy.
/// </summary>
public interface IPublishingService
{
    /// <summary>Every published port, as the proxy's map has them.</summary>
    Task<IReadOnlyList<Publication>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish one port: the container joins the proxy's network, the name goes in
    /// the map, the proxy restarts. The name has to be one label under the
    /// configured domain; a name already in use is refused.
    /// </summary>
    Task<Publication> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default);

    /// <summary>Take a name out of the map and restart the proxy; that name answers 404 from then on.</summary>
    Task UnpublishAsync(string hostname, CancellationToken cancellationToken = default);
}
