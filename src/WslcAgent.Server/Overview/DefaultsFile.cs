using System.Text.Json;
using System.Text.Json.Nodes;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Overview;

/// <summary>
/// A defaults file as it travels between agents (docs/dashboard-defaults.md):
/// the objects' defaults or a dashboard, wrapped with the version it comes
/// from, the client's revision within it (an export of v2.3 is version 2,
/// revision 3; 0 for a file as released), and the oldest agent that
/// understands it. The content is the text the agent keeps today, untouched,
/// so nothing that reads it changes.
/// </summary>
/// <param name="Kind"><see cref="DefaultsKinds.ObjectDefaults"/> or <see cref="DefaultsKinds.Dashboard"/>.</param>
/// <param name="From">The agent that exported it; empty for a file as released.</param>
/// <param name="Created">When it was written, ISO 8601; empty when it does not say.</param>
public sealed record DefaultsFile(string Kind, int Version, string MinAgentVersion, string From, string Created, JsonObject Content, int Revision = 0)
{
    /// <summary>The name a kind's file has in the package folder and beside the installers of a release.</summary>
    public static string FileName(string kind) => kind == DefaultsKinds.Dashboard ? "wslc-dashboard-default.json" : "wslc-object-defaults.json";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The file's identity, as the API reports it.</summary>
    public DefaultsFileVersion Identity => new(Version, MinAgentVersion);

    /// <summary>The content as the agent keeps it.</summary>
    public string ContentText => Content.ToJsonString();

    /// <summary>A file read back; null for text that is not one, so a file copied by hand or half-written is simply not there.</summary>
    public static DefaultsFile? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject file
                || KindOf(file["kind"]) is not { } kind
                || Number(file["version"]) is not { } version
                || file["content"] is not JsonObject content)
            {
                return null;
            }

            return new DefaultsFile(kind, version, Text(file["minAgentVersion"]), Text(file["from"]), Text(file["created"]), content,
                Number(file["revision"]) ?? 0);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The file as it is written to disk and downloaded.</summary>
    public string Write()
    {
        var file = new JsonObject
        {
            ["kind"] = "wslc-" + Kind,
            ["version"] = Version,
        };
        if (Revision > 0)
        {
            file["revision"] = Revision;
        }

        file["minAgentVersion"] = MinAgentVersion;
        file["from"] = From;
        file["created"] = Created;
        file["content"] = Content.DeepClone();
        return file.ToJsonString(Indented);
    }

    /// <summary>The kind a file names, <c>wslc-object-defaults</c> or <c>wslc-dashboard</c>; null for any other.</summary>
    private static string? KindOf(JsonNode? node) => Text(node) switch
    {
        "wslc-" + DefaultsKinds.ObjectDefaults => DefaultsKinds.ObjectDefaults,
        "wslc-" + DefaultsKinds.Dashboard => DefaultsKinds.Dashboard,
        _ => null,
    };

    private static int? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
