using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The volume list, read for the dashboard's objects of type <c>volume</c>,
/// with what every container has read and written, which a volume's dials are
/// a share of. Not asked while the session is stopped.
/// </summary>
public sealed class VolumeRows(WslcAgentApi api, SessionState session)
    : RegistryRows<VolumeListResponse, VolumeSummary>(TimeSpan.FromSeconds(5))
{
    public override int UidOf(VolumeSummary row) => row.Uid;

    public override string NameOf(VolumeSummary row) => row.Name;

    protected override IReadOnlyList<VolumeSummary> RowsOf(VolumeListResponse list) => list.Volumes;

    protected override bool CanRead => session.Active;

    protected override async Task<VolumeListResponse?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetVolumesAsync(cancellationToken);
}
