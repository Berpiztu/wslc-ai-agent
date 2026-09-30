namespace WslcAgent.Server.Updates;

/// <summary>
/// How the last update ended, as the updater leaves it in the agent's data
/// folder once the installer is done: the agent that stopped to be replaced is
/// not there to hear it, so the one that starts next reads it. One line,
/// <c>ok|failed</c>, the installer's exit code, the version it installed and
/// its log, separated by <c>|</c> (AgentLogonTask.ps1 -Update writes it).
/// </summary>
/// <param name="Ok">The installer finished (0, or 3010: done, a restart pending).</param>
/// <param name="ExitCode">msiexec's own code.</param>
/// <param name="Version">The version the installer carried.</param>
/// <param name="Log">msiexec's verbose log, which says why a failure failed.</param>
public sealed record AgentUpdateResult(bool Ok, int ExitCode, string Version, string Log)
{
    /// <summary>What Settings shows and the log says.</summary>
    public string Text => Ok
        ? $"Updated to {Version}."
        : $"The update to {Version} failed: the installer ended with {ExitCode}, and the agent went on as it was. Its log: {Log}";

    /// <summary>The line in the file, or null when there is none or it cannot be read.</summary>
    public static AgentUpdateResult? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var parts = File.ReadAllText(path).Trim().Split('|');
            return parts.Length == 4 && int.TryParse(parts[1], out var exitCode)
                ? new AgentUpdateResult(parts[0] == "ok", exitCode, parts[2], parts[3])
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
