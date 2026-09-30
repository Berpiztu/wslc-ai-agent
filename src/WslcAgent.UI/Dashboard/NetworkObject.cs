using Microsoft.AspNetCore.Components;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>An object of type <c>network</c>: its source is a network, and it reads the network list.</summary>
public abstract class NetworkObject : RegistryObject<NetworkListResponse, NetworkSummary>
{
    [Inject] protected NetworkRows Networks { get; set; } = default!;

    protected override RegistryRows<NetworkListResponse, NetworkSummary> Rows => Networks;

    /// <summary>The network's row; null until the list is read, or while its network is not in it.</summary>
    protected NetworkSummary? Network => Resource;
}
