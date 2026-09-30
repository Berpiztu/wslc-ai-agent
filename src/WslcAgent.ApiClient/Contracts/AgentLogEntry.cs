using System.Globalization;

namespace WslcAgent.ApiClient.Contracts;

/// <summary>One entry of the agent's own log; rows of <c>GET /api/v1/logs</c>.</summary>
/// <param name="EntryId">Its place in the merged log, oldest first: what <c>POST /api/v1/logs/delete</c> takes. Null for a command still running, which is not in the file yet.</param>
/// <param name="Timestamp">As written, <c>yyyy-MM-dd HH:mm:ss.fff</c> local time; a running command's start.</param>
/// <param name="DisplayTimestamp">The same to the tenth of a second.</param>
/// <param name="Level"><c>DEBUG</c>, <c>INFO</c>, <c>WARNING</c>, <c>ERROR</c> or <c>CRITICAL</c>; <c>RUNNING</c> for a command still running.</param>
/// <param name="Source">The logger that wrote it.</param>
/// <param name="Message">Its first line.</param>
/// <param name="Details">The expanded block: timestamp, level, source and message, then any further lines (a stack trace, a command's outcome and output).</param>
/// <param name="Kind">The group a <c>wslc</c> command line belongs to (<c>containers</c>, <c>images</c>, <c>networks</c>, <c>volumes</c>, <c>general</c>); <c>general</c> for any other message.</param>
/// <param name="Command">The <c>wslc</c> command this entry records — running, or finished with its outcome and output — when it records one; null for every other entry. In a list (<c>GET /api/v1/logs</c>) the output is left out, as are its lines in <paramref name="Details"/>; <c>POST /api/v1/logs/entries</c> gives the entry whole.</param>
public sealed record AgentLogEntry(
    int? EntryId,
    string Timestamp,
    string DisplayTimestamp,
    string Level,
    string Source,
    string Message,
    IReadOnlyList<string> Details,
    string Kind,
    CliTraceEntry? Command)
{
    /// <summary>The lines of <see cref="Details"/> that restate the entry itself, before anything more it carries.</summary>
    public const int HeaderLines = 4;

    /// <summary>What the entry has beyond its own line, a stack trace or a command's outcome and output; empty for most entries.</summary>
    public IReadOnlyList<string> Trace => Details.Skip(HeaderLines).SkipWhile(string.IsNullOrEmpty).ToList();

    /// <summary>A command that has started and not ended: listed from memory, not from the file, so it cannot be deleted.</summary>
    public bool Running => EntryId is null;

    /// <summary>
    /// What names this row across reloads: a command keeps its trace id from the
    /// moment it starts to the entry that says how it ended, so a page sees the
    /// same row change state instead of one row leaving and another arriving.
    /// </summary>
    public string Key => Command?.Id ?? EntryId?.ToString(CultureInfo.InvariantCulture) ?? "";
}

/// <summary>Body of <c>POST /api/v1/logs/delete</c>.</summary>
public sealed record DeleteLogEntriesRequest(IReadOnlyList<int> EntryIds);

/// <summary>Body of <c>POST /api/v1/logs/entries</c>: the entries wanted whole, a command's output included.</summary>
public sealed record LogEntriesRequest(IReadOnlyList<int> EntryIds);

/// <summary>Answer of <c>POST /api/v1/logs/delete</c> and of <c>DELETE /api/v1/logs</c>.</summary>
public sealed record DeleteLogEntriesResult(int Deleted);
