using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Publishing;

/// <summary>
/// Settings → Publish's Set up: the network, the proxy's data folder and the proxy
/// container (the wslc-published plugin) set up by the agent, and every published
/// name handed to it. Its own class, not the publishing service's, because it runs a
/// container and the container service is what publishing is a dependency of.
/// </summary>
public sealed class PublishingSetup(
    PublishingSettingsStore settings,
    PublishingService publishing,
    IPublishedPlugin plugin,
    INetworkService networks,
    IContainerService containers,
    ILogger<PublishingSetup> logger) : IPublishingSetup
{
    /// <summary>
    /// The wslc-published plugin (plugins/wslc-published, built by
    /// .github/workflows/published-image.yml): nginx, the login in front of the
    /// published names and its root API. Latest, so Update image on the proxy brings
    /// the newest.
    /// </summary>
    public const string ProxyImage = "ghcr.io/berpiztu/wslc-published:latest";

    /// <summary>The repository of <see cref="ProxyImage"/>: a proxy of another image (plain nginx, before the plugin) is replaced.</summary>
    private const string ProxyRepository = "ghcr.io/berpiztu/wslc-published";

    /// <summary>Where the plugin keeps what outlives an update of its image.</summary>
    public const string DataMount = "/data";

    /// <summary>
    /// The port the proxy publishes on the loopback: the VPS's tunnel for
    /// published names forwards its 8081 to it, so the two are one number
    /// written twice, on purpose.
    /// </summary>
    public const int PublishedPort = 8081;

    /// <summary>A proxy just run is waited for this long to answer.</summary>
    private static readonly TimeSpan PluginWait = TimeSpan.FromSeconds(30);

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

        var folderCreated = !Directory.Exists(current.DataFolder);
        Directory.CreateDirectory(current.DataFolder);
        notes.Add(folderCreated ? $"Data folder {current.DataFolder} made." : $"Data folder {current.DataFolder} was there.");

        var proxyCreated = await EnsureProxyAsync(current, notes, cancellationToken);
        await WaitForPluginAsync(cancellationToken);
        var handed = await publishing.SyncAsync(cancellationToken);
        notes.Add(handed == 0 ? "No name published yet." : $"{handed} published name(s) handed to the proxy; each is checked for a login of its own.");

        logger.LogInformation("publishing set up: network {Network} created={NetworkCreated}, data folder created={FolderCreated}, proxy {Proxy} created={ProxyCreated}",
            current.Network, networkCreated, folderCreated, current.ProxyContainer, proxyCreated);
        return new PublishingSetupResult(networkCreated, folderCreated, proxyCreated, notes);
    }

    /// <summary>
    /// The proxy run if it is not there, and run again if it is plain nginx, which has
    /// neither the login nor the API the agent calls. True when it was (re)created.
    /// </summary>
    private async Task<bool> EnsureProxyAsync(PublishingSettings current, List<string> notes, CancellationToken cancellationToken)
    {
        var listed = await containers.ListAsync(all: true, cancellationToken: cancellationToken);
        var existing = listed.Containers.FirstOrDefault(c => c.Name.Equals(current.ProxyContainer, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && existing.Image.StartsWith(ProxyRepository, StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"Container {current.ProxyContainer} ({existing.Image}) was there.");
            return false;
        }

        if (existing is not null)
        {
            await containers.RemoveAsync(existing.Id, force: true, cancellationToken);
            notes.Add($"Container {current.ProxyContainer} ran {existing.Image}, which has no login: replaced by {ProxyImage}.");
        }

        var run = await containers.RunAsync(new ContainerLaunchRequest
        {
            Image = ProxyImage,
            Name = current.ProxyContainer,
            Publish = [$"127.0.0.1:{PublishedPort}:80", $"127.0.0.1:{PublishedPlugin.AdminPort}:{PublishedPlugin.AdminPort}"],
            Volumes = [$"{current.DataFolder.Replace('\\', '/')}:{DataMount}"],
            ConnectNetworks = [current.Network],
            RestartPolicy = RestartPolicyInfo.Always,
            Start = true,
        }, cancellationToken);
        notes.Add($"Container {current.ProxyContainer} ({ProxyImage}) running: published names on 127.0.0.1:{PublishedPort}, its own page on 127.0.0.1:{PublishedPlugin.AdminPort}, on {current.Network}, its data in {current.DataFolder}, restarted always.");
        notes.AddRange(run.Notes);
        return true;
    }

    /// <summary>
    /// Until the plugin answers: a proxy just run listens a few seconds later, and its
    /// token file may already be there from the one before it, so the file says nothing.
    /// </summary>
    private async Task WaitForPluginAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + PluginWait;
        while (true)
        {
            try
            {
                await plugin.SitesAsync(cancellationToken);
                return;
            }
            catch (InvalidOperationException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500, cancellationToken);
            }
        }
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
