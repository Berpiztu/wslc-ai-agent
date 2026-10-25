using System.Text.RegularExpressions;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// What the agent writes about a <c>wslc</c> command, in its log, in an error
/// and in its activity, with the values of environment variables and build
/// arguments hidden: <c>KEY=***</c>. A container's variables carry its keys
/// and passwords, and a command that failed or ran too long was writing them
/// whole into the log and into the error the user was shown. The names stay,
/// so what was asked can still be read; the command <c>wslc</c> runs is never
/// touched, only what is said about it.
/// </summary>
public static partial class ShownValues
{
    /// <summary>The flags whose next argument is <c>KEY=VALUE</c>.</summary>
    private static readonly string[] ValueFlags = ["--env", "-e", "--build-arg"];

    private const string Hidden = "***";

    /// <summary>The arguments as they may be shown: each value after <c>--env</c>, <c>-e</c> or <c>--build-arg</c>, joined or not (<c>--env=KEY=VALUE</c>), hidden.</summary>
    public static IReadOnlyList<string> Args(IReadOnlyList<string> args)
    {
        var shown = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (i > 0 && ValueFlags.Contains(args[i - 1]))
            {
                shown.Add(Pair(arg));
            }
            else if (ValueFlags.FirstOrDefault(flag => arg.StartsWith(flag + "=", StringComparison.Ordinal)) is { } joined)
            {
                shown.Add($"{joined}={Pair(arg[(joined.Length + 1)..])}");
            }
            else
            {
                shown.Add(arg);
            }
        }

        return shown;
    }

    /// <summary>The arguments as one line, as <see cref="Args"/> shows them.</summary>
    public static string Line(IReadOnlyList<string> args) => string.Join(' ', Args(args));

    /// <summary>
    /// What a command printed, with the values of every <c>"Env"</c> list of its
    /// JSON hidden (<c>container inspect</c>, <c>image inspect</c>): each
    /// <c>"KEY=VALUE"</c> in it becomes <c>"KEY=***"</c>.
    /// </summary>
    public static string Output(string text) =>
        string.IsNullOrEmpty(text) || !text.Contains("\"Env\"", StringComparison.Ordinal)
            ? text
            : EnvList().Replace(text, list => EnvItem().Replace(list.Value, item => $"\"{item.Groups["key"].Value}={Hidden}\""));

    /// <summary><c>KEY=VALUE</c> as <c>KEY=***</c>; a bare <c>KEY</c> (taken from the host's environment) carries no value and is left.</summary>
    private static string Pair(string pair) =>
        pair.IndexOf('=') is var at and > 0 ? $"{pair[..at]}={Hidden}" : pair;

    /// <summary>An <c>"Env"</c> array of quoted strings, compact or indented.</summary>
    [GeneratedRegex("""
        "Env"\s*:\s*\[\s*(?:"(?:[^"\\]|\\.)*"\s*,?\s*)*\]
        """)]
    private static partial Regex EnvList();

    /// <summary>One <c>"KEY=VALUE"</c> item of such an array.</summary>
    [GeneratedRegex("""
        "(?<key>[^"=\\]+)=(?:[^"\\]|\\.)*"
        """)]
    private static partial Regex EnvItem();
}
