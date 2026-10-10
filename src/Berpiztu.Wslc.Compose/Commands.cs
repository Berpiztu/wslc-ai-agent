using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// The commands, each one a call or two to the agent and what it answered,
/// printed. What a Compose file becomes, in which order its containers start
/// and what depends on what are the agent's to say: nothing of that is
/// decided here, so the command line and the Containers page never disagree.
/// </summary>
internal sealed partial class Commands(AgentConnection agent, Arguments arguments, Interrupt interrupt)
{
    private readonly ComposeFile _file = new(agent, arguments);

    private WslcAgentApi Api => agent.Api;

    private CancellationToken Stop => interrupt.Token;

    /// <summary>The level of the agent's API from which a Compose file is read for the services asked for by name.</summary>
    private const int ServicesByName = 10;

    public Task<int> RunAsync() => arguments.Command switch
    {
        "config" => ConfigAsync(),
        "build" => OfTheServicesAsked(BuildAsync),
        "up" => OfTheServicesAsked(UpAsync),
        "down" => DownAsync(),
        "start" => VerbAsync("start", "Started"),
        "stop" => VerbAsync("stop", "Stopped"),
        "restart" => VerbAsync("restart", "Restarted"),
        "ps" => PsAsync(),
        "ls" => LsAsync(),
        "version" => VersionAsync(),
        _ => throw new UsageException($"Unknown command: {arguments.Command}"),
    };

    /// <summary>
    /// A build or an up of the services named, refused on an agent that does
    /// not know the names are there: it would read the request without them
    /// and bring up the whole project, which is not what was asked.
    /// </summary>
    private Task<int> OfTheServicesAsked(Func<Task<int>> command) =>
        arguments.Services.Count > 0 && agent.ApiLevel < ServicesByName
            ? throw new CommandFailed($"The agent at {agent.Address} is older than this wslc-compose and cannot take services by name: it would {arguments.Command} the whole project. Update the agent, or leave the names out.")
            : command();

    /// <summary>
    /// The file, each line with what the agent makes of it written after it
    /// as a comment, so what is printed is still the file. What is about the
    /// file as a whole, and the count, go to the error stream: the output
    /// stays something to keep or to pipe.
    /// </summary>
    private async Task<int> ConfigAsync()
    {
        var (_, plan) = await _file.ReadAsync(Stop);
        if (Listed(plan) is { } names)
        {
            // One of Compose's lists was asked for instead of the file: the names, one a line.
            foreach (var name in names)
            {
                Terminal.Line(name);
            }

            return Program.Ok;
        }

        // Quiet, as Compose has it: the file is read and judged, and nothing of it is printed.
        foreach (var line in arguments.Has(Arguments.Quiet) ? [] : plan.Lines)
        {
            Terminal.Line(Remark(line) is { Length: > 0 } remark
                ? $"{Written(line.Text)}  {Terminal.Tint($"# {remark}", Tone(line.Verdict))}"
                : Written(line.Text));
        }

        foreach (var note in plan.Notes)
        {
            Terminal.Warning(note);
        }

        Terminal.Aside($"{Terminal.Tint(plan.Name, Terminal.Bold, onError: true)}: {plan.Services.Count} services; "
            + $"{Counted(plan, ComposeLine.Converted)} lines converted, {Counted(plan, ComposeLine.Unsupported)} not supported, {Counted(plan, ComposeLine.Warning)} with a warning.");
        return Program.Ok;
    }

    /// <summary>
    /// What <c>config</c> prints in place of the file when one of its lists
    /// is asked for: the services taken, the file's profiles, the images, the
    /// volumes, or the variables with the value each was read as. Null when
    /// none was asked for.
    /// </summary>
    private IEnumerable<string>? Listed(ComposePlan plan) =>
        arguments.Has(Arguments.ServiceNames) ? plan.Services.Where(service => service.Enabled).Select(service => service.Name)
        : arguments.Has(Arguments.ProfileNames) ? plan.Services.SelectMany(service => service.Profiles).Distinct(StringComparer.Ordinal)
        : arguments.Has(Arguments.Images) ? plan.Services.Where(service => service.Enabled).Select(service => service.Launch.Image).Distinct(StringComparer.Ordinal)
        : arguments.Has(Arguments.Volumes) ? plan.Volumes.Select(volume => volume.Key)
        : arguments.Has(Arguments.Variables) ? plan.Variables.Select(variable => $"{variable.Name}={variable.Value}")
        : null;

