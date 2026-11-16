using System.Text;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// A shell script of the agent's run inside a session that is up, through
/// <c>wslc system session run sh -c</c>.
/// </summary>
public static class GuestScript
{
    /// <summary>
    /// The one shell line that runs <paramref name="script"/> with
    /// <paramref name="arguments"/>: base64 keeps the script's quotes and new
    /// lines away from the command line, and the shell reads it from its
    /// standard input. The arguments are the agent's own words, never text a
    /// user typed.
    /// </summary>
    public static string Piped(string script, params string[] arguments)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(script.ReplaceLineEndings("\n")));
        var tail = arguments.Length == 0 ? "" : " " + string.Join(' ', arguments);
        return $"echo {encoded} | base64 -d | sh -s --{tail}";
    }
}