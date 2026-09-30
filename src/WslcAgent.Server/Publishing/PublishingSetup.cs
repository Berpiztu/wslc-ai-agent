using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Publishing;

/// <summary>
/// Settings → Publish's Set up: the network, the map file and the
/// proxy container set up by the agent. Its own class, not the publishing service's, because it
/// runs a container and the container service is what publishing is a
/// dependency of.
/// </summary>
public sealed class PublishingSetup(
    PublishingSettingsStore settings,
    PublicationStore store,
    INetworkService networks,
    IContainerService containers,
    ILogger<PublishingSetup> logger) : IPublishingSetup
{
    /// <summary>The nginx image the checklist runs: small, and enough for a map and a proxy_pass.</summary>
    public const string ProxyImage = "nginx:alpine";

    /// <summary>Where nginx reads its site configuration, the path the map is mounted at.</summary>
    public const string MapMount = "/etc/nginx/conf.d/default.conf";

    /// <summary>
    /// The port the proxy publishes on the loopback: the VPS's tunnel for
    /// published names forwards its 8081 to it, so the two are one number
    /// written twice, on purpose.
    /// </summary>
    public const int PublishedPort = 8081;

    public async Task<PublishingSetupResult> SetupAsync(CancellationToken cancellationToken = default)
    {
        var current = settings.Get();
        var notes = new List<string>();

        var networkCreated = false;
        if (!await ExistsAsync(current.Network, cancellationToken))
        {
            await networks.CreateAsync(new CreateNetworkRequest(current.Network, "", "", "", "", false, "", ""), cancellationToken);
            networkCreated = true;
            notes.Add($"Network {current.Network} created.");
        }
        else
        {
            notes.Add($"Network {current.Network} was there.");
        }

        var mapWritten = false;
        if (!File.Exists(current.MapFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(current.MapFile))!);
            await File.WriteAllTextAsync(current.MapFile, PublishedMap.Render(store.All()), cancellationToken);
            mapWritten = true;
            notes.Add($"Map written at {current.MapFile}.");
        }
        else
        {
            notes.Add($"Map at {current.MapFile} was there.");
        }

        var proxyCreated = false;
        var listed = await containers.ListAsync(all: true, cancellationToken: cancellationToken);
        if (!listed.Containers.Any(c => c.Name.Equals(current.ProxyContainer, StringComparison.OrdinalIgnoreCase)))
        {
            var run = await containers.RunAsync(new ContainerLaunchRequest
            {
                Image = ProxyImage,
                Name = current.ProxyContainer,
                Publish = [$"127.0.0.1:{PublishedPort}:80"],
                Volumes = [$"{current.MapFile.Replace('\\', '/')}:{MapMount}:ro"],
                ConnectNetworks = [current.Network],
                RestartPolicy = RestartPolicyInfo.Always,
                Start = true,
            }, cancellationToken);
            proxyCreated = true;
            notes.Add($"Container {current.ProxyContainer} ({ProxyImage}) running on 127.0.0.1:{PublishedPort}, on {current.Network}, restarted always.");
            notes.AddRange(run.Notes);
        }
        else
        {
            notes.Add($"Container {current.ProxyContainer} was there.");
        }

        logger.LogInformation("publishing set up: network {Network} created={NetworkCreated}, map written={MapWritten}, proxy {Proxy} created={ProxyCreated}",
            current.Network, networkCreated, mapWritten, current.ProxyContainer, proxyCreated);
        return new PublishingSetupResult(networkCreated, mapWritten, proxyCreated, notes);
    }

    private async Task<bool> ExistsAsync(string network, CancellationToken cancellationToken)
    {
        try
        {
            await networks.DetailsAsync(network, cancellationToken);
            return true;
        }
        catch (WslcException)
        {
            return false;
        }
    }
}