    /// <summary>
    /// Nothing is done, and what would be is said: Compose's <c>--dry-run</c>.
    /// Taken by every command that changes something, before it changes it:
    /// an option that makes a command harmless cannot be one that is let go
    /// by unheeded.
    /// </summary>
    private bool DryRun(IEnumerable<string> would)
    {
        if (!arguments.Has(Arguments.DryRun))
        {
            return false;
        }

        foreach (var line in would)
        {
            Terminal.Line($"{Terminal.Tint("DRY-RUN", Terminal.Gray)}  {line}");
        }

        return true;
    }

    /// <summary>How many lines have the verdict, in the verdict's colour when there are any.</summary>
    private static string Counted(ComposePlan plan, string verdict) =>
        plan.Lines.Count(line => line.Verdict == verdict) is > 0 and var some ? Terminal.Tint($"{some}", Tone(verdict), onError: true) : "0";

    /// <summary>What is said of a line beside it; nothing for one taken as written.</summary>
    private static string Remark(ComposeLine line)
    {
        var verdict = line.Verdict switch
        {
            ComposeLine.Converted => "converted",
            ComposeLine.Unsupported => "NOT SUPPORTED",
            ComposeLine.NotNeeded => "not needed",
            ComposeLine.Warning => "warning",
            _ => "",
        };
        return verdict.Length > 0 && line.Note.Length > 0 ? $"{verdict}: {line.Note}" : verdict + line.Note;
    }

    /// <summary>A verdict's colour, the one the Compose window gives it: taken green, changed or doubtful yellow, not given red.</summary>
    private static string Tone(string verdict) => verdict switch
    {
        ComposeLine.Converted or ComposeLine.Warning => Terminal.Yellow,
        ComposeLine.Unsupported => Terminal.Red,
        ComposeLine.NotNeeded => Terminal.Gray,
        _ => Terminal.Green,
    };

    /// <summary>A line of the file as an editor shows it: its key in a colour, a comment in grey.</summary>
    private static string Written(string text)
    {
        if (Keyed().Match(text) is { Success: true } keyed)
        {
            return keyed.Groups["lead"].Value + Terminal.Tint(keyed.Groups["key"].Value, Terminal.Cyan) + keyed.Groups["rest"].Value;
        }

        return text.TrimStart().StartsWith('#') ? Terminal.Tint(text, Terminal.Gray) : text;
    }

    /// <summary><c>key:</c> at the start of a line, after its indent and a list's dash: a key is followed by a space or by the line's end, which a port or an address is not.</summary>
    [GeneratedRegex(@"^(?<lead>\s*(?:- )?)(?<key>[A-Za-z_][\w.-]*)(?<rest>:(?:\s.*)?)$")]
    private static partial Regex Keyed();

    /// <summary>
    /// The images the file's services are built from, built and nothing else
    /// made: a job of the agent, followed line by line as an up is.
    /// </summary>
    private async Task<int> BuildAsync()
    {
        var (request, plan) = await _file.ReadAsync(Stop);
        foreach (var doubt in plan.Warnings)
        {
            Terminal.Warning(doubt);
        }

        if (DryRun(Built(plan).Keys.Select(image => $"would build image {image}")))
        {
            return Program.Ok;
        }

        var build = new ProjectBuildRequest(request, NoCache: arguments.Has(Arguments.NoCache), Pull: arguments.Has(Arguments.Pull));
        return await new JobFollower(Api, interrupt, Built(plan)).FollowAsync(await Api.BuildProjectAsync(build, Stop));
    }

    /// <summary>The service each image the file builds is for, by the image's name: what a build's lines are said by.</summary>
    private static Dictionary<string, string> Built(ComposePlan plan) =>
        plan.Services
            .Where(service => service.Enabled && service.Build is not null)
            .GroupBy(service => service.Launch.Image, StringComparer.Ordinal)
            .ToDictionary(image => image.Key, image => image.First().Name, StringComparer.Ordinal);

