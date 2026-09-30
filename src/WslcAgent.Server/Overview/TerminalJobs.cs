using System.Text;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// Named commands the host terminal types into its shell once it is up.
/// Only <c>compact-vhdx</c> exists (System → Compact
/// VHDX): its script is far longer than a command line allows, so it is written
/// to <c>%TEMP%</c> and the line to type is a short <c>powershell -File</c>.
/// </summary>
public sealed class TerminalJobs(IWslcRunner wslc, ISelectedSession selected)
{
    public const string CompactVhdx = "compact-vhdx";
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Stages <paramref name="job"/>. <see cref="KeyNotFoundException"/> (404) for an
    /// unknown job or no VHDX to compact; <see cref="InvalidOperationException"/>
    /// (409) while a session is active, since compaction never terminates one.
    /// </summary>
    public async Task<TerminalJob> PrepareAsync(string job, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(job.Trim(), CompactVhdx, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException($"Unknown terminal job: {job}");
        }

        IReadOnlyList<ActiveSession> active;
        try
        {
            active = SystemParsing.ActiveSessions((await wslc.RunAsync(["system", "session", "list"], cancellationToken: cancellationToken)).Stdout);
        }
        catch (WslcException ex)
        {
            throw new InvalidOperationException($"Could not verify active WSLC sessions; not compacting: {ex.Message}");
        }

        // The session whose VHDX this is, and no other: another store being in
        // use does not hold this file open, and refusing over it refused
        // compactions that were perfectly possible.
        var session = selected.Name ?? "";
        if (active.FirstOrDefault(s => s.DisplayName == session) is { } running)
        {
            // Whose word this is, and the process it names: `wslc system
            // session list` goes on listing a session for a while after it is
            // stopped, so a user who has just stopped one and is told it is
            // running needs to see that the agent is repeating wslc rather
            // than deciding anything.
            throw new InvalidOperationException(
                $"Compaction blocked: wslc still lists session {session} as running (id {running.Id}, creator pid {running.CreatorPid?.ToString() ?? "unknown"}), and a running session holds its VHDX open. "
                + "If you have just stopped it, wslc can take a moment to let go; try again shortly.");
        }

        var (_, storagePath) = SystemParsing.CompactionTarget(SessionStoreReader.Read(), [], session);
        if (storagePath.Length == 0)
        {
            throw new KeyNotFoundException("No WSLC session VHDX file to compact.");
        }

        var script = CompactVhdxScript.Build(storagePath, session, wslc.Resolve([]).Executable);
        var scriptPath = Path.Combine(Path.GetTempPath(), CompactVhdxScript.FileName);
        // With a BOM, as Set-Content -Encoding UTF8 writes it: Windows PowerShell
        // reads a file without one as the ANSI code page and breaks non-ASCII paths.
        await File.WriteAllTextAsync(scriptPath, script, Utf8WithBom, cancellationToken);

        return new TerminalJob(
            $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            scriptPath,
            storagePath,
            session);
    }
}
