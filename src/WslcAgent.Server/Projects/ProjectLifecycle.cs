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
    Containers.StartFailures failures,
    Images.ImageWarnings imageWarnings,
    Images.ImageSecurity security,
    ProjectGaps gaps,
    WslcEvents events)
{
    /// <summary>What a line a container wrote begins with when <c>wslc</c> stamps it: <c>2026-10-10T14:26:54.726523128Z</c> and a space.</summary>
    private const int StampLength = 30;
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
            .Select(member => new ProjectMember(member.Id, member.Name, member.Service, member.IsRunning, [.. ProjectDependencies.Needs(plan, member.Service)], Taken(plan, member.Service)))];
    }

    /// <summary>
    /// The service is one the project was saved for: of the profiles and the
    /// services its file was kept with. A start of the project is of these:
    /// what the user saved is what comes up again, and a container of the
    /// project that was left out of that save (another profile's, a service
    /// not among the ones asked for) is left as it is. With no file kept, or
    /// a service the file no longer has, there is nothing to go by, and the
    /// container is taken.
    /// </summary>
    private static bool Taken(ComposePlan? plan, string service) =>
        plan?.Services.FirstOrDefault(other => other.Name == service)?.Enabled ?? true;

    /// <summary>
    /// The project's logs as one: what each of its containers wrote, said by
    /// the container, in the order it was written, which is the order of the
    /// stamps <c>wslc</c> puts on each line. <paramref name="tail"/> is the
    /// last lines of each container (0: all). <paramref name="since"/>, a
    /// stamp one of these lines carried, keeps the lines after it, exactly:
    /// <c>wslc</c> tells seconds apart and no finer, so what it gives back of
    /// that second is dropped here, and whoever follows the logs gets each
    /// line once. <paramref name="services"/> keeps the containers of those.
    /// A container that could not start has nothing of its own to show, and
    /// says so here, in its place in time.
    /// </summary>
    public async Task<ProjectLogs> LogsAsync(string name, int tail, string since, IReadOnlyCollection<string> services, CancellationToken cancellationToken = default)
    {
        var plan = await dependencies.PlanAsync(name, cancellationToken);
        var members = (await MembersAsync(name, plan, cancellationToken)).Where(member => services.Count == 0 || services.Contains(member.Service));
        var lines = new List<ProjectLogLine>();

        foreach (var member in members)
        {
            string written;
            try
            {
                written = (await containers.LogsAsync(member.Id, tail, timestamps: true, cancellationToken, since)).Text;
            }
            catch (WslcException)
            {
                // Gone since the list was read, or with no log to give: the others still have theirs.
                written = "";
            }

            var time = "";
            foreach (var line in written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')))
            {
                // A line with no stamp of its own goes on from the one before it, and stands with it.
                var stamped = line.Length >= StampLength && line[10] == 'T' && line[StampLength - 1] == 'Z';
                time = stamped ? line[..StampLength] : time;
                lines.Add(new ProjectLogLine(member.Name, member.Service, time, stamped ? line[StampLength..].TrimStart(' ') : line));
            }

            // Made from an image with known vulnerabilities: what the check found, at the moment the container was made.
            if (imageWarnings.Of(member.Id) is { } warning)
            {
                var made = warning.At.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff00Z", System.Globalization.CultureInfo.InvariantCulture);
                lines.AddRange(warning.Lines.Select(found => new ProjectLogLine(member.Name, member.Service, made, $"WARNING {found}")));
            }

            if (failures.Of(member.Id) is { } failure)
            {
                lines.Add(new ProjectLogLine(member.Name, member.Service, failure.At.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff00Z", System.Globalization.CultureInfo.InvariantCulture),
                    $"The container could not start: {failure.Why}"));
            }
        }

        // Stamps of one form and one zone: in order as texts, they are in order in time. A stable
        // sort, so the lines of one container that share a stamp keep the order they were written in.
        var read = lines
            .Where(line => since.Length == 0 || string.CompareOrdinal(line.Time, since) > 0)
            .OrderBy(line => line.Time, StringComparer.Ordinal)
            .ToList();

        // Incomplete: what is missing and what holds its name, last on a whole reading, where the
        // view stands when it opens, so it is read without looking for it; the reading of the
        // lines since a moment does not repeat it.
        if (since.Length == 0 && gaps.Of(name) is { } gap)
        {
            read.Add(new ProjectLogLine(name, "", read.LastOrDefault()?.Time ?? "", $"{(gap.Blocked.Count > 0 ? "ERROR" : "WARNING")} {gap.Text}"));
        }

        return new ProjectLogs(read);
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
                Start => member.IsRunning || !Taken(plan, member.Service) ? Task.CompletedTask : containers.StartAsync(member.Id, cancellationToken),
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
    /// The project changed around the agent (<see cref="ProjectGapResponse"/>):
    /// what ran is stopped and started again, as an update of the project
    /// would, its dependents first down and its dependencies first up. A
    /// container whose dependency is missing from the project does not start
    /// again, whatever another container is named (<see cref="ProjectDependencies"/>),
    /// so it sends its work to nothing that is not its own; one left stopped
    /// stays so. One whose image the agent would not start again (built here,
    /// vulnerable, Block: <see cref="Images.ImageSecurity.GuardStartAsync"/>)
    /// is not touched and runs on: the check never stops what runs. The
    /// containers that did not start again, each with why.
    /// </summary>
    public async Task<IReadOnlyList<string>> RenewAsync(string name, CancellationToken cancellationToken = default)
    {
        var plan = await dependencies.PlanAsync(name, cancellationToken);
        var running = new List<ContainerSummary>();
        var done = new List<string>();
        var failed = new List<string>();
        foreach (var member in (await MembersAsync(name, plan, cancellationToken)).Where(member => member.IsRunning))
        {
            try
            {
                await security.GuardStartAsync(member.Id, cancellationToken, restart: true);
                running.Add(member);
            }
            catch (Images.ImageRefusedException ex)
            {
                failed.Add($"{member.Name}: left running, not started again: {ex.Said[0]}");
            }
        }

        foreach (var member in Enumerable.Reverse(running))
        {
            await TryAsync(member.Name, done, failed, () => containers.StopAsync(member.Id, cancellationToken));
        }

        foreach (var member in running)
        {
            await TryAsync(member.Name, done, failed, () => containers.StartAsync(member.Id, cancellationToken));
        }

        events.Publish(new ChangeNotice([ChangeNotice.Container]));
        return failed;
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
