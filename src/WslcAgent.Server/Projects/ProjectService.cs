using System.Collections;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Projects;

/// <summary>
/// Compose files on the agent's machine: found, read with the <c>.env</c>
/// beside them, and handed to <see cref="ComposeReader"/>. A file given as
/// text has no folder, and is read with the variables alone. A folder may
/// hold several Compose files: the one with a standard name is taken, as
/// Compose does, and the others are said, so the user can ask for one of them
/// by its own path. A file that names a network or a volume as external is
/// read knowing which of them the machine has, so one that is missing is
/// said on its line: asked of the two services that list them, when the
/// agent gives them (a reading with none, as a test's, says nothing of it).
/// </summary>
public sealed partial class ProjectService(INetworkService? networks = null, IVolumeService? volumes = null) : IProjectService
{
    /// <summary>The names a Compose file goes by in its folder, in the order Compose looks for them.</summary>
    private static readonly string[] FileNames = ["compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml"];

    /// <summary>A file larger than this is not a Compose file: not opened to find out.</summary>
    private const long LargestFile = 1024 * 1024;

    /// <summary>
    /// The file is read; and, when it names something external, read again
    /// with what the machine has, since only then is there anything to look
    /// for. Most files name nothing external and cost no question to
    /// <c>wslc</c>.
    /// </summary>
    public async Task<ComposePlan> PlanAsync(ComposePlanRequest request, CancellationToken cancellationToken = default)
    {
        var plan = Plan(request, null);
        return (plan.Networks.Any(network => network.External) || plan.Volumes.Any(volume => volume.External))
            && await ThereAsync(cancellationToken) is { } there
            ? Plan(request, there)
            : plan;
    }

    /// <summary>The networks and the volumes on the machine, by name; null when they cannot be asked for (no services given, <c>wslc</c> not there, its session stopped): the file is still read, without that check.</summary>
    private async Task<Machine?> ThereAsync(CancellationToken cancellationToken)
    {
        if (networks is null || volumes is null)
        {
            return null;
        }

        try
        {
            return new Machine(
                (await networks.ListAsync(cancellationToken)).Networks.Select(network => network.Name).ToHashSet(StringComparer.Ordinal),
                (await volumes.ListAsync(cancellationToken)).Volumes.Select(volume => volume.Name).ToHashSet(StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>What the machine has that a file may name as external.</summary>
    private sealed record Machine(HashSet<string> Networks, HashSet<string> Volumes);

    private static ComposePlan Plan(ComposePlanRequest request, Machine? there)
    {
        var path = request.Path.Trim();
        var context = Context(request.Folder);
        if (path.Length == 0)
        {
            return request.Yaml.Trim().Length > 0
                ? ComposeReader.Read(request.Yaml, Source(request, context, there))
                : throw new ArgumentException("A Compose file is required: its path on this machine, or its text.");
        }

        var file = Find(path);
        var beside = Path.GetDirectoryName(file) ?? "";
        var folder = context.Length > 0 ? context : beside;
        var name = Path.GetFileName(file);
        var plan = ComposeReader.Read(File.ReadAllText(file), Source(request, folder, there), file);
        var notes = new List<string>();

        // Compose would merge the override over the file. Until the agent does, the plan says whose it is.
        var overriding = Path.Combine(beside, Path.GetFileNameWithoutExtension(name) + ".override" + Path.GetExtension(name));
        if (File.Exists(overriding))
        {
            notes.Add($"{Path.GetFileName(overriding)} is beside the file and is not read yet: this is the plan of {name} alone.");
        }

        // A folder was given and it holds more than the file taken: said, so another can be asked for.
        var others = Directory.Exists(Path.GetFullPath(path))
            ? Candidates(beside).Where(other => other != file).Select(other => Path.GetFileName(other)).ToList()
            : new List<string>();
        if (others.Count > 0)
        {
            notes.Add($"This folder holds other Compose files too: {string.Join(", ", others)}. {name} was read; give a file's own path to read another.");
        }

        return notes.Count == 0 ? plan : plan with { Warnings = [.. plan.Warnings, .. notes], Notes = [.. plan.Notes, .. notes] };
    }

    /// <summary>The context asked for, as a full path; it has to be a folder of this machine, since everything relative is read from it.</summary>
    private static string Context(string folder)
    {
        var text = folder.Trim();
        if (text.Length == 0)
        {
            return "";
        }

        var full = Path.GetFullPath(text);
        return Directory.Exists(full) ? full : throw new ArgumentException($"The context is not a folder on this machine: {folder}");
    }

    /// <summary>
    /// The Compose file a path names: the file itself, or the one in that
    /// folder. In a folder the standard name wins, as with Compose; with none
    /// of them there, a single Compose file is taken and several are a
    /// question only the user can answer.
    /// </summary>
    private static string Find(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full))
        {
            return full;
        }

        if (!Directory.Exists(full))
        {
            throw new ArgumentException($"Not a file or a folder on this machine: {path}");
        }

        var candidates = Candidates(full);
        if (candidates.FirstOrDefault(IsStandardName) is { } standard)
        {
            return standard;
        }

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new ArgumentException($"No Compose file in {path}: no .yaml or .yml there names any services."),
            _ => throw new ArgumentException($"{path} holds several Compose files and none has a standard name ({string.Join(", ", FileNames)}): {string.Join(", ", candidates.Select(candidate => Path.GetFileName(candidate)))}. Give the path of the one to read."),
        };
    }

    /// <summary>
    /// The Compose files of a folder, by their full paths: those with a
    /// standard name first, in the order Compose looks for them, then the
    /// others by name. An override is not one: it is read with its file.
    /// </summary>
    private static List<string> Candidates(string folder) =>
        Directory.EnumerateFiles(folder)
            .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".yaml" or ".yml")
            .Where(file => !Path.GetFileNameWithoutExtension(file).EndsWith(".override", StringComparison.OrdinalIgnoreCase))
            .Where(NamesServices)
            .OrderBy(StandardRank)
            .ThenBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsStandardName(string file) => StandardRank(file) < FileNames.Length;

