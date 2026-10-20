namespace Berpiztu.Wslc.Compose;

/// <summary>
/// The command line as it was meant: the command, what its options were given
/// and the switches set. It is Compose's own command line, whole: a script
/// written for Compose has its first word changed and runs, so every option
/// Compose has for these commands is taken here, and none of them is an
/// error. What each one gets is one of three things, said per command below:
/// it is done; it is taken and there is nothing to do, since what it asks
/// for is what happens anyway; or it is not there yet, and then the command
/// says so and goes on without it (<see cref="NotThereYet"/>), which a
/// script survives and a refusal would not. An option that is no option of
/// Compose's, or not of this command, is refused, as Compose refuses it.
/// An option is taken wherever it stands, before the command or after it.
/// </summary>
internal sealed class Arguments
{
    public const string Help = "help";
    public const string VersionFlag = "version";
    public const string DryRun = "dry-run";
    public const string NoStart = "no-start";
    public const string RemoveOrphans = "remove-orphans";
    public const string Volumes = "volumes";
    public const string Quiet = "quiet";
    public const string NoCache = "no-cache";
    public const string Pull = "pull";
    public const string ServiceNames = "services";
    public const string ProfileNames = "profiles";
    public const string Images = "images";
    public const string Variables = "variables";
    public const string Short = "short";
    public const string Detach = "detach";
    public const string Follow = "follow";
    public const string Timestamps = "timestamps";
    public const string NoLogPrefix = "no-log-prefix";
    public const string Yes = "yes";

    private const string FileOption = "file";
    private const string ProjectOption = "project-name";
    private const string DirectoryOption = "project-directory";
    private const string ProfileOption = "profile";
    private const string EnvFileOption = "env-file";
    private const string AgentOption = "agent";
    private const string TokenOption = "token";
    private const string AnsiOption = "ansi";
    private const string ProgressOption = "progress";
    private const string FormatOption = "format";
    private const string Parallel = "parallel";
    private const string TailOption = "tail";
    private const string SinceOption = "since";
    private const string Logs = "logs";

    /// <summary>The short ways of writing an option, and the names Compose keeps for an older one.</summary>
    private static readonly Dictionary<string, string> Spellings = new(StringComparer.Ordinal)
    {
        ["-f"] = FileOption,
        ["-p"] = ProjectOption,
        ["--workdir"] = DirectoryOption,
        ["-h"] = Help,
        ["-d"] = Detach,
        ["-n"] = TailOption,
        ["-q"] = Quiet,
        ["-v"] = Volumes,
        ["-t"] = "timeout",
        ["-a"] = "all",
        ["-m"] = "memory",
        ["-o"] = "output",
        ["-w"] = "watch",
        ["-y"] = Yes,
        ["-V"] = "renew-anon-volumes",
    };

    /// <summary>The options that are followed by a value. Two more are, after some commands and not after others (<see cref="TakesValue"/>).</summary>
    private static readonly HashSet<string> Valued = new(StringComparer.Ordinal)
    {
        FileOption, ProjectOption, DirectoryOption, ProfileOption, EnvFileOption, AgentOption, TokenOption, AnsiOption, ProgressOption, FormatOption,
        "timeout", "wait-timeout", "scale", "exit-code-from", "attach", "no-attach", "build-arg", "builder", "memory", "ssh", "provenance", "sbom",
        "rmi", "filter", "status", "output", "hash", TailOption, SinceOption, "until", "index",
    };

    /// <summary>The options of every command: Compose's own, and the two of this one (which agent, and its token).</summary>
    private static readonly Heed Everywhere = new(
        Done: [FileOption, ProjectOption, DirectoryOption, ProfileOption, EnvFileOption, AgentOption, TokenOption, AnsiOption, "no-ansi", ProgressOption, DryRun, Help, VersionFlag],
        Same: ["compatibility", "all-resources", Parallel],
        Later: []);

