using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The files on the move in or out of any container, read for the
/// dashboard's container headers, which wear a container's transfers as a
/// ring where its avatar goes. The agent keeps them whatever the session, so
/// it is asked even while the session is stopped.
/// </summary>
public sealed class TransferRows(WslcAgentApi api)
    : SharedRead<IReadOnlyList<ContainerTransfer>>(TimeSpan.FromSeconds(5))
{
    protected override async Task<IReadOnlyList<ContainerTransfer>?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetContainerTransfersAsync(cancellationToken);
}
