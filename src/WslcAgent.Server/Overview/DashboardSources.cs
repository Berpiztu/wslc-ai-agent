using System.Text.Json.Nodes;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The resources a dashboard's objects read — a container, an image, a volume,
/// a network — carried by name from one agent to another. An object keeps only
/// its resource's uid, a number each agent gives its resources in its own
/// order, so the same container is 25 on one machine and 1 on the next. An
/// exported dashboard says what each of its uids is (<c>"25": { "kind":
/// "container", "name": "wslc-published" }</c>), and a dashboard loaded from it
/// takes the uid this agent has for the same resource; an object whose resource
/// this agent does not have is kept without one, for the user to choose,
/// rather than read as a resource that was deleted.
/// </summary>
public static class DashboardSources
{
    private const string Source = "source";

    /// <summary>What each uid the dashboard's objects read is: its kind and name, as <paramref name="describe"/> knows it.</summary>
    public static JsonObject Describe(JsonObject dashboard, Func<int, (string Kind, string Name)?> describe)
    {
        var sources = new JsonObject();
        foreach (var uid in Objects(dashboard).Select(Uid).OfType<int>().Distinct())
        {
            if (describe(uid) is { } resource)
            {
                sources[uid.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JsonObject { ["kind"] = resource.Kind, ["name"] = resource.Name };
            }
        }

        return sources;
    }

    /// <summary>
    /// <paramref name="dashboard"/> with each uid <paramref name="sources"/> names
    /// changed for this agent's uid of the same resource (<paramref name="uidOf"/>,
    /// 0 when it has none); an object whose resource is not here is left
    /// without a source. A uid the file does not name stays as it is.
    /// </summary>
    public static JsonObject Remap(JsonObject dashboard, JsonObject? sources, Func<string, string, int> uidOf)
    {
        var remapped = (JsonObject)dashboard.DeepClone();
        if (sources is null || sources.Count == 0)
        {
            return remapped;
        }

        foreach (var item in Objects(remapped))
        {
            if (Uid(item) is not { } uid || Resource(sources, uid) is not { } resource)
            {
                continue;
            }

            if (uidOf(resource.Kind, resource.Name) is > 0 and var local)
            {
                item[Source] = local.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                item.Remove(Source);
            }
        }

        return remapped;
    }

    /// <summary>The resources <paramref name="sources"/> names that this agent does not have, as "container wslc-published".</summary>
    public static IReadOnlyList<string> Missing(JsonObject dashboard, JsonObject? sources, Func<string, string, int> uidOf) =>
        sources is null
            ? []
            : [.. Objects(dashboard).Select(Uid).OfType<int>().Distinct()
                .Select(uid => Resource(sources, uid))
                .OfType<(string Kind, string Name)>()
                .Where(resource => uidOf(resource.Kind, resource.Name) <= 0)
                .Select(resource => $"{resource.Kind} {resource.Name}")
                .Distinct()];

    /// <summary>Every object of every page and view: each layout's <c>objects</c>, wherever it is in the dashboard.</summary>
    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is not JsonObject layout)
        {
            yield break;
        }

        if (layout["objects"] is JsonArray objects)
        {
            foreach (var item in objects.OfType<JsonObject>())
            {
                yield return item;
            }
        }

        foreach (var (key, child) in layout)
        {
            if (key != "objects" && child is JsonObject)
            {
                foreach (var item in Objects(child))
                {
                    yield return item;
                }
            }
        }
    }

    /// <summary>The uid an object reads, kept as text or as a number; null for an object that reads none.</summary>
    private static int? Uid(JsonObject item) => item[Source] switch
    {
        JsonValue value when value.TryGetValue<int>(out var number) && number > 0 => number,
        JsonValue value when value.TryGetValue<string>(out var text) && int.TryParse(text, out var number) && number > 0 => number,
        _ => null,
    };

    private static (string Kind, string Name)? Resource(JsonObject sources, int uid) =>
        sources[uid.ToString(System.Globalization.CultureInfo.InvariantCulture)] is JsonObject resource
        && resource["kind"] is JsonValue kind && kind.TryGetValue<string>(out var kindText)
        && resource["name"] is JsonValue name && name.TryGetValue<string>(out var nameText)
            ? (kindText, nameText)
            : null;
}
