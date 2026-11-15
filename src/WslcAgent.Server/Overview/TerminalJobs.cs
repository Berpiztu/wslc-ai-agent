using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>
    /// Whether the job's script is still running: the process whose id it
    /// wrote as it started is alive, is a PowerShell, and was started before
    /// that id was written, so a later process that was given the same id is
    /// not taken for it.
    /// </summary>
    public TerminalJobState State(string job)
    {
        if (!string.Equals(job.Trim(), CompactVhdx, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException($"Unknown terminal job: {job}");
        }

        using var script = Script();
        return new TerminalJobState(script is not null);
    }

    /// <summary>The compaction script's process while it runs; null once it has ended, or when it cannot be told.</summary>
    private static Process? Script()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), CompactVhdxScript.PidFileName);
        Process? process = null;
        try
        {
            if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid))
            {
                return null;
            }

            process = Process.GetProcessById(pid);
            if (!process.HasExited
                && process.ProcessName.Contains("powershell", StringComparison.OrdinalIgnoreCase)
                && process.StartTime <= File.GetLastWriteTime(pidFile))
            {
                return process;
            }
        }
        catch (Exception gone) when (gone is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            // Ended, or not ours to look at: not running, as far as the job goes.
        }

        process?.Dispose();
        return null;
    }

    /// <summary>
    /// Stops the job wherever it is, the safe way for where it is. The work on
    /// the store is done by a script inside the job's temporary session, which
    /// leaves between two steps once a file is made there: a layer is never
    /// left half removed, and nothing is trimmed or compacted after it. With
    /// no such session the job is changing nothing of the store — it is asking
    /// its question, waiting, or relaying Windows' own compaction of the file
    /// — and its script is simply ended; that compaction, once Windows has
    /// begun it, runs elevated and goes on to its end by itself.
    /// </summary>
    public async Task<TerminalJobStop> StopAsync(string job, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(job.Trim(), CompactVhdx, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException($"Unknown terminal job: {job}");
        }

        var active = SystemParsing.ActiveSessions((await wslc.RunAsync(["system", "session", "list"], cancellationToken: cancellationToken)).Stdout);
        if (active.All(session => session.DisplayName != CompactVhdxScript.TrimSession))
        {
            using var script = Script();
            if (script is null)
            {
                return new TerminalJobStop(false, "No compaction is running.");
            }

            try
            {
                script.Kill(entireProcessTree: true);
            }
            catch (Exception failed) when (failed is InvalidOperationException or Win32Exception or AggregateException)
            {
                // Already gone, or a child it may not end (Windows' elevated compaction): the script itself is what is stopped.
            }

            return new TerminalJobStop(true, "Stopped: it was not removing or trimming anything. If Windows had already begun to compact the VHDX file, that goes on to its end by itself.");
        }

        await wslc.RunAsync(
            ["--session", CompactVhdxScript.TrimSession, "system", "session", "run", "touch", StoreCleanupScript.StopFlag],
            cancellationToken: cancellationToken);
        return new TerminalJobStop(true, "Stopping: it ends the step it is on and leaves. Nothing is left half done, and the VHDX is not compacted.");
    }
}
