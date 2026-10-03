using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>
/// The first use of publishing on a machine: the network, the map file and the
/// proxy container, set up by the agent from Settings → Publish.
/// Implemented by the server.
/// </summary>
public interface IPublishingSetup
{
    /// <summary>
    /// The network of the settings created if it is not there, the data folder
    /// made if it is not there, the proxy container run if it is not there
    /// (ghcr.io/berpiztu/wslc-published, the plugin: nginx, the login in front of
    /// the published names and its own API and page; its data folder mounted, the
    /// published port and the API's on the loopback, restarted always, on that
    /// network) or run again if it is plain nginx. Every published name is then
    /// handed to it. What is there already is left as it is, and the result says
    /// which was which.
    /// </summary>
    Task<PublishingSetupResult> SetupAsync(CancellationToken cancellationToken = default);
}
