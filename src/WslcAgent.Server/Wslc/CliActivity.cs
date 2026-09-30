using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// Every <c>wslc</c> command the agent runs, on its way through. A command is
/// entered when it starts, so a slow one is listed as running from the moment
/// it begins, and written to the agent's log when it ends — its line, how it
/// ended, how long it took and what it printed (<see cref="CliTraceBlock"/>) —
/// where the Logs page and the <c>cli_activity</c> tool read it back. Only
/// what is running right now lives in memory; the log file is the record.
/// </summary>
public interface ICliActivity
{
    /// <summary>Enters a command as running — or, with <paramref name="program"/> <c>transfer</c>, a file on its way; returns the id to complete it with, the one its log entry will carry.</summary>
    string Start(IReadOnlyList<string> args, string program = "wslc");

    /// <summary>Completes a running command with how it ended, which writes its entry to the log.</summary>
    void Finish(string id, TimeSpan duration, int? exitCode, string status, string stdout, string stderr);

    /// <summary>The commands running at this moment, oldest first.</summary>
    IReadOnlyList<CliTraceEntry> Running();
}

public sealed class CliActivity(ILogger<CliActivity> logger) : ICliActivity
{
    private readonly Dictionary<string, CliTraceEntry> _running = [];
    private readonly Lock _gate = new();

    public string Start(IReadOnlyList<string> args, string program = "wslc")
    {
        var (_, title, session) = CliTraceDescription.Describe(args);
        var kind = CliTraceDescription.KindOf(program, args);
        // Run inside a scope that names the operation (a file carried in or
        // out), the command carries that name instead of its arguments'.
        if (CliTitle.Now is { } named)
        {
            title = CliTitle.For(named, args);
        }

        var trace = new CliTraceEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.Now, null, args.ToList(), null, "running", "", "", kind, title, session, program);
        lock (_gate)
        {
            _running[trace.Id] = trace;
        }

        return trace.Id;
    }

    /// <summary>
    /// A command that went wrong is a warning, as its failure line was before the
    /// whole command moved into the log; one that worked, or that the user
    /// cancelled, is information.
    /// </summary>
    public void Finish(string id, TimeSpan duration, int? exitCode, string status, string stdout, string stderr)
    {
        CliTraceEntry? trace;
        lock (_gate)
        {
            _running.Remove(id, out trace);
        }

        if (trace is null)
        {
            return;
        }

        // A command that failed is an error: the Logs page's Error filter is where
        // a user looks for what went wrong, and at Warning it found nothing.
        // A container that was not there is not something that went wrong
        // ("not_found"): it stays in the record, out of that filter.
        var level = status is "success" or "cancelled" or "not_found" ? LogLevel.Information : LogLevel.Error;
        logger.Log(level, "{Program} {Args}\n{Outcome}", trace.Program, string.Join(' ', trace.Args), CliTraceBlock.Write(id, status, exitCode, duration, stdout, stderr, Named(trace)));
    }

    /// <summary>The title a scope gave the command, which its arguments alone would not give back when the log is read; null for the rest.</summary>
    private static string? Named(CliTraceEntry trace) =>
        trace.Title == CliTraceDescription.Describe(trace.Args).Title ? null : trace.Title;

    public IReadOnlyList<CliTraceEntry> Running()
    {
        lock (_gate)
        {
            return _running.Values.OrderBy(trace => trace.StartedAt).ToList();
        }
    }
}
