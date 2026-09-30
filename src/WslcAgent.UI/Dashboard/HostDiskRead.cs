using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The drive the sessions' VHDX files are on, read for the host's disk
/// reading, dial and alarm. Asked whatever the session: the drive is Windows'.
/// </summary>
public sealed class HostDiskRead(WslcAgentApi api)
    : SharedRead<HomeDisk>(TimeSpan.FromSeconds(5))
{
    protected override async Task<HomeDisk?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetHomeDiskAsync(cancellationToken);
}
