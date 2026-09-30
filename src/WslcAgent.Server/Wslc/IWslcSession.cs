namespace WslcAgent.Server.Wslc;

/// <summary>
/// One <c>wslc</c> process running for as long as a user keeps a terminal
/// open, with its streams for the caller to bridge. Two kinds, and the
/// difference is what the shell believes: behind a pseudo console it has a
/// terminal (its own prompt, its echo, job control, <c>ls</c> in columns),
/// behind plain pipes it does not and the client draws what is typed.
/// </summary>
public interface IWslcSession : IDisposable
{
    /// <summary>The effective arguments, session flag included.</summary>
    IReadOnlyList<string> Args { get; }

    /// <summary>True when a real terminal backs this session.</summary>
    bool HasTerminal { get; }

    /// <summary>What the agent tells the client it is running on: <c>conpty</c> or <c>pipe</c>.</summary>
    string Backend { get; }

    TextWriter Input { get; }

    TextReader Output { get; }

    /// <summary>Null with a terminal: a console has one screen, not two streams.</summary>
    TextReader? Error { get; }

    /// <summary>The window changed size; ignored where there is no terminal to tell.</summary>
    void Resize(int columns, int rows);

    bool HasExited { get; }

    /// <summary>Only meaningful once it has exited.</summary>
    int ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>Ends the session's process tree; safe on one that already exited.</summary>
    void Kill();
}
