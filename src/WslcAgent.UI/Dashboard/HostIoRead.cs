using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The containers' aggregate disk and network I/O, as the CLI's cumulative
/// counters, read for the host's disk and network activity: their readings
/// and their charts. Not asked while the session is stopped.
/// </summary>
public sealed class HostIoRead(WslcAgentApi api, SessionState session)
    : SampledRead<HomeIo>(TimeSpan.FromSeconds(5))
{
    protected override bool CanRead => session.Active;

    protected override async Task<HomeIo?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetHomeIoAsync(cancellationToken);

    protected override bool Failed(HomeIo sample) => sample.Error;
}