    /// <summary>The commands there are, each with Compose's options for it and what each one gets.</summary>
    private static readonly Dictionary<string, Heed> Known = new(StringComparer.Ordinal)
    {
        ["config"] = new(
            Done: [Quiet, ServiceNames, ProfileNames, Images, Volumes, Variables],
            Same: [],
            Later: [FormatOption, "output", "hash", "environment", "no-consistency", "no-interpolate", "no-normalize", "no-path-resolution", "resolve-image-digests", "lock-image-digests"]),
        ["build"] = new(
            Done: [NoCache, Pull],
            Same: [Quiet, "with-dependencies", "compress", "force-rm", "no-rm"],
            Later: ["build-arg", "builder", "check", "memory", "print", "push", "ssh", "provenance", "sbom"]),
        ["up"] = new(
            Done: [Detach, NoStart, RemoveOrphans, NoLogPrefix, Timestamps, "no-color", Yes],
            // It always builds what has a build, and what it attaches to is every service it brought up:
            // what these ask for is what happens, or is about a choice this command does not offer.
            Same: ["build", "quiet-pull", "quiet-build", "menu", "attach", "no-attach", "attach-dependencies"],
            Later: ["force-recreate", "no-recreate", "always-recreate-deps", "no-build", "no-deps", Pull, "scale", "timeout", "wait", "wait-timeout",
                "abort-on-container-exit", "abort-on-container-failure", "exit-code-from", "renew-anon-volumes", "watch"]),
        ["down"] = new(Done: [Volumes], Same: [], Later: [RemoveOrphans, "rmi", "timeout"]),
        ["start"] = new(Done: [], Same: [], Later: ["wait", "wait-timeout"]),
        ["stop"] = new(Done: [], Same: [], Later: ["timeout"]),
        ["restart"] = new(Done: [], Same: [], Later: ["timeout", "no-deps"]),
        [Logs] = new(Done: [Follow, TailOption, Timestamps, SinceOption, NoLogPrefix, "no-color"], Same: [], Later: ["until", "index"]),
        ["ps"] = new(Done: [Quiet, ServiceNames, FormatOption], Same: ["all", "no-trunc"], Later: ["filter", "status", "orphans"]),
        ["ls"] = new(Done: [Quiet, FormatOption], Same: ["all"], Later: ["filter"]),
        ["version"] = new(Done: [Short], Same: [], Later: [FormatOption]),
    };

    /// <summary>Compose's commands that the agent has nothing behind yet: named, so the answer is "not yet" and not "unknown".</summary>
    private static readonly string[] NotYet = ["pull", "push", "exec", "run", "kill", "rm", "create", "pause", "unpause", "top", "events", "images", "port", "cp"];

    /// <summary>The commands that take services' names after them, and are then of those services alone.</summary>
    private static readonly string[] OfServices = ["build", "up", "start", "stop", "restart", Logs];

    private readonly Dictionary<string, List<string>> _values;
    private readonly HashSet<string> _switches;
    private readonly List<string> _rest;

    private Arguments(string command, Dictionary<string, List<string>> values, HashSet<string> switches, List<string> rest) =>
        (Command, _values, _switches, _rest) = (command, values, switches, rest);

    /// <summary>The command; empty when none was written.</summary>
    public string Command { get; }

    /// <summary>The Compose file named; empty to look for the standard one.</summary>
    public string File => One(FileOption);

    /// <summary>The project's name; empty to take the file's.</summary>
    public string Project => One(ProjectOption);

    /// <summary>The folder the file's relative paths start from; empty for the file's own.</summary>
    public string Directory => One(DirectoryOption);

    public IReadOnlyList<string> Profiles => _values.GetValueOrDefault(ProfileOption) ?? [];

    /// <summary>The files the variables are read from before the folder's own <c>.env</c>, in the order given: a later one wins.</summary>
    public IReadOnlyList<string> EnvFiles => _values.GetValueOrDefault(EnvFileOption) ?? [];

    /// <summary>The services named after the command: it is of them alone. None, of the whole project.</summary>
    public IReadOnlyList<string> Services => _rest;

    /// <summary>Another agent than this machine's; empty for this machine's.</summary>
    public string Agent => One(AgentOption);

    public string Token => One(TokenOption);

    /// <summary>
    /// Colour on what is printed: <c>auto</c>, <c>always</c> or <c>never</c>;
    /// empty for auto. Never, too, when the older <c>--no-ansi</c> says so or
    /// when the progress asked for is not the one drawn on a terminal
    /// (<c>--progress plain</c>): plain lines are what is printed with no
    /// colour.
    /// </summary>
    public string Ansi =>
        One(AnsiOption) is { Length: > 0 } ansi ? ansi
        : Has("no-ansi") || Has("no-color") || One(ProgressOption) is "plain" or "quiet" or "json" ? "never"
        : "";

    /// <summary>How many lines of each container's log to begin with; 0 for all of them, which is what Compose shows when it is not said (and for <c>all</c>).</summary>
    public int Tail => int.TryParse(One(TailOption), out var lines) && lines > 0 ? lines : 0;

    /// <summary>The moment to read the logs from, as given (<c>--since</c>); empty for none.</summary>
    public string Since => One(SinceOption);

    /// <summary>How a list is printed: <c>table</c> (or nothing) or <c>json</c>.</summary>
    public string Format => One(FormatOption);

    public bool Has(string option) => _switches.Contains(option);

    /// <summary>
    /// The options given that this command does not do yet: Compose has
    /// them, so they are no error, and the command says it goes on without
    /// each one.
    /// </summary>
    public IReadOnlyList<string> NotThereYet =>
        Known.TryGetValue(Command, out var heed) ? [.. Given.Where(option => heed.Later.Contains(option))] : [];

