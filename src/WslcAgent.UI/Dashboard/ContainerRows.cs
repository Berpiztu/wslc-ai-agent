using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The container list, read for the dashboard's objects of type
/// <c>container</c>. A stopped session is not asked — asking would open it
/// again — and keeps the rows it had.
/// </summary>
public sealed class ContainerRows(WslcAgentApi api, SessionState session)
    : RegistryRows<IReadOnlyList<ContainerSummary>, ContainerSummary>(TimeSpan.FromSeconds(5))
{
    public override int UidOf(ContainerSummary row) => row.Uid;

    public override string NameOf(ContainerSummary row) => row.Name;

    protected override IReadOnlyList<ContainerSummary> RowsOf(IReadOnlyList<ContainerSummary> list) => list;

    protected override bool CanRead => session.Active;

    protected override async Task<IReadOnlyList<ContainerSummary>?> ReadAsync(CancellationToken cancellationToken) =>
        (await api.GetContainersAsync(all: true, cancellationToken: cancellationToken)).Containers;
}
