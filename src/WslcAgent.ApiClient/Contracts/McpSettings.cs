namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Body of <c>PUT /api/v1/mcp/settings</c>: the two switches the operator has over
/// the MCP server the agent hosts. Both take effect at once on the running agent.
/// </summary>
/// <param name="Enabled">Serve MCP at all. Off, the endpoint answers every client that it is switched off and lists no tool.</param>
/// <param name="AllowDestructiveTools">Offer the tools that remove, kill, exec and stop a session. Off, they are not even listed; on, each one still needs the user's approval, twice.</param>
public sealed record McpSettings(bool Enabled, bool AllowDestructiveTools);

/// <summary>
/// Body of <c>GET /api/v1/mcp/settings</c>: the switches plus what a client would
/// find right now, so Settings can show the state instead of describing it.
/// </summary>
/// <param name="Settings">What the operator has chosen.</param>
/// <param name="LocalUrl">The URL to register in an MCP client on this machine.</param>
/// <param name="Tools">How many tools a client sees with these switches.</param>
/// <param name="DestructiveTools">How many of them are the gated ones, listed or not.</param>
/// <param name="ApiToken">Whether a caller from another machine can be let in at all.</param>
/// <param name="SkillTargets">Where each known AI client keeps its skills on the agent's machine.</param>
/// <param name="SkillCommands">Each client's own installer line, already pointing at this agent.</param>
/// <param name="SkillAddress">The address those lines were written with: this agent's own, or the one the caller asked for.</param>
/// <param name="SshHosts">The machines this one already knows how to reach, from its SSH config, so a destination is picked rather than typed.</param>
public sealed record McpStatus(McpSettings Settings, string LocalUrl, int Tools, int DestructiveTools, bool ApiToken, IReadOnlyList<SkillTarget> SkillTargets, IReadOnlyList<SkillCommand> SkillCommands, string SkillAddress, IReadOnlyList<string> SshHosts);

/// <summary>
/// A client on a machine, as found there: the same client lives on more than one
/// (Hermes runs on the agent's machine and on the VPS), so what is offered is
/// what the machine that will run the install actually has.
/// </summary>
/// <param name="Client">Hermes, OpenClaw or Claude Code.</param>
/// <param name="Found">Its command answers there, or its folder is there.</param>
/// <param name="Profiles">The profiles it holds, each of which installs on its own.</param>
public sealed record SkillClient(string Client, bool Found, IReadOnlyList<string> Profiles);

/// <summary>A client this agent knows how to install its skill for, and where.</summary>
/// <param name="Client">Its name, as the operator knows it: one per Hermes profile, since each profile reads its own skills.</param>
/// <param name="Folder">The folder it reads skills from, under the account the agent runs as.</param>
/// <param name="ClientFound">Whether that client is installed on this machine at all.</param>
/// <param name="Installed">Whether this agent's skill is already in that folder.</param>
public sealed record SkillTarget(string Client, string Folder, bool ClientFound, bool Installed);

/// <summary>
/// A client's own way of installing a skill, ready to run. Its installer is the
/// standard path — it scans, versions and records the skill as that client
/// expects — so the agent offers the line rather than writing the file behind
/// its back.
/// </summary>
/// <param name="Client">The client the line is for.</param>
/// <param name="Command">The command to run, with this agent's address already in it.</param>
/// <param name="Note">What to watch out for when the client runs on another machine.</param>
public sealed record SkillCommand(string Client, string Command, string Note);

/// <summary>
/// Body of <c>POST /api/v1/mcp/skill/install</c>: which client to install for,
/// where, and what its installer needs. The agent runs that client's own
/// command — here, or over ssh on the machine the client lives on.
/// </summary>
/// <param name="Client">Hermes, OpenClaw or Claude Code.</param>
/// <param name="Address">How that client reaches this agent, for an installer that takes a URL; empty for the agent's own address.</param>
/// <param name="Profile">A Hermes profile, which is its own command; empty for the default one.</param>
/// <param name="SshDestination">The machine to run it on, as ssh takes it (user@host); empty runs it on the agent's own machine.</param>
public sealed record SkillInstallRequest(string Client, string Address = "", string Profile = "", string SshDestination = "");

/// <summary>What the install did: the line it ran, how it went, and what it said.</summary>
/// <param name="Client">The client it was for.</param>
/// <param name="Command">The command run, or what was written when there is no installer.</param>
/// <param name="ExitCode">0 when the installer was happy; -1 when it could not be run at all.</param>
/// <param name="Output">Its own words, which is what the operator needs to read.</param>
/// <param name="Ok">
/// Whether it really worked. Not the exit code alone: Hermes prints
/// "Error: Could not find …" and still exits 0, and a page that reads only the
/// code paints that green, which is worse than saying nothing.
/// </param>
public sealed record SkillInstallResult(string Client, string Command, int ExitCode, string Output, bool Ok);

/// <summary>Where the skill was written, and whether that folder had to be made.</summary>
public sealed record SkillInstalled(string Path, bool FolderCreated);
