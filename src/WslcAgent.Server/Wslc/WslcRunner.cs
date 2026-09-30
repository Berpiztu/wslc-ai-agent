using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Wslc;

/// <summary>Runs one <c>wslc</c> command and returns what it printed.</summary>
public interface IWslcRunner
{
    /// <summary>
    /// Run <c>wslc</c> with <paramref name="args"/>. Throws <see cref="WslcException"/>
    /// on a non-zero exit code, <see cref="WslcNotFoundException"/> when the CLI is
    /// not installed, and <see cref="TimeoutException"/> when it exceeds the timeout.
    /// <paramref name="standardInput"/> is written to the command and closed: how a
    /// secret reaches <c>wslc</c> without appearing in its arguments.
    /// </summary>
    /// <param name="session">
    /// Which session the command targets: null for the selected one (what nearly
    /// every caller wants), a name to override it, and an empty string for none —
    /// the session verbs themselves, which must not be scoped to one.
    /// </param>
    Task<WslcResult> RunAsync(IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken cancellationToken = default, string? standardInput = null, string? session = null);

    /// <summary>
    /// The same run with no time limit, handing every line of output (standard output
    /// and error alike) to <paramref name="onLine"/> as it arrives: a build whose
    /// progress the user follows. Cancel through <paramref name="cancellationToken"/>.
    /// </summary>
    Task<WslcResult> StreamAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// A command the agent keeps open for as long as it lasts — the event
    /// stream — handing every line to <paramref name="onLine"/> and keeping
    /// none of them. It answers with the exit code instead of throwing, because
    /// ending is what these do: <c>wslc events</c> is aborted with the session
    /// it belongs to, and the caller decides whether to open another.
    /// </summary>
    Task<int> WatchAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// The command line a run would execute: the resolved executable and the
    /// effective arguments, session flag included. For callers that hand the
    /// command to something else (the native terminal window).
    /// </summary>
    WslcCommandLine Resolve(IReadOnlyList<string> args);

    /// <summary>True where the platform can give a session a real terminal (Windows 10 1809 and later).</summary>
    bool SupportsTerminal { get; }

    /// <summary>
    /// Starts <c>wslc</c> and leaves it running for the caller to bridge (the
    /// exec terminal): behind a pseudo console of <paramref name="columns"/> ×
    /// <paramref name="rows"/> where the platform has one, behind plain pipes
    /// otherwise. The caller owns the session and disposes it.
    /// </summary>
    IWslcSession StartInteractive(IReadOnlyList<string> args, int columns = 120, int rows = 30);
}

/// <summary>What would be executed for one command: the <c>wslc</c> binary and its effective arguments.</summary>
public sealed record WslcCommandLine(string Executable, IReadOnlyList<string> Args)
{
    /// <summary>The same command as one line, for a window title or a log.</summary>
    public override string ToString() => "wslc " + string.Join(' ', Args);
}

/// <summary>What a finished <c>wslc</c> command produced.</summary>
public sealed record WslcResult(IReadOnlyList<string> Args, int ExitCode, string Stdout, string Stderr, TimeSpan Duration)
{
    public string CommandLine => "wslc " + string.Join(' ', Args);

    /// <summary>What it printed, for a service that hands it back to its caller.</summary>
    public WslcAgent.Mcp.CommandOutput Output => new(Stdout, Stderr);
}

/// <summary><c>wslc</c> ran and failed. <see cref="Message"/> is what the user should read.</summary>
public sealed class WslcException(string message, WslcResult result) : Exception(message)
{
    private static readonly IReadOnlyDictionary<string, string> NoFields = new Dictionary<string, string>();

    public WslcResult Result { get; } = result;

    /// <summary>The launch form's fields the failure is about, each with the CLI's reason (<see cref="Containers.LaunchErrors"/>); empty when it is about none in particular.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = NoFields;

    /// <summary>The CLI rejected the verb itself: an older <c>wslc</c> without that command.</summary>
    public bool IsUnrecognizedCommand =>
        (Result.Stderr.Length > 0 ? Result.Stderr : Message).Contains("unrecognized command", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The <c>wslc</c> CLI is not on this machine (or not where configured).</summary>
public sealed class WslcNotFoundException(string message) : Exception(message);

/// <summary>
/// Executes the local <c>wslc.exe</c>. <c>--session</c> is
/// prepended for the selected session except on commands that must see every
/// session; stderr is the error message the user gets; every run goes through
/// <see cref="ICliActivity"/>, which lists it while it runs and writes it to the
/// agent's log when it ends. Nothing runs that would open a session the user
/// stopped (<see cref="StoppedSessions"/>).
/// </summary>
public sealed class WslcRunner(IOptionsMonitor<WslcOptions> options, ISelectedSession session, StoppedSessions stopped, ICliActivity activity, ILogger<WslcRunner> logger) : IWslcRunner
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public Task<WslcResult> RunAsync(IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken cancellationToken = default, string? standardInput = null, string? session = null) =>
        ExecuteAsync(args, timeout, standardInput, onLine: null, cancellationToken, session);

    public Task<WslcResult> StreamAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default) =>
        ExecuteAsync(args, Timeout.InfiniteTimeSpan, standardInput: null, onLine, cancellationToken);

