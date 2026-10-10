using System.Diagnostics;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Notifications;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// Keeps <c>wslc events</c> open for as long as the agent runs, and turns what
/// it prints into notices (<see cref="WslcEvents"/>). Measured behaviour, which
/// is why this is a loop and not a single read:
/// the stream belongs to the session, so stopping the session aborts it with
/// <c>E_ABORT</c> — and says nothing at all about the containers that went down
/// with it. So every reconnection announces that everything is stale, rather
/// than pretending the silence meant nothing happened. And it never opens a
/// stream on a session that is down: like any command, <c>wslc events</c>
/// opens the store it names, so listening again brought back the session the
/// user had just stopped, and compacting its VHDX, which needs it stopped, was
/// refused a few seconds later.
/// <para>
/// It is where the notifications hear what happens:
/// every container event goes to <see cref="ContainerStops"/> and
/// <see cref="ContainerHealth"/>, every event to <see cref="RecentWslcEvents"/>
/// (the Recent events list), and a stream
/// that died with a session the user did not stop from the agent is a session
/// lost.
/// </para>
/// </summary>
public sealed class WslcEventReader(
    IWslcRunner wslc,
    ISessionService sessions,
    WslcEvents events,
    RecentWslcEvents recent,
    StoppedSessions held,
    ContainerStops stops,
    ContainerHealth health,
    Notifier notifier,
    ILogger<WslcEventReader> logger) : BackgroundService
{
    /// <summary>What arrives together is one notice: a stop brings its network's disconnect with it, in the same second.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(400);

    /// <summary>How long to wait before listening again, and how long that wait is allowed to grow to.</summary>
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromMinutes(1);

    /// <summary>A stream that lasted this long was a real one: the next failure starts the wait over.</summary>
    private static readonly TimeSpan Settled = TimeSpan.FromSeconds(30);

    private readonly HashSet<string> _pending = [];
    private readonly Lock _gate = new();
    private Task? _flush;
    private bool _unsupported;

    /// <summary>The session was down at the last look: said once in the log, not at every look.</summary>
    private bool _waiting;

    /// <summary>The session the agent targets, as the last look named it; empty when that look failed.</summary>
    private string _session = "";

    /// <summary>A stream has just ended: the next look says whether its session went with it.</summary>
    private bool _ended;

    /// <summary>
    /// The first line of this stream that was not an event, kept for one
    /// reason: to say why it ended. Its output is not collected — days of
    /// events would be held in memory for nothing — and without this the log
    /// had the exit code and no cause at all.
    /// </summary>
    private string _lastWord = "";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retry = FirstRetry;
        while (!stoppingToken.IsCancellationRequested)
        {
            var running = await SessionRunningAsync(stoppingToken);
            if (_ended && _session.Length > 0)
            {
                // Only a look that answered decides: one that failed says
                // nothing of the session, and the next look is asked again.
                _ended = false;
                if (!running && !held.Holds(_session))
                {
                    notifier.Raise(NotificationKind.SessionLost, NotificationSeverity.Error, "WSLC session down",
                        $"{_session} stopped, and its containers with it; nobody stopped it from the agent.", NotificationLink.System);
                }
            }

            if (!running)
            {
                // The screens read for themselves while nothing listens, as
                // they did before events existed; the wait grows as a failed
                // stream's does, so a session left down is asked once a minute.
                retry = Slower(retry);
                if (!await WaitAsync(retry, stoppingToken))
                {
                    return;
                }

                continue;
            }

            // Every arrival is a fresh start: what happened while nothing was
            // listening was never reported, and --since cannot fill it either —
            // the event store starts empty with the session.
            events.Publish(ChangeNotice.Everything);
            recent.Listening(_session);
            events.Live = true;
            _lastWord = "";
            var lasted = Stopwatch.StartNew();
            int exit;
            try
            {
                exit = await wslc.WatchAsync(["events"], Take, stoppingToken);
            }
            catch (Exception broke) when (broke is not OperationCanceledException)
            {
                // Listening is how the agent hears about changes sooner; it is
                // not how it works. A background service that throws takes the
                // whole host down with it by default, so an agent that could
                // not start one stream would stop answering anything at all —
                // and the screens have read for themselves since before this
                // existed.
                exit = -1;
                _lastWord = broke.Message;
            }
            finally
            {
                events.Live = false;
                lasted.Stop();
            }

            if (_unsupported)
            {
                // Nothing to wait for: this wslc has no events to give, and the
                // screens go on reading for themselves as they always have.
                logger.LogWarning("this wslc has no events command; the agent will not listen for changes (needs wslc 2.9.13)");
                return;
            }

            // The stream dies with its session, stopped from a client or from a
            // terminal on the machine alike: the clients read the session again
            // now, which is the one moment they learn of a stop made elsewhere.
            events.Publish(ChangeNotice.SessionChanged);
            _ended = !stoppingToken.IsCancellationRequested;
            retry = lasted.Elapsed >= Settled ? FirstRetry : Slower(retry);
            logger.LogInformation(
                "event stream ended with exit code {Exit} after {Seconds:0}s ({Reason}); listening again in {Retry:0}s",
                exit, lasted.Elapsed.TotalSeconds, _lastWord.Length > 0 ? _lastWord : "said nothing", retry.TotalSeconds);

            if (!await WaitAsync(retry, stoppingToken))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Whether the session the agent targets is running, asked the way the
    /// sessions panel asks (<c>system info</c>), which opens nothing. A look
    /// that fails counts as down: the stream is a convenience, and a session
    /// reopened behind the user's back is not.
    /// </summary>
    private async Task<bool> SessionRunningAsync(CancellationToken cancellationToken)
    {
        bool running;
        string target;
        try
        {
            var list = await sessions.ListAsync(cancellationToken);
            target = list.Selected;
            running = list.Sessions.Any(s => s.Active && s.Name == list.Selected);
            _session = target;
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            target = "the session";
            running = false;
            _session = "";
        }

        if (!running && !_waiting)
        {
            logger.LogInformation("{Session} is not running; the event stream waits for it rather than open it", target);
        }

        _waiting = !running;
        return running;
    }

    /// <summary>The pause before the next look; false when the agent is stopping.</summary>
    private static async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Twice as long each time, up to a minute. A session that is simply not
    /// running would otherwise be asked every five seconds for as long as it
    /// stays down, and every one of those asks is a line in CLI Activity.
    /// </summary>
    private static TimeSpan Slower(TimeSpan retry) =>
        retry >= LongestRetry ? LongestRetry : TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LongestRetry.Ticks));

    /// <summary>One line: what it names is added to the next notice, which leaves in a moment.</summary>
    private void Take(string line)
    {
        if (line.Contains("nrecognized command", StringComparison.Ordinal))
        {
            _unsupported = true;
            return;
        }

        if (WslcEventParsing.Parse(line) is not { } read)
        {
            // Not an event: the CLI saying why it is about to end. The first of
            // those lines is the one worth keeping — "Operation aborted" — and
            // the ones after it are the error code and the invitation to file a
            // bug, which say nothing about this stream.
            if (_lastWord.Length == 0 && line.Trim() is { Length: > 0 } word)
            {
                _lastWord = word;
            }

            return;
        }

        stops.Heard(read);
        health.Heard(read);
        recent.Heard(read);
        lock (_gate)
        {
            _pending.Add(read.Type);
            _flush ??= FlushAsync();
        }
    }

    /// <summary>
    /// The window that makes one user action one refresh. A container being
    /// stopped prints its network's disconnect and its own stop in the same
    /// second, and both mean the same thing to a screen: read the list again.
    /// </summary>
    private async Task FlushAsync()
    {
        await Task.Delay(Window);
        string[] kinds;
        lock (_gate)
        {
            kinds = [.. _pending];
            _pending.Clear();
            _flush = null;
        }

        if (kinds.Length > 0)
        {
            events.Publish(new ChangeNotice(kinds));
        }
    }
}
