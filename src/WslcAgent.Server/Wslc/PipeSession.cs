using System.Diagnostics;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// A session over the three standard pipes, for hosts without a pseudo
/// console. The shell has no terminal: it does not echo, its prompt is the
/// bare one and programs that ask print their plain-text form.
/// </summary>
public sealed class PipeSession(Process process, IReadOnlyList<string> args) : IWslcSession
{
    public IReadOnlyList<string> Args { get; } = args;

    public bool HasTerminal => false;

    public string Backend => "pipe";

    public TextWriter Input => process.StandardInput;

    public TextReader Output => process.StandardOutput;

    public TextReader? Error => process.StandardError;

    public bool HasExited => process.HasExited;

    public int ExitCode => process.HasExited ? process.ExitCode : 0;

    public void Resize(int columns, int rows)
    {
        // A pipe has no window size.
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => process.WaitForExitAsync(cancellationToken);

    public void Kill()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // It exited between the check and the kill: nothing left to end.
        }
    }

    public void Dispose()
    {
        Kill();
        process.Dispose();
    }
}
