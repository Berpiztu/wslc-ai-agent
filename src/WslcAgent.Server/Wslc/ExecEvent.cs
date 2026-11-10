using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// One exec event of <c>wslc events</c> (wslc 3.0.5 on), read whole, every
/// line of it. Measured on wslc 3.0.5:
/// <code>
/// 2026-11-02T23:03:07.521322451-05:00 container exec_create: /bin/sh -c python -c '…' 8b884498cb80… (ai.berpiztu.wslc.project=ia-apps, …, execID=…, image=…, name=ia-pdf-excel-backend)
/// 2026-11-02T23:03:07.764345048-05:00 container exec_die 8b884498cb80… (ai.berpiztu.wslc.project=ia-apps, …, execID=…, exitCode=0, image=…, name=ia-pdf-excel-backend)
/// </code>
/// The command stands between the action and the container's id, as it was
/// written, line breaks too; <c>exec_die</c> has none. The attributes are in
/// alphabetical order, the container's labels among them.
/// </summary>
/// <param name="Action"><c>exec_create</c>, <c>exec_start</c> or <c>exec_die</c>.</param>
/// <param name="ContainerId">The container's full id.</param>
/// <param name="ExecId">The exec's id: what ties its create to its die.</param>
/// <param name="Command">The command run, on <c>exec_create</c> and <c>exec_start</c>; empty on <c>exec_die</c>.</param>
/// <param name="Name">The container's name; null when the line has none.</param>
/// <param name="ExitCode">The command's exit code, on <c>exec_die</c>.</param>
public sealed partial record ExecEvent(DateTimeOffset Time, string Action, string ContainerId, string ExecId, string Command, string? Name, int? ExitCode)
{
    public const string Create = "exec_create";
    public const string Die = "exec_die";

    /// <summary>The event in <paramref name="text"/>, all its lines joined; null when it is not one.</summary>
    public static ExecEvent? Parse(string text)
    {
        var match = Shape().Match(text);
        if (!match.Success || !DateTimeOffset.TryParse(match.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return null;
        }

        var attributes = $"({match.Groups["attributes"].Value})";
        return WslcEventParsing.AttributeOf(attributes, "execID=") is { } execId
            ? new ExecEvent(time, match.Groups["action"].Value, match.Groups["id"].Value, execId, match.Groups["command"].Value.Trim(),
                WslcEventParsing.AttributeOf(attributes, "name="), WslcEventParsing.ExitCodeOf(attributes))
            : null;
    }

    /// <summary>The command, which may be empty and span lines, is the shortest text before the container's id and its bracket.</summary>
    [GeneratedRegex(@"^(?<time>\S+) container (?<action>exec_create|exec_start|exec_die):?(?: (?<command>.*?))? (?<id>[0-9a-f]{12,64}) \((?<attributes>.*)\)\s*$", RegexOptions.Singleline)]
    private static partial Regex Shape();
}

/// <summary>
/// wslc prints an exec's command as it was written, its line breaks too, so
/// one event can take several lines, and only its last has the attributes:
/// <code>
/// 2026-11-02T23:07:41.411800854-05:00 container exec_create: sh -c true
/// true 331b3c371b72… (execID=65cc463a…, image=…, name=…)
/// </code>
/// Any event that is not an exec ends one, so a line that never came cannot
/// hide what follows it.
/// </summary>
internal sealed class ExecLines
{
    private const string Attribute = "execID=";

    private readonly StringBuilder _text = new();
    private bool _inside;

    /// <summary>
    /// Whether this line, parsed as <paramref name="read"/>, belongs to an exec
    /// event; <paramref name="whole"/> is the event's text once its last line
    /// has come, null before.
    /// </summary>
    public bool Hold(string line, WslcEvent? read, out string? whole)
    {
        whole = null;
        if (read is { IsExec: false } || (read is null && !_inside))
        {
            Reset();
            return false;
        }

        if (read is not null)
        {
            _text.Clear();
        }
        else
        {
            _text.Append('\n');
        }

        _text.Append(line.TrimEnd('\r'));
        _inside = !line.Contains(Attribute, StringComparison.Ordinal);
        if (!_inside)
        {
            whole = _text.ToString();
            _text.Clear();
        }

        return true;
    }

    /// <summary>A new stream: what the last one was printing ended with it.</summary>
    public void Reset()
    {
        _inside = false;
        _text.Clear();
    }
}
