using System.Text.Json;
using System.Text.Json.Nodes;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Overview;

/// <summary>
/// How each kind of dashboard object is born, view by view — its cells, its
/// type size, its alignment and margins, its colours and its parts: what an
/// object dropped from the toolbox takes and what Reset brings back. Designed
/// on the board of every object, one kind at a time (two clients each sent
/// their whole copy of the table once, and the one that saved last wrote over
/// the other); the agent reads the file only as far as its views and kinds.
/// <para>
/// A development agent writes them back to the repository
/// (object-defaults.json), which the installer ships, and reads them from
/// there. An installed agent reads a base — the shipped ones, or an official
/// file the user loaded — and over it the kinds the user changed on it, kept
/// apart (<see cref="LoadedDefaults.ReadChanges"/>), so a newer base, from an
/// update or a file, brings its new and improved kinds and leaves the user's
/// as they are. A kind the user changed that the newer base changed too is a
/// conflict, which the user settles: theirs, or the base's.
/// </para>
/// </summary>
public sealed class ObjectDefaultsStore(ILogger<ObjectDefaultsStore> logger, LoadedDefaults loaded)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly ShippedFile _file = new("object-defaults.json", "ObjectDefaultsSource");

    private readonly Lock _gate = new();

    /// <summary>Whether this agent writes them back to where it is built from (a development build).</summary>
    public bool Writable => _file.Writable;

    /// <summary>The base: on an installed agent the official file loaded, else the shipped ones; empty when there are none.</summary>
    public string Base() => !Writable && loaded.Read(DefaultsKinds.ObjectDefaults)?.ContentText is { Length: > 0 } text ? text : _file.Read() ?? "";

    /// <summary>The defaults in use: the base with the user's changes over it.</summary>
    public string Get()
    {
        if (Writable)
        {
            return _file.Read() ?? "";
        }

        lock (_gate)
        {
            var defaults = Parse(Base());
            foreach (var (view, type, change) in Changes(loaded.ReadChanges()))
            {
                Kinds(defaults, view)[type] = change["value"]?.DeepClone();
            }

            return defaults.ToJsonString();
        }
    }

    /// <summary>How many kinds the user changed on this agent, over the base.</summary>
    public int ChangeCount => Changes(loaded.ReadChanges()).Count();

    /// <summary>The kinds the user changed whose base has changed since: <c>view</c> and object type.</summary>
    public IReadOnlyList<ObjectDefaultConflict> Conflicts()
    {
        var baseline = Parse(Base());
        return [.. Changes(loaded.ReadChanges())
            .Where(change => !JsonNode.DeepEquals(baseline[change.View]?[change.Type], change.Change["was"]))
            .Select(change => new ObjectDefaultConflict(change.View, change.Type))];
    }

    /// <summary>
    /// One kind's default in one view, merged into the rest: written back to the
    /// repository on a development build, kept among the user's changes on an
    /// installed one, with the base's value it replaces; false on a body that
    /// is not a kind's default.
    /// </summary>
    public bool Set(string view, string kind, string value)
    {
        JsonNode? entry;
        try
        {
            entry = JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return false;
        }

        if (entry is not JsonObject)
        {
            return false;
        }

        lock (_gate)
        {
            if (Writable)
            {
                var defaults = Parse(Get());
                Kinds(defaults, view)[kind] = entry;
                if (!_file.Write(defaults.ToJsonString(Indented)))
                {
                    return false;
                }
            }
            else
            {
                var changes = loaded.ReadChanges();
                var kinds = Kinds(changes, view);
                // A kind changed again keeps the base's value it first replaced.
                var was = kinds[kind]?["was"]?.DeepClone() ?? Parse(Base())[view]?[kind]?.DeepClone();
                kinds[kind] = new JsonObject { ["value"] = entry, ["was"] = was };
                loaded.WriteChanges(changes);
            }
        }

        logger.LogInformation("default saved: {View}/{Kind}", view, kind);
        return true;
    }

    /// <summary>
    /// The conflicts settled, kind by kind: the user's kept — now over the
    /// base as it is — or the base's taken, the user's change dropped.
    /// </summary>
    public void Settle(IEnumerable<ObjectDefaultChoice> choices)
    {
        lock (_gate)
        {
            var changes = loaded.ReadChanges();
            var baseline = Parse(Base());
            foreach (var choice in choices)
            {
                if (changes[choice.View]?[choice.Type] is not JsonObject change)
                {
                    continue;
                }

                if (choice.KeepMine)
                {
                    change["was"] = baseline[choice.View]?[choice.Type]?.DeepClone();
                }
                else
                {
                    Kinds(changes, choice.View).Remove(choice.Type);
                }
            }

            loaded.WriteChanges(changes);
        }

        logger.LogInformation("object defaults: conflicts settled");
    }

    /// <summary>Every change the user made dropped, backed up first: the base alone again.</summary>
    public void ClearChanges()
    {
        lock (_gate)
        {
            loaded.ClearChanges();
        }

        logger.LogInformation("object defaults: the user's changes dropped");
    }

    private static IEnumerable<(string View, string Type, JsonObject Change)> Changes(JsonObject changes)
    {
        foreach (var (view, kinds) in changes)
        {
            if (kinds is not JsonObject types)
            {
                continue;
            }

            foreach (var (type, change) in types)
            {
                if (change is JsonObject entry)
                {
                    yield return (view, type, entry);
                }
            }
        }
    }

    /// <summary>A view's kinds in <paramref name="defaults"/>, made where it is missing.</summary>
    private static JsonObject Kinds(JsonObject defaults, string view)
    {
        if (defaults[view] is not JsonObject kinds)
        {
            defaults[view] = kinds = [];
        }

        return kinds;
    }

    /// <summary>The file as an object of views; a file that is none starts over rather than being written on.</summary>
    private static JsonObject Parse(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
