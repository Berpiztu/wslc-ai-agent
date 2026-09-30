namespace WslcAgent.Server.Wslc;

/// <summary>
/// The sessions the user stopped from the agent, kept down until the user
/// starts them again. Stopping a session is not enough to keep it stopped: any
/// command that names a session opens it, so the next list a screen polled, a
/// client's stats or the restart policy brought it back within a second of the
/// terminate. <see cref="WslcRunner"/> asks here
/// before it runs anything, and refuses what would open a session held down.
/// A session opened from somewhere else — a terminal on the machine — is a new
/// session with a new ID, and that is the user's word too: the hold lets go.
/// </summary>
public sealed class StoppedSessions(ILogger<StoppedSessions> logger)
{
    private readonly Dictionary<string, int> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>
    /// Holds <paramref name="name"/> down from now on; <paramref name="id"/> is
    /// the session being stopped, so that only a different one lets go of it.
    /// Taken before the terminate, which lasts seconds, so nothing opens it on
    /// the way down either.
    /// </summary>
    public void Hold(string name, int id)
    {
        lock (_gate)
        {
            _held[name] = id;
        }

        logger.LogInformation("{Session} was stopped by the user; commands that would open it are refused until it is started", name);
    }

    /// <summary>The user starts <paramref name="name"/>: commands reach it again.</summary>
    public void Release(string name)
    {
        bool released;
        lock (_gate)
        {
            released = _held.Remove(name);
        }

        if (released)
        {
            logger.LogInformation("{Session} is no longer held down", name);
        }
    }

    /// <summary>
    /// What <c>system info</c> reports running: a held session that runs with
    /// another ID was opened by someone else after the stop, and is let go.
    /// The same ID is the session on its way down, still listed.
    /// </summary>
    public void Observe(IEnumerable<(string Name, int Id)> running)
    {
        foreach (var (name, id) in running)
        {
            bool reopened;
            lock (_gate)
            {
                reopened = _held.TryGetValue(name, out var stopped) && stopped != id;
            }

            if (reopened)
            {
                logger.LogInformation("{Session} was opened again from outside the agent", name);
                Release(name);
            }
        }
    }

    /// <summary>
    /// Why <paramref name="args"/> may not run in <paramref name="session"/>, or
    /// null when it may: the session is not held, or the command is one of those
    /// measured to leave a stopped session down.
    /// </summary>
    public string? Refusal(IReadOnlyList<string> args, string session)
    {
        lock (_gate)
        {
            if (!_held.ContainsKey(session))
            {
                return null;
            }
        }

        return LeavesSessionDown(args)
            ? null
            : $"Session {session} is stopped. Start it to use it again.";
    }

    /// <summary>Whether the user stopped <paramref name="name"/> from the agent and it is held down: a session gone that is not is one nobody here stopped.</summary>
    public bool Holds(string name)
    {
        lock (_gate)
        {
            return _held.ContainsKey(name);
        }
    }

    /// <summary><c>version</c>, <c>info</c>, <c>system info</c> and the session verbs open nothing.</summary>
    internal static bool LeavesSessionDown(IReadOnlyList<string> args) =>
        args.Count > 0 && (args[0] is "version" or "info"
            || (args.Count > 1 && args[0] == "system" && args[1] is "info" or "session"));
}
