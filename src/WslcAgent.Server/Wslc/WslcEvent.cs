using System.Globalization;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// One line of <c>wslc events</c>, kept down to what it is for: something of
/// this kind changed, so whoever lists that kind has to read it again.
/// </summary>
/// <param name="Time">When the CLI says it happened.</param>
/// <param name="Type">The object's kind as the CLI names it: <c>container</c>, <c>network</c>, <c>image</c>, <c>volume</c>.</param>
/// <param name="Action">What happened to it: <c>start</c>, <c>kill</c>, <c>die</c>, <c>stop</c>, <c>health_status: unhealthy</c>, <c>connect</c>, <c>disconnect</c>, …</param>
/// <param name="Id">The object the action is about, by its full id.</param>
/// <param name="ExitCode">A container's exit code, on its <c>die</c> (wslc 3.0.2 on) or its <c>stop</c> (before); null on anything else.</param>
public sealed record WslcEvent(DateTimeOffset Time, string Type, string Action, string Id, int? ExitCode = null)
{
    /// <summary>What the agent tells its clients: kind and action, without the id's noise.</summary>
    public override string ToString() => $"{Type} {Action}";
}

/// <summary>
/// Reads the lines <c>wslc events</c> prints. Their shape, measured on wslc
/// 2.9.13 and read in wslc 3.0.2's source:
/// <code>
/// 2026-09-22T19:33:11.000000000-05:00 container stop 8449a1cce27f… (exitCode=137, image=alpine:latest, name=jade_wasatch)
/// 2026-10-07T09:12:40.123456789Z container die 8449a1cce27f… (execDuration=12, exitCode=1, image=alpine:latest, name=jade_wasatch)
/// 2026-10-07T09:13:02.000000000Z container health_status: unhealthy 8449a1cce27f… (image=alpine:latest, name=jade_wasatch)
/// </code>
/// The fields before the bracket are all that is read, the action being two
/// words when the first ends in a colon (a health status). What follows is
/// the object's attributes, and an image label's own value carries commas and
/// brackets of its own — <c>description=User-friendly AI Interface (Supports
/// Ollama, OpenAI API, ...)</c> — so there is no honest way to split them, and
/// no need: an event is the notice that a list changed, never the data. The
/// data comes from the list, as it always has. One exception: the exit code
/// of a container that ended, which no list keeps and a notification that it
/// stopped on its own has to say. Up to wslc 3.0.1 it opened the bracket; from
/// 3.0.2 the attributes come in alphabetical order, so it is looked for as an
/// attribute of its own, opening the bracket or after a comma.
/// </summary>
public static class WslcEventParsing
{
    public static WslcEvent? Parse(string line)
    {
        var text = line.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var fields = text.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4 || !DateTimeOffset.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            // The CLI's own errors arrive on the same stream; they are not events.
            return null;
        }

        if (!fields[2].EndsWith(':'))
        {
            var attributes = fields.Length > 4 ? string.Join(' ', fields[4..]) : "";
            return new WslcEvent(time, fields[1], fields[2], fields[3], ExitCodeOf(attributes));
        }

        return fields.Length < 5 ? null : new WslcEvent(time, fields[1], $"{fields[2]} {fields[3]}", fields[4]);
    }

    private const string ExitCode = "exitCode=";

    /// <summary>The number of the <c>exitCode</c> attribute; null when there is none.</summary>
    private static int? ExitCodeOf(string attributes)
    {
        var at = attributes.StartsWith("(" + ExitCode, StringComparison.Ordinal)
            ? 1
            : attributes.IndexOf(", " + ExitCode, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var digits = attributes.AsSpan(attributes.IndexOf(ExitCode, at, StringComparison.Ordinal) + ExitCode.Length);
        var end = digits.IndexOfAny(',', ')');
        return int.TryParse(end < 0 ? digits : digits[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : null;
    }
}
