using System.Diagnostics;
using System.Text;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// The Terminal page's shell: the agent machine's own command prompt, not
/// <c>wslc exec</c>. <c>%ComSpec%</c>, else
/// <c>%SystemRoot%\System32\cmd.exe</c>, behind a pseudo console where Windows
/// has one and plain pipes otherwise; <c>$SHELL -l</c> away from Windows.
/// </summary>
public static class HostShell
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>What the client is told it is talking to.</summary>
    public const string Label = "local host shell";

    public static IWslcSession Start(int columns, int rows)
    {
        var shell = Command();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return new TerminalSession(PseudoConsole.Start($"\"{shell[0]}\"", columns, rows), shell);
        }

        var startInfo = new ProcessStartInfo(shell[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var arg in shell.Skip(1))
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            process.Dispose();
            throw new IOException($"Could not start {shell[0]}: {ex.Message}");
        }

        return new PipeSession(process, shell);
    }

    /// <summary>The shell's argv: the command interpreter, or the user's login shell off Windows.</summary>
    public static IReadOnlyList<string> Command()
    {
        if (!OperatingSystem.IsWindows())
        {
            var posix = Environment.GetEnvironmentVariable("SHELL");
            return [string.IsNullOrWhiteSpace(posix) ? "/bin/bash" : posix, "-l"];
        }

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (!string.IsNullOrWhiteSpace(comSpec) && File.Exists(comSpec))
        {
            return [comSpec];
        }

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? Environment.GetEnvironmentVariable("WINDIR");
        var cmd = string.IsNullOrEmpty(systemRoot) ? "" : Path.Combine(systemRoot, "System32", "cmd.exe");
        return [File.Exists(cmd) ? cmd : "cmd.exe"];
    }
}
