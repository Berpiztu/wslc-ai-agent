using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Projects;

/// <summary>
/// The verbs on a whole project once it is on the machine: start, stop and
/// restart of its containers, in the order its services start in (the
/// reverse to stop), and down, which removes them, the project's networks
/// and, only when asked, its volumes. The containers are the ones that carry
/// the project's label; the order, the networks and the volumes come from the
/// Compose file kept with the project (<see cref="ProjectStore"/>). One
/// container that fails does not stop the others: the verb goes through the
/// whole project and then fails as one, naming each that did not go. The
/// exception is what depends on it: a service is not started when one it
/// depends on did not start.
/// </summary>
public sealed class ProjectLifecycle(
    ProjectDependencies dependencies,
    IContainerService containers,
    INetworkService networks,
    IVolumeService volumes,
    ProjectStore store,
    WslcEvents events)
{
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Restart = "restart";

    /// <summary>The Compose file kept for the project, for whoever opens it.</summary>
    public StoredProject Stored(string name) =>
        store.Read(name) ?? throw new KeyNotFoundException($"No Compose file is kept for project {name}.");

    /// <summary>
    /// The project's containers in the order its services start in, each with
    /// the services it depends on: what a client needs to go through them one
    /// by one itself, showing which one it is on.
    /// </summary>
    public async Task<IReadOnlyList<ProjectMember>> ContainersAsync(string name, CancellationToken cancellationToken = default)
    {
        var plan = await dependencies.PlanAsync(name, cancellationToken);
        return [.. (await MembersAsync(name, plan, cancellationToken))
            .Select(member => new ProjectMember(member.Id, member.Name, member.Service, member.IsRunning, [.. ProjectDependencies.Needs(plan, member.Service)]))];
    }

    public async Task<ProjectResult> VerbAsync(string name, string verb, CancellationToken cancellationToken = default)
    {
        var plan = await dependencies.PlanAsync(name, cancellationToken);
        var members = await MembersAsync(name, plan, cancellationToken);
        if (verb == Stop)
        {
            members.Reverse();
        }

        var done = new List<string>();
        var failed = new List<string>();

        // The services that did not come up: what depends on one of them is not started, as Compose does not.
        var down = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (verb != Stop && ProjectDependencies.Needs(plan, member.Service).FirstOrDefault(down.Contains) is { } missing)
            {
                failed.Add($"{member.Name}: not started, it depends on {missing}, which did not start");
                down.Add(member.Service);
                continue;
            }

            var failures = failed.Count;
            await TryAsync(member.Name, done, failed, () => verb switch
            {
                Start => member.IsRunning ? Task.CompletedTask : containers.StartAsync(member.Id, cancellationToken),
                Stop => member.IsRunning ? containers.StopAsync(member.Id, cancellationToken) : Task.CompletedTask,
                // A restart is of what runs: a container left stopped was stopped for a reason, and stays so.
                Restart => member.IsRunning ? containers.RestartAsync(member.Id, cancellationToken) : Task.CompletedTask,
                _ => throw new ArgumentException($"Not a project's verb: {verb}"),
            });
            if (failed.Count > failures)
            {
                down.Add(member.Service);
            }
        }

        return Result(name, verb, done, failed);
    }

    /// <summary>
    /// Removes the project: its containers, then its networks, then, when
    /// <paramref name="removeVolumes"/> says so and never otherwise, its
    /// volumes; and the file kept for it once nothing of it failed to go.
    /// Networks and volumes external to the project are not its own, and stay.
    /// </summary>
    public async Task<ProjectResult> DownAsync(string name, bool removeVolumes, CancellationToken cancellationToken = default)
    {
        var plan = await dependencies.PlanAsync(name, cancellationToken);
        var members = await MembersAsync(name, plan, cancellationToken);
        members.Reverse();

        var done = new List<string>();
        var failed = new List<string>();
        foreach (var member in members)
        {
            await TryAsync(member.Name, done, failed, () => containers.RemoveAsync(member.Id, force: true, cancellationToken));
        }

        foreach (var network in plan?.Networks.Where(network => !network.External) ?? [])
        {
            await TryAsync($"network {network.Name}", done, failed, () => networks.RemoveAsync(network.Name, cancellationToken));
        }

        foreach (var volume in removeVolumes ? plan?.Volumes.Where(volume => !volume.External) ?? [] : [])
        {
            await TryAsync($"volume {volume.Name}", done, failed, () => volumes.RemoveAsync(volume.Name, cancellationToken));
        }

        if (failed.Count == 0)
        {
            store.Forget(name);
        }

        return Result(name, "down", done, failed);
    }

    /// <summary>What the verb went through on; what it did not is the verb's failure, after every client was told to look again.</summary>
    private ProjectResult Result(string name, string verb, List<string> done, List<string> failed)
    {
        events.Publish(new ChangeNotice([ChangeNotice.Container]));
        return failed.Count == 0
            ? new ProjectResult(name, done)
            : throw new InvalidOperationException($"Project {name}: {verb} left {failed.Count} of {done.Count + failed.Count} undone.\n{string.Join('\n', failed)}");
    }

    private static async Task TryAsync(string what, List<string> done, List<string> failed, Func<Task> action)
    {
        try
        {
            await action();
            done.Add(what);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ArgumentException)
        {
            // Whatever the CLI refused, the rest of the project is still gone through.
            failed.Add($"{what}: {ex.Message}");
        }
    }

    /// <summary>The project's containers, in the order its services start in; by name where the file kept does not say.</summary>
    private async Task<List<ContainerSummary>> MembersAsync(string name, ComposePlan? plan, CancellationToken cancellationToken)
    {
        var members = (await containers.ListAsync(all: true, cancellationToken: cancellationToken)).Containers
            .Where(container => container.Project == name)
            .ToList();
        if (members.Count == 0 && plan is null)
        {
            throw new KeyNotFoundException($"No project {name} on this machine.");
        }

        var order = plan?.Services.Select(service => service.Name).ToList() ?? [];
        int Place(ContainerSummary member) => order.IndexOf(member.Service) is >= 0 and var place ? place : int.MaxValue;
        return [.. members.OrderBy(Place).ThenBy(member => member.Name, StringComparer.Ordinal)];
    }
}
