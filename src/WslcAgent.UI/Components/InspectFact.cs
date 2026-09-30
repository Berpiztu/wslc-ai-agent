using System.Text.Json;

namespace WslcAgent.UI.Components;

/// <summary>One label and value of an inspect summary or form; <see cref="Mono"/> sets a fact in the code face, a field on several lines.</summary>
public sealed record InspectFact(string Label, string Value, bool Mono = false)
{
    /// <summary>A property's text, whatever JSON type it is; empty when missing or null.</summary>
    public static string Text(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => value.GetRawText(),
            }
            : "";

    /// <summary>A string array joined with spaces (<c>Cmd</c>, <c>Entrypoint</c>).</summary>
    public static string Words(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? string.Join(' ', value.EnumerateArray().Select(v => v.ToString()))
            : "";

    /// <summary>An object's keys joined (<c>ExposedPorts</c>), or with <paramref name="pairs"/> its <c>key=value</c> pairs one per line (<c>Labels</c>).</summary>
    public static string Keys(JsonElement item, string property, bool pairs = false) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? string.Join(pairs ? "\n" : ", ", value.EnumerateObject().Select(p => pairs ? $"{p.Name}={p.Value}" : p.Name))
            : "";

    /// <summary>A nested object (<c>Config</c>), or an undefined element that every reader above treats as empty.</summary>
    public static JsonElement Child(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
}
