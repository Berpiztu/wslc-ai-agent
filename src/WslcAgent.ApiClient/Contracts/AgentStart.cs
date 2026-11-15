namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Body of <c>GET</c> and <c>PUT /api/v1/agent-start</c>: Settings → Security →
/// Session at agent start.
/// </summary>
/// <param name="StartStoppedSession">
/// When the agent starts and finds its session stopped, it starts it to bring
/// up the containers whose restart policy says they should run. Off, a session
/// found stopped is left stopped, and those containers with it, until someone
/// starts it.
/// </param>
public sealed record AgentStartSettings(bool StartStoppedSession = true);