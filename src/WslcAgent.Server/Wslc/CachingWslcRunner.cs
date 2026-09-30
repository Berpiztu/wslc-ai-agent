using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// A short-lived read cache, in front of <see cref="WslcRunner"/>.
/// Every open client polls <c>container list</c> and <c>container stats</c> on
/// its own timer, and Home, the containers list and the usage scanner each ask
/// for the stats again, so one tick cost several launches — and wslc makes
/// every other command wait while a <c>stats</c> samples (1.6–2 s measured:
/// a Files listing of 90 ms took 1.7 s behind one).
/// <list type="bullet">
/// <item>A read younger than <see cref="WslcOptions.ReadCacheSeconds"/> is served from memory.</item>
/// <item>Identical reads at the same time share one launch, which none of them can cancel for the others.</item>
/// <item><c>stats</c> is served stale for up to <see cref="StaleWindows"/> windows while it refreshes behind.</item>
/// <item>Any command that may change state clears the cache, whether it worked or not: a failed start or rm may have changed things half-way.</item>
/// </list>
/// Only whole-list reads are cached: <c>list</c> and <c>stats</c> of every
/// container, never one container's, never anything else.
/// </summary>
public sealed class CachingWslcRunner(IWslcRunner inner, ISelectedSession selected, IOptions<WslcOptions> options, ILogger<CachingWslcRunner> logger) : IWslcRunner
{
    /// <summary>How many windows old a <c>stats</c> answer may be and still be handed out while a fresh one is read.</summary>
    private const double StaleWindows = 3;

    /// <summary>The window's bounds: below half a second nothing is shared, above thirty a list is too old.</summary>
    private const double MinSeconds = 0.5, MaxSeconds = 30;

    private static readonly TimeSpan ReportEvery = TimeSpan.FromMinutes(1);

    /// <summary>Verbs that never change wslc's state; anything else that ends clears the cache.</summary>
    private static readonly HashSet<string> ReadOnlyVerbs = ["list", "ps", "stats", "inspect", "version", "logs", "info", "top", "port", "diff", "events"];

    /// <summary>The same, under a resource verb (<c>image ls</c>, <c>volume inspect</c>…).</summary>
    private static readonly HashSet<string> ReadOnlySubverbs = ["ls", "list", "inspect", "df", "info", "version", "history"];

    private static readonly HashSet<string> ResourceVerbs = ["image", "images", "network", "volume", "session", "system", "container"];

    private readonly Dictionary<string, (long At, WslcResult Result)> _cache = [];
    private readonly Dictionary<string, Task<WslcResult>> _inflight = [];
    private readonly Lock _gate = new();

    /// <summary>Raised by every clearing: a read that started before a change does not store what it read.</summary>
    private long _generation;

    private long _hits, _misses, _shared, _stale;
    private long _lastReport = Stopwatch.GetTimestamp();

    public bool SupportsTerminal => inner.SupportsTerminal;

    public Task<WslcResult> RunAsync(IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken cancellationToken = default, string? standardInput = null, string? session = null)
    {
        var seconds = options.Value.ReadCacheSeconds;
        var ttl = seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Clamp(seconds, MinSeconds, MaxSeconds));
        if (ttl > TimeSpan.Zero && timeout is null && standardInput is null && CachedRead(args) is { } stale)
        {
            return ReadAsync(args, session, ttl, stale, cancellationToken);
        }

