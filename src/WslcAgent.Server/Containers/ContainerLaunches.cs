using System.Collections.Concurrent;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Images;
using WslcAgent.Server.Projects;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The pull-then-run jobs: Run returns at once and the agent owns
/// the rest, so closing the dialog does not drop a run whose image is still
/// downloading. The pull is the Images page's own (<see cref="ImagePulls"/>),
/// joined when one is already under way, so its progress and console are the
/// same everywhere. A finished job is listed two seconds more (the real row
/// replaces it), a cancelled one 45 seconds so the table says why, and a failed
/// one until it is dismissed: it keeps the request it was started with, so the
/// row can open it in the form again, fixed and run again, instead of the
/// settings going with the row.
/// </summary>
public sealed class ContainerLaunches(IContainerService containers, IImageService images, ImagePulls pulls, ILogger<ContainerLaunches> logger)
{
    private static readonly TimeSpan KeepDone = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KeepCancelled = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PullTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    public ContainerLaunch Enqueue(ContainerLaunchRequest request)
    {
        // In lower case, as the pull it may wait on knows it and wslc takes it.
        var image = ImageReference.Normalize(WslcArgs.Require(request.Image, "image"));
        var id = Guid.NewGuid().ToString("N");
        var name = request.Name.Trim().Length > 0 ? request.Name.Trim() : $"run-{id[..8]}";
        // Run and Create alike: the image pulled first when missing, the
        // container then started or only created, as the request says.
        var job = new Job(id, image, name, request with { Image = image });
        _jobs[id] = job;
        _ = Task.Run(() => ExecuteAsync(job, job.Request));
        return job.Snapshot();
    }

