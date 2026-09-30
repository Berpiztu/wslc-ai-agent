using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Overview;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Sessions;

/// <summary>
/// The sessions this agent can work in: the
/// stores on disk (<c>%LOCALAPPDATA%\wslc\sessions</c>, one folder each) marked
/// with the ones <c>wslc system info</c> reports running, plus any running
/// session with no folder — minus the elevated process's store, which the agent
/// cannot open, enter or run in, so offering it is offering a dead end
/// (<see cref="SessionStores"/>). Starting and stopping follow this
/// sequence: a session is opened by opening the default store first (any
/// command does it) and, for a store of the user's own, <c>system session
/// enter</c>; the CLI's reserved <c>wslc-cli-…</c> stores cannot be entered
/// that way and say so. A session that comes up gets the restart policy applied
/// in it, as the agent's own start-up does
/// (<see cref="RestartPolicyReconciler"/>). A session stopped here stays down
/// until it is started here or opened from outside (<see cref="StoppedSessions"/>).
/// Every start, stop and choice is announced on the change stream
/// (<see cref="ChangeNotice.SessionChanged"/>): the other clients read the
/// session again when it happens, instead of asking on a clock.
/// </summary>
public sealed class SessionService(IWslcRunner wslc, ISelectedSession selected, RestartPolicyReconciler restarts, StoppedSessions stopped, WslcEvents events) : ISessionService
{
    /// <summary>Opening a store by name has to answer quickly; it is a handshake, not work.</summary>
    private static readonly TimeSpan EnterTimeout = TimeSpan.FromSeconds(10);

    public Task<SessionsResponse> ListAsync(CancellationToken cancellationToken = default) =>
        ListAsync(SessionStoreReader.Read(), cancellationToken);

    public string Select(string name)
    {
        selected.Name = name;
        events.Publish(ChangeNotice.SessionChanged);
        return selected.Name;
    }

    /// <summary>
    /// Stops a session with <c>system session terminate</c>, which takes down
    /// everything running inside it. An empty name means the one the agent
    /// targets. The session is held down before it is terminated: the stop
    /// lasts seconds, and a command arriving then would open it again.
    /// </summary>
    public async Task<SessionActionResult> StopAsync(string name, CancellationToken cancellationToken = default)
    {
        var target = Target(name);
        var label = Label(target);
        var before = await ListAsync(cancellationToken);
        if (!IsActive(before, target))
        {
            return new SessionActionResult(false, $"{label} is not running.", before);
        }

        stopped.Hold(target, before.Sessions.First(s => s.Active && s.Name == target).Id);
        try
        {
            await wslc.RunAsync(["system", "session", "terminate"], cancellationToken: cancellationToken, session: target);
        }
        catch
        {
            // Still running: holding it down would refuse every command in a live session.
            stopped.Release(target);
            throw;
        }

        events.Publish(ChangeNotice.SessionChanged);
        return new SessionActionResult(true, $"Stopped {label}.", await ListAsync(cancellationToken));
    }

    /// <summary>
    /// Starts a session: opening the default store
    /// (running any command without a session does it) activates the CLI's
    /// default and, often, the store just asked for; a store of the user's own
    /// is then entered by path. A reserved store that did not come up that way
    /// cannot be started from here, and the message says why.
    /// </summary>
    public async Task<SessionActionResult> StartAsync(string name, CancellationToken cancellationToken = default)
    {
        var target = Target(name);
        var label = Label(target);

        // Starting is the user's word that lets a stopped session be opened,
        // and the default store with it: opening it is how any session starts.
        stopped.Release(target);
        stopped.Release(SessionStores.Default);
        var sessions = await ListAsync(cancellationToken);
        if (IsActive(sessions, target))
        {
            // Running again is not the same as never stopped: a session that was
            // terminated comes back on its own with the next command — a list
            // that was polling reopens it two seconds later — while everything
            // that lived in it stays down. So this is not "nothing to do": the
            // policy is applied here too, which is what was being asked for.
            return await UpAsync($"{label} is already running.", target, sessions, changed: false, cancellationToken);
        }

        // Any command run with no session opens (or creates) the default store.
        await wslc.RunAsync(["images", "--format", "json"], cancellationToken: cancellationToken, session: "");
        sessions = await ListAsync(cancellationToken);
        if (IsActive(sessions, target))
        {
            return await UpAsync($"Started {label}.", target, sessions, changed: true, cancellationToken);
        }

        if (SessionStores.IsDefault(target))
        {
            throw new InvalidOperationException(
                $"{label} is the store every command opens and it did not come up. Running now: {Running(sessions)}. " +
                "Start it from a terminal on this machine and try again.");
        }

        var store = sessions.Sessions.FirstOrDefault(s => s.Name == target);
        if (store is { Reserved: true } or null)
        {
            throw new InvalidOperationException(SessionStores.IsAdmin(target)
                ? $"{label} belongs to an elevated wslc process and cannot be started from the agent. Running now: {Running(sessions)}."
                : $"{label} is one of the CLI's own stores and did not open. Running now: {Running(sessions)}. Start it from a terminal on this machine.");
        }

        await wslc.RunAsync(["system", "session", "enter", "--name", target, store.Path], EnterTimeout, cancellationToken, session: "");
        sessions = await ListAsync(cancellationToken);
        return IsActive(sessions, target)
            ? await UpAsync($"Started {label}.", target, sessions, changed: true, cancellationToken)
            : throw new InvalidOperationException($"{label} did not come up. Running now: {Running(sessions)}.");
    }

