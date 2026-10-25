using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Projects;

/// <summary>
/// A project's services that have no container, by the file kept with it
/// (<see cref="ProjectStore"/>): it is incomplete, whoever took the container
/// away. <c>wslc</c> can be run around the agent (a script that removes a
/// container to run one of its own under the name), and nothing can stop
/// that; what the agent can do is see the state it left. Every member of the
/// project carries the gap (<see cref="ContainerSummary.MissingServices"/>),
/// and a container that holds the name the missing one had, of no project or
/// of another, says whose it is (<see cref="ContainerSummary.HoldsNameOf"/>):
/// it is what took its place. A gap that opens is written in the log once,
/// and once more when it closes; the project's logs and that container's
/// begin with it. A save of the project makes the missing containers again
/// from the file, once the name is free.
/// </summary>
public sealed class ProjectGaps(ProjectStore store, ProjectDependencies dependencies, ILogger<ProjectGaps> logger)
{
    private readonly Lock _gate = new();

    /// <summary>The gaps as last seen, by project, with when each opened: what the log said, and what the logs begin with.</summary>
    private readonly Dictionary<string, ProjectGap> _gaps = new(StringComparer.Ordinal);

    /// <summary>The projects being saved now, how many saves each: their containers are on their way, not missing.</summary>
    private readonly Dictionary<string, int> _saving = new(StringComparer.Ordinal);

    /// <summary>The kept file read, by project and the moment it was kept: its services' container names and what each depends on; read again only when it changes.</summary>
    private readonly Dictionary<string, (DateTimeOffset SavedAt, ComposePlan? Plan)> _plans = new(StringComparer.Ordinal);

    /// <summary>
    /// A gap opened, or changed, while no save of the project ran: the project
    /// was changed around the agent. Raised once per change, never per reading
    /// (<see cref="ProjectGapResponse"/> answers it).
    /// </summary>
    public event Action<ProjectGap>? Opened;

    /// <summary>A save of the project begins (<see cref="ProjectRunner"/>): until it ends, what it has not made yet is not a gap.</summary>
    public void Hold(string project)
    {
        lock (_gate)
        {
            _saving[project] = _saving.GetValueOrDefault(project) + 1;
        }
    }

    /// <summary>The save is over, however it ended: what it left missing is a gap from here on.</summary>
    public void Release(string project)
    {
        lock (_gate)
        {
            if (_saving.GetValueOrDefault(project) <= 1)
            {
                _saving.Remove(project);
            }
            else
            {
                _saving[project]--;
            }
        }
    }

    /// <summary>The project's gap as last seen; null when it is complete.</summary>
    public ProjectGap? Of(string project)
    {
        lock (_gate)
        {
            return _gaps.GetValueOrDefault(project);
        }
    }

    /// <summary>Why a container of an incomplete project cannot start, asked by its name or id: a service it depends on has no container in its project. Null when it can.</summary>
    public string? CannotStart(string container)
    {
        lock (_gate)
        {
            return _gaps.Values.SelectMany(gap => gap.Blocked)
                .FirstOrDefault(blocked => string.Equals(blocked.Name, container, StringComparison.OrdinalIgnoreCase) || Same(blocked.Id, container)).Why;
        }
    }

