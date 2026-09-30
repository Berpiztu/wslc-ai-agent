namespace WslcAgent.Server.Wslc;

/// <summary>
/// What a <c>wslc</c> command line is, for CLI Activity: the group it belongs to
/// (containers, images, networks, volumes, file transfers or general), a title a person reads
/// before the raw line, and the session it ran in.
/// </summary>
public static class CliTraceDescription
{
    public const string Containers = "containers";
    public const string Images = "images";
    public const string Networks = "networks";
    public const string Volumes = "volumes";
    public const string General = "general";

    /// <summary>
    /// A file carried into or out of a container (a group of its own,
    /// to follow a transfer through the log): the copy
    /// itself, whichever way, and the steps around it inside the container,
    /// which work on the names a transfer travels under. A copy made inside a
    /// container — the Files view's paste — is the container's.
    /// </summary>
    public const string Transfers = "transfers";

    /// <summary>The names a transfer travels under: the file inside the container (ContainerFiles) and the folder it is staged in on the host (Staging).</summary>
    private static readonly string[] TransferNames = ["wslc-cp-", "wslc-files-"];

    /// <summary>
    /// What a file the agent carries is written as in CLI Activity and in the
    /// log, where a command is written as <c>wslc</c>: one row from the moment
    /// a client announces it to its end (a file
    /// that started and never finished has to show).
    /// </summary>
    public const string TransferProgram = "transfer";

    /// <summary>The programs a logged line can be the run of: wslc's commands, and the agent's own transfers.</summary>
    public static readonly string[] Programs = ["wslc", TransferProgram];

    /// <summary>The group of a run: a transfer's is always file transfers; a command's is read from its arguments.</summary>
    public static string KindOf(string program, IReadOnlyList<string> args) =>
        program == TransferProgram ? Transfers : Describe(args).Kind;

    private static readonly Dictionary<string, string> NounKind = new(StringComparer.Ordinal)
    {
        ["container"] = Containers, ["containers"] = Containers,
        ["image"] = Images, ["images"] = Images,
        ["network"] = Networks, ["networks"] = Networks,
        ["volume"] = Volumes, ["volumes"] = Volumes,
    };

    private static readonly HashSet<string> ContainerVerbs = new(StringComparer.Ordinal)
    {
        "run", "create", "start", "stop", "restart", "kill", "rm", "remove", "list", "ls", "ps", "stats",
        "logs", "exec", "inspect", "attach", "export", "cp", "commit", "rename", "pause", "unpause",
        "wait", "top", "port", "update", "diff",
    };

    private static readonly HashSet<string> ImageVerbs = new(StringComparer.Ordinal)
    {
        "pull", "push", "build", "tag", "rmi", "load", "save", "import", "history", "images",
    };

    private static readonly Dictionary<string, string> VerbTitle = new(StringComparer.Ordinal)
    {
        ["ls"] = "List", ["list"] = "List", ["ps"] = "List", ["rm"] = "Remove", ["rmi"] = "Remove",
        ["remove"] = "Remove", ["run"] = "Run", ["create"] = "Create", ["start"] = "Start", ["stop"] = "Stop",
        ["restart"] = "Restart", ["kill"] = "Kill", ["stats"] = "Stats", ["logs"] = "Logs", ["exec"] = "Exec",
        ["inspect"] = "Inspect", ["attach"] = "Attach", ["export"] = "Export", ["cp"] = "Copy",
        ["commit"] = "Commit", ["rename"] = "Rename", ["pause"] = "Pause", ["unpause"] = "Unpause",
        ["wait"] = "Wait", ["top"] = "Top", ["port"] = "Port", ["update"] = "Update", ["diff"] = "Diff",
        ["pull"] = "Pull", ["push"] = "Push", ["build"] = "Build", ["tag"] = "Tag", ["load"] = "Load",
        ["save"] = "Save", ["import"] = "Import", ["history"] = "History", ["connect"] = "Connect",
        ["disconnect"] = "Disconnect", ["prune"] = "Prune", ["version"] = "Tool version",
    };

