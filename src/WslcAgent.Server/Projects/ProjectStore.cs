using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Projects;

/// <summary>
/// The Compose file kept with each project, in the agent's data folder
/// (<c>projects/NAME/</c>): the file as it was brought up and what it was read
/// with. It is what the project shows as its own, whatever happens to the file
/// on disk after, and what tells the order its services start in.
/// </summary>
public sealed partial class ProjectStore(IOptions<WslcOptions> options)
{
    private const string FileName = "compose.yaml";
    private const string AboutName = "project.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Keeps the file a project was brought up from, over the one kept before.</summary>
    public void Keep(ComposePlan plan, ComposePlanRequest file)
    {
        var folder = Folder(plan.Name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileName), string.Join('\n', plan.Lines.Select(line => line.Text)));
        File.WriteAllText(Path.Combine(folder, AboutName), JsonSerializer.Serialize(
            // The folder it was read from, asked for or found: what a file given as text is read from the next time.
            new About(plan.Name, plan.File, file.Folder.Trim().Length > 0 ? file.Folder : plan.Folder, file.Variables ?? [], file.Profiles ?? [], DateTimeOffset.UtcNow, file.Services ?? [],
                Sent(file), file.ClientFile ?? ""), Json));
    }

    /// <summary>
    /// The build contexts a client on another machine sent for this save,
    /// each with its id; none when the images were built from this machine's
    /// folders. Kept so the project says, when it is opened again, what it is
    /// built from: saved again with none named, it was built from a folder of
    /// this machine that had never held its sources.
    /// </summary>
    private static Dictionary<string, string> Sent(ComposePlanRequest file) =>
        (file.Contexts ?? new Dictionary<string, string>())
            .Where(context => !string.IsNullOrEmpty(context.Value))
            .ToDictionary(context => context.Key, context => context.Value, StringComparer.Ordinal);

    /// <summary>The file kept for a project; null when none is (a project whose containers were made on another agent, or before the file was kept).</summary>
    public StoredProject? Read(string name)
    {
        var folder = Folder(name);
        var file = Path.Combine(folder, FileName);
        var about = Path.Combine(folder, AboutName);
        if (!File.Exists(file) || !File.Exists(about) || JsonSerializer.Deserialize<About>(File.ReadAllText(about), Json) is not { } kept)
        {
            return null;
        }

        return new StoredProject(name, kept.File ?? "", kept.Folder ?? "", File.ReadAllText(file), kept.Variables ?? [], kept.Profiles ?? [], kept.SavedAt, kept.Services ?? [],
            kept.Contexts ?? new Dictionary<string, string>(), kept.ClientFile ?? "");
    }

    /// <summary>Whether a file is kept for any project at all.</summary>
    public bool Any()
    {
        var folder = Path.Combine(options.Value.DataDirectory, "projects");
        return Directory.Exists(folder) && Directory.EnumerateDirectories(folder).Any();
    }

    /// <summary>
    /// The ids of the build contexts the kept projects were built from, sent
    /// by a client on another machine: what the agent holds on to for as long
    /// as the project is kept (<see cref="ProjectContexts"/>), so it can be
    /// built again from them when the machine that has the folder is not the
    /// one asking.
    /// </summary>
    public HashSet<string> Contexts()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var folder = Path.Combine(options.Value.DataDirectory, "projects");
        if (!Directory.Exists(folder))
        {
            return ids;
        }

        foreach (var project in Directory.EnumerateDirectories(folder))
        {
            try
            {
                var about = Path.Combine(project, AboutName);
                if (File.Exists(about) && JsonSerializer.Deserialize<About>(File.ReadAllText(about), Json) is { Contexts: { } sent })
                {
                    ids.UnionWith(sent.Values);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // One project's file that cannot be read now says nothing of the others.
            }
        }

        return ids;
    }

    /// <summary>Lets go of the file kept for a project that is no more.</summary>
    public void Forget(string name)
    {
        var folder = Folder(name);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The project's folder. Its name is a part of a path here, so only what a project can be named is taken.</summary>
    private string Folder(string name) =>
        ProjectName().IsMatch(name)
            ? Path.Combine(options.Value.DataDirectory, "projects", name)
            : throw new ArgumentException($"Not a project's name: {name}");

    // What the reader leaves of any name: lower case, digits, _ and -, starting with a letter or a digit.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex ProjectName();

    private sealed record About(string Name, string? File, string? Folder, IReadOnlyList<string>? Variables, IReadOnlyList<string>? Profiles, DateTimeOffset SavedAt, IReadOnlyList<string>? Services = null,
        Dictionary<string, string>? Contexts = null, string? ClientFile = null);
}
