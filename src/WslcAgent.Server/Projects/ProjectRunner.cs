using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Projects;

/// <summary>
/// Brings a Compose file up as a project: its networks and volumes, the
/// images it builds, then a container a service, in the order they depend on
/// each other, each marked as the project's. Asked again for a project that
/// exists, it applies the file to what is there: a service that did not
/// change is left alone, one that did is recreated through the container's
/// own recreate (rehearsed first, the old one put back if the new one does
/// not start), a new one is created, and a container whose service left the
/// file is removed only when the caller said so. It runs as a job of the
/// agent: the caller gets its id at once and asks how it goes. A container it
/// makes is a Run of its own (<see cref="ContainerLaunches"/>), so the
/// Containers table shows each one on its way, with its image's pull, its
/// cancel, and, failed, its row kept with its settings to open in the form.
/// The project is a row of that table too, the one its containers stand
/// under: while it is being saved, and for as long as a failure of it has not
/// been dismissed, that row says how far the save has got and opens its log,
/// which holds every step and what the builds printed. The log is read in the view a container's logs are read in, which
/// colours a line by the time and the level it starts with: the job's own
/// lines are written so, and what <c>wslc</c> prints is kept as printed.
/// </summary>
public sealed class ProjectRunner(
    IProjectService projects,
    IContainerService containers,
    INetworkService networks,
    IVolumeService volumes,
    ContainerLaunches launches,
    WslcEvents events,
    IWslcRunner wslc,
    ProjectStore store,
    ILogger<ProjectRunner> logger)
{
    /// <summary>What a project's row says in the Image column: it is no container's image.</summary>
    public const string RowImage = "Compose project";

    private const int MaxLines = 2000;
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LaunchPoll = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HealthyWithin = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CompletedWithin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan KeepDone = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KeepCancelled = TimeSpan.FromSeconds(45);

    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    /// <summary>What saving the file would do, with nothing done: what the question before the save is made of.</summary>
    public async Task<ProjectPreview> PreviewAsync(ProjectUpRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await projects.PlanAsync(request.File, cancellationToken);
        var state = await StateAsync(plan, cancellationToken);
        var services = plan.Services.Where(service => service.Enabled).ToList();
        return new ProjectPreview(
            plan.Name,
            state.Containers.Count > 0,
            services.Where(service => !state.Containers.ContainsKey(service.Name)).Select(service => service.Launch.Name).ToList(),
            services.Where(service => state.Containers.TryGetValue(service.Name, out var existing) && existing.Config != Hash(service)).Select(service => service.Launch.Name).ToList(),
            services.Where(service => state.Containers.TryGetValue(service.Name, out var existing) && existing.Config == Hash(service)).Select(service => service.Launch.Name).ToList(),
            Orphans(state, services).Select(orphan => orphan.Name).ToList(),
            plan.Networks.Where(network => !network.External && !state.Networks.Contains(network.Name)).Select(network => network.Name).ToList(),
            plan.Volumes.Where(volume => !volume.External && !state.Volumes.Contains(volume.Name)).Select(volume => volume.Name).ToList(),
            services.Where(service => service.Build is not null).Select(service => service.Launch.Image).ToList(),
            plan.Unsupported,
            plan.Warnings);
    }

    /// <summary>Starts the save. The file is read first, so one that cannot be read is refused here and no job is begun.</summary>
    public async Task<ProjectJob> StartAsync(ProjectUpRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await projects.PlanAsync(request.File, cancellationToken);
        var job = new Job(Guid.NewGuid().ToString("N")[..12], plan.Name);
        _jobs[job.Id] = job;
        _ = Task.Run(() => RunAsync(job, plan, request), CancellationToken.None);

        // Its row is new in the Containers table: every client is told to look.
        events.Publish(new ChangeNotice([ChangeNotice.Container]));
        return job.Snapshot();
    }

    public ProjectJob Get(string id) =>
        _jobs.TryGetValue(id, out var job) ? job.Snapshot() : throw new KeyNotFoundException("Unknown project job");

    /// <summary>
    /// The projects being saved, as rows of the Containers table beside the
    /// runs it follows: one saved is listed two seconds more, a cancelled one
    /// 45 seconds so the table says why, and a failed one until it is
    /// dismissed, with its log.
    /// </summary>
    public IReadOnlyList<ContainerLaunch> Rows()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, job) in _jobs)
        {
            if (job.State != ProjectJob.Failed && job.FinishedAt is { } finished && now - finished > (job.State == ProjectJob.Done ? KeepDone : KeepCancelled))
            {
                _jobs.TryRemove(new KeyValuePair<string, Job>(id, job));
            }
        }

        return _jobs.Values.Select(job => job.Row()).ToList();
    }

    /// <summary>The id is a project's save, not a container's run: the table's cancel and dismiss are for this runner.</summary>
    public bool Owns(string id) => _jobs.ContainsKey(id);

    /// <summary>Stops the save where it is: what it has made stays, and the run it was waiting on is cancelled with it.</summary>
    public void Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            throw new KeyNotFoundException("Unknown project job");
        }

        job.Cancellation.Cancel();
    }

    /// <summary>Forgets a save that has ended: the row's cross once it failed or was cancelled. One still on its way is not dismissed.</summary>
    public void Dismiss(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            return;
        }

        if (job.FinishedAt is null)
        {
            throw new InvalidOperationException("The project is still being saved; cancel it first.");
        }

        _jobs.TryRemove(new KeyValuePair<string, Job>(id, job));
    }

    private async Task RunAsync(Job job, ComposePlan plan, ProjectUpRequest request)
    {
        var stop = job.Cancellation.Token;
        try
        {
            var state = await StateAsync(plan, stop);
            var services = plan.Services.Where(service => service.Enabled).ToList();
            var newNetworks = plan.Networks.Where(network => !state.Networks.Contains(network.Name)).ToList();
            var newVolumes = plan.Volumes.Where(volume => !state.Volumes.Contains(volume.Name)).ToList();
            var builds = services.Where(service => service.Build is not null).ToList();
            var orphans = request.RemoveOrphans ? Orphans(state, services) : new List<Existing>();
            job.Expect(newNetworks.Count + newVolumes.Count + builds.Count + services.Count + orphans.Count);

            foreach (var network in newNetworks)
            {
                await NetworkAsync(job, plan, network, stop);
            }

            foreach (var volume in newVolumes)
            {
                await VolumeAsync(job, plan, volume, stop);
            }

            foreach (var service in builds)
            {
                await BuildAsync(job, service, stop);
            }

            foreach (var service in services)
            {
                if (request.Start)
                {
                    await DependenciesAsync(job, plan, service, stop);
                }

                await ContainerAsync(job, plan, service, state, request.Start, stop);
            }

            // Never without the caller having been asked: a container is not removed for having left a file.
            foreach (var orphan in orphans)
            {
                job.Say($"Removing {orphan.Name}");
                await containers.RemoveAsync(orphan.Id, force: true, stop);
                job.Advance();
            }

            store.Keep(plan, request.File);
            job.Say(request.Start ? "Started" : "Saved");
            job.Finish(ProjectJob.Done, "");
        }
        catch (OperationCanceledException)
        {
            job.Note("WARNING", "Cancelled");
            job.Finish(ProjectJob.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            // Whatever stops it, the job has to end: a row of the table is waiting on it.
            logger.LogError("project {Project} failed at '{Step}': {Message}", plan.Name, job.Step, ex.Message);
            job.Note("ERROR", ex.Message);
            job.Finish(ProjectJob.Failed, ex.Message);
        }

        events.Publish(new ChangeNotice([ChangeNotice.Container]));
    }

    private async Task NetworkAsync(Job job, ComposePlan plan, ComposeNetwork network, CancellationToken stop)
    {
        if (network.External)
        {
            throw new InvalidOperationException($"The network {network.Name} is external to the project and is not on this machine: create it first.");
        }

        job.Say($"Creating network {network.Name}");
        await networks.CreateAsync(new CreateNetworkRequest(network.Name, Subnet: network.Subnet, Gateway: network.Gateway, Internal: network.Internal, Label: $"{ProjectLabels.Project}={plan.Name}"), stop);
        job.Advance();
    }

    private async Task VolumeAsync(Job job, ComposePlan plan, ComposeVolume volume, CancellationToken stop)
    {
        if (volume.External)
        {
            throw new InvalidOperationException($"The volume {volume.Name} is external to the project and is not on this machine: create it first.");
        }

        job.Say($"Creating volume {volume.Name}");
        await volumes.CreateAsync(new CreateVolumeRequest(volume.Name, volume.Driver, Label: $"{ProjectLabels.Project}={plan.Name}", Option: volume.Options.FirstOrDefault() ?? ""), stop);
        job.Advance();
    }

    /// <summary><c>build --progress plain --tag IMAGE [--file] [--target] [--build-arg]… CONTEXT</c>, its output into the job's log. The Dockerfile is the context's, as Compose reads it.</summary>
    private async Task BuildAsync(Job job, ComposeService service, CancellationToken stop)
    {
        var build = service.Build!;
        job.Say($"Building {service.Launch.Image}");
        var dockerfile = build.Dockerfile.Length == 0 || Path.IsPathFullyQualified(build.Dockerfile)
            ? build.Dockerfile
            : Path.GetFullPath(Path.Combine(build.Context, build.Dockerfile));
        var args = new List<string> { "build", "--progress", "plain" }
            .Option("--tag", service.Launch.Image)
            .Option("--file", dockerfile)
            .Option("--target", build.Target);
        foreach (var value in build.Args)
        {
            args.Option("--build-arg", value);
        }

        args.Add(build.Context);
        job.Note("DEBUG", "wslc " + string.Join(' ', args));
        await wslc.StreamAsync(args, job.Add, stop);
        job.Advance();
    }

    /// <summary>What the service waits for before it starts: a dependency healthy, or run to its end.</summary>
    private async Task DependenciesAsync(Job job, ComposePlan plan, ComposeService service, CancellationToken stop)
    {
        foreach (var dependency in service.DependsOn.Where(dependency => dependency.Condition != ComposeDependency.Started))
        {
            if (plan.Services.FirstOrDefault(other => other.Name == dependency.Service && other.Enabled) is not { } other)
            {
                continue;
            }

            var healthy = dependency.Condition == ComposeDependency.Healthy;
            job.Say($"Waiting for {other.Launch.Name} to {(healthy ? "be healthy" : "finish")}");
            var until = DateTimeOffset.UtcNow + (healthy ? HealthyWithin : CompletedWithin);
            while (true)
            {
                var row = (await containers.ListAsync(all: true, cancellationToken: stop)).Containers.FirstOrDefault(container => container.Name == other.Launch.Name);
                if (row is not null && (healthy ? row.HealthStatus == "healthy" : !row.IsRunning && row.State != "created"))
                {
                    break;
                }

                if (DateTimeOffset.UtcNow > until)
                {
                    throw new TimeoutException($"{other.Launch.Name} did not {(healthy ? "become healthy" : "finish")} in time, and {service.Launch.Name} waits for it.");
                }

                await Task.Delay(Poll, stop);
            }
        }
    }

    /// <summary>The service's container: made, left as it is, started, or recreated when the service changed.</summary>
    private async Task ContainerAsync(Job job, ComposePlan plan, ComposeService service, State state, bool start, CancellationToken stop)
    {
        var launch = service.Launch with { Project = $"{plan.Name}/{service.Name}", ProjectConfig = Hash(service), Start = start };
        if (!state.Containers.TryGetValue(service.Name, out var existing))
        {
            job.Say($"{(start ? "Starting" : "Creating")} {launch.Name}");
            await LaunchedAsync(launches.Enqueue(launch), stop);
        }
        else if (existing.Config != launch.ProjectConfig)
        {
            // Not stopped half way: a recreate cut short could leave neither the old container nor the new one.
            job.Say($"Recreating {existing.Name}");
            await containers.RecreateAsync(existing.Id, launch, CancellationToken.None);
            stop.ThrowIfCancellationRequested();
        }
        else if (start && !existing.Running)
        {
            job.Say($"Starting {existing.Name}");
            await containers.StartAsync(existing.Id, stop);
        }
        else
        {
            job.Note("INFO", $"{existing.Name}: unchanged");
        }

        job.Advance();
    }

    /// <summary>
    /// Waits for a Run the project began. Every client is told a run has
    /// appeared, since no window asked for this one and the table would not
    /// look for it. One that fails or is cancelled stops the project there:
    /// its row stays in the table, with what it was started with. The project
    /// cancelled, its run is cancelled with it.
    /// </summary>
    private async Task LaunchedAsync(ContainerLaunch launched, CancellationToken stop)
    {
        events.Publish(new ChangeNotice([ChangeNotice.Container]));
        while (true)
        {
            try
            {
                await Task.Delay(LaunchPoll, stop);
            }
            catch (OperationCanceledException)
            {
                // Ended and forgotten in this half second, it has nothing left to cancel.
                if (launches.List().Any(launch => launch.Id == launched.Id && launch.Active))
                {
                    launches.Cancel(launched.Id);
                }

                throw;
            }

            var now = launches.List().FirstOrDefault(launch => launch.Id == launched.Id);

            // A finished run is listed two seconds more, then its real row takes its place.
            if (now is null || now.Phase == "done")
            {
                return;
            }

            if (now.Phase is "error" or "cancelled")
            {
                throw new InvalidOperationException($"{launched.Name}: {(now.Error.Length > 0 ? now.Error : now.Status)}");
            }
        }
    }

    /// <summary>The project's containers whose service the file no longer has, or leaves out.</summary>
    private static List<Existing> Orphans(State state, List<ComposeService> services) =>
        state.Containers.Values.Where(existing => services.All(service => service.Name != existing.Service)).ToList();

    /// <summary>The service as it was read, in a few characters: what its container carries, and what a later save compares.</summary>
    private static string Hash(ComposeService service)
    {
        var text = JsonSerializer.Serialize(new { Launch = service.Launch with { Start = true, Project = "", ProjectConfig = "" }, service.Build });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>What is on the machine of this project: its containers by service, and the names of the networks and volumes there are.</summary>
    private async Task<State> StateAsync(ComposePlan plan, CancellationToken cancellationToken)
    {
        var result = await wslc.RunAsync(["container", "list", "--all", "--no-trunc", "--filter", $"label={ProjectLabels.Project}={plan.Name}", "--format", "json"], cancellationToken: cancellationToken);
        var existing = new Dictionary<string, Existing>(StringComparer.Ordinal);
        foreach (var row in WslcJson.ParseRows(result.Stdout))
        {
            var (_, service, config) = ProjectLabels.Of(row.GetString("Labels"));
            if (service.Length > 0)
            {
                existing[service] = new Existing(row.GetString("ID"), row.GetString("Names"), service, config, row.GetString("State") == "running");
            }
        }

        var networkNames = (await networks.ListAsync(cancellationToken)).Networks.Select(network => network.Name).ToHashSet(StringComparer.Ordinal);
        var volumeNames = (await volumes.ListAsync(cancellationToken)).Volumes.Select(volume => volume.Name).ToHashSet(StringComparer.Ordinal);
        return new State(existing, networkNames, volumeNames);
    }

    private sealed record Existing(string Id, string Name, string Service, string Config, bool Running);

    private sealed record State(Dictionary<string, Existing> Containers, HashSet<string> Networks, HashSet<string> Volumes);

    /// <summary>One save: the step it is at, how far it has got, and what it has printed.</summary>
    private sealed class Job(string id, string name)
    {
        private readonly Lock _gate = new();
        private readonly LinkedList<string> _log = new();
        private string _state = ProjectJob.Running;
        private string _step = "Reading the project";
        private string _error = "";
        private int _expected = 1;
        private int _made;
        private DateTimeOffset? _finishedAt;

        public string Id { get; } = id;

        public CancellationTokenSource Cancellation { get; } = new();

        public DateTimeOffset? FinishedAt
        {
            get
            {
                lock (_gate)
                {
                    return _finishedAt;
                }
            }
        }

        public string State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        public string Step
        {
            get
            {
                lock (_gate)
                {
                    return _step;
                }
            }
        }

        /// <summary>How many things the save has to make: what its percentage is counted over.</summary>
        public void Expect(int things)
        {
            lock (_gate)
            {
                _expected = Math.Max(things, 1);
            }
        }

        public void Advance()
        {
            lock (_gate)
            {
                _made++;
            }
        }

        /// <summary>A new step: what the row waiting on the job says, and a line of its log.</summary>
        public void Say(string step)
        {
            lock (_gate)
            {
                _step = step;
            }

            Note("INFO", step);
        }

        /// <summary>A line of the job's own, as the agent's log writes one (<c>time | LEVEL | text</c>): what the logs view colours it by.</summary>
        public void Note(string level, string text) => Add($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} | {level,-8} | {text}");

        /// <summary>A line as it came: what <c>wslc</c> printed.</summary>
        public void Add(string line)
        {
            lock (_gate)
            {
                _log.AddLast(line);
                if (_log.Count > MaxLines)
                {
                    _log.RemoveFirst();
                }
            }
        }

        public void Finish(string state, string error)
        {
            lock (_gate)
            {
                _state = state;
                _error = error;
                _finishedAt = DateTimeOffset.UtcNow;
            }
        }

        public ProjectJob Snapshot()
        {
            lock (_gate)
            {
                return new ProjectJob(Id, name, _state, _step, [.. _log], _error, Percent());
            }
        }

        /// <summary>The save as a row of the Containers table: a run's shape, marked as a project's with the job's id.</summary>
        public ContainerLaunch Row()
        {
            lock (_gate)
            {
                var phase = _state switch
                {
                    ProjectJob.Running => "run",
                    ProjectJob.Done => "done",
                    ProjectJob.Cancelled => "cancelled",
                    _ => "error",
                };
                return new ContainerLaunch(Id, RowImage, name, phase, _error.Length > 0 ? $"{_step}: {_error}" : _step, Percent(), _error, "", [], null, Id, name);
            }
        }

        private int Percent() => _state == ProjectJob.Done ? 100 : Math.Clamp(_made * 100 / _expected, 0, 99);
    }
}
