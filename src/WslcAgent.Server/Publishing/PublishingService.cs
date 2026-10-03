using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Publishing;

/// <summary>
/// Publish and unpublish: the agent keeps what is published (the store) and tells the
/// wslc-published plugin, which writes and reloads nginx, checks whether each
/// application asks for a login of its own and keeps each name's users
/// (<see cref="IPublishedPlugin"/>). The network is the user's, never the agent's:
/// the proxy reaches a container by name only on a user-defined network they share,
/// and the container is put on one through the form's Networks rows; a row with
/// Reverse proxy ticked on a container that shares none with the proxy is an error,
/// before anything is touched. Settings names a default network, the one the form
/// offers to add. The launch form's rows come through <see cref="ValidateRowsAsync"/>
/// before a container is touched and <see cref="ApplyAsync"/> once it exists.
/// </summary>
public sealed partial class PublishingService(
    PublicationStore store,
    PublishingSettingsStore settings,
    INetworkService networks,
    IPublishedPlugin plugin,
    ILogger<PublishingService> logger) : IPublishingService
{
    /// <summary>Every published port, with who the proxy lets in when it answers; without, when it does not.</summary>
    public async Task<IReadOnlyList<Publication>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PluginSite> sites;
        try
        {
            sites = await plugin.SitesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation("publications: the proxy did not say who it lets in: {Message}", ex.Message);
            sites = [];
        }

        return store.All().Select(p => Describe(p, sites.FirstOrDefault(s => s.Host == p.Hostname))).ToList();
    }

    /// <summary>Asks the application again whether it has a login of its own (a button, after fixing it); the publication as it stands.</summary>
    public async Task<Publication> CheckAsync(string hostname, CancellationToken cancellationToken = default)
    {
        var publication = store.Find(hostname.Trim().ToLowerInvariant()) ?? throw new KeyNotFoundException($"{hostname} is not published.");
        return Describe(publication, await plugin.CheckAsync(publication.Hostname, cancellationToken));
    }

    /// <summary>The proxy's login in front of the name even when its application has its own (two logins), or back to the check's choice.</summary>
    public async Task<Publication> SetForceLoginAsync(string hostname, bool force, CancellationToken cancellationToken = default)
    {
        var publication = store.Find(hostname.Trim().ToLowerInvariant()) ?? throw new KeyNotFoundException($"{hostname} is not published.");
        var site = await plugin.ForceLoginAsync(publication.Hostname, force, cancellationToken);
        logger.LogInformation("{Hostname}: proxy login forced={Force}", publication.Hostname, force);
        return Describe(publication, site);
    }

    public async Task<Publication> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
    {
        var current = settings.Get();
        var publication = Validate(request, current);
        RefuseIfTaken(publication);
        await RequireOnNetworkAsync(publication.Container, current.Network, cancellationToken);
        await plugin.PutSiteAsync(publication, cancellationToken);
        store.Add(publication);
        logger.LogInformation("published {Hostname} -> {Container}:{Port}", publication.Hostname, publication.Container, publication.ContainerPort);
        return publication;
    }

    public async Task UnpublishAsync(string hostname, CancellationToken cancellationToken = default)
    {
        var key = hostname.Trim().ToLowerInvariant();
        if (store.Find(key) is null)
        {
            throw new KeyNotFoundException($"{hostname} is not published.");
        }

        await plugin.RemoveSiteAsync(key, cancellationToken);
        store.Remove(key);
        logger.LogInformation("unpublished {Hostname}", hostname);
    }

    /// <summary>Every published name handed to the plugin again: what Set up does for a proxy just made.</summary>
    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        var all = store.All();
        foreach (var publication in all)
        {
            await plugin.PutSiteAsync(publication, cancellationToken);
        }

        return all.Count;
    }

    /// <summary>The form's rows read back for a container: what View &amp; edit shows.</summary>
    public IReadOnlyList<string> RowsOf(string container)
    {
        var current = settings.Get();
        return store.ForContainer(container).Select(p => PublicNameRow.Format(p, current)).ToList();
    }

    /// <summary>
    /// Before a container is created, run or recreated with public names: every
    /// row well formed, no name held by another container, the publishing
    /// network among the form's networks, and that network there. A save that
    /// would fail must fail here, while nothing has been changed.
    /// </summary>
    public async Task ValidateRowsAsync(string container, ContainerLaunchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PublicNames.Count == 0)
        {
            return;
        }

        var current = settings.Get();
        foreach (var publication in Desired(container, request.PublicNames, current))
        {
            RefuseIfTaken(publication);
        }

        var networksOfForm = request.ConnectNetworks.Select(n => n.Split(' ', 2)[0].Trim()).Prepend(request.Network.Trim())
            .Where(n => n.Length > 0 && !NetworkSummary.IsBuiltIn(n.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var network in networksOfForm)
        {
            if (await HasAsync(network, current.ProxyContainer, cancellationToken))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Reverse proxy needs the container to share a user-defined network with the proxy container '{current.ProxyContainer}': "
            + "names resolve only there, never on the default bridge, and none of the form's networks has it. "
            + $"Add the network '{current.Network}' (Settings → Publish) in Networks, or untick Reverse proxy.");
    }

    /// <summary>
    /// After the container exists: the rows against its names in the store, the
    /// difference handed to the plugin.
    /// </summary>
    public async Task ApplyAsync(string container, IReadOnlyList<string> rows, CancellationToken cancellationToken = default)
    {
        var desired = Desired(container, rows, settings.Get());
        var existing = store.ForContainer(container);
        var removed = existing.Where(e => !desired.Any(d => d.Hostname == e.Hostname && d.ContainerPort == e.ContainerPort)).ToList();
        var added = desired.Where(d => !existing.Any(e => e.Hostname == d.Hostname && e.ContainerPort == d.ContainerPort)).ToList();
        if (removed.Count == 0 && added.Count == 0)
        {
            return;
        }

        foreach (var publication in removed)
        {
            await plugin.RemoveSiteAsync(publication.Hostname, cancellationToken);
            store.Remove(publication.Hostname);
        }

        foreach (var publication in added)
        {
            await plugin.PutSiteAsync(publication, cancellationToken);
            store.Add(publication);
        }

        logger.LogInformation("{Container}: {Added} name(s) published, {Removed} unpublished", container, added.Count, removed.Count);
    }

    private static Publication Describe(Publication publication, PluginSite? site) =>
        site is null ? publication : publication with { Access = site.Access, UserCount = site.Users.Count, AccessNote = site.Check, ForceLogin = site.ForceLogin };

    /// <summary>Whether a container is on a network; a network that is not there has nobody on it.</summary>
    private async Task<bool> HasAsync(string network, string container, CancellationToken cancellationToken)
    {
        try
        {
            var details = await networks.DetailsAsync(network, cancellationToken);
            return details.Containers.Any(c => c.Equals(container, StringComparison.OrdinalIgnoreCase) || c.StartsWith(container + " ", StringComparison.OrdinalIgnoreCase));
        }
        catch (WslcException)
        {
            return false;
        }
    }

    private static List<Publication> Desired(string container, IReadOnlyList<string> rows, PublishingSettings current) =>
        rows.Select(r => r.Trim()).Where(r => r.Length > 0)
            .Select(row => PublicNameRow.Parse(row, current))
            .Select(parsed => new Publication(container, parsed.Port, parsed.Hostname))
            .DistinctBy(p => p.Hostname)
            .ToList();

    /// <summary>One label under the domain, a real container port, a container name that is not a flag.</summary>
    private static Publication Validate(PublishRequest request, PublishingSettings current)
    {
        var container = WslcArgs.Require(request.Container, "container");
        if (request.ContainerPort is < 1 or > 65535)
        {
            throw new ArgumentException("The container port has to be between 1 and 65535.");
        }

        var hostname = request.Hostname.Trim().ToLowerInvariant();
        var suffix = "." + current.Domain;
        if (!hostname.EndsWith(suffix, StringComparison.Ordinal) || !Label().IsMatch(hostname[..^suffix.Length]))
        {
            throw new ArgumentException($"The name has to be one label under {current.Domain}, like webui-home{suffix}: letters, digits and hyphens.");
        }

        return new Publication(container, request.ContainerPort, hostname);
    }

    private void RefuseIfTaken(Publication publication)
    {
        if (store.Find(publication.Hostname) is { } taken && !taken.Container.Equals(publication.Container, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{publication.Hostname} already reaches {taken.Container}:{taken.ContainerPort}. Unpublish it first.");
        }
    }

    /// <summary>The network of Settings has to exist; a missing one is the user's to create, not the agent's to guess.</summary>
    private async Task<NetworkDetails> RequireNetworkAsync(string network, CancellationToken cancellationToken)
    {
        try
        {
            return await networks.DetailsAsync(network, cancellationToken);
        }
        catch (WslcException ex)
        {
            throw new InvalidOperationException($"Publishing needs the network '{network}' (Settings → Publishing), and it is not there: {ex.Message}");
        }
    }

    /// <summary>
    /// A name published directly (the API, an MCP tool, Settings → Add name) names
    /// no network, so the default one of Settings is where the container and the
    /// proxy have to meet; the form is the place for any other. The agent never
    /// attaches a container on its own.
    /// </summary>
    private async Task RequireOnNetworkAsync(string container, string network, CancellationToken cancellationToken)
    {
        var details = await RequireNetworkAsync(network, cancellationToken);
        bool On(string name) => details.Containers.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase) || c.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));
        if (!On(container))
        {
            throw new InvalidOperationException($"{container} is not on the network '{network}' (Settings → Publish): connect it first, in its View & edit's Networks.");
        }

        if (!On(settings.Get().ProxyContainer))
        {
            throw new InvalidOperationException($"The proxy container '{settings.Get().ProxyContainer}' is not on the network '{network}' (Settings → Publish), so it cannot reach {container} by name: connect it first.");
        }
    }

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex Label();
}
