using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Mcp;

/// <summary>
/// Installs the skill the way each client installs skills: by running that
/// client's own command, here or on another machine over SSH. Their installers
/// scan the file, name it and record it as that client expects, and a later
/// <c>skills list</c> or update knows about it — none of which happens when a
/// file is dropped into a folder, which is why nothing here copies one.
/// </summary>
public sealed partial class SkillInstaller
{
    /// <summary>A profile is a command name, so it may be a name and nothing else.</summary>
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex ProfileName { get; }

    /// <summary>An SSH destination, as ssh takes it: user@host, or a host from the config.</summary>
    [GeneratedRegex("^[A-Za-z0-9._@:-]+$")]
    private static partial Regex Destination { get; }

    /// <summary>
    /// An installer that says it failed while exiting 0 — Hermes answers
    /// "Error: Could not find … in any source" and returns success — must not
    /// be reported as done. What it said counts, not only how it ended.
    /// </summary>
    [GeneratedRegex(@"(^|\n)\s*(error|fatal)\b|\berror:|could not\b|couldn't\b|unable to\b|not found\b|failed\b", RegexOptions.IgnoreCase)]
    private static partial Regex Complaint { get; }

    private static bool Complained(string output) => Complaint.IsMatch(output);

    /// <summary>
    /// Runs the install for one client. With no <paramref name="sshDestination"/>
    /// it runs on the agent's own machine, through its shell so a <c>.cmd</c>
    /// shim resolves as it would in a terminal; with one, it runs over ssh with
    /// the keys of the account the agent runs as, and the far side is taken to
    /// be a POSIX shell.
    /// </summary>
    public async Task<SkillInstallResult> InstallAsync(
        string client,
        string skillUrl,
        string profile,
        string sshDestination,
        CancellationToken cancellationToken = default)
    {
        var remote = sshDestination.Trim();
        var windows = remote.Length == 0 && OperatingSystem.IsWindows();
        var command = SkillFile.Commands(skillUrl, windows).FirstOrDefault(entry => entry.Client == client)
            ?? throw new ArgumentException($"No installer is known for '{client}'.", nameof(client));

        var line = command.Command;
        if (client == "Hermes" && profile.Trim() is { Length: > 0 } named)
        {
            // A Hermes profile is its own command, so naming one swaps the first
            // word: `alex skills install …` installs into the profile alex.
            line = ProfileName.IsMatch(named)
                ? named + line["hermes".Length..]
                : throw new ArgumentException($"'{named}' is not a profile name.", nameof(profile));
        }

        if (remote.Length == 0)
        {
            return await RunAsync(client, line, Shell(line), cancellationToken);
        }

        if (!Destination.IsMatch(remote))
        {
            throw new ArgumentException($"'{remote}' is not an SSH destination.", nameof(sshDestination));
        }

        // The command travels as one argument, so the remote shell reads it and
        // no quoting of ours has to survive two shells.
        return await RunAsync(client, $"ssh {remote} {line}",
            new ProcessStartInfo("ssh")
            {
                ArgumentList = { "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=accept-new", remote, line },
            },
            cancellationToken);
    }

    /// <summary>The agent's own shell, so the client's command resolves as it would in a terminal here.</summary>
    private static ProcessStartInfo Shell(string line)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh");
        start.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        start.ArgumentList.Add(line);
        return start;
    }

    /// <summary>
    /// Runs it and brings back what it said: an installer's own words are what
    /// the operator needs, whether it worked or not.
    /// </summary>
    private static async Task<SkillInstallResult> RunAsync(string client, string line, ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, data) => Append(output, data.Data);
        process.ErrorDataReceived += (_, data) => Append(output, data.Data);

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new SkillInstallResult(client, line, -1, $"Could not run it: {exception.Message}", false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        var said = output.ToString().Trim();
        return new SkillInstallResult(client, line, process.ExitCode,
            said.Length > 0 ? said : process.ExitCode == 0 ? "Done." : "It failed and said nothing.",
            process.ExitCode == 0 && !Complained(said));
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is not null)
        {
            output.AppendLine(line);
        }
    }
}
