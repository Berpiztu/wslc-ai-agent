using System.Text.Json.Nodes;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Overview;

/// <summary>
/// What differs between two dashboards in one view, JSON against JSON: the
/// objects and the cards each holds, matched by their id, those only in one of
/// them and those in both that differ, property by property
/// (<c>size: small → large</c>). The order of the lists does not count.
/// </summary>
public static class DashboardDiff
{
    private const string Objects = "objects";

    private const string Cards = "groups";

    /// <summary>What <paramref name="to"/> changes from <paramref name="from"/> in the view named; empty when they are the same.</summary>
    public static IReadOnlyList<DashboardChange> Compare(JsonObject from, JsonObject to, string view)
    {
        var before = StoredDashboardViews.Layout(from, view);
        var after = StoredDashboardViews.Layout(to, view);
        return
        [
            .. Items(before, after, Objects, item => Text(item["type"])),
            .. Items(before, after, Cards, item => $"card {Text(item["template"])}".TrimEnd()),
        ];
    }

    private static IEnumerable<DashboardChange> Items(JsonObject? before, JsonObject? after, string list, Func<JsonObject, string> name)
    {
        var old = ById(before?[list]);
        var now = ById(after?[list]);
        foreach (var (_, item) in now.Where(entry => !old.ContainsKey(entry.Key)))
        {
            yield return new DashboardChange(name(item), "added", Where(item));
        }

        foreach (var (_, item) in old.Where(entry => !now.ContainsKey(entry.Key)))
        {
            yield return new DashboardChange(name(item), "removed", Where(item));
        }

        foreach (var (id, item) in now.Where(entry => old.ContainsKey(entry.Key)))
        {
            if (Changed(old[id], item) is { Length: > 0 } detail)
            {
                yield return new DashboardChange(name(item), "changed", detail);
            }
        }
    }

    /// <summary>Each property that differs, its value before and after; empty when none does.</summary>
    private static string Changed(JsonObject before, JsonObject after) =>
        string.Join("; ", before.Select(pair => pair.Key).Union(after.Select(pair => pair.Key))
            .Where(key => key != "id" && !JsonNode.DeepEquals(before[key], after[key]))
            .Select(key => $"{key}: {Value(before[key])} → {Value(after[key])}"));

    /// <summary>Where an object or card stands and how big it is.</summary>
    private static string Where(JsonObject item) => $"at {Value(item["x"])},{Value(item["y"])}, {Value(item["w"])}×{Value(item["h"])}";

    private static Dictionary<string, JsonObject> ById(JsonNode? list)
    {
        var items = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (list is JsonArray array)
        {
            foreach (var item in array.OfType<JsonObject>())
            {
                if (Text(item["id"]) is { Length: > 0 } id)
                {
                    items[id] = item;
                }
            }
        }

        return items;
    }

    /// <summary>A value as it reads in a change: a number or word as it is, a structure as its JSON, a missing one as a dash.</summary>
    private static string Value(JsonNode? node) => node switch
    {
        null => "—",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString(),
    };

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
