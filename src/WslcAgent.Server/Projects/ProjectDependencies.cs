using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Projects;

/// <summary>
/// What the services of a project depend on (<c>depends_on</c>), by the
/// Compose file kept with the project, and the rule that follows from it: a
/// container of a project is not started while a service its own depends on
/// is not running, and is not stopped while one that depends on its own
/// still runs. The rule holds however the verb is asked for, the container's
/// own as much as the project's, since it is the container's start and stop
/// that ask it; the project's verbs go through its containers in the order
/// that satisfies it.
/// </summary>
public sealed class ProjectDependencies(ProjectStore store, IProjectService projects, IWslcRunner wslc)
{
    /// <summary>The file kept for the project, read again; null when none is kept, or it no longer reads (its folder gone, a variable it needs not set).</summary>
    public async Task<ComposePlan?> PlanAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            return store.Read(name) is { } stored
                ? await projects.PlanAsync(new ComposePlanRequest(Yaml: stored.Yaml, Name: stored.Name, Variables: stored.Variables, Profiles: stored.Profiles, Folder: stored.Folder), cancellationToken)
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return null;
        }
    }

    /// <summary>The services a service depends on, by the file kept; none when no file is kept.</summary>
    public static IEnumerable<string> Needs(ComposePlan? plan, string service) =>
        plan?.Services.FirstOrDefault(other => other.Name == service)?.DependsOn.Select(dependency => dependency.Service) ?? [];

    /// <summary>
    /// Refuses the start of a project's container while a service its own
    /// depends on is not running, saying which. A container on its own, or of
    /// a project with no file kept, depends on nothing here. With no project
    /// kept at all nothing is asked of <c>wslc</c>.
    /// </summary>
    public async Task RequireRunningAsync(string container, CancellationToken cancellationToken = default)
    {
        var (starting, rows) = await FindAsync(container, cancellationToken);
        if (starting is null)
        {
            return;
        }

        foreach (var needed in Needs(await PlanAsync(starting.Project, cancellationToken), starting.Service))
        {
            var dependency = rows.FirstOrDefault(row => row.Project == starting.Project && row.Service == needed);
            if (dependency is not { IsRunning: true })
            {
                throw new InvalidOperationException(
                    $"{starting.Name} depends on {needed}, which is not running: start {dependency?.Name ?? needed} first, or start the project {starting.Project}.");
            }
        }
    }

    /// <summary>
    /// Refuses the stop of a project's container while another of the
    /// project, whose service depends on its own, still runs, saying which:
    /// that one cannot go on without it, and is to be stopped first.
    /// </summary>
    public async Task RequireNoDependentsAsync(string container, CancellationToken cancellationToken = default)
    {
        var (stopping, rows) = await FindAsync(container, cancellationToken);
        if (stopping is null)
        {
            return;
        }

        var plan = await PlanAsync(stopping.Project, cancellationToken);
        var dependents = rows
            .Where(row => row.Project == stopping.Project && row.IsRunning && Needs(plan, row.Service).Contains(stopping.Service))
            .Select(row => row.Name)
            .ToList();
        if (dependents.Count > 0)
        {
            throw new InvalidOperationException(
                $"{stopping.Name} is depended on by {string.Join(", ", dependents)}, still running: stop {(dependents.Count == 1 ? "it" : "them")} first, or stop the project {stopping.Project}.");
        }
    }

    /// <summary>The container among those of the machine, when it is a project's; null for one on its own. With no project kept at all nothing is asked of <c>wslc</c>.</summary>
    private async Task<(ContainerSummary? Container, List<ContainerSummary> Rows)> FindAsync(string container, CancellationToken cancellationToken)
    {
        if (!store.Any())
        {
            return (null, []);
        }

        var result = await wslc.RunAsync(["container", "list", "--all", "--format", "json"], cancellationToken: cancellationToken);
        var rows = WslcJson.ParseRows(result.Stdout).Select(ContainerService.ToSummary).ToList();
        var found = rows.FirstOrDefault(row => row.Name == container || (row.Id.Length > 0 && container.StartsWith(row.Id, StringComparison.Ordinal)));
        return (found is { Project.Length: > 0 } ? found : null, rows);
    }
}
