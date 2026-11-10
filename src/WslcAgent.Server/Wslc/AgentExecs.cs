namespace WslcAgent.Server.Wslc;

/// <summary>
/// The commands the agent itself ran inside containers, kept for a minute:
/// how an exec event that is the agent's own (a Files listing, a backup, a
/// terminal) is told from one run from elsewhere. An exec is created as its
/// command starts, so its event falls just after the start recorded here,
/// however long the command then runs.
/// </summary>
public sealed class AgentExecs(TimeProvider time)
{
    private static readonly TimeSpan Kept = TimeSpan.FromMinutes(1);

    /// <summary>How far from a start of the agent's an exec event is still taken for it: the clocks are the same, the CLI takes a moment to begin.</summary>
    private static readonly TimeSpan Before = TimeSpan.FromSeconds(1), After = TimeSpan.FromSeconds(15);

    private readonly List<(DateTimeOffset At, string[] Args)> _started = [];
    private readonly Lock _gate = new();

    /// <summary>A command the agent is about to run: kept when it is an exec.</summary>
    public void Starting(IReadOnlyList<string> args)
    {
        if (args is not (["exec", ..] or ["container", "exec", ..]))
        {
            return;
        }

        var now = time.GetLocalNow();
        lock (_gate)
        {
            _started.RemoveAll(started => now - started.At > Kept);
            _started.Add((now, [.. args]));
        }
    }

    /// <summary>Whether an exec created at <paramref name="at"/> in this container is one the agent started.</summary>
    public bool Started(string containerId, string? name, DateTimeOffset at)
    {
        lock (_gate)
        {
            return _started.Any(started => at >= started.At - Before && at <= started.At + After && started.Args.Any(arg => Names(arg, containerId, name)));
        }
    }

    /// <summary>A command names a container by its name, its id or a prefix of its id (twelve characters at the least, as the CLI shows them).</summary>
    private static bool Names(string arg, string containerId, string? name) =>
        (name is { Length: > 0 } && arg == name) || (arg.Length >= 12 && containerId.StartsWith(arg, StringComparison.Ordinal));
}

/// <summary>Records in <see cref="AgentExecs"/> every exec the agent runs, whichever way it runs it, before passing it on.</summary>
public sealed class AgentExecRunner(IWslcRunner inner, AgentExecs execs) : IWslcRunner
{
    public bool SupportsTerminal => inner.SupportsTerminal;

    public Task<WslcResult> RunAsync(IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken cancellationToken = default, string? standardInput = null, string? session = null)
    {
        execs.Starting(args);
        return inner.RunAsync(args, timeout, cancellationToken, standardInput, session);
    }

    public Task<WslcResult> StreamAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default, Stream? standardInput = null)
    {
        execs.Starting(args);
        return inner.StreamAsync(args, onLine, cancellationToken, standardInput);
    }

    public Task<int> WatchAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default) =>
        inner.WatchAsync(args, onLine, cancellationToken);

    /// <summary>A command line resolved is one about to be run by another program: the native terminal window.</summary>
    public WslcCommandLine Resolve(IReadOnlyList<string> args)
    {
        execs.Starting(args);
        return inner.Resolve(args);
    }

    public IWslcSession StartInteractive(IReadOnlyList<string> args, int columns = 120, int rows = 30)
    {
        execs.Starting(args);
        return inner.StartInteractive(args, columns, rows);
    }
}
