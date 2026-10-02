using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Image transfers the agent owns — a pull (<see cref="ImagePulls"/>) or a push
/// (<see cref="ImagePushes"/>) — so the dialog that asked for one can close: a page
/// follows the progress, stops it, and opens its live output. <c>wslc</c> reports
/// progress only to a terminal, so the command runs behind one; the progress is
/// read from what it draws. A finished job stays listed a few seconds; a failed or
/// cancelled one keeps its message five minutes, unless the user dismisses it first.
/// </summary>
public abstract class ImageJobs(IWslcRunner wslc, ICliActivity activity, Notifier notifier, ILogger logger)
{
    private static readonly TimeSpan KeepDone = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepError = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);

    /// <summary>The job's name in its messages and notifications: <c>Pull</c>, <c>Push</c>.</summary>
    protected abstract string Verb { get; }

    /// <summary>The status a job that succeeded shows.</summary>
    protected abstract string DoneStatus { get; }

    /// <summary>The <c>wslc</c> command before its flags and the image: <c>image pull</c>, <c>image push</c>.</summary>
    protected abstract IReadOnlyList<string> Command { get; }

    /// <summary>The percentage and the status line the output drawn so far gives.</summary>
    protected abstract (int Pct, string Status) Read(string output);

    /// <summary>
    /// Starts a job on <paramref name="image"/>. One already under way is joined (a run
    /// waiting for its image, a second click); a finished or failed one is replaced.
    /// </summary>
    public ImagePullState Start(string image, bool allTags = false)
    {
        var reference = Key(WslcArgs.Require(image, "image reference"));
        if (_jobs.TryGetValue(reference, out var existing) && existing.State == "running")
        {
            return existing.Snapshot();
        }

        var job = new Job(reference, allTags, Read, DoneStatus);
        _jobs[reference] = job;
        _ = Task.Run(() => RunAsync(job));
        return job.Snapshot();
    }

    /// <summary>Every job still worth showing: running ones with their progress, failed ones with their message.</summary>
    public IReadOnlyList<ImagePullState> List()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (image, job) in _jobs)
        {
            if (job.FinishedAt is { } finished && now - finished > (job.State == "success" ? KeepDone : KeepError))
            {
                _jobs.TryRemove(new KeyValuePair<string, Job>(image, job));
            }
        }

        return _jobs.Values.Select(j => j.Snapshot()).OrderBy(j => j.Image, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// A job is known by its image in lower case, the form wslc takes: one
    /// asked for as it was typed on a phone (Alpine) is the same job as alpine,
    /// whoever asks for it — a dialog, a run waiting for its image, an MCP tool.
    /// </summary>
    private static string Key(string image) => ImageReference.Normalize(image);

    public ImagePullState? Get(string image) => _jobs.TryGetValue(Key(image), out var job) ? job.Snapshot() : null;

    /// <summary>The live output for the job's console, as lines without terminal codes.</summary>
    public ImagePullLog Log(string image)
    {
        var reference = Key(WslcArgs.Require(image, "image reference"));
        if (!_jobs.TryGetValue(reference, out var job))
        {
            return new ImagePullLog(reference, "", false, 0, "", false);
        }

        var state = job.Snapshot();
        var (log, truncated) = PullProgress.Log(job.Output());
        return new ImagePullLog(reference, log, truncated, state.Pct, state.Status, state.State == "running");
    }

    /// <summary>Stops a running job; false when there is none.</summary>
    public bool Cancel(string image)
    {
        if (!_jobs.TryGetValue(Key(image), out var job) || job.State != "running")
        {
            return false;
        }

        job.Cancelled = true;
        job.Session?.Kill();
        return true;
    }

    /// <summary>
    /// Forgets a job that has ended before its time is up: the row's cross once
    /// it failed or was cancelled, for every client at once. One that is still
    /// running is not dismissed (<see cref="Cancel"/> is for that); one that is
    /// not there is already forgotten.
    /// </summary>
    public void Dismiss(string image)
    {
        var reference = Key(image);
        if (!_jobs.TryGetValue(reference, out var job))
        {
            return;
        }

        if (job.State == "running")
        {
            throw new InvalidOperationException($"The {Verb.ToLowerInvariant()} of {reference} is still running; cancel it first.");
        }

        _jobs.TryRemove(new KeyValuePair<string, Job>(reference, job));
    }

    private async Task RunAsync(Job job)
    {
        var args = new List<string>(Command).Flag("--all-tags", job.AllTags);
        args.Add(job.Image);
        var elapsed = Stopwatch.StartNew();
        string? trace = null;
        try
        {
            using var session = wslc.StartInteractive(args, 160, 40);
            job.Session = session;
            trace = activity.Start(session.Args);
            var output = Task.WhenAll(new[] { session.Output, session.Error }.OfType<TextReader>().Select(reader => PumpAsync(reader, job)));
            await session.WaitForExitAsync();
            // A pseudo console keeps its output open after the process exits: its last
            // lines are given a moment, then disposing the session ends the reading.
            await Task.WhenAny(output, Task.Delay(TimeSpan.FromSeconds(1)));

            var text = job.Output();
            if (job.Cancelled)
            {
                job.Finish("cancelled", $"{Verb} cancelled by user");
            }
            else if (session.ExitCode == 0)
            {
                job.Finish("success", "");
            }
            else
            {
                job.Finish("error", PullProgress.Failure(text) ?? $"{Verb} failed (exit code {session.ExitCode})");
            }

            var (log, _) = PullProgress.Log(text);
            activity.Finish(trace, elapsed.Elapsed, session.ExitCode, job.State, log.Length > 4000 ? log[^4000..] : log, job.Error);
            if (!job.Cancelled)
            {
                notifier.JobEnded(Verb, job.Image, job.State == "success" ? null : job.Error, elapsed.Elapsed, NotificationLink.Images, CliTraceDescription.Images);
            }
        }
        catch (Exception ex) when (ex is WslcNotFoundException or WslcException or IOException or ArgumentException)
        {
            logger.LogError("{Verb} {Image} could not run: {Message}", Verb.ToLowerInvariant(), job.Image, ex.Message);
            job.Finish("error", ex.Message);
            notifier.JobEnded(Verb, job.Image, ex.Message, elapsed.Elapsed, NotificationLink.Images, CliTraceDescription.Images);
            if (trace is not null)
            {
                activity.Finish(trace, elapsed.Elapsed, null, "error", "", ex.Message);
            }
        }
        finally
        {
            job.Session = null;
        }
    }

    private static async Task PumpAsync(TextReader reader, Job job)
    {
        var buffer = new char[4096];
        try
        {
            while (await reader.ReadAsync(buffer) is var read && read > 0)
            {
                job.Append(buffer.AsSpan(0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process ended or was killed: what arrived is all there is.
        }
    }

    private sealed class Job(string image, bool allTags, Func<string, (int Pct, string Status)> read, string doneStatus)
    {
        private readonly Lock _gate = new();
        private readonly StringBuilder _output = new();
        private int _pct;
        private string _status = read("").Status;

        public string Image { get; } = image;

        /// <summary>Every tag of the repository, not the one named (wslc 2.9.13).</summary>
        public bool AllTags { get; } = allTags;

        public string State { get; private set; } = "running";

        public string Error { get; private set; } = "";

        public DateTimeOffset? FinishedAt { get; private set; }

        public bool Cancelled { get; set; }

        public IWslcSession? Session { get; set; }

        public void Append(ReadOnlySpan<char> text)
        {
            lock (_gate)
            {
                _output.Append(text);
                if (_output.Length > PullProgress.MaxLogChars * 2)
                {
                    _output.Remove(0, _output.Length - PullProgress.MaxLogChars);
                }

                // Lines still draining after the end must not undo its 100% or its message.
                if (State == "running")
                {
                    (_pct, _status) = read(_output.ToString());
                }
            }
        }

        public string Output()
        {
            lock (_gate)
            {
                return _output.ToString();
            }
        }

        public void Finish(string state, string error)
        {
            lock (_gate)
            {
                State = state;
                Error = error;
                if (state == "success")
                {
                    (_pct, _status) = (100, doneStatus);
                }
                else
                {
                    _status = error;
                }

                FinishedAt = DateTimeOffset.UtcNow;
            }
        }

        public ImagePullState Snapshot()
        {
            lock (_gate)
            {
                return new ImagePullState(Image, State, _pct, _status, Error);
            }
        }
    }
}
