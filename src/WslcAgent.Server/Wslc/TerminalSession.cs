using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// A session behind a Windows pseudo console: the shell sees a terminal, so
/// what comes out is what a console window would show, VT sequences and all,
/// and what goes in is keystrokes. This is the one the exec terminal uses on
/// the agent's own platform.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TerminalSession : IWslcSession
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly PseudoConsole _console;
    private readonly Process? _process;

    internal TerminalSession(PseudoConsole console, IReadOnlyList<string> args)
    {
        _console = console;
        Args = args;
        Input = new StreamWriter(console.Input, Utf8) { AutoFlush = true };
        Output = new StreamReader(console.Output, Utf8);
        // Only to wait on and to read the exit code from; the console owns the child.
        _process = Attach(console.ProcessId);
    }

    public IReadOnlyList<string> Args { get; }

    public bool HasTerminal => true;

    public string Backend => "conpty";

    public TextWriter Input { get; }

    public TextReader Output { get; }

    public TextReader? Error => null;

    public bool HasExited => _process?.HasExited ?? _console.HasExited;

    public int ExitCode => _console.ExitCode;

    public void Resize(int columns, int rows) => _console.Resize(columns, rows);

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (_process is not null)
        {
            await _process.WaitForExitAsync(cancellationToken);
            return;
        }

        // Without a handle to wait on, watch the console itself.
        while (!_console.HasExited)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    public void Kill() => _console.Kill();

    public void Dispose()
    {
        _console.Dispose();
        _process?.Dispose();
    }

    private static Process? Attach(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            // Already gone: the console still answers for the exit code.
            return null;
        }
    }
}
