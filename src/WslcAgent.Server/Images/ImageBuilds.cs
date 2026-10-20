using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// The build jobs: one long <c>wslc build --progress plain</c> the
/// agent owns and whose lines it keeps (the last 2000), followed by the page
/// and cancelled from it. <c>plain</c> is what makes the output usable from an
/// agent with no console: <c>auto</c> would draw with cursor control. Each
/// tagged image built is then checked (<see cref="ImageSecurity"/>): under
/// Block, a vulnerability moderate or worse fails the job. What a build is
/// for once it is done (a patch's containers moved to it) runs in the same
/// job, after the check: its lines in its console, its failure the job's. A
/// Dockerfile sent from the user's device (<see cref="BuildImageRequest.DockerfileText"/>)
/// is written in a folder of its own under the agent's data, <c>image-builds</c>,
/// and built from there.
/// </summary>
public sealed class ImageBuilds(IWslcRunner wslc, Notifier notifier, ImageSecurity security, IOptions<WslcOptions> options, ILogger<ImageBuilds> logger)
{
    private const int MaxLines = 2000;
    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    /// <summary>Starts a build; <paramref name="then"/> runs in the job once it is built and checked, <paramref name="ended"/> once the job is over, however it ended.</summary>
    public BuildJob Start(BuildImageRequest request, Func<Action<string>, CancellationToken, Task>? then = null, Action? ended = null)
    {
        if (request.DockerfileText.Trim().Length > 0)
        {
            var folder = Path.Combine(options.Value.DataDirectory, "image-builds", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Dockerfile"), request.DockerfileText.ReplaceLineEndings("\n"));
            request = request with { Path = folder, Dockerfile = "", DockerfileText = "" };
        }

        var args = Args(request);
        var job = new Job(Guid.NewGuid().ToString("N"), request.Tag.Trim());
        _jobs[job.Id] = job;
        job.Run = RunAsync(job, args, then, ended);
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

    private async Task RunAsync(Job job, List<string> args, Func<Action<string>, CancellationToken, Task>? then, Action? ended)
    {
        var took = Stopwatch.StartNew();
        try
        {
            await BuildAsync(job, args, then, took);
        }
        finally
        {
            ended?.Invoke();
        }
    }

    private async Task BuildAsync(Job job, List<string> args, Func<Action<string>, CancellationToken, Task>? then, Stopwatch took)
    {
        try
        {
            await wslc.StreamAsync(args, job.Add, job.Cancellation.Token);

            // Checked as a Compose build is (ImageSecurity): an image with no tag
            // has no name to run it by, and is said so.
            if (job.Tag.Length > 0)
            {
                await security.GuardAsync([job.Tag], (level, text) => job.Add($"{level}: {text}"), job.Cancellation.Token);
            }
            else
            {
                job.Add("INFO: An image with no tag is not checked for vulnerabilities: give it one to have it checked.");
            }

            if (then is not null)
            {
                await then(job.Add, job.Cancellation.Token);
            }

            job.Finish("done", "");
            notifier.JobEnded("Build", Named(job), null, took.Elapsed, NotificationLink.Images, CliTraceDescription.Images);
        }
        catch (OperationCanceledException)
        {
            job.Finish("cancelled", "");
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or ArgumentException or InvalidOperationException)
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
