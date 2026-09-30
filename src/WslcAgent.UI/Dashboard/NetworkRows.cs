using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The network list, read for the dashboard's objects of type <c>network</c>,
/// with what every container has received and sent, which a network's dials
/// are a share of. Not asked while the session is stopped.
/// </summary>
public sealed class NetworkRows(WslcAgentApi api, SessionState session)
    : RegistryRows<NetworkListResponse, NetworkSummary>(TimeSpan.FromSeconds(5))
{
    public override int UidOf(NetworkSummary row) => row.Uid;

    public override string NameOf(NetworkSummary row) => row.Name;

    protected override IReadOnlyList<NetworkSummary> RowsOf(NetworkListResponse list) => list.Networks;

    protected override bool CanRead => session.Active;

    protected override async Task<NetworkListResponse?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetNetworksAsync(cancellationToken);
}
