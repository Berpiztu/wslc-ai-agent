namespace WslcAgent.ApiClient.Contracts;

/// <summary>A WSLC session: a store on this machine, running or stopped.</summary>
/// <param name="Id">Session id as reported by <c>wslc system info</c>; 0 while it is stopped.</param>
/// <param name="Name">Session name, e.g. <c>wslc-cli-user</c>.</param>
/// <param name="Active">True while the session runs.</param>
/// <param name="Reserved">A CLI-reserved default store (<c>wslc-cli-…</c>), which cannot be entered by path.</param>
/// <param name="CanStart">The agent can start it: it is running already, or it is not reserved, or it is this user's default store.</param>
/// <param name="Path">The store folder, what <c>session enter</c> needs; empty for a session with no folder on disk.</param>
public sealed record SessionInfo(int Id, string Name, bool Active, bool Reserved, bool CanStart, string Path);

/// <summary>Body of <c>GET /api/v1/sessions</c>.</summary>
/// <param name="Sessions">The sessions the agent can work in, running and stopped; an elevated process's store is not one of them.</param>
/// <param name="Selected">The session the agent targets, always by name: the CLI's own store for this user when none was chosen.</param>
public sealed record SessionsResponse(IReadOnlyList<SessionInfo> Sessions, string Selected);

/// <summary>Body of <c>POST /api/v1/sessions/select</c>.</summary>
/// <param name="Name">Session to target; empty for the CLI's own store for this user.</param>
public sealed record SelectSessionRequest(string Name);

/// <summary>Body of <c>POST /api/v1/sessions/start</c> and <c>/stop</c>.</summary>
/// <param name="Name">The session to act on; empty for the one the agent targets.</param>
public sealed record SessionActionRequest(string Name);

/// <summary>What a start or a stop did.</summary>
/// <param name="Changed">False when there was nothing to do (already running, already stopped).</param>
/// <param name="Message">What to tell the user.</param>
/// <param name="Sessions">The sessions as they stand afterwards, so the caller need not ask again.</param>
public sealed record SessionActionResult(bool Changed, string Message, SessionsResponse Sessions);