    public IReadOnlyList<ContainerLaunch> List()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, job) in _jobs)
        {
            if (job.Phase != "error" && job.FinishedAt is { } finished && now - finished > (job.Phase == "done" ? KeepDone : KeepCancelled))
            {
                _jobs.TryRemove(new KeyValuePair<string, Job>(id, job));
            }
        }

        return _jobs.Values.Select(j => j.Snapshot()).ToList();
    }

    /// <summary>The request a run was started with, for the failed row's Edit: the form opens on it.</summary>
    public ContainerLaunchRequest Request(string id) =>
        _jobs.TryGetValue(id, out var job) ? job.Request : throw new KeyNotFoundException("No such run");

    /// <summary>Cancels one job, and its pull when no other job waits on that image.</summary>
    public void Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            throw new KeyNotFoundException("No such run");
        }

        job.Fail("Cancelled", cancelled: true);
        if (!_jobs.Values.Any(other => other.Id != id && other.Image == job.Image && other.Phase == "pull"))
        {
            pulls.Cancel(job.Image);
        }
    }

    /// <summary>
    /// Forgets a job that has ended before its time is up: the row's cross once
    /// the run failed or was cancelled, for every client at once. One still on
    /// its way is not dismissed (<see cref="Cancel"/> is for that); one that is
    /// not there is already forgotten.
    /// </summary>
    public void Dismiss(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            return;
        }

        if (job.FinishedAt is null)
        {
            throw new InvalidOperationException("The run is still on its way; cancel it first.");
        }

        _jobs.TryRemove(new KeyValuePair<string, Job>(id, job));
    }

    /// <summary>Whether the image is in the catalog: <c>repo:tag</c>, or the repository with <c>latest</c>.</summary>
    internal static bool IsLocal(IEnumerable<ImageSummary> catalog, string image)
    {
        // A row without its tag is not latest: it is the image a pull moved the
        // tag away from, and the repository names the new one.
        var (repository, tag) = ImageReference.Split(image);
        return catalog.Any(row => row.Reference == image || (row.Repository == repository && row.Tag == tag));
    }

    private async Task<bool> LocalAsync(string image) => IsLocal((await images.ListAsync()).Images, image);

    private async Task ExecuteAsync(Job job, ContainerLaunchRequest request)
    {
        try
        {
            if (!await LocalAsync(job.Image))
            {
                job.Progress("pull", "Pulling…", 0);
                pulls.Start(job.Image);
                if (!await WaitForPullAsync(job))
                {
                    return;
                }
            }

            if (job.Cancelled)
            {
                return;
            }

            job.Progress("run", request.Start ? "Starting container…" : "Creating container…", 100);
            var created = request.Start ? await containers.RunAsync(request) : await containers.CreateAsync(request);
            job.Finish(created, request.Start);
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or ArgumentException or InvalidOperationException)
        {
            job.Fail(ex.Message, fields: (ex as WslcException)?.Fields);
        }

        // Every failure, the pull's included: the page shows it in red, so Logs has it under Error.
        if (job.Snapshot() is { Phase: "error" } failed)
        {
            logger.LogError("run {Name} of {Image} failed: {Error}", failed.Name, failed.Image, failed.Error);
        }
    }

    /// <summary>Follows the pull until the image is local; false (and the job failed) otherwise.</summary>
    private async Task<bool> WaitForPullAsync(Job job)
    {
        var deadline = DateTimeOffset.UtcNow + PullTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (job.Cancelled)
            {
                return false;
            }

            var pull = pulls.Get(job.Image);
            if (pull is not null)
            {
                job.Progress("pull", pull.Status, Math.Clamp(pull.Pct, 0, 100));
                if (pull.State == "cancelled")
                {
                    job.Fail("Pull cancelled", cancelled: true);
                    return false;
                }

                if (pull.State == "error")
                {
                    job.Fail(pull.Error.Length > 0 ? pull.Error : "Pull failed");
                    return false;
                }

                if (pull.State == "success")
                {
                    if (await LocalAsync(job.Image))
                    {
                        return true;
                    }

                    job.Fail("Pull did not register the image");
                    return false;
                }
            }

            await Task.Delay(PollEvery);
        }

        job.Fail("Pull timed out");
        return false;
    }

    private sealed class Job(string id, string image, string name, ContainerLaunchRequest request)
    {
        private readonly Lock _gate = new();
        private string _status = "Waiting…";
        private int _pct;
        private string _error = "";
        private string _containerId = "";
        private IReadOnlyList<string> _notes = [];
        private IReadOnlyDictionary<string, string> _fields = new Dictionary<string, string>();

        public string Id { get; } = id;

        public string Image { get; } = image;

        public ContainerLaunchRequest Request { get; } = request;

        public string Phase { get; private set; } = "pull";

        public bool Cancelled { get; private set; }

        public DateTimeOffset? FinishedAt { get; private set; }

        public void Progress(string phase, string status, int pct)
        {
            lock (_gate)
            {
                if (FinishedAt is not null)
                {
                    return;
                }

                Phase = phase;
                _status = status.Length > 0 ? status : _status;
                _pct = pct;
            }
        }

        public void Finish(ContainerCreated created, bool started)
        {
            lock (_gate)
            {
                if (FinishedAt is not null)
                {
                    return;
                }

                (Phase, _status, _pct, _containerId, _notes) = ("done", started ? "Started" : "Created", 100, created.Id, created.Notes);
                FinishedAt = DateTimeOffset.UtcNow;
            }
        }

        public void Fail(string message, bool cancelled = false, IReadOnlyDictionary<string, string>? fields = null)
        {
            lock (_gate)
            {
                if (FinishedAt is not null)
                {
                    return;
                }

                Cancelled |= cancelled;
                var text = message.Trim().Length > 0 ? message.Trim() : "Failed";
                Phase = Cancelled || text.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "cancelled" : "error";
                (_error, _status) = (text, text);
                _fields = fields ?? _fields;
                FinishedAt = DateTimeOffset.UtcNow;
            }
        }

        public ContainerLaunch Snapshot()
        {
            lock (_gate)
            {
                return new ContainerLaunch(Id, Image, name, Phase, _status, _pct, _error, _containerId, _notes, _fields, Group: ProjectLabels.Split(Request.Project).Project);
            }
        }
    }
}
