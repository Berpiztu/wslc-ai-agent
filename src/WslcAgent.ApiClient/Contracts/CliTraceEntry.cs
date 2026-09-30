namespace WslcAgent.ApiClient.Contracts;

/// <summary>One <c>wslc</c> command the agent ran or is running — or one file it carried into or out of a container, a row of its own from announced to ended; rows of <c>GET /api/v1/cli/activity</c>.</summary>
/// <param name="Id">Opaque id of the run.</param>
/// <param name="StartedAt">When the command started.</param>
/// <param name="Duration">How long it took; null while it is still running.</param>
/// <param name="Args">Arguments passed to <c>wslc</c>; for a transfer, what it carries and where.</param>
/// <param name="ExitCode">Exit code; null while running, or when the command timed out or was cancelled.</param>
/// <param name="Status"><c>running</c>, <c>success</c>, <c>error</c>, <c>timeout</c> or <c>cancelled</c>.</param>
/// <param name="Stdout">Captured standard output, truncated.</param>
/// <param name="Stderr">Captured standard error, truncated.</param>
/// <param name="Kind">The group it belongs to: <c>containers</c>, <c>images</c>, <c>networks</c>, <c>volumes</c>, <c>transfers</c> or <c>general</c>.</param>
/// <param name="Title">What the command does, in words: <c>Pull alpine:latest</c>, <c>List containers</c>.</param>
/// <param name="Session">The WSLC session it ran in; empty when none was named.</param>
/// <param name="Program"><c>wslc</c> for a command; <c>transfer</c> for a file the agent carried, which is no command of wslc's.</param>
public sealed record CliTraceEntry(
    string Id,
    DateTimeOffset StartedAt,
    TimeSpan? Duration,
    IReadOnlyList<string> Args,
    int? ExitCode,
    string Status,
    string Stdout,
    string Stderr,
    string Kind,
    string Title,
    string Session,
    string Program = "wslc")
{
    public string CommandLine => $"{Program} {string.Join(' ', Args)}";
}
