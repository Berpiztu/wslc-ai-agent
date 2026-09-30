using System.Text.Json;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// <c>wslc … --format json</c> prints one JSON object per line (NDJSON), not
/// an array. Some commands print a single object, indented over several
/// lines, and so does a file the user exported from here. Every shape parses.
/// </summary>
public static class WslcJson
{
    public static IReadOnlyList<JsonElement> ParseRows(string stdout)
    {
        var text = stdout.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split('\n');
        return lines.Length > 1 && lines[0].TrimEnd().EndsWith('}')
            ? lines.Select(l => l.Trim()).Where(l => l.Length > 0).Select(ParseOne).ToList()
            : ParseDocument(text);
    }

    /// <summary>One value: an array gives its items, anything else is the single row.</summary>
    private static List<JsonElement> ParseDocument(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().Select(e => e.Clone()).ToList()
            : [root.Clone()];
    }

    private static JsonElement ParseOne(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.Clone();
    }

    public static string GetString(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => value.ToString(),
            }
            : "";
}
