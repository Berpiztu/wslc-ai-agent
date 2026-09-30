using System.Diagnostics;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Updates;

/// <summary>
/// Hands the installer over and steps aside. The agent cannot run its own
/// installer: the installer replaces the agent's files, and they are in use
/// while it runs. So the installer and the logon script are copied out of the
/// install folder, the script schedules a one-off task that runs the update in
/// the user's session (AgentLogonTask.ps1 -ScheduleUpdate), and the agent stops.
/// The task waits for the port to be free, runs the installer quietly with the
/// agent's own address, and the installer's last step starts the new agent —
/// or, when it fails, the task starts the old one again.
/// <para>
/// A task and not a child process: the agent lives inside its logon task, and
/// the installer re-registers that task, which would take a child of the agent
/// down with it halfway through the install.
/// </para>
/// </summary>
public sealed class AgentUpdateLauncher(IOptions<WslcOptions> options, IHostApplicationLifetime lifetime, ILogger<AgentUpdateLauncher> logger)
{
    private const string ScriptName = "AgentLogonTask.ps1";

    /// <summary>Long enough for PowerShell to start and register a task on a busy machine.</summary>
    private static readonly TimeSpan ScheduleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Time for the notice that the agent is installing to reach the clients before it goes.</summary>
    private static readonly TimeSpan Farewell = TimeSpan.FromSeconds(1);

    /// <summary>Where the updater leaves how the install went.</summary>
    public string ResultPath => Path.Combine(options.Value.DataDirectory, "agent-update.result");

    /// <summary>Schedules the update of <paramref name="installer"/> (version <paramref name="version"/>) and stops the agent; throws when the task could not be scheduled, and the agent goes on.</summary>
    public void Launch(string installer, string version)
    {
        var folder = Path.Combine(Path.GetTempPath(), "wslc-ai-agent-update");
        Directory.CreateDirectory(folder);

        // Copies, not the originals: the package folder may be written again
        // while the installer runs, and the install folder is what it replaces.
        var staged = Path.Combine(folder, $"wslc-ai-agent-{version}.msi");
        File.Copy(installer, staged, overwrite: true);
        var script = Path.Combine(folder, ScriptName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, ScriptName), script, overwrite: true);
        File.Delete(ResultPath);

        Schedule(script, folder, ["-ScheduleUpdate", "-Msi", staged, "-Version", version, "-Result", ResultPath]);
        logger.LogWarning(AgentUpdater.LogPrefix + "The agent stops to update itself to {Version}: the task WSLC-AI-Agent-Update installs {Installer} and starts it again", version, staged);
        _ = StopAsync();
    }

    private static void Schedule(string script, string folder, IReadOnlyList<string> arguments)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            WorkingDirectory = folder,
        };
        foreach (var argument in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, .. arguments])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start, so the update could not be scheduled.");
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(ScheduleTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Scheduling the update did not finish in time.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Scheduling the update failed ({process.ExitCode}): {errors.Result.Trim()}");
        }
    }

    private async Task StopAsync()
    {
        await Task.Delay(Farewell);
        lifetime.StopApplication();
    }
}
