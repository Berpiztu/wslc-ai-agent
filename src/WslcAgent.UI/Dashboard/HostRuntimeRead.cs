using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The containers' aggregate CPU and memory, read for the host's readings,
/// dials, charts and alarms. Not asked while the session is stopped.
/// </summary>
public sealed class HostRuntimeRead(WslcAgentApi api, SessionState session)
    : SampledRead<HomeRuntime>(TimeSpan.FromSeconds(5))
{
    protected override bool CanRead => session.Active;

    protected override async Task<HomeRuntime?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetHomeRuntimeAsync(cancellationToken);

    protected override bool Failed(HomeRuntime sample) => sample.Error;
}
