using System.Collections.Concurrent;
using System.Diagnostics;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// The build jobs: one long <c>wslc build --progress plain</c> the
/// agent owns and whose lines it keeps (the last 2000), followed by the page
/// and cancelled from it. <c>plain</c> is what makes the output usable from an
/// agent with no console: <c>auto</c> would draw with cursor control.
/// </summary>
public sealed class ImageBuilds(IWslcRunner wslc, Notifier notifier, ILogger<ImageBuilds> logger)
{
    private const int MaxLines = 2000;
    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    public BuildJob Start(BuildImageRequest request)
    {
        var args = Args(request);
        var job = new Job(Guid.NewGuid().ToString("N"), request.Tag.Trim());
        _jobs[job.Id] = job;
        job.Run = RunAsync(job, args);
        return job.Snapshot();
    }

    public BuildJob Get(string id) =>
        _jobs.TryGetValue(id, out var job) ? job.Snapshot() : throw new KeyNotFoundException("Unknown build job");

    /// <summary>Stops the build (the process goes with it) and forgets the job.</summary>
    public async Task CancelAsync(string id)
    {
        if (!_jobs.TryRemove(id, out var job))
        {
            throw new KeyNotFoundException("Unknown build job");
        }

        await job.Cancellation.CancelAsync();
        if (job.Run is not null)
        {
            await job.Run;
        }
    }

    /// <summary><c>build --progress plain [--tag] [--file] [--target] [--build-arg]… [--label]… [--no-cache] [--pull] PATH</c>.</summary>
    internal static List<string> Args(BuildImageRequest request)
    {
        var context = WslcArgs.Require(request.Path, "build context");
        var args = new List<string> { "build", "--progress", "plain" }
            .Option("--tag", request.Tag)
            .Option("--file", request.Dockerfile)
            .Option("--target", request.Target);
        foreach (var value in Lines(request.BuildArgs))
        {
            args.Option("--build-arg", value);
        }

        foreach (var value in Lines(request.Labels))
        {
            args.Option("--label", value);
        }

        args.Flag("--no-cache", request.NoCache).Flag("--pull", request.Pull);
        args.Add(context);
        return args;
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task RunAsync(Job job, List<string> args)
    {
        var took = Stopwatch.StartNew();
        try
        {
            await wslc.StreamAsync(args, job.Add, job.Cancellation.Token);
            job.Finish("done", "");
            notifier.JobEnded("Build", Named(job), null, took.Elapsed, NotificationLink.Images, CliTraceDescription.Images);
        }
        catch (OperationCanceledException)
        {
            job.Finish("cancelled", "");
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or ArgumentException)
        {
            logger.LogError("build {Tag} failed: {Message}", job.Tag, ex.Message);
            job.Finish("error", ex.Message);
            notifier.JobEnded("Build", Named(job), ex.Message, took.Elapsed, NotificationLink.Images, CliTraceDescription.Images);
        }
    }

    /// <summary>A build as a notification names it: its tag, or that it had none.</summary>
    private static string Named(Job job) => job.Tag.Length > 0 ? job.Tag : "An untagged image";

    private sealed class Job(string id, string tag)
    {
        private readonly Lock _gate = new();
        private readonly LinkedList<string> _output = new();
        private string _state = "running";
        private string _error = "";
        private bool _truncated;

        public string Id { get; } = id;

        public string Tag { get; } = tag;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task? Run { get; set; }

        public void Add(string line)
        {
            lock (_gate)
            {
                if (_output.Count >= MaxLines)
                {
                    _output.RemoveFirst();
                    _truncated = true;
                }

                _output.AddLast(line);
            }
        }

        public void Finish(string state, string error)
        {
            lock (_gate)
            {
                _state = state;
                _error = error;
            }
        }

        public BuildJob Snapshot()
        {
            lock (_gate)
            {
                return new BuildJob(Id, _state, Tag, _output.ToList(), _truncated, _error);
            }
        }
    }
}