    /// <summary>
    /// The file saved as a project and brought up, as the Compose window's
    /// Run does: a job of the agent, followed here line by line until it
    /// ends. What the file asks for and will not get is said first.
    /// </summary>
    private async Task<int> UpAsync()
    {
        var (request, plan) = await _file.ReadAsync(Stop);
        foreach (var lost in plan.Unsupported)
        {
            Terminal.Lost(lost);
        }

        foreach (var doubt in plan.Warnings)
        {
            Terminal.Warning(doubt);
        }

        // An up brings services up beside the ones the project already has: what it takes is added
        // to what the project is kept with, so `up web` and then `up` leave it kept with both.
        var up = new ProjectUpRequest(request, Start: !arguments.Has(Arguments.NoStart), RemoveOrphans: arguments.Has(Arguments.RemoveOrphans), AddServices: true);
        var preview = await Api.PreviewProjectAsync(up, Stop);
        if (preview.Edited is { Count: > 0 } edited)
        {
            Terminal.Warning($"Changed by hand since the file made them, and recreated now as the file says ({string.Join(", ", edited)}): what was changed in them is lost, the names they are published on apart.");
        }

        if (preview.Orphans.Count > 0 && !up.RemoveOrphans)
        {
            Terminal.Warning($"{preview.Name} has containers whose service the file no longer has ({string.Join(", ", preview.Orphans)}). --remove-orphans removes them.");
        }

        if (DryRun(
            [
                .. preview.Networks.Select(network => $"would create network {network}"),
                .. preview.Volumes.Select(volume => $"would create volume {volume}"),
                .. preview.Builds.Select(image => $"would build image {image}"),
                .. preview.Create.Select(container => $"would create {(up.Start ? "and start " : "")}container {container}"),
                .. preview.Recreate.Select(container => $"would recreate container {container}"),
                .. preview.Keep.Select(container => $"would leave container {container} as it is"),
                .. (up.RemoveOrphans ? preview.Orphans : []).Select(container => $"would remove container {container}"),
            ]))
        {
            return Program.Ok;
        }

        return await new JobFollower(Api, interrupt, Built(plan)).FollowAsync(await Api.StartProjectAsync(up, Stop));
    }

    private async Task<int> DownAsync()
    {
        var project = await _file.ProjectNameAsync(Stop);
        if (DryRun(
            [
                .. (await Api.GetProjectContainersAsync(project, Stop)).Select(member => $"would remove container {member.Name}"),
                $"would remove the networks of project {project}{(arguments.Has(Arguments.Volumes) ? ", and its volumes" : "")}",
            ]))
        {
            return Program.Ok;
        }

        Said(await Api.DownProjectAsync(project, arguments.Has(Arguments.Volumes), Stop), "Removed");
        return Program.Ok;
    }

    private async Task<int> VerbAsync(string verb, string done)
    {
        var project = await _file.ProjectNameAsync(Stop);
        if (arguments.Has(Arguments.DryRun))
        {
            // The containers the verb would go through: of the services named, or all of them.
            var members = await Api.GetProjectContainersAsync(project, Stop);
            DryRun(members
                .Where(member => arguments.Services.Count == 0 || arguments.Services.Contains(member.Service, StringComparer.Ordinal))
                .Select(member => $"would {verb} container {member.Name}"));
            return Program.Ok;
        }

        if (arguments.Services.Count > 0)
        {
            return await ServicesVerbAsync(project, verb, done);
        }

        Said(await Api.ProjectVerbAsync(project, verb, Stop), done);
        return Program.Ok;
    }

    /// <summary>
    /// The verb on the containers of the services named, and no other: each
    /// with its own verb, in the order the project's services start in, the
    /// other way round to stop. The agent's rules stand as for any container:
    /// one whose service depends on a stopped one is not started, and says
    /// which.
    /// </summary>
    private async Task<int> ServicesVerbAsync(string project, string verb, string done)
    {
        var members = await Api.GetProjectContainersAsync(project, Stop);
        if (arguments.Services.FirstOrDefault(name => members.All(member => member.Service != name)) is { } missing)
        {
            throw new CommandFailed($"Project {project} has no container of service '{missing}'. Its services with one: {string.Join(", ", members.Select(member => member.Service))}.");
        }

        var asked = members.Where(member => arguments.Services.Contains(member.Service, StringComparer.Ordinal)).ToList();
        foreach (var member in verb == "stop" ? Enumerable.Reverse(asked) : asked)
        {
            await (verb switch
            {
                "start" => Api.StartContainerAsync(member.Id, Stop),
                "stop" => Api.StopContainerAsync(member.Id, Stop),
                _ => Api.RestartContainerAsync(member.Id, Stop),
            });
            Terminal.Done($"{done} {member.Name}");
        }

        return Program.Ok;
    }

    private static void Said(ProjectResult result, string done)
    {
        if (result.Done.Count == 0)
        {
            Terminal.Line($"{result.Name}: nothing to do.");
        }

        foreach (var item in result.Done)
        {
            Terminal.Done($"{done} {item}");
        }
    }

