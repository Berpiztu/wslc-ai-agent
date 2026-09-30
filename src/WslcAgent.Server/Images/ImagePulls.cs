using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Pulls the agent owns, so the dialog that asked for one can close: the
/// Images table shows the progress, stops a pull, and opens its live output.
/// <c>wslc image pull</c> reports progress only to a terminal, so it runs
/// behind one; the progress is read from what
/// it draws. A finished pull stays listed a few seconds; a failed or cancelled
/// one keeps its message five minutes, unless the
/// user dismisses it first.
/// </summary>
public sealed class ImagePulls(IWslcRunner wslc, ICliActivity activity, Notifier notifier, ILogger<ImagePulls> logger)
{
    private static readonly TimeSpan KeepDone = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepError = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Pull> _pulls = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts a pull of <paramref name="image"/>. One already under way is joined (a run
    /// waiting for its image, a second click on Pull); a finished or failed one is replaced.
    /// </summary>
    public ImagePullState Start(string image, bool allTags = false)
    {
        var reference = Key(WslcArgs.Require(image, "image reference"));
        if (_pulls.TryGetValue(reference, out var existing) && existing.State == "running")
        {
            return existing.Snapshot();
        }

        var pull = new Pull(reference, allTags);
        _pulls[reference] = pull;
        _ = Task.Run(() => RunAsync(pull));
        return pull.Snapshot();
    }

    /// <summary>Every pull still worth showing: running ones with their progress, failed ones with their message.</summary>
    public IReadOnlyList<ImagePullState> List()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (image, pull) in _pulls)
        {
            if (pull.FinishedAt is { } finished && now - finished > (pull.State == "success" ? KeepDone : KeepError))
            {
                _pulls.TryRemove(new KeyValuePair<string, Pull>(image, pull));
            }
        }

        return _pulls.Values.Select(p => p.Snapshot()).OrderBy(p => p.Image, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// A pull is known by its image in lower case, the form wslc takes: one
    /// asked for as it was typed on a phone (Alpine) is the same pull as alpine,
    /// whoever asks for it — the dialog, a run waiting for its image, an MCP tool.
    /// </summary>
    private static string Key(string image) => ImageReference.Normalize(image);

    public ImagePullState? Get(string image) => _pulls.TryGetValue(Key(image), out var pull) ? pull.Snapshot() : null;

    /// <summary>The live output for the pull's console, as lines without terminal codes.</summary>
    public ImagePullLog Log(string image)
    {
        var reference = Key(WslcArgs.Require(image, "image reference"));
        if (!_pulls.TryGetValue(reference, out var pull))
        {
            return new ImagePullLog(reference, "", false, 0, "", false);
        }

        var state = pull.Snapshot();
        var (log, truncated) = PullProgress.Log(pull.Output());
        return new ImagePullLog(reference, log, truncated, state.Pct, state.Status, state.State == "running");
    }

    /// <summary>Stops a running pull; false when there is none.</summary>
    public bool Cancel(string image)
    {
        if (!_pulls.TryGetValue(Key(image), out var pull) || pull.State != "running")
        {
            return false;
        }

        pull.Cancelled = true;
        pull.Session?.Kill();
        return true;
    }

    /// <summary>
    /// Forgets a pull that has ended before its time is up: the row's cross once
    /// the pull failed or was cancelled, for every client at once. One that is
    /// still running is not dismissed (<see cref="Cancel"/> is for that); one
    /// that is not there is already forgotten.
    /// </summary>
    public void Dismiss(string image)
    {
        var reference = Key(image);
        if (!_pulls.TryGetValue(reference, out var pull))
        {
            return;
        }

        if (pull.State == "running")
        {
            throw new InvalidOperationException($"The pull of {reference} is still running; cancel it first.");
        }

        _pulls.TryRemove(new KeyValuePair<string, Pull>(reference, pull));
    }

    private async Task RunAsync(Pull pull)
    {
        var args = new List<string> { "image", "pull" }.Flag("--all-tags", pull.AllTags);
        args.Add(pull.Image);
        var elapsed = Stopwatch.StartNew();
        string? trace = null;
        try
        {
            using var session = wslc.StartInteractive(args, 160, 40);
            pull.Session = session;
            trace = activity.Start(session.Args);
            var output = Task.WhenAll(new[] { session.Output, session.Error }.OfType<TextReader>().Select(reader => PumpAsync(reader, pull)));
            await session.WaitForExitAsync();
            // A pseudo console keeps its output open after the process exits: its last
            // lines are given a moment, then disposing the session ends the reading.
            await Task.WhenAny(output, Task.Delay(TimeSpan.FromSeconds(1)));

            var text = pull.Output();
            if (pull.Cancelled)
            {
                pull.Finish("cancelled", "Pull cancelled by user");
            }
            else if (session.ExitCode == 0)
            {
                pull.Finish("success", "");
            }
            else
            {
                pull.Finish("error", PullProgress.Failure(text) ?? $"Pull failed (exit code {session.ExitCode})");
            }

            var (log, _) = PullProgress.Log(text);
            activity.Finish(trace, elapsed.Elapsed, session.ExitCode, pull.State, log.Length > 4000 ? log[^4000..] : log, pull.Error);
            if (!pull.Cancelled)
            {
                notifier.JobEnded("Pull", pull.Image, pull.State == "success" ? null : pull.Error, elapsed.Elapsed, NotificationLink.Images, CliTraceDescription.Images);
            }
        }
        catch (Exception ex) when (ex is WslcNotFoundException or WslcException or IOException or ArgumentException)
        {
            logger.LogError("pull {Image} could not run: {Message}", pull.Image, ex.Message);
            pull.Finish("error", ex.Message);
            notifier.JobEnded("Pull", pull.Image, ex.Message, elapsed.Elapsed, NotificationLink.Images, CliTraceDescription.Images);
            if (trace is not null)
            {
                activity.Finish(trace, elapsed.Elapsed, null, "error", "", ex.Message);
            }
        }
        finally
        {
            pull.Session = null;
        }
    }

    private static async Task PumpAsync(TextReader reader, Pull pull)
    {
        var buffer = new char[4096];
        try
        {
            while (await reader.ReadAsync(buffer) is var read && read > 0)
            {
                pull.Append(buffer.AsSpan(0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process ended or was killed: what arrived is all there is.
        }
    }

    private sealed class Pull(string image, bool allTags)
    {
        private readonly Lock _gate = new();
        private readonly StringBuilder _output = new();
        private int _pct;
        private string _status = "Pulling...";

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
                    (_pct, _status) = PullProgress.Read(_output.ToString());
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
                    (_pct, _status) = (100, "Downloaded");
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
