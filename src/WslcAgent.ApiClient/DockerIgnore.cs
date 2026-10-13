using System.Text;
using System.Text.RegularExpressions;

namespace WslcAgent.ApiClient;

/// <summary>
/// A folder's <c>.dockerignore</c>: what of a build context is not sent. A
/// pattern a line, from the folder: <c>*</c> for any run of characters of
/// one name, <c>?</c> for one, <c>[abc]</c> for one of those, <c>**</c> for
/// any number of folders; a pattern that names a folder leaves out what is
/// in it; <c>!</c> before one brings back what an earlier one left out; the
/// last pattern that matches decides. The Dockerfile and the
/// <c>.dockerignore</c> at the folder's top are sent whatever it says, as
/// <c>docker build</c> sends them. Read the same way by every client that
/// sends a context, the command line and the Compose window.
/// </summary>
public sealed class DockerIgnore
{
    public const string FileName = ".dockerignore";

    private const string Dockerfile = "Dockerfile";

    private readonly List<(Regex Pattern, bool Keeps, string Written)> _rules = [];

    /// <summary>The file's text read; nothing, or a folder with no such file, leaves nothing out.</summary>
    public static DockerIgnore Parse(string? text)
    {
        var ignore = new DockerIgnore();
        foreach (var written in (text ?? "").Split('\n'))
        {
            var line = written.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var keeps = line.StartsWith('!');
            var pattern = (keeps ? line[1..].Trim() : line).Replace('\\', '/').Trim('/');
            while (pattern.StartsWith("./", StringComparison.Ordinal))
            {
                pattern = pattern[2..];
            }

            if (pattern.Length > 0)
            {
                ignore._rules.Add((Compiled(pattern), keeps, pattern));
            }
        }

        return ignore;
    }

    /// <summary>The entry, by its path from the folder with forward slashes, is left out of what is sent.</summary>
    public bool Leaves(string name)
    {
        if (name is Dockerfile or FileName)
        {
            return false;
        }

        var left = false;
        foreach (var (pattern, keeps, _) in _rules)
        {
            if (pattern.IsMatch(name))
            {
                left = !keeps;
            }
        }

        return left;
    }

    /// <summary>
    /// A folder is gone into when its files are looked for: one that is not
    /// left out, or one that is and holds something an exception brings
    /// back, which is an exception written as a path inside it
    /// (<c>!logs/.gitkeep</c> for <c>logs</c>). As <c>docker build</c> walks
    /// a context: a folder left out whole is never read, so a virtual
    /// environment of forty thousand files costs nothing, and a link inside
    /// it that leads nowhere stops nothing. An exception written with no
    /// folder (<c>!README.md</c>) is about the top of the context, and
    /// opens none.
    /// </summary>
    public bool Enters(string folder) =>
        !Leaves(folder) || _rules.Any(rule => rule.Keeps && (rule.Written + "/").StartsWith(folder + "/", StringComparison.Ordinal));

    /// <summary>A pattern as the expression that matches the entry it names and everything under it.</summary>
    private static Regex Compiled(string pattern)
    {
        var expression = new StringBuilder("^");
        for (var at = 0; at < pattern.Length; at++)
        {
            if (pattern[at] == '*' && at + 1 < pattern.Length && pattern[at + 1] == '*')
            {
                // Any number of folders: with the slash after it, none at all too.
                var folders = at + 2 < pattern.Length && pattern[at + 2] == '/';
                expression.Append(folders ? "(?:.*/)?" : ".*");
                at += folders ? 2 : 1;
            }
            else if (Class(pattern, at) is { } named)
            {
                expression.Append(named.Expression);
                at = named.End;
            }
            else
            {
                expression.Append(pattern[at] switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    var other => Regex.Escape(other.ToString()),
                });
            }
        }

        return new Regex(expression.Append("(?:/.*)?$").ToString(), RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// The class of characters a pattern opens at <paramref name="at"/>
    /// (<c>*.py[cod]</c>: one of those; with <c>!</c> or <c>^</c> first,
    /// one of none of them), as an expression, and where it closes. Null
    /// for a bracket that opens none (it is not closed, or holds nothing):
    /// it is then the character it is.
    /// </summary>
    private static (string Expression, int End)? Class(string pattern, int at)
    {
        if (pattern[at] != '[' || at + 2 >= pattern.Length)
        {
            return null;
        }

        var close = pattern.IndexOf(']', at + 2);
        if (close < 0)
        {
            return null;
        }

        var inside = pattern[(at + 1)..close];
        var none = inside[0] is '!' or '^';
        var named = none ? inside[1..] : inside;
        return named.Length == 0 ? null : ($"[{(none ? "^" : "")}{named.Replace("\\", "\\\\")}]", close);
    }
}
