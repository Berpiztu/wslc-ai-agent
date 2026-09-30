namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>POST /api/v1/containers/{id}/exec</c>: one command to run inside the container.</summary>
/// <param name="Command">Split like a shell (<c>ls -la /app</c>); use <c>sh -c "…"</c> for pipes or redirection.</param>
public sealed record ContainerExecRequest(string Command);

/// <summary>What one <c>wslc exec</c> printed. A non-zero <paramref name="ExitCode"/> is the command's own failure, not the agent's.</summary>
public sealed record ContainerExecResult(string Stdout, string Stderr, int ExitCode);

/// <summary>Answer of <c>POST /api/v1/containers/{id}/open-terminal</c>: the shell that was opened and the window it went to.</summary>
/// <param name="Command">The <c>wslc exec</c> command line the window runs.</param>
/// <param name="Window"><c>Windows Terminal</c> or <c>console</c>.</param>
public sealed record NativeTerminalResult(string Command, string Window);

/// <summary>Body of <c>POST /api/v1/containers/{id}/open-terminal</c>; an empty command opens the default shell.</summary>
public sealed record OpenTerminalRequest(string Command = "");
