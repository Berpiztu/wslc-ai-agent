namespace WslcAgent.ApiClient.Contracts;

/// <summary>One row of <c>GET /api/v1/networks</c>: the list row plus the subnet and gateway from <c>inspect</c>.</summary>
/// <param name="Id">Short network id (12 hex characters).</param>
/// <param name="Name">Network name; <c>bridge</c>, <c>host</c> and <c>none</c> are built in.</param>
/// <param name="Driver">Network driver, normally <c>bridge</c>.</param>
/// <param name="Scope">Network scope, normally <c>local</c>.</param>
/// <param name="Subnet">First IPAM subnet in CIDR form, empty when none.</param>
/// <param name="Gateway">First IPAM gateway, empty when none.</param>
/// <param name="Internal">External access restricted.</param>
/// <param name="InUse">Some container (any state) is attached.</param>
/// <param name="Containers">How many containers, any state, are attached to the network.</param>
/// <param name="Running">How many of those are running.</param>
/// <param name="NetReceivedBytes">What the containers on it have received, together (each container's whole figure from <c>stats</c>): the card's Received dial.</param>
/// <param name="NetSentBytes">What they have sent, together: the card's Sent dial.</param>
/// <param name="Uid">The agent's own number for this network, kept through a recreate (the resource registry): what a resource card on the dashboard points at.</param>
public sealed record NetworkSummary(
    string Id,
    string Name,
    string Driver,
    string Scope,
    string Subnet,
    string Gateway,
    bool Internal,
    bool InUse,
    int Containers,
    int Running,
    long NetReceivedBytes,
    long NetSentBytes,
    int Uid = 0)
{
    /// <summary>WSLC accepts a static <c>--ip</c> only on a user-defined network.</summary>
    public bool AllowsStaticIp => !IsBuiltIn(Name);

    public static bool IsBuiltIn(string name) => name is "bridge" or "host" or "none";
}

/// <summary>Body of <c>GET /api/v1/networks</c>.</summary>
/// <param name="NetReceivedBytes">What every container has received: what a network's Received dial is a share of.</param>
/// <param name="NetSentBytes">What every container has sent: what a network's Sent dial is a share of.</param>
public sealed record NetworkListResponse(IReadOnlyList<NetworkSummary> Networks, int Count, long NetReceivedBytes, long NetSentBytes);

/// <summary>Body of <c>POST /api/v1/networks</c>. Empty strings mean "not given".</summary>
public sealed record CreateNetworkRequest(
    string Name,
    string Driver = "",
    string Subnet = "",
    string Gateway = "",
    string IpRange = "",
    bool Internal = false,
    string Label = "",
    string Option = "");

/// <summary>Body of <c>POST /api/v1/networks/{name}/connect</c> and <c>/disconnect</c>. <paramref name="Ip"/> is a static address for connect, used only on user-defined networks.</summary>
public sealed record NetworkContainerRequest(string Container, string Ip = "");

/// <summary>Body of <c>GET /api/v1/networks/{name}/details</c>: View &amp; edit.</summary>
/// <param name="Id">The full network id.</param>
/// <param name="Containers">Attached containers as <c>name (ip)</c>.</param>
/// <param name="Form">The create-time settings as the edit form holds them: labels and options as <c>KEY=value, …</c>.</param>
/// <param name="BuiltIn"><c>bridge</c>, <c>host</c> or <c>none</c>: shown, never recreated.</param>
/// <param name="Inspect">The raw inspect JSON, indented.</param>
public sealed record NetworkDetails(
    string Name,
    string Id,
    string Scope,
    string Created,
    IReadOnlyList<string> Containers,
    CreateNetworkRequest Form,
    bool BuiltIn,
    string Inspect);

/// <summary>Answer of <c>POST /api/v1/networks/{name}/recreate</c>.</summary>
/// <param name="Name">The replacement's name.</param>
/// <param name="Reattached">Containers connected to it again.</param>
/// <param name="ConnectFailed">Containers that could not be connected to it.</param>
public sealed record RecreateNetworkResult(string Name, int Reattached, IReadOnlyList<string> ConnectFailed);

/// <summary>Body of <c>GET /api/v1/networks/topology</c>: what the network map draws.</summary>
/// <param name="Networks">Every network, as the list shows it.</param>
/// <param name="Containers">Every container in any state (the agent's file helpers left out) with the networks it is on.</param>
public sealed record NetworkTopology(IReadOnlyList<NetworkSummary> Networks, IReadOnlyList<TopologyContainer> Containers);

/// <summary>A container on the network map.</summary>
/// <param name="State">Lower case, as inspect reports it: <c>running</c>, <c>exited</c>…</param>
public sealed record TopologyContainer(string Name, string Id, string State, IReadOnlyList<TopologyAttachment> Attachments);

/// <summary>One network a container is on, and its address there (empty when it has none yet).</summary>
public sealed record TopologyAttachment(string Network, string Ip);