    public async Task<int> WatchAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default)
    {
        try
        {
            return (await ExecuteAsync(args, Timeout.InfiniteTimeSpan, standardInput: null, onLine, cancellationToken, collect: false)).ExitCode;
        }
        catch (WslcException ex)
        {
            // Ending is what a watch does: the event stream is aborted with the
            // session it belongs to. The caller decides whether to open another.
            return ex.Result.ExitCode;
        }
    }

    private async Task<WslcResult> ExecuteAsync(IReadOnlyList<string> args, TimeSpan? timeout, string? standardInput, Action<string>? onLine, CancellationToken cancellationToken, string? target = null, bool collect = true)
    {
        var current = options.CurrentValue;
        var executable = ResolveExecutable(current.ExecutablePath);
        var effectiveArgs = ApplySession(args, target ?? session.Name);
        EnsureNotStopped(args, effectiveArgs, target ?? session.Name);
        var limit = timeout ?? TimeSpan.FromSeconds(Math.Max(1, current.DefaultTimeoutSeconds));

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        if (standardInput is not null)
        {
            startInfo.StandardInputEncoding = Utf8;
        }

        foreach (var arg in effectiveArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Entered as running before it starts, and completed on every way out —
        // could not start, timed out, cancelled, or exited — so no command is
        // left showing as running after it is over. Completing it is what
        // writes the command to the agent's log, output and all.
        var trace = activity.Start(effectiveArgs);
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            activity.Finish(trace, stopwatch.Elapsed, null, "error", "", ex.Message);
            throw new WslcNotFoundException($"Could not start {executable}: {ex.Message}");
        }

        var stdoutTask = ReadAsync(process.StandardOutput, onLine, collect, cancellationToken);
        var stderrTask = ReadAsync(process.StandardError, onLine, collect, cancellationToken);
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(limit);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            stopwatch.Stop();
            activity.Finish(trace, stopwatch.Elapsed, null, "timeout", "", $"Timed out after {limit.TotalSeconds:0}s");
            throw new TimeoutException($"wslc {string.Join(' ', effectiveArgs)} timed out after {limit.TotalSeconds:0}s");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            stopwatch.Stop();
            activity.Finish(trace, stopwatch.Elapsed, null, "cancelled", "", "");
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();

        var result = new WslcResult(effectiveArgs, process.ExitCode, stdout, stderr, stopwatch.Elapsed);
        activity.Finish(trace, stopwatch.Elapsed, process.ExitCode, Outcome(args, process.ExitCode, stdout, stderr), stdout, stderr);

        if (process.ExitCode != 0)
        {
            var message = FirstMeaningfulLine(stderr) ?? FirstMeaningfulLine(stdout) ?? $"exit code {process.ExitCode}";
            // The verb, not the whole line. A run carries every --env the image
            // has, a kilobyte of flags, and this message ends up in a toast: it
            // covered half the screen and buried the one thing worth reading.
            // The line itself is in the log, with its output, as always.
            throw new WslcException($"wslc {string.Join(' ', args.Take(2))}: {message}", result);
        }

        return result;
    }

    /// <summary>
    /// How a command that ran to its end is recorded. A container that is not
    /// there is not an error: removing a helper
    /// that may be left over, or asking after one already gone, answers so on
    /// purpose, and the Error filter of the Logs page is for what went wrong.
    /// Older wslc names the code; 2.9.11 says "Object not found" and no more.
    /// Nor is a <c>test</c> run in a container that answers no (exit code 1
    /// and nothing said): an upload asks
    /// whether its file is already there, and a new file is the usual answer,
    /// which drew a failure in the log for every file sent.
    /// </summary>
    private static string Outcome(IReadOnlyList<string> args, int exitCode, string stdout, string stderr) =>
        exitCode == 0 ? "success"
        : $"{stderr} {stdout}" is var output && (output.Contains("CONTAINER_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Object not found", StringComparison.OrdinalIgnoreCase)) ? "not_found"
        : exitCode == 1 && string.IsNullOrWhiteSpace(output) && args is ["exec", _, "test", ..] ? "not_found"
        : "error";

    /// <summary>
    /// All of a stream at once, or line by line to <paramref name="onLine"/>
    /// while it is kept whole too — unless <paramref name="collect"/> says not
    /// to keep it. A watch that runs for days would otherwise hold every line
    /// it ever read and write the lot to the log when it ends.
    /// </summary>
    private static async Task<string> ReadAsync(StreamReader reader, Action<string>? onLine, bool collect, CancellationToken cancellationToken)
    {
        if (onLine is null)
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }

        var text = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (collect)
            {
                text.Append(line).Append('\n');
            }

            onLine(line);
        }

        return text.ToString();
    }

    public WslcCommandLine Resolve(IReadOnlyList<string> args) =>
        new(ResolveExecutable(options.CurrentValue.ExecutablePath), ApplySession(args, session.Name));

    /// <summary>
    /// Pseudo consoles arrived in Windows 10 1809 (build 17763). Marked as the
    /// Windows check it is, so the analyser sees the pseudo console behind it
    /// is only reached on Windows (CA1416).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows")]
    public bool SupportsTerminal => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    public IWslcSession StartInteractive(IReadOnlyList<string> args, int columns = 120, int rows = 30)
    {
        var command = Resolve(args);
        EnsureNotStopped(args, command.Args, session.Name);
        if (SupportsTerminal)
        {
            logger.LogInformation("wslc {Args} (terminal)", string.Join(' ', command.Args));
            return new TerminalSession(PseudoConsole.Start(CommandLine(command), columns, rows), command.Args);
        }

        var startInfo = new ProcessStartInfo(command.Executable)
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
        foreach (var arg in command.Args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        logger.LogInformation("wslc {Args} (interactive)", string.Join(' ', command.Args));
        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            process.Dispose();
            throw new WslcNotFoundException($"Could not start {command.Executable}: {ex.Message}");
        }

        // The session, not the start, is what CLI Activity records: the caller
        // knows when it ended and with which code (ExecTerminals).
        return new PipeSession(process, command.Args);
    }

    /// <summary>
    /// Refuses, without running it, a command that would open a session the
    /// user stopped. It fails the way a failed command does, so every screen
    /// and tool that already says why a command failed says this too. Not
    /// traced: screens poll every few seconds, and a refusal per poll would
    /// bury the log; the hold is logged once, when it is taken.
    /// </summary>
    private void EnsureNotStopped(IReadOnlyList<string> args, IReadOnlyList<string> effectiveArgs, string? target)
    {
        // A command that names no session opens the CLI's own store.
        var name = target?.Trim() is { Length: > 0 } named ? named : SessionStores.Default;
        if (stopped.Refusal(args, name) is { } refusal)
        {
            throw new WslcException($"wslc {string.Join(' ', args.Take(2))}: {refusal}", new WslcResult(effectiveArgs, -1, "", refusal, TimeSpan.Zero));
        }
    }

    /// <summary>
    /// The command as one string, which is what <c>CreateProcess</c> takes.
    /// Windows splits it again in the child, so an argument with spaces or
    /// quotes is quoted the way the C runtime parses it back.
    /// </summary>
    private static string CommandLine(WslcCommandLine command) =>
        string.Join(' ', new[] { command.Executable }.Concat(command.Args).Select(Quote));

    private static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => c is ' ' or '\t' or '"'))
        {
            return argument;
        }

        var quoted = new StringBuilder("\"");
        for (var i = 0; i < argument.Length; i++)
        {
            var slashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                slashes++;
                i++;
            }

            if (i == argument.Length)
            {
                quoted.Append('\\', slashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                quoted.Append('\\', (slashes * 2) + 1);
            }
            else
            {
                quoted.Append('\\', slashes);
            }

            quoted.Append(argument[i]);
        }

        return quoted.Append('"').ToString();
    }

    /// <summary>
    /// Prepend <c>--session NAME</c> unless the command must not be scoped, or
    /// the session is the CLI's own store for this user: that one is named by
    /// naming none. It is not a shorthand — a command with no <c>--session</c>
    /// opens that store when it is closed, which is how a session that was
    /// terminated comes back, and nothing else does it.
    /// </summary>
    internal static IReadOnlyList<string> ApplySession(IReadOnlyList<string> args, string? session)
    {
        var name = session?.Trim();
        if (string.IsNullOrEmpty(name) || SessionStores.IsDefault(name) || !CommandAllowsSession(args))
        {
            return args;
        }

        return ["--session", name, .. args];
    }

    private static bool CommandAllowsSession(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Contains("--session"))
        {
            return false;
        }

        // Two of the session verbs must not be scoped: list sees every session,
        // and enter carries its own target. Terminate does take the flag — "if
        // no session is specified, the default session will be terminated",
        // which is not what stopping the session in the panel means.
        return !(args.Count >= 3 && args[0] == "system" && args[1] == "session" && args[2] is "list" or "enter");
    }

    private static string ResolveExecutable(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
            return File.Exists(expanded)
                ? expanded
                : throw new WslcNotFoundException($"wslc executable not found at the configured path {expanded}");
        }

        var name = OperatingSystem.IsWindows() ? "wslc.exe" : "wslc";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new WslcNotFoundException("wslc is not on the PATH. Install WSLC or set Wslc:ExecutablePath.");
    }

    private static void TryKill(Process process)
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
            // Best effort: the process may have exited between the check and the kill.
        }
    }

    private static string? FirstMeaningfulLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
}