    /// <summary>Flags that take no value, so the word after them is the command's target.</summary>
    private static readonly HashSet<string> BooleanFlags = new(StringComparer.Ordinal)
    {
        "--all", "-a", "--force", "--quiet", "-q", "--detach", "-d", "--rm", "--no-trunc", "--no-cache",
        "--internal", "--latest", "--no-healthcheck", "--help", "-h", "--version", "--fixed", "--tty", "-t",
        "--interactive", "-i", "--privileged", "--no-color", "--insecure",
    };

    private static readonly Dictionary<string, string> KindNoun = new(StringComparer.Ordinal)
    {
        [Containers] = "containers", [Images] = "images", [Networks] = "networks", [Volumes] = "volumes",
    };

    /// <summary>The kind, title and session of one <c>wslc</c> argument list.</summary>
    public static (string Kind, string Title, string Session) Describe(IReadOnlyList<string> args)
    {
        var tokens = args.Where(part => part.Length > 0).ToList();
        var (session, command) = SplitGlobalFlags(tokens);
        var (kind, title) = Classify(command);
        return (IsTransfer(command) ? Transfers : kind, title, session);
    }

    /// <summary>A copy between the host and a container, either way, or a step on the names a transfer travels under.</summary>
    private static bool IsTransfer(List<string> command)
    {
        var verbs = command.Where(token => !token.StartsWith('-')).Take(2).Select(token => token.ToLowerInvariant()).ToList();
        var copies = verbs is ["cp", ..] or ["container" or "containers", "cp"];
        return copies || command.Any(token => TransferNames.Any(name => token.Contains(name, StringComparison.Ordinal)));
    }

    /// <summary>The group and the title of a command with its global flags taken off.</summary>
    private static (string Kind, string Title) Classify(List<string> command)
    {
        if (command.Count == 0)
        {
            return (General, "wslc");
        }

        var first = command[0].ToLowerInvariant();
        var rest = command.Skip(1).ToList();
        if (NounKind.TryGetValue(first, out var kind))
        {
            var verb = rest.Count > 0 && !rest[0].StartsWith('-') ? rest[0].ToLowerInvariant() : "";
            var after = verb.Length > 0 ? rest.Skip(1).ToList() : rest;
            return (kind, Title(kind, verb, after, command));
        }

        if (ContainerVerbs.Contains(first))
        {
            return (Containers, Title(Containers, first, rest, command));
        }

        if (ImageVerbs.Contains(first))
        {
            return (Images, Title(Images, first, rest, command));
        }

        if (first == "system")
        {
            return (General, SystemTitle(rest));
        }

        if (first is "--help" or "-h" or "help")
        {
            return (General, "Help");
        }

        if (first is "--version" or "version")
        {
            return (General, "Tool version");
        }

        var pretty = VerbTitle.GetValueOrDefault(first, Capitalize(first.Replace('-', ' ')));
        var target = FirstTarget(rest);
        var title = target.Length > 0 ? $"{pretty} {target}".Trim() : pretty;
        return (General, title.Length > 0 ? title : "wslc");
    }

    /// <summary>
    /// Lifts the session out of the global flags and drops the output format,
    /// which says nothing about what the command does; the rest is the command.
    /// </summary>
    private static (string Session, List<string> Command) SplitGlobalFlags(List<string> tokens)
    {
        var session = "";
        var command = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.StartsWith("--session=", StringComparison.Ordinal))
            {
                session = token["--session=".Length..];
                continue;
            }

            if (token == "--session" && index + 1 < tokens.Count)
            {
                session = tokens[++index];
                continue;
            }

            if (token.StartsWith("--format=", StringComparison.Ordinal))
            {
                continue;
            }

            if (token == "--format" && index + 1 < tokens.Count)
            {
                index++;
                continue;
            }

