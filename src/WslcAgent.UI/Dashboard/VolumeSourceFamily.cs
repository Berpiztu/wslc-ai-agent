using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>The volumes, as a family objects take their source from.</summary>
public sealed class VolumeSourceFamily(VolumeRows rows) : RegistrySourceFamily<VolumeListResponse, VolumeSummary>(rows)
{
    /// <summary>The key objects of type volume name in their descriptor.</summary>
    public const string FamilyKey = "volume";

    public override string Key => FamilyKey;

    public override string Label => "Volume";

    public override string Noun => "volume";
}
