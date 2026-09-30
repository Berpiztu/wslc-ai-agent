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
    /// The network of the settings created if it is not there, the map file
    /// written if it is not there, the nginx container run if it is not there
    /// (nginx:alpine, the map mounted read-only, the published port on the
    /// loopback, restarted always, on that network). What is there already is
    /// left as it is, and the result says which was which.
    /// </summary>
    Task<PublishingSetupResult> SetupAsync(CancellationToken cancellationToken = default);
}
