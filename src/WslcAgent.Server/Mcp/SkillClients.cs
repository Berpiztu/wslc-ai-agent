using System.Diagnostics;
using System.Text;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Mcp;

/// <summary>
/// Which AI clients a machine has, and which profiles they hold — this machine,
/// or another one over SSH. The same client lives in both places (Hermes runs
/// here and on the VPS), so the page asks the machine that will run the install
/// rather than assuming the agent's own is the only one.
/// </summary>
public sealed class SkillClients
{
    /// <summary>The command each client answers to, and where it keeps profiles.</summary>
    private static readonly (string Client, string Command, string Root)[] Known =
    [
        ("Hermes", "hermes", ".hermes"),
        ("OpenClaw", "openclaw", ".openclaw"),
        ("Claude Code", "claude", ".claude"),
    ];

    /// <summary>
    /// The machines this one already knows how to reach, read from the SSH
    /// config of the account the agent runs as. An alias is the whole
    /// destination — ssh takes the user, the port and any jump from that same
    /// file — so the operator picks a name instead of typing a connection.
    /// </summary>
    public IReadOnlyList<string> Hosts()
    {
        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
        try
        {
            if (!File.Exists(config))
            {
                return [];
            }

            return
            [
                .. File.ReadLines(config)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("Host ", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(line => line[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    // A pattern is not somewhere to connect: it is a rule for the ones that are.
                    .Where(host => !host.Contains('*') && !host.Contains('?'))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>What this machine has: the command on PATH, or the client's folder.</summary>
    public IReadOnlyList<SkillClient> Local()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            .. Known.Select(known => new SkillClient(
                known.Client,
                OnPath(known.Command) || Directory.Exists(Path.Combine(home, known.Root)),
                Profiles(Path.Combine(home, known.Root, "profiles")))),
        ];
    }

    /// <summary>
    /// What the far end of an SSH hop has, asked in one round trip: which
    /// commands answer, and what the profiles folder holds. A machine that
    /// refuses the connection answers nothing rather than a guess.
    /// </summary>
    public async Task<IReadOnlyList<SkillClient>> RemoteAsync(string destination, CancellationToken cancellationToken = default)
    {
        var probe = string.Join("; ", Known.Select(known =>
            $"command -v {known.Command} >/dev/null 2>&1 && echo cli:{known.Client}"))
            + "; for p in ~/.hermes/profiles/*/; do [ -d \"$p\" ] && echo \"profile:Hermes:$(basename \"$p\")\"; done";

        var said = await SshAsync(destination, probe, cancellationToken);
        var found = said.Where(line => line.StartsWith("cli:", StringComparison.Ordinal))
            .Select(line => line[4..].Trim()).ToHashSet();
        var profiles = said.Where(line => line.StartsWith("profile:", StringComparison.Ordinal))
            .Select(line => line[8..].Split(':', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], parts => parts[1].Trim())
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)[.. group.Order()]);

        return
        [
            .. Known.Select(known => new SkillClient(
                known.Client,
                found.Contains(known.Client),
                profiles.TryGetValue(known.Client, out var theirs) ? theirs : [])),
        ];
    }

    private static IReadOnlyList<string> Profiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? [.. Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>().Order()] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>A command answers on this machine when it is on PATH, shim or all.</summary>
    private static bool OnPath(string command)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];

        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => extensions.Any(extension => SafeExists(Path.Combine(folder.Trim(), command + extension))));
    }

    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The probe travels as one argument, so the far shell reads it whole.</summary>
    private static async Task<IReadOnlyList<string>> SshAsync(string destination, string probe, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("ssh")
        {
            ArgumentList = { "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=accept-new", "-o", "ConnectTimeout=15", destination, probe },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = startInfo };
        var lines = new List<string>();
        process.OutputDataReceived += (_, data) => Keep(lines, data.Data);

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return [];
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return lines;
    }

    private static void Keep(List<string> lines, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            lines.Add(line.Trim());
        }
    }
}
