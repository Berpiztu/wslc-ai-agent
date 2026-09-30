using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>Read and additive tools for networks. Remove and prune are destructive and come with the gated tools.</summary>
[McpServerToolType]
public static class NetworkTools
{
    [McpServerTool(Name = "list_networks", ReadOnly = true)]
    [Description("List WSLC networks on the selected session: id, name, driver, scope, subnet, gateway, internal and whether a container is attached. bridge, host and none are built in.")]
    public static async Task<IReadOnlyList<NetworkSummary>> ListNetworks(INetworkService networks, CancellationToken cancellationToken = default) =>
        (await networks.ListAsync(cancellationToken)).Networks;

    [McpServerTool(Name = "inspect_network", ReadOnly = true)]
    [Description("A network's own facts: driver, subnet, gateway, options and labels, plus the containers attached to it with their addresses.")]
    public static Task<NetworkDetails> InspectNetwork(
        INetworkService networks,
        [Description("Network name.")] string name,
        CancellationToken cancellationToken = default) =>
        networks.DetailsAsync(name, cancellationToken);

    [McpServerTool(Name = "network_topology", ReadOnly = true)]
    [Description("The whole map at once: every network with the containers attached to it, and every container with the addresses it holds on each. Use it to answer who can reach whom without inspecting one network at a time.")]
    public static Task<NetworkTopology> Topology(
        INetworkTopology topology,
        CancellationToken cancellationToken = default) =>
        topology.TopologyAsync(cancellationToken);

    [McpServerTool(Name = "remove_network", Destructive = true)]
    [Description("Remove a network (wslc network remove); containers attached to it lose it. Destructive: it asks for the user's approval first, through their client's prompt or a confirm token.")]
    public static async Task<object> RemoveNetwork(
        INetworkService networks,
        ApprovalGate approvals,
        McpServer? server,
        [Description("Network name.")] string name,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "remove_network", $"remove network {name}", name,
                new Dictionary<string, string> { ["network"] = name },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        await networks.RemoveAsync(name, cancellationToken);
        return $"removed {name}";
    }

    [McpServerTool(Name = "create_network")]
    [Description("Create a network (wslc network create). Driver bridge by default; subnet, gateway and ip range in CIDR/IP form; internal restricts external access.")]
    public static async Task<string> CreateNetwork(
        INetworkService networks,
        [Description("Network name.")] string name,
        [Description("Driver; empty for bridge.")] string driver = "",
        [Description("Subnet in CIDR form, e.g. 172.28.0.0/16, or empty.")] string subnet = "",
        [Description("Gateway address, or empty.")] string gateway = "",
        [Description("Container ip range in CIDR form, or empty.")] string ipRange = "",
        [Description("Restrict external access.")] bool @internal = false,
        [Description("One label as key=value, or empty.")] string label = "",
        [Description("One driver option as key=value, or empty.")] string option = "",
        CancellationToken cancellationToken = default)
    {
        await networks.CreateAsync(new CreateNetworkRequest(name, driver, subnet, gateway, ipRange, @internal, label, option), cancellationToken);
        return $"created network {name}";
    }

    [McpServerTool(Name = "connect_container_to_network")]
    [Description("Attach a container to a network (wslc network connect). A static ip is honoured on user-defined networks only.")]
    public static async Task<string> ConnectContainer(
        INetworkService networks,
        [Description("Network name.")] string network,
        [Description("Container name or id.")] string container,
        [Description("Static IPv4 address, or empty.")] string ip = "",
        CancellationToken cancellationToken = default)
    {
        await networks.ConnectAsync(network, container, ip, cancellationToken);
        return $"connected {container} to {network}";
    }

    [McpServerTool(Name = "disconnect_container_from_network")]
    [Description("Detach a container from a network (wslc network disconnect).")]
    public static async Task<string> DisconnectContainer(
        INetworkService networks,
        [Description("Network name.")] string network,
        [Description("Container name or id.")] string container,
        CancellationToken cancellationToken = default)
    {
        await networks.DisconnectAsync(network, container, cancellationToken);
        return $"disconnected {container} from {network}";
    }
}
