using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>Tools for WSLC sessions.</summary>
[McpServerToolType]
public static class SessionTools
{
    [McpServerTool(Name = "list_sessions", ReadOnly = true)]
    [Description("List the running WSLC sessions and which one the agent targets (empty = the CLI default).")]
    public static Task<SessionsResponse> ListSessions(ISessionService sessions, CancellationToken cancellationToken = default) =>
        sessions.ListAsync(cancellationToken);

    [McpServerTool(Name = "start_session")]
    [Description("Start a WSLC session (empty name: the one the agent targets). The session itself is left alone when it already runs, but the restart policy is applied in it either way when it is the session the agent targets: the enrolled containers are started again and the message names them. A session that was stopped comes back on its own with the next command while everything inside it stays down, which is what this brings back.")]
    public static Task<SessionActionResult> StartSession(
        ISessionService sessions,
        [Description("Session name, or empty for the one the agent targets.")] string name = "",
        CancellationToken cancellationToken = default) =>
        sessions.StartAsync(name, cancellationToken);

    [McpServerTool(Name = "stop_session", Destructive = true)]
    [Description("Stop a WSLC session (wslc system session terminate); every container running in it stops with it. Empty name: the one the agent targets. Destructive: the first call answers with a confirm token and the action to approve; call again with confirm=<token> after the user approved.")]
    public static async Task<object> StopSession(
        ISessionService sessions,
        ApprovalGate approvals,
        McpServer? server,
        [Description("Session name, or empty for the one the agent targets.")] string name = "",
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        var session = name.Trim().Length > 0 ? name.Trim() : "the one the agent targets";
        if (await approvals.CheckAsync(server, "stop_session", $"stop session {session}",
                session, new Dictionary<string, string> { ["session"] = session },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        return await sessions.StopAsync(name, cancellationToken);
    }

    [McpServerTool(Name = "switch_session")]
    [Description("Make the agent target another WSLC session for every following command. Pass an empty name for the CLI's own store for this user, which is the session the agent targets when none was chosen. Does not start or stop anything.")]
    public static string SwitchSession(
        ISessionService sessions,
        [Description("Session name, or empty for the CLI's own store for this user.")] string name = "") =>
        $"targeting session {sessions.Select(name)}";
}
