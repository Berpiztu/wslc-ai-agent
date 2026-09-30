using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>The containers, as a family objects take their source from.</summary>
public sealed class ContainerSourceFamily(ContainerRows rows) : RegistrySourceFamily<IReadOnlyList<ContainerSummary>, ContainerSummary>(rows)
{
    /// <summary>The key objects of type container name in their descriptor.</summary>
    public const string FamilyKey = "container";

    public override string Key => FamilyKey;

    public override string Label => "Container";

    public override string Noun => "container";
}