            command.Add(token);
        }

        return (session, command);
    }

    /// <summary>The first word that is not a flag or a flag's value: what the command acts on.</summary>
    private static string FirstTarget(List<string> tokens)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.StartsWith("--", StringComparison.Ordinal) && token.Contains('='))
            {
                continue;
            }

            if (BooleanFlags.Contains(token) || (token.StartsWith('-') && !token.StartsWith("--", StringComparison.Ordinal)))
            {
                continue;
            }

            if (token.StartsWith('-') && index + 1 < tokens.Count && !tokens[index + 1].StartsWith('-'))
            {
                index++;
                continue;
            }

            if (token.StartsWith('-'))
            {
                continue;
            }

            return DisplayTarget(token);
        }

        return "";
    }

    /// <summary>A 64-hex id is shown by its first twelve characters, as everywhere else.</summary>
    private static string DisplayTarget(string token)
    {
        var cleaned = token.Trim();
        return cleaned.Length >= 32 && cleaned.All(Uri.IsHexDigit) ? cleaned[..12] : cleaned;
    }

    private static string Title(string kind, string verb, List<string> after, List<string> rawCommand)
    {
        var target = FirstTarget(after);
        var hasAll = rawCommand.Any(token => token is "--all" or "-a");
        var action = VerbTitle.GetValueOrDefault(verb, verb.Length > 0 ? Capitalize(verb.Replace('-', ' ')) : "");
        var noun = KindNoun.GetValueOrDefault(kind, "");
        if (verb is "ls" or "list" or "ps" or "images")
        {
            return noun.Length > 0 ? $"List {noun}" : "List";
        }

        if (verb == "prune")
        {
            return noun.Length > 0 ? $"Prune {noun}" : "Prune";
        }

        if (verb == "stats")
        {
            if (hasAll)
            {
                return "Stats (all containers)";
            }

            return target.Length > 0 ? $"Stats {target}".Trim() : "Container stats";
        }

        if (verb == "logs" && target.Length == 0)
        {
            return "Container logs";
        }

        if (verb.Length == 0)
        {
            return noun.Length > 0 ? $"List {noun}" : "wslc";
        }

        if (target.Length > 0)
        {
            if (verb == "create" && kind is Volumes or Networks)
            {
                var singular = noun.EndsWith('s') ? noun[..^1] : noun;
                return $"Create {singular} {target}";
            }

            return $"{action} {target}".Trim();
        }

        if (kind == Containers && action.Length > 0)
        {
            return $"Container {action.ToLowerInvariant()}";
        }

        if (kind == Images && action.Length > 0)
        {
            return $"Image {action.ToLowerInvariant()}";
        }

        if (noun.Length > 0 && action.Length > 0)
        {
            return $"{action} {noun}";
        }

        return action.Length > 0 ? action : noun.Length > 0 ? Capitalize(noun) : "wslc";
    }

    private static string SystemTitle(List<string> rest)
    {
        if (rest.Count == 0)
        {
            return "System";
        }

        var head = rest[0].ToLowerInvariant();
        if (head == "session")
        {
            var sub = rest.Count > 1 && !rest[1].StartsWith('-') ? rest[1].ToLowerInvariant() : "";
            var target = FirstTarget(rest.Skip(sub.Length > 0 ? 2 : 1).ToList());
            var action = VerbTitle.GetValueOrDefault(sub, sub.Length > 0 ? Capitalize(sub.Replace('-', ' ')) : "Session");
            if (sub is "ls" or "list")
            {
                return "List sessions";
            }

            if (sub == "enter")
            {
                return target.Length > 0 ? $"Enter session {target}".Trim() : "Enter session";
            }

            if (sub is "terminate" or "stop")
            {
                return target.Length > 0 ? $"Stop session {target}".Trim() : "Stop session";
            }

            if (sub == "start")
            {
                return target.Length > 0 ? $"Start session {target}".Trim() : "Start session";
            }

            if (target.Length > 0)
            {
                return $"{action} {target}";
            }

            return action != "Session" ? action : "Sessions";
        }

        switch (head)
        {
            case "df":
                return "Disk usage";
            case "info":
                return "System info";
            case "version":
                return "Tool version";
        }

        var headAction = VerbTitle.GetValueOrDefault(head, Capitalize(head.Replace('-', ' ')));
        var headTarget = FirstTarget(rest.Skip(1).ToList());
        return headTarget.Length > 0 ? $"{headAction} {headTarget}".Trim() : headAction;
    }

    /// <summary>Python's <c>str.capitalize</c>: the first letter upper, the rest lower.</summary>
    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();
}
