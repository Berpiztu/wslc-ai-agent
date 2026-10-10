using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>How far a verb has got through a project's containers.</summary>
/// <param name="Step">What it is doing now: "Starting web".</param>
/// <param name="Done">The containers it has gone through.</param>
/// <param name="Total">The containers it has to go through.</param>
public sealed record ProjectWork(string Step, int Done, int Total)
{
    public int Pct => Total == 0 ? 0 : Done * 100 / Total;
}

/// <summary>
/// Start, stop and restart of a whole project, gone through from the client a
/// container at a time, in the order the agent gives (the one its services
/// start in; the reverse to stop). Going through them here is what lets the
/// screen show it: the project's group is opened, the container being started
/// or stopped is marked as working, as for its own verb
/// (<see cref="BusyRows"/>), each for as long as a verb is shown
/// (<see cref="VerbSpin"/>), and the project says how far the verb has got,
/// which its row and its card draw as their progress, and the containers
/// still to come wait their turn, drawn between the two states. A restart
/// is of the containers that run: one left stopped stays so. One container that
/// fails does not stop the others, except those whose service depends on its
/// own, which are not started; what did not go is said at the end, as one
/// failure. The same order holds for one container of a project: one that
/// others depend on is stopped after them, and only once the user has said so
/// (<see cref="StopWithDependentsAsync"/>), with the same progress on the
/// project as its own stop has. The work is the service's, not a
/// component's: it goes on whatever the screen does meanwhile.
/// </summary>
public sealed class ProjectVerbs(WslcAgentApi api, ActivityLine line, BusyRows rows, ProjectGroups groups)
{
    /// <summary>What the key of a project's row begins with in the Containers list, before the project's name: its row has no container's id.</summary>
    public const string RowPrefix = "project:";

    private readonly Dictionary<string, ProjectWork> _going = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cancelled = new(StringComparer.Ordinal);

    /// <summary>The containers a verb will get to and has not yet, by id.</summary>
    private readonly HashSet<string> _waiting = new(StringComparer.Ordinal);

    /// <summary>A verb began on a project, moved on, or ended.</summary>
    public event Action? Changed;

    /// <summary>
    /// Reads the list the containers are shown in, given by the page that
    /// shows them while it does. A container's turn is over once the list has
    /// been read after it, not when the agent has answered: it shows as it
    /// is (running, stopped) before the verb goes on to the next, and the
    /// project is not said to be through before its last container shows so.
    /// </summary>
    public Func<Task>? ReadList { get; set; }

    /// <summary>The verb going through the project now; null when none is.</summary>
    public ProjectWork? Of(string project) => _going.GetValueOrDefault(project);

    /// <summary>
    /// The container is one a verb going through its project will get to and
    /// has not yet: it waits its turn, and is drawn so, between the two
    /// states, until its turn leaves it in one of them.
    /// </summary>
    public bool IsWaiting(string container) => _waiting.Contains(container);

    /// <summary>Stops going through the project after the container it is on: the ones not reached are left as they are.</summary>
    public void Cancel(string project)
    {
        if (_going.ContainsKey(project))
        {
            _cancelled.Add(project);
        }
    }

    /// <summary><c>start</c>, <c>stop</c> or <c>restart</c> on the project's containers; ends when the last one has been gone through. A second verb on a project that has one going is not begun.</summary>
    public async Task RunAsync(string project, string verb)
    {
        if (_going.ContainsKey(project))
        {
            return;
        }

        // What is done to each container is the thing to see: the group opens, and stays open.
        groups.Open(project);
        Set(project, new ProjectWork($"{VerbWords.Gerund(verb)} project {project}", 0, 0));
        using var work = line.Begin($"{VerbWords.Gerund(verb)} project {project}");
        try
        {
            var members = await api.GetProjectContainersAsync(project);

            // Already as asked: nothing to do to them, and they are not counted. A restart is
            // of what runs: a container left stopped was stopped for a reason, and stays so.
            var todo = (verb == "stop" ? Enumerable.Reverse(members) : members)
                .Where(member => verb == "start" ? !member.Running : member.Running)
                .ToList();
            var failed = await ThroughAsync(project, todo, verb, work);
            if (failed.Count > 0)
            {
                work.Fail($"project {project}: {verb} left {failed.Count} undone.\n{string.Join("\n", failed)}");
            }
            else if (_cancelled.Contains(project))
            {
                work.Done($"project {project}: {verb} cancelled");
            }
            else
            {
                work.Done($"project {project}: {verb} ok", verb);
            }
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            work.Fail($"project {project}: {verb} failed. {ex.Message}");
        }
        finally
        {
            End(project);
        }
    }

