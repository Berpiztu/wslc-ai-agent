using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>
/// A container port on a public name. Publishing puts a container on the
/// internet, so it runs only with the user's approval (<see cref="ApprovalGate"/>);
/// unpublishing takes it off again and is free.
/// </summary>
[McpServerToolType]
public static class PublishingTools
{
    [McpServerTool(Name = "list_publications", ReadOnly = true)]
    [Description("The container ports published on a public name: container, its internal port, the hostname and the https URL that opens it from anywhere.")]
    public static Task<IReadOnlyList<Publication>> ListPublications(IPublishingService publishing, CancellationToken cancellationToken = default) =>
        publishing.ListAsync(cancellationToken);

    [McpServerTool(Name = "publish_container_port", Destructive = true)]
    [Description("Publish one container port on a public name (one label under the agent's domain, e.g. webui-home.example.com): the proxy (the wslc-published plugin) serves it at once. The container has to be on the publishing network already (connect_container_to_network); the agent never attaches it on its own. This puts the container on the internet: an application that asks for a login of its own is let through, any other gets the proxy's login, whose first administrator is created at the address with the name's claim code (the user hands it over; no tool reads it). The first call answers with a confirm token and the action to approve; call again with confirm=<token> after the user approved.")]
    public static async Task<object> PublishContainerPort(
        IPublishingService publishing,
        ApprovalGate approvals,
        McpServer? server,
        [Description("Container name.")] string container,
        [Description("The port inside the container (e.g. 8080), not a host port.")] int containerPort,
        [Description("The full public name, one label under the domain.")] string hostname,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "publish_container_port", $"publish {container}:{containerPort} on the internet as https://{hostname}/", $"{container}:{containerPort}->{hostname}",
                new Dictionary<string, string> { ["container"] = container, ["containerPort"] = containerPort.ToString(System.Globalization.CultureInfo.InvariantCulture), ["hostname"] = hostname },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        return await publishing.PublishAsync(new PublishRequest(container, containerPort, hostname), cancellationToken);
    }

    [McpServerTool(Name = "setup_publishing")]
    [Description("The first use of publishing on this machine: creates the network and the proxy's data folder of the settings if they are missing, and runs the proxy container (ghcr.io/berpiztu/wslc-published: nginx and the login in front of the published names; its data folder mounted, port 8081 for the names and 8082 for its own page on the loopback, restarted always, on that network) if it is missing, or in place of a plain nginx one. What is there already is left alone; the answer says which was which. The tunnel and the VPS are not the agent's: docs/developer/remote-access.md.")]
    public static Task<PublishingSetupResult> SetupPublishing(IPublishingSetup setup, CancellationToken cancellationToken = default) =>
        setup.SetupAsync(cancellationToken);

    [McpServerTool(Name = "unpublish_hostname")]
    [Description("Take a public name off: it leaves the proxy's map and answers 404 from then on. The container and its network are left as they are.")]
    public static async Task<string> UnpublishHostname(
        IPublishingService publishing,
        [Description("The public name, as list_publications shows it.")] string hostname,
        CancellationToken cancellationToken = default)
    {
        await publishing.UnpublishAsync(hostname, cancellationToken);
        return $"unpublished {hostname}";
    }
}
