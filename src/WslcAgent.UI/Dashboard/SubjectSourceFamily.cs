using Berpiztu.Dashboard.Fields;
using Berpiztu.Dashboard.Sources;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// What a chart, its legend and its summary show: the host, a Compose
/// project (its containers added up), by its project source value as a
/// project object's source is, or one of the user's containers, by registry
/// uid as a container object's source is.
/// </summary>
public sealed class SubjectSourceFamily(ContainerSourceFamily containers, ProjectSourceFamily projects) : ISourceFamily
{
    /// <summary>The key a chart's objects name in their descriptor.</summary>
    public const string FamilyKey = "subject";

    /// <summary>The host's value, a chart's default.</summary>
    public const string Host = "host";

    public string Key => FamilyKey;

    public string Label => "Subject";

    public string Noun => "subject";

    /// <summary>The host first, then every project, named as its cards name it, then every container, as the containers' family lists them.</summary>
    public async Task<IReadOnlyList<FieldOption<string>>> ListAsync() =>
        [
            new(Host, "Host"),
            .. (await projects.ListAsync()).Select(project => project with { Text = ProjectRow.Titled(project.Text) }),
            .. await containers.ListAsync(),
        ];
}
