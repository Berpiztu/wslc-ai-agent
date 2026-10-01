using System.Text.Json;
using System.Text.Json.Nodes;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The version of each file the agent ships — the objects' defaults and the
/// default dashboard — and the oldest agent that loads it
/// (<c>defaults-versions.json</c>, embedded). A development agent raises the
/// dashboard's itself each time Save as default changes it; the rest is raised
/// by hand. A file without its entry is version 0, needing no agent in
/// particular.
/// </summary>
public sealed class DefaultsVersions
{
    private const string DashboardKey = "dashboard";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly ShippedFile _file = new("defaults-versions.json", "DefaultsVersionsSource");

    private readonly Lock _gate = new();

    /// <summary>The kind's version as this agent ships it.</summary>
    public DefaultsFileVersion For(string kind)
    {
        var entry = Parse(_file.Read())[kind == DefaultsKinds.Dashboard ? DashboardKey : "objectDefaults"] as JsonObject;
        var version = entry?["version"] is JsonValue number && number.TryGetValue<int>(out var value) ? value : 0;
        var minimum = entry?["minAgentVersion"] is JsonValue text && text.TryGetValue<string>(out var least) ? least : "";
        return new DefaultsFileVersion(version, minimum);
    }

    /// <summary>The default dashboard one version up, written back to the repository; nothing on a release build, which ships the versions.</summary>
    public void RaiseDashboard()
    {
        if (!_file.Writable)
        {
            return;
        }

        lock (_gate)
        {
            var versions = Parse(_file.Read());
            if (versions[DashboardKey] is not JsonObject dashboard)
            {
                versions[DashboardKey] = dashboard = new JsonObject { ["version"] = 0, ["minAgentVersion"] = "" };
            }

            dashboard["version"] = (dashboard["version"] is JsonValue current && current.TryGetValue<int>(out var number) ? number : 0) + 1;
            _file.Write(versions.ToJsonString(Indented));
        }
    }

    private static JsonObject Parse(string? text)
    {
        try
        {
            return text is not null && JsonNode.Parse(text) is JsonObject versions ? versions : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
