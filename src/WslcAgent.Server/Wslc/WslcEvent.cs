using System.Globalization;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// One line of <c>wslc events</c>, kept down to what it is for: something of
/// this kind changed, so whoever lists that kind has to read it again.
/// </summary>
/// <param name="Time">When the CLI says it happened.</param>
/// <param name="Type">The object's kind as the CLI names it: <c>container</c>, <c>network</c>, <c>image</c>, <c>volume</c>.</param>
/// <param name="Action">What happened to it: <c>start</c>, <c>kill</c>, <c>stop</c>, <c>connect</c>, <c>disconnect</c>, …</param>
/// <param name="Id">The object the action is about, by its full id.</param>
/// <param name="ExitCode">A container's exit code, on its <c>stop</c>; null on anything else.</param>
public sealed record WslcEvent(DateTimeOffset Time, string Type, string Action, string Id, int? ExitCode = null)
{
    /// <summary>What the agent tells its clients: kind and action, without the id's noise.</summary>
    public override string ToString() => $"{Type} {Action}";
}

/// <summary>
/// Reads the lines <c>wslc events</c> prints. Its shape, measured on wslc
/// 2.9.13:
/// <code>
/// 2026-09-22T19:33:11.000000000-05:00 container stop 8449a1cce27f… (exitCode=137, image=alpine:latest, name=jade_wasatch)
/// </code>
/// The four fields before the bracket are all that is read. What follows is
/// the object's attributes, and an image label's own value carries commas and
/// brackets of its own — <c>description=User-friendly AI Interface (Supports
/// Ollama, OpenAI API, ...)</c> — so there is no honest way to split them, and
/// no need: an event is the notice that a list changed, never the data. The
/// data comes from the list, as it always has. One exception, read where it
/// stands and nowhere else: a <c>stop</c>'s exit code, which opens the bracket,
/// before any label — the one thing a notification that a container stopped
/// on its own has to say, and no list keeps it.
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

        var fields = text.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4 || !DateTimeOffset.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            // The CLI's own errors arrive on the same stream; they are not events.
            return null;
        }

        return new WslcEvent(time, fields[1], fields[2], fields[3], fields.Length > 4 ? ExitCodeOf(fields[4]) : null);
    }

    private const string ExitCodeOpening = "(exitCode=";

    /// <summary>The number after <c>(exitCode=</c> when the bracket opens with it; null otherwise.</summary>
    private static int? ExitCodeOf(string attributes)
    {
        if (!attributes.StartsWith(ExitCodeOpening, StringComparison.Ordinal))
        {
            return null;
        }

        var digits = attributes.AsSpan(ExitCodeOpening.Length);
        var end = digits.IndexOfAny(',', ')');
        return int.TryParse(end < 0 ? digits : digits[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : null;
    }
}
