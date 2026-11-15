namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/terminal/status</c>: what the Terminal page connects to.</summary>
/// <param name="Mode">Always <c>local</c>: the agent runs on the host it manages.</param>
/// <param name="Label">What the page shows as the shell's target.</param>
/// <param name="ServerLocal">The caller sits at the agent's machine, so a native terminal window can open where it can see it.</param>
public sealed record TerminalStatus(string Mode, string Label, bool ServerLocal);

/// <summary>Answer of <c>POST /api/v1/terminal/jobs/{job}</c>: the line the terminal types into its shell.</summary>
/// <param name="Command">What to type, as if the user had.</param>
/// <param name="ScriptPath">The script the command runs, written to the agent's temporary folder.</param>
/// <param name="StoragePath">The <c>storage.vhdx</c> it compacts.</param>
/// <param name="Session">The session that storage belongs to; empty for the default.</param>
public sealed record TerminalJob(string Command, string ScriptPath, string StoragePath, string Session);

/// <summary>Answer of <c>GET /api/v1/terminal/jobs/{job}</c>.</summary>
/// <param name="Running">The job's script is still under way on the agent's machine.</param>
public sealed record TerminalJobState(bool Running);

/// <summary>Answer of <c>POST /api/v1/terminal/jobs/{job}/stop</c>.</summary>
/// <param name="Stopping">The job was working on the store and has been asked to stop: it leaves at its next safe place, between two steps. False when there was nothing to stop.</param>
/// <param name="Message">What to tell the user.</param>
public sealed record TerminalJobStop(bool Stopping, string Message);
