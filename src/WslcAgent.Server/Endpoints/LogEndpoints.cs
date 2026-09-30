using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.AgentLog;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/logs</c>: the agent's own log. See docs/api-v1.md.</summary>
public static class LogEndpoints
{
    public static RouteGroupBuilder MapLogEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/logs");

        // The list, abridged: a command's outcome without its output. `after` is what a page that keeps refreshing asks for.
        group.MapGet("", (AgentLogs logs, int? tail, int? after) => logs.Read(tail, after))
            .WithName("AgentLogs");

        // The entries whole, by id: a row opened, a copy.
        group.MapPost("/entries", (LogEntriesRequest request, AgentLogs logs) => logs.Entries(request.EntryIds))
            .WithName("AgentLogEntries");

        group.MapGet("/text", (AgentLogs logs, int? tail) => Results.Text(logs.Text(tail)))
            .WithName("AgentLogsText");

        group.MapPost("/delete", (DeleteLogEntriesRequest request, AgentLogs logs) => new DeleteLogEntriesResult(logs.Delete(request.EntryIds)))
            .WithName("DeleteAgentLogEntries");

        group.MapDelete("", (AgentLogs logs) => new DeleteLogEntriesResult(logs.Clear()))
            .WithName("ClearAgentLogs");

        return api;
    }
}
