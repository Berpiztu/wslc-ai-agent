using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>The networks, as a family objects take their source from.</summary>
public sealed class NetworkSourceFamily(NetworkRows rows) : RegistrySourceFamily<NetworkListResponse, NetworkSummary>(rows)
{
    /// <summary>The key objects of type network name in their descriptor.</summary>
    public const string FamilyKey = "network";

    public override string Key => FamilyKey;

    public override string Label => "Network";

    public override string Noun => "network";
}