    /// <summary>The gap whose missing container's name a container holds, asked by that container's name or id; null when it holds none.</summary>
    public (ProjectGap Gap, string Service)? HeldBy(string container)
    {
        lock (_gate)
        {
            foreach (var gap in _gaps.Values)
            {
                foreach (var (service, holder) in gap.Holders)
                {
                    if (string.Equals(holder.Name, container, StringComparison.OrdinalIgnoreCase) || Same(holder.Id, container))
                    {
                        return (gap, service);
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The list's rows, each member of an incomplete project with what its
    /// project lacks, and each container holding a missing one's name with
    /// whose it is. Only a list of every container can tell a container is
    /// gone; any other is left as it is.
    /// </summary>
    public async Task<IReadOnlyList<ContainerSummary>> AnnotateAsync(IReadOnlyList<ContainerSummary> containers, bool complete, CancellationToken cancellationToken = default)
    {
        if (!complete)
        {
            return containers;
        }

        var projects = containers.Select(row => row.Project).Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);
        var gaps = new Dictionary<string, ProjectGap>(StringComparer.Ordinal);
        foreach (var project in projects.Where(project => !Saving(project)))
        {
            var missing = Missing(project, containers);
            if (missing.Count == 0)
            {
                continue;
            }

            var plan = await PlanAsync(project, cancellationToken);
            var holders = new Dictionary<string, ContainerSummary>(StringComparer.Ordinal);
            foreach (var service in missing)
            {
                if (plan?.Services.FirstOrDefault(other => other.Name == service)?.Launch.Name is { Length: > 0 } name
                    && containers.FirstOrDefault(row => row.Project != project && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase)) is { } holder)
                {
                    holders[service] = holder;
                }
            }

            // What depends on a missing service and is not running cannot start: it is said so, not only when a start is tried.
            var blocked = containers
                .Where(row => row.Project == project && !row.IsRunning)
                .Select(row => (row.Id, row.Name, Need: ProjectDependencies.Needs(plan, row.Service).FirstOrDefault(missing.Contains)))
                .Where(row => row.Need is not null)
                .Select(row => (row.Id, row.Name, Why: $"It cannot start: it depends on {row.Need}, which has no container in project {project}."))
                .ToList();
            gaps[project] = new ProjectGap(project, missing, holders, Of(project)?.Since ?? DateTimeOffset.Now, blocked);
        }

        Say(gaps, projects);
        if (gaps.Count == 0)
        {
            return containers;
        }

        return [.. containers.Select(row =>
        {
            if (gaps.TryGetValue(row.Project, out var gap))
            {
                row = row with { MissingServices = gap.Missing };
                if (row.StartError.Length == 0 && gap.Blocked.FirstOrDefault(blocked => blocked.Id == row.Id).Why is { } why)
                {
                    row = row with { StartError = why };
                }
            }

            var held = gaps.Values.SelectMany(gap => gap.Holders.Select(holder => (gap.Project, Service: holder.Key, Holder: holder.Value)))
                .FirstOrDefault(held => held.Holder.Id == row.Id);
            return held.Holder is null ? row : row with { HoldsNameOf = $"service {held.Service} of project {held.Project}" };
        })];
    }

    private bool Saving(string project)
    {
        lock (_gate)
        {
            return _saving.ContainsKey(project);
        }
    }

    /// <summary>The services the kept file gives the project that no container of it stands for; none when no file is kept.</summary>
    private List<string> Missing(string project, IReadOnlyList<ContainerSummary> containers)
    {
        StoredProject? kept;
        try
        {
            kept = store.Read(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            return [];
        }

        var present = containers.Where(row => row.Project == project).Select(row => row.Service).ToHashSet(StringComparer.Ordinal);
        return [.. (kept?.Services ?? []).Where(service => !present.Contains(service))];
    }

    /// <summary>The kept file read; null when it no longer reads.</summary>
    private async Task<ComposePlan?> PlanAsync(string project, CancellationToken cancellationToken)
    {
        var savedAt = store.Read(project)?.SavedAt ?? default;
        lock (_gate)
        {
            if (_plans.TryGetValue(project, out var kept) && kept.SavedAt == savedAt)
            {
                return kept.Plan;
            }
        }

        var plan = await dependencies.PlanAsync(project, cancellationToken);
        lock (_gate)
        {
            _plans[project] = (savedAt, plan);
        }

        return plan;
    }

    private static bool Same(string kept, string id) =>
        id.Length > 0 && kept.Length > 0 && (kept.StartsWith(id, StringComparison.OrdinalIgnoreCase) || id.StartsWith(kept, StringComparison.OrdinalIgnoreCase));

    /// <summary>A gap written once as it opens or changes, as a warning, and once as it closes; a project no longer listed at all is let go without a word.</summary>
    private void Say(Dictionary<string, ProjectGap> gaps, HashSet<string> projects)
    {
        var opened = new List<ProjectGap>();
        lock (_gate)
        {
            foreach (var (project, gap) in gaps)
            {
                if (_gaps.GetValueOrDefault(project) is not { } before || before.Text != gap.Text)
                {
                    logger.LogWarning("project {Project} is incomplete: {Gap}", project, gap.Text);
                    opened.Add(gap);
                }

                _gaps[project] = gap;
            }

            foreach (var closed in _gaps.Keys.Where(project => !gaps.ContainsKey(project) && !_saving.ContainsKey(project)).ToList())
            {
                _gaps.Remove(closed);
                if (projects.Contains(closed))
                {
                    logger.LogInformation("project {Project} is complete again", closed);
                }
            }
        }

        // Out of the lock: whoever answers reads the gaps again.
        foreach (var gap in opened)
        {
            Opened?.Invoke(gap);
        }
    }
}

/// <summary>A project's services with no container, the containers that hold their names, when it was first seen, and its containers that cannot start for it.</summary>
public sealed record ProjectGap(string Project, IReadOnlyList<string> Missing, IReadOnlyDictionary<string, ContainerSummary> Holders, DateTimeOffset Since,
    IReadOnlyList<(string Id, string Name, string Why)> Blocked)
{
    /// <summary>The gap in words, a service a sentence, then what cannot start for it: what the log, the project's tooltip and its logs say.</summary>
    public string Text => string.Join(" ", Missing.Select(service => Holders.TryGetValue(service, out var holder)
        ? $"No container stands for {service}, which its file has: its name, {holder.Name}, is held by container {holder.Name}, {(holder.Project.Length > 0 ? $"of project {holder.Project}" : "of no project")}, made around the agent; free the name and save the project to make it again."
        : $"No container stands for {service}, which its file has: save the project to make it again.")
        .Concat(Blocked.Select(blocked => $"{blocked.Name} cannot start without it.")));
}
