using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// Scripted <c>wslc</c>: answers each command from a table keyed by its
/// arguments, or throws. Tests never touch the real CLI.
/// </summary>
public sealed class FakeWslcRunner : IWslcRunner
{
    private readonly Dictionary<string, Func<WslcResult>> _answers = new();
    private readonly List<(Func<IReadOnlyList<string>, bool> Matches, Func<IReadOnlyList<string>, WslcResult> Answer)> _matchers = [];

    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// <summary>What each call wrote to the command's standard input, null when nothing.</summary>
    public List<string?> Inputs { get; } = [];

    /// <summary>The session each call asked for: null for the selected one, empty for none.</summary>
    public List<string?> Sessions { get; } = [];

    public FakeWslcRunner Answer(string commandLine, string stdout, int exitCode = 0, string stderr = "")
    {
        _answers[commandLine] = () => new WslcResult(commandLine.Split(' '), exitCode, stdout, stderr, TimeSpan.Zero);
        return this;
    }

    public FakeWslcRunner Fail(string commandLine, string stderr, int exitCode = 1) =>
        Answer(commandLine, "", exitCode, stderr);

    /// <summary>
    /// An answer for a command a test cannot spell out in full: a staged
    /// transfer carries a path with a guid in it, and what is under test is the
    /// upload that made it, not the name of the temporary file.
    /// </summary>
    public FakeWslcRunner AnswerWhen(Func<IReadOnlyList<string>, bool> matches, string stdout, int exitCode = 0, string stderr = "")
    {
        _matchers.Add((matches, args => new WslcResult(args, exitCode, stdout, stderr, TimeSpan.Zero)));
        return this;
    }

    /// <summary>
    /// An answer that also does what the real command would do outside its
    /// output: <c>container cp</c> out of a container leaves the staged file
    /// behind, and a test of a download has nothing to send without it.
    /// </summary>
    public FakeWslcRunner AnswerWhen(Func<IReadOnlyList<string>, bool> matches, Action<IReadOnlyList<string>> effect, string stdout = "")
    {
        _matchers.Add((matches, args =>
        {
            effect(args);
            return new WslcResult(args, 0, stdout, "", TimeSpan.Zero);
        }));
        return this;
    }

    /// <summary>The scripted answer, its output handed over line by line.</summary>
    public async Task<WslcResult> StreamAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(args, cancellationToken: cancellationToken);
        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            onLine(line);
        }

        return result;
    }

    /// <summary>
    /// The same lines, and the exit code instead of an exception: a watch
    /// ends, it does not fail.
    /// <para>
    /// A watch nobody scripted says nothing and waits, which is what the real
    /// one does: <c>wslc events</c> sits there until something happens or the
    /// agent stops. Answering it with "no scripted answer" instead threw out
    /// of the event reader, and a background service that throws takes the
    /// whole test host with it — which is why every endpoint test in the suite
    /// was failing on a disposed <c>TestServer</c>.
    /// </para>
    /// </summary>
    public async Task<int> WatchAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default)
    {
        if (!Scripted(args))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        try
        {
            return (await StreamAsync(args, onLine, cancellationToken)).ExitCode;
        }
        catch (WslcException ex)
        {
            return ex.Result.ExitCode;
        }
    }

    /// <summary>Whether this test said anything about that command at all.</summary>
    private bool Scripted(IReadOnlyList<string> args) =>
        _answers.ContainsKey(string.Join(' ', args)) || _matchers.Any(matcher => matcher.Matches(args));

    public Task<WslcResult> RunAsync(IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken cancellationToken = default, string? standardInput = null, string? session = null)
    {
        Calls.Add(args);
        Inputs.Add(standardInput);
        Sessions.Add(session);
        var key = string.Join(' ', args);
        // A command this test said nothing about is a command this wslc does
        // not have, and it fails the way the real one fails. Throwing something
        // of the test framework's own instead made every fallback in the agent
        // untestable: `wslc info` falling back to `wslc system info` catches a
        // CLI failure, not an InvalidOperationException, so the fallback was
        // never taken and the endpoint answered 409 to a test that had
        // scripted exactly what it meant to.
        var result = _answers.TryGetValue(key, out var answer)
            ? answer()
            : _matchers.FirstOrDefault(matcher => matcher.Matches(args)).Answer?.Invoke(args)
                ?? new WslcResult(args, 127, "", $"unrecognized command \"{key}\" for \"wslc\"", TimeSpan.Zero);
        if (result.ExitCode != 0)
        {
            throw new WslcException($"wslc {key}: {result.Stderr.Trim()}", result);
        }

        return Task.FromResult(result);
    }

    /// <summary>The command line without touching the machine: no executable is resolved.</summary>
    public WslcCommandLine Resolve(IReadOnlyList<string> args) => new("wslc", args);

    /// <summary>No scripted terminal: a session is a real process.</summary>
    public bool SupportsTerminal => false;

    public IWslcSession StartInteractive(IReadOnlyList<string> args, int columns = 120, int rows = 30) =>
        throw new NotSupportedException("The scripted runner does not start interactive sessions.");

    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
