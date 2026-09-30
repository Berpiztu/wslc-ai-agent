using System.Globalization;
using System.Text.Json;

namespace WslcAgent.Server.Browse;

/// <summary>Reads the fields of a pane message leniently: a missing or mistyped field is its default.</summary>
internal static class BrowseMessage
{
    public static string Text(JsonElement message, string property) =>
        Field(message, property) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    public static double Number(JsonElement message, string property, double fallback = 0) =>
        Field(message, property) switch
        {
            { ValueKind: JsonValueKind.Number } value => value.GetDouble(),
            { ValueKind: JsonValueKind.String } value when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback,
        };

    /// <summary>A whole number, or null when the pane did not send one (so the caller keeps what it had).</summary>
    public static int? Integer(JsonElement message, string property) =>
        Field(message, property) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var number) ? (int)number : null;

    private static JsonElement? Field(JsonElement message, string property) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty(property, out var value) ? value : null;
}
