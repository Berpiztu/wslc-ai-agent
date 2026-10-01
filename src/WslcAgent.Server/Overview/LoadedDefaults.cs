using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The dashboard's files kept in the agent's data folder (<c>loaded-defaults</c>),
/// which an update of the agent leaves as they are: the files the user loaded —
/// the objects' defaults, read as the base instead of the shipped ones — the
/// objects' defaults the user changed on this agent, kept apart from any base
/// (<see cref="ObjectDefaultsStore"/>), and this agent's own default dashboard. Whatever replaces or removes
/// one of them backs it up first.
/// </summary>
public sealed class LoadedDefaults(IOptions<WslcOptions> options)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _folder = Path.Combine(options.Value.DataDirectory, "loaded-defaults");

    private string ChangesPath => Path.Combine(_folder, "object-defaults.changes.json");

    /// <summary>The kind's file as it was loaded; null when none was.</summary>
    public DefaultsFile? Read(string kind) => DefaultsFile.Parse(ShippedFile.ReadFile(PathOf(kind)));

    /// <summary>The kind's file, the one it replaces backed up first.</summary>
    public void Write(DefaultsFile file)
    {
        Backup(PathOf(file.Kind));
        ShippedFile.WriteFile(PathOf(file.Kind), file.Write());
    }

    /// <summary>The kind's file forgotten, backed up first: the agent goes back to the one it ships.</summary>
    public void Clear(string kind) => Remove(PathOf(kind));

    /// <summary>
    /// The objects' defaults the user changed on this agent: view → object
    /// type → <c>{ value, was }</c>, the value they chose and the base's value
    /// it replaced, which tells a later base that changed it too. Empty when
    /// they changed none.
    /// </summary>
    public JsonObject ReadChanges()
    {
        try
        {
            return ShippedFile.ReadFile(ChangesPath) is { } text && JsonNode.Parse(text) is JsonObject changes ? changes : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void WriteChanges(JsonObject changes) => ShippedFile.WriteFile(ChangesPath, changes.ToJsonString(Indented));

    private string OwnDefaultPath => Path.Combine(_folder, "dashboard-default.own.json");

    /// <summary>
    /// This agent's own default dashboard, view by view over the one it ships:
    /// the views saved as default on it or loaded into its default from a file,
    /// and for each the version of the default it was based on. Empty when it
    /// has none.
    /// </summary>
    public (JsonObject Content, IReadOnlyDictionary<string, int> Views) ReadOwnDefault()
    {
        try
        {
            if (ShippedFile.ReadFile(OwnDefaultPath) is { } text && JsonNode.Parse(text) is JsonObject own && own["content"] is JsonObject content)
            {
                return ((JsonObject)content.DeepClone(), Versions(own["views"]));
            }
        }
        catch (JsonException)
        {
            // Half-written or edited by hand: no own default.
        }

        return ([], new Dictionary<string, int>());
    }

    /// <summary>This agent's own default written, the one it replaces backed up first; none left when it holds no view.</summary>
    public void WriteOwnDefault(JsonObject content, IReadOnlyDictionary<string, int> views)
    {
        if (views.Count == 0)
        {
            Remove(OwnDefaultPath);
            return;
        }

        Backup(OwnDefaultPath);
        var own = new JsonObject
        {
            ["views"] = new JsonObject(views.Select(view => KeyValuePair.Create(view.Key, (JsonNode?)view.Value))),
            ["content"] = content.DeepClone(),
        };
        ShippedFile.WriteFile(OwnDefaultPath, own.ToJsonString(Indented));
    }

    /// <summary>The user's changes dropped, backed up first.</summary>
    public void ClearChanges() => Remove(ChangesPath);

    /// <summary>An object of view → version; empty for anything else.</summary>
    private static Dictionary<string, int> Versions(JsonNode? node)
    {
        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        if (node is JsonObject views)
        {
            foreach (var (view, value) in views)
            {
                if (value is JsonValue number && number.TryGetValue<int>(out var version))
                {
                    versions[view] = version;
                }
            }
        }

        return versions;
    }

    private string PathOf(string kind) => Path.Combine(_folder, DefaultsFile.FileName(kind));

    private void Remove(string path)
    {
        Backup(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>A copy of the file in <c>backups</c>, stamped with the time; every one is kept (trimming them comes later).</summary>
    private void Backup(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var folder = Path.Combine(_folder, "backups");
        var name = Path.GetFileNameWithoutExtension(path);
        Directory.CreateDirectory(folder);
        File.Copy(path, Path.Combine(folder, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: true);
    }
}
