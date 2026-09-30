using System.Text.Json;

namespace WslcAgent.UI.Components;

/// <summary>
/// How CLI Activity shows what a command printed and how long it took: a JSON document or JSON lines laid out
/// indented, anything else left as it came; durations in ms, tenths of a second,
/// or whole seconds; the start as its UTC month, day and clock.
/// </summary>
public static class CliOutput
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Pretty JSON or NDJSON; other text trimmed and returned untouched.</summary>
    public static string Format(string? text)
    {
        var raw = (text ?? "").Trim();
        if (raw.Length == 0)
        {
            return "";
        }

        return PrettyDocument(raw) ?? PrettyLines(raw) ?? raw;
    }

    public static string Duration(TimeSpan? duration, string status)
    {
        if (duration is not { } value)
        {
            return status == "running" ? "…" : "—";
        }

        var milliseconds = (long)value.TotalMilliseconds;
        if (milliseconds < 1000)
        {
            return $"{milliseconds} ms";
        }

        var seconds = value.TotalSeconds;
        return seconds < 10
            ? seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s"
            : seconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "s";
    }

    /// <summary>
    /// The start in the agent's own clock, the offset it was stamped with: the
    /// same time the log line beside it shows. Printed as UTC it stood five hours
    /// from the row on a machine at UTC−5, and looked like another command.
    /// </summary>
    public static string Clock(DateTimeOffset startedAt) =>
        startedAt.DateTime.ToString("MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    public static string StatusLabel(string status) => status switch
    {
        "success" => "Success",
        "error" => "Failed",
        "running" => "Running",
        "cancelled" => "Cancelled",
        "timeout" => "Timed out",
        "not_found" => "Not found",
        "" => "—",
        _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(status.Replace('_', ' ')),
    };

    private static string? PrettyDocument(string raw)
    {
        if (raw[0] is not ('{' or '['))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(document.RootElement, Indented);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? PrettyLines(string raw)
    {
        var lines = raw.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (lines.Count < 2)
        {
            return null;
        }

        var parsed = new List<string>();
        foreach (var line in lines)
        {
            if (PrettyDocument(line) is not { } pretty)
            {
                return null;
            }

            parsed.Add(pretty);
        }

        return string.Join("\n\n", parsed);
    }
}