    /// <summary>
    /// The verb on each of these containers in turn, the project saying which
    /// one it is on and how many it has gone through. A container whose
    /// service depends on one that did not start is not started. What did not
    /// go, each with why; with <paramref name="stopAtFailure"/> the first one
    /// ends it, since what comes after stands on it.
    /// </summary>
    private async Task<List<string>> ThroughAsync(string project, IReadOnlyList<ProjectMember> todo, string verb, ActivityWork work, bool stopAtFailure = false)
    {
        var failed = new List<string>();
        var down = new HashSet<string>(StringComparer.Ordinal);

        // Every one of them waits its turn from now, and leaves the wait as its turn comes.
        _waiting.UnionWith(todo.Select(member => member.Id));
        for (var index = 0; index < todo.Count && !_cancelled.Contains(project) && !(stopAtFailure && failed.Count > 0); index++)
        {
            var member = todo[index];
            var step = $"{VerbWords.Gerund(verb)} {member.Name}";
            _waiting.Remove(member.Id);
            Set(project, new ProjectWork(step, index, todo.Count));
            work.Set(step);
            if (verb != "stop" && member.DependsOn.FirstOrDefault(down.Contains) is { } missing)
            {
                failed.Add($"{member.Name}: not started, it depends on {missing}, which did not start");
                down.Add(member.Service);
            }
            else if (await TryAsync(member, verb) is { } why)
            {
                failed.Add(why);
                down.Add(member.Service);
            }
        }

        // The ones a cancel or a failure never got to are left as they were.
        _waiting.ExceptWith(todo.Select(member => member.Id));
        return failed;
    }

    /// <summary>The verb on the project is over: its progress goes.</summary>
    private void End(string project)
    {
        _cancelled.Remove(project);
        _going.Remove(project);
        Changed?.Invoke();
    }

    /// <summary>
    /// Before a container of a project is started or restarted by its own
    /// verb: when a service its own depends on is not running it cannot be,
    /// and that is said in a window of its own, which names what has to be
    /// started first, and not on the activity line, where it would pass as
    /// one more message. True when the verb can go on. The agent refuses the
    /// same start itself; this is the same rule, said before it is asked.
    /// </summary>
    public async Task<bool> CanStartAsync(IDialogService dialogs, ContainerSummary container, string verb)
    {
        if (await MembersAsync(container) is not { } members || members.FirstOrDefault(member => member.Id == container.Id) is not { } own)
        {
            return true;
        }

        var missing = members.Where(member => own.DependsOn.Contains(member.Service) && !member.Running).Select(member => member.Name).ToList();
        if (missing.Count == 0)
        {
            return true;
        }

        await DialogFlow.ExplainAsync(dialogs, $"Cannot {verb} container",
            $"{container.Name} cannot be {VerbWords.PastTense(verb)}: it depends on {string.Join(", ", missing)}, which {(missing.Count == 1 ? "has" : "have")} to be started first.",
            $"Start {(missing.Count == 1 ? missing[0] : "them")} first, or start the project {container.Project}, which starts its containers in order.");
        return false;
    }

    /// <summary>
    /// Before a container of a project is removed: the others of the project
    /// that depend on it will not be able to work without it, so the user is
    /// told which they are and asked. Null when nothing depends on it, and
    /// the removal is asked about as any other; else whether the user said
    /// yes.
    /// </summary>
    public async Task<bool?> ConfirmRemoveAsync(IDialogService dialogs, ContainerSummary container)
    {
        if (await MembersAsync(container) is not { } members || Dependents(members, container.Service, running: false) is not { Count: > 0 } dependents)
        {
            return null;
        }

        var names = string.Join(", ", dependents.Select(dependent => dependent.Name));
        return await DialogFlow.ConfirmAsync(dialogs, "Remove container",
            $"Attention: {names} {(dependents.Count == 1 ? "depends" : "depend")} on {container.Name}.\n\n"
            + $"If you remove {container.Name}, {(dependents.Count == 1 ? "it" : "they")} will not be able to work. This cannot be undone.\n\nRemove {container.Name} anyway?",
            "Remove", destructive: true);
    }

