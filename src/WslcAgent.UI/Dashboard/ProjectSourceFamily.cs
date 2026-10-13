using Berpiztu.Dashboard.Fields;
using Berpiztu.Dashboard.Sources;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The Compose projects, as a family objects take their source from. A
/// project is not one of the agent's numbered resources: it is the containers
/// that carry its name, so the family is read from the containers' list, and
/// a source's value is the project's name behind what its row's key begins
/// with in the Containers list (<see cref="ProjectVerbs.RowPrefix"/>). No
/// number is ever that, so nothing takes it for a registry uid, and a
/// dashboard taken to another agent reads the project of the same name there.
/// </summary>
public sealed class ProjectSourceFamily(ContainerRows rows) : ISourceFamily
{
    /// <summary>The key objects of type project name in their descriptor.</summary>
    public const string FamilyKey = "project";

    public string Key => FamilyKey;

    public string Label => "Project";

    public string Noun => "project";

    /// <summary>Read again each time the list opens, so one just saved is there; by name.</summary>
    public async Task<IReadOnlyList<FieldOption<string>>> ListAsync()
    {
        await rows.RefreshAsync();
        return [.. (rows.Rows ?? [])
            .Select(container => container.Project)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new FieldOption<string>(ProjectSource.Of(name), name))];
    }
}

/// <summary>What a project object's source value holds.</summary>
public static class ProjectSource
{
    /// <summary>The source value of the project of this name.</summary>
    public static string Of(string name) => ProjectVerbs.RowPrefix + name;

    /// <summary>The project a source value names; null for none, or for one that is not a project's.</summary>
    public static string? Name(string? value) =>
        value is not null && value.StartsWith(ProjectVerbs.RowPrefix, StringComparison.Ordinal) && value.Length > ProjectVerbs.RowPrefix.Length
            ? value[ProjectVerbs.RowPrefix.Length..]
            : null;
}