    public static Arguments Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var switches = new HashSet<string>(StringComparer.Ordinal);
        var words = new List<string>();
        for (var index = 0; index < args.Count; index++)
        {
            var (written, inline) = Split(args[index]);
            if (Name(written, words.FirstOrDefault() ?? "") is not { } option)
            {
                words.Add(args[index]);
            }
            else if (TakesValue(option, words.FirstOrDefault() ?? ""))
            {
                var value = inline ?? (index + 1 < args.Count ? args[++index] : throw new UsageException($"{written} needs a value."));
                if (!values.TryGetValue(option, out var given))
                {
                    values[option] = given = [];
                }

                given.Add(value);
            }
            else if (inline is not "false")
            {
                // --flag and --flag=true set it; --flag=false is the flag not given.
                switches.Add(option);
            }
        }

        return new Arguments(words.FirstOrDefault() ?? "", values, switches, words.Skip(1).ToList());
    }

    /// <summary>
    /// The command exists, and was given nothing that is not Compose's for
    /// it. Said before the agent is asked for anything.
    /// </summary>
    public void Check()
    {
        if (!Known.TryGetValue(Command, out var heed))
        {
            throw new UsageException(NotYet.Contains(Command, StringComparer.Ordinal)
                ? $"wslc-compose has no '{Command}' yet."
                : $"Unknown command: {Command}");
        }

        if (Given.FirstOrDefault(option => !Everywhere.Has(option) && !heed.Has(option)) is { } stray)
        {
            throw new UsageException($"'{Command}' does not take --{stray}.");
        }

        if (_rest.Count > 0 && !OfServices.Contains(Command, StringComparer.Ordinal))
        {
            throw new UsageException($"'{Command}' is of the whole project: it takes no service's name ({string.Join(' ', _rest)}).");
        }

        if (_values.GetValueOrDefault(FileOption) is { Count: > 1 })
        {
            throw new UsageException("One Compose file at a time: several are not merged yet.");
        }
    }

    /// <summary>Every option given, by its name.</summary>
    private IEnumerable<string> Given => _switches.Concat(_values.Keys);

    /// <summary>
    /// The option an argument is, by its name; null for what is no option
    /// (the command, a service's name). What begins as an option and is none
    /// of Compose's is refused here: it is a slip of the hand, and going on
    /// without it would do something other than what was asked.
    /// </summary>
    private static string? Name(string written, string command)
    {
        if (written.Length < 2 || !written.StartsWith('-'))
        {
            return null;
        }

        // Two short ones are another option after logs than anywhere else, as Compose has them:
        // -f is the file before the command and follow after logs, -t the timeout of a stop and the timestamps of a log.
        var name = (written, command) switch
        {
            ("-f", Logs) => Follow,
            ("-t", Logs) => Timestamps,
            _ => Spellings.GetValueOrDefault(written) ?? (written.StartsWith("--", StringComparison.Ordinal) ? written[2..] : ""),
        };
        return Everywhere.Has(name) || Known.Values.Any(heed => heed.Has(name)) ? name : throw new UsageException($"Unknown option: {written}");
    }

    /// <summary>
    /// Whether the option is followed by a value. Two of Compose's are after
    /// one command and not after another: <c>--pull</c> is a switch of
    /// <c>build</c> and takes a policy after <c>up</c>, and <c>--parallel</c>
    /// takes a number everywhere but after <c>build</c>, where it is an old
    /// switch.
    /// </summary>
    private static bool TakesValue(string option, string command) => option switch
    {
        Pull => command == "up",
        Parallel => command != "build",
        _ => Valued.Contains(option),
    };

    /// <summary><c>--name=value</c> in its two halves; anything else whole.</summary>
    private static (string Name, string? Value) Split(string arg)
    {
        var at = arg.StartsWith("--", StringComparison.Ordinal) ? arg.IndexOf('=') : -1;
        return at < 0 ? (arg, null) : (arg[..at], arg[(at + 1)..]);
    }

    /// <summary>An option's value; given twice, the last one.</summary>
    private string One(string option) => _values.GetValueOrDefault(option) is { Count: > 0 } given ? given[^1] : "";

    /// <summary>What Compose's options for a command get here.</summary>
    /// <param name="Done">Done as asked.</param>
    /// <param name="Same">Taken with nothing to do: what they ask for is what happens anyway, or is about something this command does not have.</param>
    /// <param name="Later">Not there yet: said, and the command goes on without them.</param>
    private sealed record Heed(string[] Done, string[] Same, string[] Later)
    {
        public bool Has(string option) => Done.Contains(option) || Same.Contains(option) || Later.Contains(option);
    }
}