    /// <summary>The containers of the container's project, in start order; null for a container on its own, or when the agent does not say (the verb itself is then asked, and the agent answers for it).</summary>
    private async Task<IReadOnlyList<ProjectMember>?> MembersAsync(ContainerSummary container)
    {
        if (container.Project.Length == 0)
        {
            return null;
        }

        try
        {
            return await api.GetProjectContainersAsync(container.Project);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// The stop of a container of a project by its own verb, when others of
    /// the project depend on it and run: they cannot go on without it, so the
    /// user is told which they are and asked, and on a yes they are stopped
    /// first, the last started first, and then the container itself, as one
    /// piece of work on the project, with the progress its own stop has.
    /// Null when nothing running depends on it, or it is a container on its
    /// own: its stop is then the caller's, as any other. Else true when all
    /// of it was stopped, false when the user said no or something did not
    /// stop.
    /// </summary>
    public async Task<bool?> StopWithDependentsAsync(IDialogService dialogs, ContainerSummary container)
    {
        if (await MembersAsync(container) is not { } members
            || Dependents(members, container.Service, running: true) is not { Count: > 0 } dependents
            || members.FirstOrDefault(member => member.Id == container.Id) is not { } own)
        {
            return null;
        }

        // The one that depends is named first, so nobody reads it the other way round.
        var question = dependents.Count == 1
            ? $"{dependents[0].Name} depends on {container.Name}, and is running.\n\nStopping {container.Name} stops {dependents[0].Name} too. Stop both?"
            : $"These containers depend on {container.Name}, and are running:\n\n{string.Join("\n", dependents.Select(dependent => "• " + dependent.Name))}\n\n"
              + $"Stopping {container.Name} stops them too. Stop them all?";
        if (!await DialogFlow.ConfirmAsync(dialogs, "Stop container", question, "Stop", destructive: true) || _going.ContainsKey(container.Project))
        {
            return false;
        }

        groups.Open(container.Project);
        using var work = line.Begin($"Stopping {container.Name} and what depends on it");
        try
        {
            var failed = await ThroughAsync(container.Project, [.. dependents, own], "stop", work, stopAtFailure: true);
            if (failed.Count > 0)
            {
                work.Fail($"{container.Name}: stop left undone.\n{string.Join("\n", failed)}");
                return false;
            }

            if (_cancelled.Contains(container.Project))
            {
                work.Done($"{container.Name}: stop cancelled");
                return false;
            }

            work.Done($"{container.Name}: stop ok", "stop");
            return true;
        }
        finally
        {
            End(container.Project);
        }
    }

    /// <summary>The containers whose services depend on a service, directly or through another, in the order to stop them: the last started first. Only the running ones when <paramref name="running"/> says so.</summary>
    private static IReadOnlyList<ProjectMember> Dependents(IReadOnlyList<ProjectMember> members, string service, bool running)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal) { service };
        var dependents = new List<ProjectMember>();

        // In start order a service comes after what it depends on, so one pass finds the whole chain.
        foreach (var member in members)
        {
            if (member.DependsOn.Any(needed.Contains) && needed.Add(member.Service) && (member.Running || !running))
            {
                dependents.Add(member);
            }
        }

        dependents.Reverse();
        return dependents;
    }

    /// <summary>One verb on one container, shown working for as long as a verb is shown and until the list has been read after it; why it failed, or null when it went through.</summary>
    private async Task<string?> TryAsync(ProjectMember member, string verb)
    {
        rows.Begin(member.Id);
        try
        {
            await VerbSpin.AtLeast(verb switch
            {
                "start" => api.StartContainerAsync(member.Id),
                "stop" => api.StopContainerAsync(member.Id),
                _ => api.RestartContainerAsync(member.Id),
            });
            return null;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            return $"{member.Name}: {ex.Message}";
        }
        finally
        {
            if (ReadList is { } read)
            {
                await read();
            }

            rows.End(member.Id);
        }
    }

    private void Set(string project, ProjectWork work)
    {
        _going[project] = work;
        Changed?.Invoke();
    }
}
