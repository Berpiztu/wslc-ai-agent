using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// wslc's version, Windows and the kernel, from the System page's own
/// reading, which opens no session: for the System card's readings, read when
/// an object first follows it and then seldom.
/// </summary>
public sealed class SystemInfoRead(WslcAgentApi api) : SharedRead<SystemOverview>(TimeSpan.FromMinutes(10))
{
    protected override async Task<SystemOverview?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetSystemAsync(cancellationToken);
}
