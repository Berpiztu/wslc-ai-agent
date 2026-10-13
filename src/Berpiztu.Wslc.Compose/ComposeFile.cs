using WslcAgent.ApiClient.Contracts;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// The Compose file a command is about: found as Compose finds it, and read
/// by the agent, which is the one that knows what it becomes on WSLC. An agent
/// on this machine is given the file's path and reads it where it is, with
/// its folder, its <c>.env</c> and its <c>build</c> contexts; one on another
/// machine is given its text, since the path means nothing there, and the
/// folders its services are built from, which are sent to it
/// (<see cref="BuildContexts"/>).
/// </summary>
internal sealed class ComposeFile(AgentConnection agent, Arguments arguments)
{
    /// <summary>The names a Compose file is looked for by, in Compose's own order.</summary>
    private static readonly string[] StandardNames = ["compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml"];

    /// <summary>
    /// The file as the agent reads it, and the request that reads it so, for
    /// the commands that go on from the reading. The file's <c>${NAME}</c>
    /// variables take what this terminal has for them, as Compose does: the
    /// agent has its own environment, not the caller's, so the values are
    /// sent with the request, where they win over the folder's <c>.env</c>.
    /// Under those, the ones of the files named with <c>--env-file</c>.
    /// With <paramref name="send"/>, for a command that builds: the folders
    /// the file's services are built from are sent to an agent that does not
    /// have them. Without it they are only said to be ones that will be.
    /// </summary>
    public async Task<(ComposePlanRequest Request, ComposePlan Plan)> ReadAsync(CancellationToken cancellationToken, bool send = false)
    {
        var (request, plan) = await WithContextsAsync(await ReadFileAsync(cancellationToken), send, cancellationToken);

        // Said to whoever is about to change something on a machine they are not at: where, on
        // that machine, the file's relative paths lead (its mounts, its .env).
        if (send && !agent.IsLocal && plan.Folder.Length > 0)
        {
            Terminal.Faint($"On the agent, the file's relative paths start from {plan.Folder}");
        }

        return (request, plan);
    }

    /// <summary>
    /// The folders the file's services are built from, for an agent on
    /// another machine: they are here, where the sources are, whatever folder
    /// of its own machine the agent reads the rest of the file from (what the
    /// containers mount is there). Each is named in the request: sent from
    /// here when the command builds and a service that is taken needs it,
    /// and otherwise named with nothing sent, which tells the agent the build
    /// can be made and has it read the file so. An agent on this machine
    /// reads the folders where they are, and one too old to take a context
    /// names none.
    /// </summary>
    private async Task<(ComposePlanRequest Request, ComposePlan Plan)> WithContextsAsync((ComposePlanRequest Request, ComposePlan Plan) read, bool send, CancellationToken cancellationToken)
    {
        var (request, plan) = read;
        if (agent.IsLocal || plan.Contexts is not { Count: > 0 } missing)
        {
            return (request, plan);
        }

        var folder = Path.GetDirectoryName(Find()) ?? Folder;
        var contexts = new BuildContexts(agent.Api);
        var sent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var context in missing)
        {
            sent[context.Path] = send && context.Needed
                ? await contexts.SendAsync(Path.GetFullPath(Path.Combine(folder, context.Path)), context.Path, cancellationToken)
                : "";
        }