    /// <summary>Where a file's name stands among the standard ones; past them all when it is not one.</summary>
    private static int StandardRank(string file)
    {
        var rank = Array.IndexOf(FileNames, Path.GetFileName(file).ToLowerInvariant());
        return rank < 0 ? FileNames.Length : rank;
    }

    /// <summary>A Compose file names its services at the top: what tells it from the other YAML files a folder holds.</summary>
    private static bool NamesServices(string file)
    {
        try
        {
            return new FileInfo(file).Length <= LargestFile && ServicesKey().IsMatch(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^services\s*:", RegexOptions.Multiline)]
    private static partial Regex ServicesKey();

    /// <summary>
    /// What the file's <c>${NAME}</c> are read with, in Compose's own order:
    /// the folder's <c>.env</c>, the environment over it (the agent's, which
    /// is the user's session), and the variables given with the request over
    /// both.
    /// </summary>
    private static ComposeSource Source(ComposePlanRequest request, string folder, Machine? there)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (folder.Length > 0 && ReadFile(Path.Combine(folder, ".env")) is { } env)
        {
            foreach (var pair in ComposeReader.ParseEnvFile(env))
            {
                variables[pair.Key] = pair.Value;
            }
        }

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                variables[key] = value;
            }
        }

        foreach (var pair in request.Variables ?? [])
        {
            var equals = pair.IndexOf('=');
            if (equals > 0)
            {
                variables[pair[..equals].Trim()] = pair[(equals + 1)..];
            }
        }

        var services = (request.Services ?? []).Select(name => name.Trim()).Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);
        return new ComposeSource(request.Name, folder, variables, (request.Profiles ?? []).ToHashSet(StringComparer.Ordinal), ReadFile, ListFolder, services, there?.Networks, there?.Volumes);
    }

    private static string? ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>A folder's entries, each with where it leads when it is a link (a junction, a symbolic link); null when it is not a folder, or cannot be read.</summary>
    private static IReadOnlyList<HostEntry>? ListFolder(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? [.. new DirectoryInfo(path).EnumerateFileSystemInfos().Select(entry => new HostEntry(entry.Name, entry.LinkTarget is { } link ? Path.GetFullPath(link, path) : ""))]
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