    private async Task<int> PsAsync()
    {
        const int Status = 3;
        var project = await _file.ProjectNameAsync(Stop);
        var containers = (await Api.GetContainersAsync(cancellationToken: Stop)).Containers
            .Where(container => container.Project == project)
            .OrderBy(container => container.Service, StringComparer.Ordinal)
            .ThenBy(container => container.Name, StringComparer.Ordinal)
            .ToList();
        if (arguments.Has(Arguments.Quiet) || arguments.Has(Arguments.ServiceNames))
        {
            // Compose's two bare lists, for a script to read: the containers' ids, or the services that have one.
            foreach (var line in arguments.Has(Arguments.Quiet)
                ? containers.Select(container => container.Id)
                : containers.Select(container => container.Service).Distinct(StringComparer.Ordinal))
            {
                Terminal.Line(line);
            }

            return Program.Ok;
        }

        if (Json())
        {
            // A line a container, as Compose prints them.
            foreach (var container in containers)
            {
                Terminal.Line(JsonSerializer.Serialize(new
                {
                    ID = container.Id,
                    container.Name,
                    container.Image,
                    container.Service,
                    container.Project,
                    container.State,
                    container.Status,
                    Ports = string.Join(", ", container.Ports),
                }));
            }

            return Program.Ok;
        }

        Table.Write(
            ["NAME", "IMAGE", "SERVICE", "STATUS", "PORTS"],
            [.. containers.Select(container => new[]
            {
                container.Name,
                container.Image,
                container.Service,
                container.Status.Length > 0 ? container.Status : container.State,
                string.Join(", ", container.Ports),
            })],
            (row, column) => column != Status ? "" : containers[row].IsRunning ? Terminal.Green : Terminal.Gray);
        return Program.Ok;
    }

    /// <summary>
    /// The projects, each with how many of its containers are in each state,
    /// the running ones first: green with all of them running, grey with
    /// none, yellow with some, as the line round a project is on the page.
    /// </summary>
    private async Task<int> LsAsync()
    {
        const int Status = 1;
        var projects = (await Api.GetContainersAsync(cancellationToken: Stop)).Containers
            .Where(container => container.Project.Length > 0)
            .GroupBy(container => container.Project, StringComparer.Ordinal)
            .OrderBy(project => project.Key, StringComparer.Ordinal)
            .ToList();
        if (arguments.Has(Arguments.Quiet))
        {
            foreach (var project in projects)
            {
                Terminal.Line(project.Key);
            }

            return Program.Ok;
        }

        if (Json())
        {
            Terminal.Line(JsonSerializer.Serialize(projects.Select(project => new { Name = project.Key, Status = States(project) })));
            return Program.Ok;
        }

        Table.Write(
            ["NAME", "STATUS"],
            [.. projects.Select(project => new[] { project.Key, States(project) })],
            (row, column) => column != Status ? ""
                : projects[row].All(container => container.IsRunning) ? Terminal.Green
                : projects[row].Any(container => container.IsRunning) ? Terminal.Yellow
                : Terminal.Gray);
        return Program.Ok;
    }

    /// <summary>How many of a project's containers are in each state, the running ones first: <c>running(2), exited(1)</c>.</summary>
    private static string States(IEnumerable<ContainerSummary> project) =>
        string.Join(", ", project
            .GroupBy(container => container.State, StringComparer.Ordinal)
            .OrderByDescending(state => state.Key == "running")
            .ThenBy(state => state.Key, StringComparer.Ordinal)
            .Select(state => $"{state.Key}({state.Count()})"));

    /// <summary>
    /// The list is asked for as JSON. Compose's <c>--format</c> takes a
    /// template too: that is not there, and is said, and the table is
    /// printed.
    /// </summary>
    private bool Json()
    {
        if (arguments.Format is not ("" or "table" or "json"))
        {
            Terminal.Warning($"--format {arguments.Format} is not there yet in wslc-compose {arguments.Command}: table and json are. The table is printed.");
        }

        return arguments.Format == "json";
    }

    private async Task<int> VersionAsync()
    {
        if (arguments.Has(Arguments.Short))
        {
            Terminal.Line(Program.Version);
            return Program.Ok;
        }

        Terminal.Line($"wslc-compose {Program.Version}");
        var health = await Api.GetHealthAsync(Stop);
        Terminal.Line($"WSLC AI Agent {health?.Version ?? "unknown"} at {agent.Address}");
        return Program.Ok;
    }
}
