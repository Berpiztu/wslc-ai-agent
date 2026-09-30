using Microsoft.AspNetCore.Components;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>An object of type <c>volume</c>: its source is a volume, and it reads the volume list.</summary>
public abstract class VolumeObject : RegistryObject<VolumeListResponse, VolumeSummary>
{
    [Inject] protected VolumeRows Volumes { get; set; } = default!;

    protected override RegistryRows<VolumeListResponse, VolumeSummary> Rows => Volumes;

    /// <summary>The volume's row; null until the list is read, or while its volume is not in it.</summary>
    protected VolumeSummary? Volume => Resource;
}
