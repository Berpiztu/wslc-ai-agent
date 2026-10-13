using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Host;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/logs</c>: the agent's own log. See docs/api-v1.md.</summary>
public static class LogEndpoints
{
    /// <summary>A report is a line or a few of what a screen showed: more than this is cut, so the log cannot be filled through it.</summary>
    private const int LongestReport = 4000;

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

        // What a client showed its user as a warning or an error: written here too, as a
        // warning or an error, so that what was on a screen for a moment is found afterwards.
        // Some of it the agent said itself and is here already; the rest only the client knew.
        group.MapPost("/client", NoContent (ClientReport report, HttpContext http, ILoggerFactory loggers) =>
            {
                var text = (report.Text ?? "").Trim();
                if (text.Length > 0)
                {
                    var from = LocalClient.IsLocal(http) ? "this machine" : http.Connection.RemoteIpAddress?.ToString() ?? "another machine";
                    loggers.CreateLogger("Client").Log(
                        report.Level == ClientReport.Error ? LogLevel.Error : LogLevel.Warning,
                        "Shown to the user of a client ({From}; {Where}): {Text}", from, (report.Where ?? "").Trim(), text.Length > LongestReport ? text[..LongestReport] + "…" : text);
                }

                return TypedResults.NoContent();
            })
            .WithName("ReportClient");

        return api;
    }
}
