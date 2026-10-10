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
/// <param name="Name">The object's name (its <c>name</c> attribute), for the Recent events list; null when the line has none.</param>
public sealed record WslcEvent(DateTimeOffset Time, string Type, string Action, string Id, int? ExitCode = null, string? Name = null)
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
/// data comes from the list, as it always has. Two exceptions: the exit code
/// of a container that ended, which no list keeps and a notification that it
/// stopped on its own has to say, and the object's name, which the Recent
/// events list shows instead of an id. Up to wslc 3.0.1 the exit code opened
/// the bracket; from 3.0.2 the attributes come in alphabetical order, so each
/// is looked for as an attribute of its own, opening the bracket or after a
/// comma.
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
            return new WslcEvent(time, fields[1], fields[2], fields[3], ExitCodeOf(attributes), AttributeOf(attributes, "name="));
        }

        return fields.Length < 5
            ? null
            : new WslcEvent(time, fields[1], $"{fields[2]} {fields[3]}", fields[4], Name: AttributeOf(fields.Length > 5 ? fields[5] : "", "name="));
    }

    /// <summary>The number of the <c>exitCode</c> attribute; null when there is none.</summary>
    private static int? ExitCodeOf(string attributes) =>
        int.TryParse(AttributeOf(attributes, "exitCode="), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : null;

    /// <summary>
    /// The value of one attribute (<paramref name="key"/> with its <c>=</c>):
    /// opening the bracket or after a comma, up to the next comma or the closing
    /// bracket; null when there is none.
    /// </summary>
    private static string? AttributeOf(string attributes, string key)
    {
        var at = attributes.StartsWith("(" + key, StringComparison.Ordinal)
            ? 1
            : attributes.IndexOf(", " + key, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var value = attributes.AsSpan(attributes.IndexOf(key, at, StringComparison.Ordinal) + key.Length);
        var end = value.IndexOfAny(',', ')');
        var text = (end < 0 ? value : value[..end]).Trim().ToString();
        return text.Length > 0 ? text : null;
    }
}
