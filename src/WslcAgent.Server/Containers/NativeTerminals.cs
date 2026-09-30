using System.Diagnostics;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// A real terminal window on the agent's own desktop, Windows Terminal when it
/// is installed and a plain console otherwise: <c>wslc exec -it</c> into a
/// container ("Open in terminal"), or the host's own shell (the Terminal page).
/// It only makes sense for someone sitting at that machine, so the endpoints
/// refuse a remote client and offer the in-app terminal instead
/// (<see cref="ExecTerminals"/>).
/// </summary>
public sealed class NativeTerminals(IWslcRunner wslc, ILogger<NativeTerminals> logger)
{
    private const string HostTitle = "wslc host shell";

    public async Task<NativeTerminalResult> OpenAsync(string container, string? command, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var argv = await ContainerShell.ResolveAsync(wslc, id, command, cancellationToken);
        var line = wslc.Resolve(["exec", "--interactive", "--tty", id, .. argv]);

        var window = Launch($"wslc exec {Short(id)}", line.Executable, line.Args);
        logger.LogInformation("opened a terminal window for {Container}", Short(id));
        return new NativeTerminalResult(line.ToString(), window);
    }

    /// <summary>The host shell in its own window; <paramref name="command"/> replaces the command interpreter when given.</summary>
    public NativeTerminalResult OpenHost(string? command)
    {
        var shell = string.IsNullOrWhiteSpace(command) ? HostShell.Command()[0] : command.Trim();
        var window = Launch(HostTitle, shell, []);
        logger.LogInformation("opened a host terminal window ({Shell})", shell);
        return new NativeTerminalResult(shell, window);
    }

    /// <summary>Starts <paramref name="executable"/> in a new window; which kind of window it went to.</summary>
    private static string Launch(string title, string executable, IReadOnlyList<string> args)
    {
        var terminal = WindowsTerminal();
        var startInfo = new ProcessStartInfo { UseShellExecute = true };
        if (terminal is not null)
        {
            startInfo.FileName = terminal;
            startInfo.ArgumentList.Add("new-tab");
            startInfo.ArgumentList.Add("--title");
            startInfo.ArgumentList.Add(title);
            startInfo.ArgumentList.Add(executable);
        }
        else
        {
            startInfo.FileName = executable;
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not open a terminal window: {ex.Message}");
        }

        return terminal is null ? "console" : "Windows Terminal";
    }

    /// <summary>The Windows Terminal launcher, or null when it is not installed.</summary>
    private static string? WindowsTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var name = "wt.exe";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Short(string container) =>
        container.Length > 12 && container.All(Uri.IsHexDigit) ? container[..12] : container;
}