        return Changing(args, () => inner.RunAsync(args, timeout, cancellationToken, standardInput, session));
    }

    public Task<WslcResult> StreamAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default) =>
        Changing(args, () => inner.StreamAsync(args, onLine, cancellationToken));

    public Task<int> WatchAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default) =>
        Changing(args, () => inner.WatchAsync(args, onLine, cancellationToken));

    public WslcCommandLine Resolve(IReadOnlyList<string> args) => inner.Resolve(args);

    /// <summary>An exec terminal can change anything from inside the container: what was read before it is not trusted after.</summary>
    public IWslcSession StartInteractive(IReadOnlyList<string> args, int columns = 120, int rows = 30)
    {
        Clear();
        return inner.StartInteractive(args, columns, rows);
    }

    /// <summary>
    /// Whether a command is one of the cached reads, and if so whether it may be
    /// served stale: <c>stats</c> takes a second or two per launch against a
    /// tenth for <c>list</c>, so pages polling it no longer wait on every tick.
    /// Null for everything else, including a read of one container.
    /// </summary>
    private static bool? CachedRead(IReadOnlyList<string> args)
    {
        var (verb, sub) = Positions(args);
        var (read, from) = At(args, verb) == "container" ? (At(args, sub), sub + 1) : (At(args, verb), verb + 1);
        if (read is not ("list" or "stats") || !OnlyFlags(args, from))
        {
            return null;
        }

        return read == "stats";
    }

    /// <summary>Nothing but flags from <paramref name="from"/> on, the value of <c>--format</c> included: no container named.</summary>
    private static bool OnlyFlags(IReadOnlyList<string> args, int from)
    {
        for (var i = from; i < args.Count; i++)
        {
            if (args[i] == "--format")
            {
                i++;
            }
            else if (!args[i].StartsWith('-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsReadOnly(IReadOnlyList<string> args)
    {
        var (verbAt, subAt) = Positions(args);
        var verb = At(args, verbAt);
        var sub = At(args, subAt);
        return ReadOnlyVerbs.Contains(verb)
            || (ResourceVerbs.Contains(verb) && (ReadOnlySubverbs.Contains(sub) || ReadOnlyVerbs.Contains(sub)));
    }

    /// <summary>Where the verb stands, past any global option, and the first word after it that is not a flag; <c>args.Count</c> when there is none.</summary>
    private static (int Verb, int Sub) Positions(IReadOnlyList<string> args)
    {
        var verb = 0;
        while (verb < args.Count && args[verb].StartsWith('-'))
        {
            verb += args[verb] is "--session" or "-s" ? 2 : 1;
        }

        var sub = verb + 1;
        while (sub < args.Count && args[sub].StartsWith('-'))
        {
            sub++;
        }

        return (Math.Min(verb, args.Count), Math.Min(sub, args.Count));
    }

    private static string At(IReadOnlyList<string> args, int index) => index < args.Count ? args[index] : "";

    private async Task<T> Changing<T>(IReadOnlyList<string> args, Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        finally
        {
            if (!IsReadOnly(args))
            {
                Clear();
            }
        }
    }

    private void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
            _generation++;
        }
    }

    private async Task<WslcResult> ReadAsync(IReadOnlyList<string> args, string? session, TimeSpan ttl, bool mayBeStale, CancellationToken cancellationToken)
    {
        // The session is part of the key: two sessions' containers are two lists.
        var key = $"{session ?? selected.Name}\u001f{string.Join('\u001f', args)}";
        Task<WslcResult> shared;
        lock (_gate)
        {
            Report();
            if (_cache.TryGetValue(key, out var hit))
            {
                var age = Stopwatch.GetElapsedTime(hit.At);
                if (age < ttl)
                {
                    _hits++;
                    return hit.Result;
                }

                if (mayBeStale && age < ttl * StaleWindows)
                {
                    _stale++;
                    if (!_inflight.ContainsKey(key))
                    {
                        _ = RefreshBehindAsync(Shared(key, args, session));
                    }

                    return hit.Result;
                }
            }

            shared = Shared(key, args, session);
        }

        // Only this caller stops waiting: the launch goes on for the others.
        return await shared.WaitAsync(cancellationToken);
    }

    /// <summary>The launch already under way for this read, or a new one. Called under the gate.</summary>
    private Task<WslcResult> Shared(string key, IReadOnlyList<string> args, string? session)
    {
        if (_inflight.TryGetValue(key, out var running))
        {
            _shared++;
            return running;
        }

        _misses++;
        var generation = _generation;
        var launch = Task.Run(() => LaunchAsync(key, args, session, generation));
        _inflight[key] = launch;
        return launch;
    }

    private async Task<WslcResult> LaunchAsync(string key, IReadOnlyList<string> args, string? session, long generation)
    {
        try
        {
            var result = await inner.RunAsync(args, cancellationToken: CancellationToken.None, session: session);
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _cache[key] = (Stopwatch.GetTimestamp(), result);
                }
            }

            return result;
        }
        finally
        {
            lock (_gate)
            {
                _inflight.Remove(key);
            }
        }
    }

    /// <summary>A refresh nobody is waiting for: a failure only means the next read tries again.</summary>
    private async Task RefreshBehindAsync(Task<WslcResult> refresh)
    {
        try
        {
            await refresh;
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            logger.LogDebug("background refresh failed: {Message}", ex.Message);
        }
    }

    /// <summary>One line per minute of activity: the launches the cache saved. Called under the gate.</summary>
    private void Report()
    {
        if (Stopwatch.GetElapsedTime(_lastReport) < ReportEvery || _hits + _misses + _shared == 0)
        {
            return;
        }

        _lastReport = Stopwatch.GetTimestamp();
        logger.LogInformation(
            "Read cache: {Misses} wslc launches, {Hits} served from memory, {Shared} shared in flight, {Stale} stale-while-refreshing ({Saved} launches saved so far)",
            _misses, _hits, _shared, _stale, _hits + _shared);
    }
}