    /// <summary>
    /// The session is up, with the restart policy applied in it and named in
    /// the message: stopping a session takes every container inside it down,
    /// and nothing else brings them back — the agent's own start-up pass has
    /// long run. Something started is a change, whether or not the session
    /// itself had to be. The policy is about the containers of the session the
    /// agent targets, so a different one that is started is left alone.
    /// </summary>
    private async Task<SessionActionResult> UpAsync(string message, string target, SessionsResponse sessions, bool changed, CancellationToken cancellationToken)
    {
        events.Publish(ChangeNotice.SessionChanged);
        if (target != selected.Name)
        {
            return new SessionActionResult(changed, message, sessions);
        }

        var started = await restarts.ReconcileAsync(target, cancellationToken);
        return started.Count == 0
            ? new SessionActionResult(changed, message, sessions)
            : new SessionActionResult(true, $"{message} Restart policy started {string.Join(", ", started)}.", sessions);
    }

    internal async Task<SessionsResponse> ListAsync(SessionStoreUsage stores, CancellationToken cancellationToken)
    {
        var result = await wslc.RunAsync(["system", "info", "--format", "json"], cancellationToken: cancellationToken);
        var running = WslcJson.ParseRows(result.Stdout).FirstOrDefault() is { ValueKind: JsonValueKind.Object } info
            ? ParseSessions(info)
            : [];
        stopped.Observe(running.Select(s => (s.Name, s.Id)));
        return new SessionsResponse(Merge(stores, running), selected.Name);
    }

    /// <summary>
    /// One row per store, marked with what runs; a running session with no
    /// store of its own is added. The elevated process's store is left out of
    /// both: the agent cannot run a single command in it, and a row that can
    /// only be picked to be told no is not an option, it is a trap.
    /// </summary>
    internal static IReadOnlyList<SessionInfo> Merge(SessionStoreUsage stores, IReadOnlyList<SessionInfo> running)
    {
        var byName = running.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var rows = stores.Sessions
            .Where(store => !SessionStores.IsAdmin(store.Name))
            .Select(store => Row(store.Name, byName.GetValueOrDefault(store.Name)?.Id ?? 0, byName.ContainsKey(store.Name), store.Path))
            .ToList();

        var known = rows.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        rows.AddRange(running.Where(s => !known.Contains(s.Name) && !SessionStores.IsAdmin(s.Name)).Select(s => Row(s.Name, s.Id, true, "")));
        return rows.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The sessions <c>wslc system info</c> reports under <c>Server.Sessions</c>; all of them run.</summary>
    internal static IReadOnlyList<SessionInfo> ParseSessions(JsonElement info)
    {
        if (!info.TryGetProperty("Server", out var server) || !server.TryGetProperty("Sessions", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return list.EnumerateArray()
            .Select(s => Row(
                s.GetString("Name"),
                s.TryGetProperty("ID", out var id) && id.TryGetInt32(out var parsed) ? parsed : 0,
                active: true,
                path: ""))
            .Where(s => s.Name.Length > 0)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SessionInfo Row(string name, int id, bool active, string path)
    {
        var reserved = SessionStores.IsReserved(name);
        return new SessionInfo(id, name, active, reserved, active || !reserved || !SessionStores.IsAdmin(name), path);
    }

    /// <summary>An empty name means the session the agent targets, which is never nameless.</summary>
    private string Target(string name) => name.Trim().Length > 0 ? name.Trim() : selected.Name;

    private static string Label(string target) => $"Session {target}";

    /// <summary>
    /// One named session, and only it. "Something is running" used to answer
    /// for the session with no name, and it answered wrong: with another
    /// session up and this one down, the panel said Active and offered to stop
    /// what it was not showing.
    /// </summary>
    private static bool IsActive(SessionsResponse sessions, string target) =>
        sessions.Sessions.Any(s => s.Active && s.Name == target);

    private static string Running(SessionsResponse sessions)
    {
        var names = sessions.Sessions.Where(s => s.Active).Select(s => s.Name).ToList();
        return names.Count > 0 ? string.Join(", ", names) : "nothing";
    }
}
