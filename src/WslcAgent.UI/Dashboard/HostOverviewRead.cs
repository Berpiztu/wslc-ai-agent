using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The overview (<c>GET /api/v1/home</c>), read for the host's counts: how
/// many containers run of how many, and how many images, networks and
/// volumes there are. A stopped session is not asked — asking would open it
/// again — and keeps what it had.
/// </summary>
public sealed class HostOverviewRead(WslcAgentApi api, SessionState session)
    : SharedRead<HomeOverview>(TimeSpan.FromSeconds(5))
{
    protected override bool CanRead => session.Active;

    protected override async Task<HomeOverview?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetHomeOverviewAsync(cancellationToken);
}
