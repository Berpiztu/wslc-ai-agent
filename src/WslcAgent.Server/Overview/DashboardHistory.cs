using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// Every state the user's dashboard was saved in, kept whole and all of them
/// (<c>dashboard-history</c> in the data folder, one file each; trimming it
/// comes later), so any can be brought back. Each names its origin — the
/// version it comes from, given by the installation or by a file imported —
/// and its revision within that origin: v2.0 is version 2 as it came, v2.3 the
/// third save since. A new origin starts again at revision 0.
/// </summary>
public sealed class DashboardHistory(IOptions<WslcOptions> options)
{
    /// <summary>Where a dashboard's origin came from.</summary>
    public const string Installation = "installation";

    public const string Import = "import";

    /// <summary>A dashboard saved before the history was kept: its origin was never recorded.</summary>
    public const string Unrecorded = "unrecorded";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _folder = Path.Combine(options.Value.DataDirectory, "dashboard-history");

    private readonly Lock _gate = new();

    /// <summary>The newest revision; null before anything was recorded.</summary>
    public DashboardRevision? Current()
    {
        lock (_gate)
        {
            return Files().LastOrDefault() is { } newest ? Read(newest)?.Revision : null;
        }
    }

    /// <summary>Every revision, the newest first.</summary>
    public IReadOnlyList<DashboardRevision> List()
    {
        lock (_gate)
        {
            return [.. Files().Reverse().Select(Read).OfType<Entry>().Select(entry => entry.Revision)];
        }
    }

    /// <summary>A revision's dashboard; null for one that is not kept.</summary>
    public string? Content(int id)
    {
        lock (_gate)
        {
            return Read(PathOf(id))?.Content.ToJsonString();
        }
    }

    /// <summary>
    /// A state of the dashboard recorded: from a new origin — revision 0 of
    /// it, or <paramref name="revision"/> when one is brought back as it was
    /// (v2.3 again) — or, with <paramref name="origin"/> null, one revision on
    /// from the newest one's origin.
    /// </summary>
    public DashboardRevision Record(string layout, DashboardOrigin? origin, string note, int? revision = null)
    {
        lock (_gate)
        {
            var current = Files().LastOrDefault() is { } newest ? Read(newest)?.Revision : null;
            var from = origin ?? (current is null
                ? new DashboardOrigin(0, Unrecorded, "")
                : new DashboardOrigin(current.Version, current.Source, current.Name));
            var recorded = new DashboardRevision(
                (current?.Id ?? 0) + 1,
                from.Version,
                revision ?? (origin is null ? (current?.Revision ?? 0) + 1 : 0),
                from.Source,
                from.Name,
                DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                note);
            var entry = new JsonObject
            {
                ["id"] = recorded.Id,
                ["version"] = recorded.Version,
                ["revision"] = recorded.Revision,
                ["source"] = recorded.Source,
                ["name"] = recorded.Name,
                ["saved"] = recorded.Saved,
                ["note"] = recorded.Note,
                ["content"] = JsonNode.Parse(layout),
            };
            ShippedFile.WriteFile(PathOf(recorded.Id), entry.ToJsonString(Indented));
            return recorded;
        }
    }

    private sealed record Entry(DashboardRevision Revision, JsonObject Content);

    private string PathOf(int id) => Path.Combine(_folder, $"{id:D6}.json");

    private IEnumerable<string> Files() =>
        Directory.Exists(_folder) ? Directory.GetFiles(_folder, "*.json").Order(StringComparer.Ordinal) : [];

    private static Entry? Read(string path)
    {
        try
        {
            if (ShippedFile.ReadFile(path) is not { } text || JsonNode.Parse(text) is not JsonObject entry || entry["content"] is not JsonObject content)
            {
                return null;
            }

            return new Entry(new DashboardRevision(
                Number(entry["id"]), Number(entry["version"]), Number(entry["revision"]),
                Text(entry["source"]), Text(entry["name"]), Text(entry["saved"]), Text(entry["note"])), content);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}

/// <summary>Where a state of the dashboard comes from: a version, given by the installation or by a file imported (its name).</summary>
public sealed record DashboardOrigin(int Version, string Source, string Name);
