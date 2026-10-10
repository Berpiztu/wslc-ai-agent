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
            new About(plan.Name, plan.File, file.Folder, file.Variables ?? [], file.Profiles ?? [], DateTimeOffset.UtcNow), Json));
    }

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

        return new StoredProject(name, kept.File ?? "", kept.Folder ?? "", File.ReadAllText(file), kept.Variables ?? [], kept.Profiles ?? [], kept.SavedAt);
    }

    /// <summary>Whether a file is kept for any project at all.</summary>
    public bool Any()
    {
        var folder = Path.Combine(options.Value.DataDirectory, "projects");
        return Directory.Exists(folder) && Directory.EnumerateDirectories(folder).Any();
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

    private sealed record About(string Name, string? File, string? Folder, IReadOnlyList<string>? Variables, IReadOnlyList<string>? Profiles, DateTimeOffset SavedAt);
}