        request = request with { Contexts = sent };
        return (request, await agent.Api.PlanProjectAsync(request, cancellationToken));
    }

    private async Task<(ComposePlanRequest Request, ComposePlan Plan)> ReadFileAsync(CancellationToken cancellationToken)
    {
        var named = await EnvFilesAsync(cancellationToken);
        var request = (await RequestAsync(cancellationToken)) with { Variables = [.. named.Select(pair => $"{pair.Key}={pair.Value}")] };
        var plan = await agent.Api.PlanProjectAsync(request, cancellationToken);
        var own = plan.Variables
            .Select(variable => (variable.Name, Value: Environment.GetEnvironmentVariable(variable.Name)))
            .Where(variable => variable.Value is not null)
            .ToList();
        if (own.Count == 0)
        {
            return (request, plan);
        }

        foreach (var (name, value) in own)
        {
            named[name] = value!;
        }

        request = request with { Variables = [.. named.Select(pair => $"{pair.Key}={pair.Value}")] };
        return (request, await agent.Api.PlanProjectAsync(request, cancellationToken));
    }

    /// <summary>
    /// The variables of the files named with <c>--env-file</c>, read here: a
    /// path of this terminal's folder means nothing to the agent. A later
    /// file's value wins over an earlier one's. <c>KEY=value</c> a line,
    /// with <c>export</c> before it or quotes round the value let go; blank
    /// lines and comments skipped.
    /// </summary>
    private async Task<Dictionary<string, string>> EnvFilesAsync(CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var named in arguments.EnvFiles)
        {
            var path = Path.GetFullPath(named);
            if (!File.Exists(path))
            {
                throw new UsageException($"No such file: {path}");
            }

            foreach (var written in await File.ReadAllLinesAsync(path, cancellationToken))
            {
                var line = written.Trim();
                line = line.StartsWith("export ", StringComparison.Ordinal) ? line["export ".Length..].TrimStart() : line;
                var equals = line.IndexOf('=');
                if (equals > 0 && !line.StartsWith('#'))
                {
                    var value = line[(equals + 1)..].Trim();
                    variables[line[..equals].Trim()] = value.Length > 1 && value[0] == value[^1] && value[0] is '"' or '\'' ? value[1..^1] : value;
                }
            }
        }

        return variables;
    }

    /// <summary>
    /// The project a command is about: the one named, written as the agent
    /// names projects (lower case); without a name, the one the file makes.
    /// </summary>
    public async Task<string> ProjectNameAsync(CancellationToken cancellationToken) =>
        arguments.Project.Length > 0 && !Exists()
            ? arguments.Project.ToLowerInvariant()
            : (await ReadAsync(cancellationToken)).Plan.Name;

    private async Task<ComposePlanRequest> RequestAsync(CancellationToken cancellationToken)
    {
        var file = Find();
        return agent.IsLocal
            ? new ComposePlanRequest(Path: file, Name: arguments.Project, Profiles: arguments.Profiles,
                Folder: arguments.Directory.Length > 0 ? Path.GetFullPath(arguments.Directory) : "", Services: arguments.Services)
            : new ComposePlanRequest(Yaml: await File.ReadAllTextAsync(file, cancellationToken), Name: arguments.Project, Profiles: arguments.Profiles,
                Folder: arguments.Directory, Services: arguments.Services);
    }

    /// <summary>There is a file to read: a project named with no file at hand is taken by its name alone.</summary>
    private bool Exists() => arguments.File.Length > 0 || StandardNames.Any(name => File.Exists(Path.Combine(Folder, name)));

    /// <summary>
    /// Where the file is looked for when none is named: the project's
    /// directory, or the current one. For an agent on another machine
    /// <c>--project-directory</c> names a folder of that machine, which is
    /// no place to look in from here: the file is then the current folder's.
    /// </summary>
    private string Folder => arguments.Directory.Length > 0 && agent.IsLocal ? Path.GetFullPath(arguments.Directory) : Environment.CurrentDirectory;

    private string Find()
    {
        if (arguments.File.Length > 0)
        {
            var named = Path.GetFullPath(arguments.File);
            return File.Exists(named) ? named : throw new UsageException($"No such file: {named}");
        }

        return StandardNames.Select(name => Path.Combine(Folder, name)).FirstOrDefault(path => File.Exists(path))
            ?? throw new UsageException($"No Compose file in {Folder} ({string.Join(", ", StandardNames)}). Name one with -f, or the project with -p.");
    }
}
