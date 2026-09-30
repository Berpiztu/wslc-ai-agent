namespace WslcAgent.Server.Wslc;

/// <summary>
/// What the commands run inside a scope are called in CLI Activity and in the
/// log, in place of the title their arguments give (<see cref="CliTraceDescription"/>).
/// A file carried into a container runs <c>wslc container cp</c> on a staged
/// copy under a temporary name, and the title read from that was "Copy
/// C:\…\wslc-files-upload-…\wslc-cp-3f9a…": nothing a person recognises. The code carrying the file knows its real name
/// and where it goes, and says so here, once, around everything it runs.
/// <para>
/// Ambient, not a parameter: the commands are run several calls down, through
/// the one runner every part of the agent shares, and the title belongs to the
/// operation, not to any one of them. It flows with the async call that set it
/// and no further.
/// </para>
/// </summary>
public static class CliTitle
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>The title in force here; null outside any scope.</summary>
    public static string? Now => Current.Value;

    /// <summary>Every command run until the scope is disposed carries this title.</summary>
    public static IDisposable Use(string title)
    {
        var before = Current.Value;
        Current.Value = title;
        return new Scope(before);
    }

    /// <summary>
    /// A command's title under a scope's: the copy itself is the operation and
    /// carries it as it is; a step inside the container around it (a link, a
    /// rename, the clean-up) carries it with the program it runs, so the steps
    /// of one upload are told apart.
    /// </summary>
    public static string For(string title, IReadOnlyList<string> args)
    {
        var tokens = args.Where(token => !token.StartsWith('-')).ToList();
        var exec = tokens.FindIndex(token => token == "exec");
        return exec >= 0 && exec + 2 < tokens.Count ? $"{title} · {tokens[exec + 2]}" : title;
    }

    private sealed class Scope(string? before) : IDisposable
    {
        public void Dispose() => Current.Value = before;
    }
}
