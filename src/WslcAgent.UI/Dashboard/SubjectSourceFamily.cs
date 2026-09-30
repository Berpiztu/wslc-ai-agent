using Berpiztu.Dashboard.Fields;
using Berpiztu.Dashboard.Sources;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// What a chart, its legend and its summary show: the host, or one of the
/// user's containers, the
/// containers' own list, by registry uid as a container object's source is.
/// </summary>
public sealed class SubjectSourceFamily(ContainerSourceFamily containers) : ISourceFamily
{
    /// <summary>The key a chart's objects name in their descriptor.</summary>
    public const string FamilyKey = "subject";

    /// <summary>The host's value, a chart's default.</summary>
    public const string Host = "host";

    public string Key => FamilyKey;

    public string Label => "Subject";

    public string Noun => "subject";

    /// <summary>The host first, then every container, as the containers' family lists them.</summary>
    public async Task<IReadOnlyList<FieldOption<string>>> ListAsync() =>
        [new(Host, "Host"), .. await containers.ListAsync()];
}
