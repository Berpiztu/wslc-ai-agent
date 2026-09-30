using System.Text.Json;
using System.Text.Json.Nodes;

namespace Berpiztu.Dashboard.Storage;

/// <summary>A stored dashboard's text read as JSON, where its pages and views are found.</summary>
internal static class StoredText
{
    /// <summary>The text as a JSON object; null for none, or for one that is not.</summary>
    public static JsonObject? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(stored) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
