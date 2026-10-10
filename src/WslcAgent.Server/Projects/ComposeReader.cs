using System.Globalization;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace WslcAgent.Server.Projects;

/// <summary>What a Compose file is read with: its project's name, the folder it stands in, and what its <c>${NAME}</c> take their values from.</summary>
/// <param name="Name">The name asked for; empty takes the file's own <c>name</c>, then the folder's.</param>
/// <param name="Folder">The folder the file is in, which relative paths start from; empty for a pasted file, which has none.</param>
/// <param name="Variables">The values of <c>${NAME}</c>.</param>
/// <param name="Profiles">The profiles asked for.</param>
/// <param name="ReadFile">A file's text by its full path, null when it is not there: how an <c>env_file</c> is read.</param>
/// <param name="ListFolder">The entries of a folder by its full path, null when it is not a folder there: how a link inside a mounted folder is found. Not given, no folder is looked into.</param>
public sealed record ComposeSource(
    string Name,
    string Folder,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyCollection<string> Profiles,
    Func<string, string?> ReadFile,
    Func<string, IReadOnlyList<HostEntry>?>? ListFolder = null);

/// <summary>An entry of a folder of the agent's machine, as the reader needs it to place a mount.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Link">The real folder it leads to when it is a link (a junction, a symbolic link); empty for what is its own.</param>
public sealed record HostEntry(string Name, string Link);

/// <summary>
/// Reads a Compose file into what the agent already knows how to do: each
/// service as the launch request the Run form sends, the networks and volumes
/// they need, and the list of what the file asks for and cannot be given. It
/// is the Fill bar's reading (<see cref="RunCommandLine"/>) for several
/// containers at once, with the same rule: nothing is dropped in silence.
/// Every key this reader does not take is named, and every line of the file
/// that says something is given its verdict (<see cref="ComposeLine"/>), so
/// the plan can be read beside the file, line by line.
/// </summary>
public static partial class ComposeReader
{
    /// <summary>A key <c>wslc</c> has a flag for and the launch request no field yet, or one the agent will read later as a mount: named apart, since it is a matter of time and not of the runtime.</summary>
    private const string NotYet = " (not yet)";

    private const string NotYetNote = "not yet: the agent does not send it";

    private const string DefaultNetwork = "default";

    /// <summary>The keys that are a matter of time: see <see cref="NotYet"/>.</summary>
    private static readonly HashSet<string> LaterKeys = ["pull_policy", "secrets", "configs"];

    /// <summary>The keys of a service that change nothing in how its container is made.</summary>
    private static readonly HashSet<string> SilentKeys = ["expose"];

    /// <summary>The names a WSLC container reaches its host by with no flag: an <c>extra_hosts</c> entry for one asks for what is already there.</summary>
    private static readonly HashSet<string> HostNames = new(StringComparer.OrdinalIgnoreCase) { "host.docker.internal", "host.wslc.internal" };

    private static readonly char[] HostSeparators = [':', '='];

    /// <summary>The plan of a Compose file; an <see cref="ArgumentException"/> says why a file cannot be read at all.</summary>
    public static ComposePlan Read(string yaml, ComposeSource source, string file = "")
    {
        var reading = new Reading(source);
        if (reading.Load(yaml) is not { } root)
        {
            throw new ArgumentException("The file is not a Compose file: it has no services.");
        }

        return reading.Plan(root, file, yaml);
    }

    /// <summary>
    /// The pairs of a <c>.env</c> or an <c>env_file</c>: <c>KEY=value</c> a
    /// line, comments and blank lines skipped, a leading <c>export</c> and the
    /// quotes round a value taken off.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ParseEnvFile(string text)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            pairs.Add(new KeyValuePair<string, string>(line[..equals].Trim(), EnvValue(line[(equals + 1)..].Trim())));
        }

        return pairs;
    }

    /// <summary>A value without its quotes; unquoted, without the comment after it.</summary>
    private static string EnvValue(string value)
    {
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
        {
            return value[1..^1];
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment < 0 ? value : value[..comment].TrimEnd();
    }

    /// <summary><c>$$</c>, <c>$NAME</c> and <c>${NAME}</c> with its <c>:-</c>, <c>-</c>, <c>:?</c>, <c>?</c>, <c>:+</c> and <c>+</c> forms.</summary>
    [GeneratedRegex(@"\$(?:(?<escaped>\$)|(?<name>[A-Za-z_][A-Za-z0-9_]*)|\{(?<braced>[A-Za-z_][A-Za-z0-9_]*)(?:(?<op>:?[-?+])(?<arg>[^}]*))?\})")]
    private static partial Regex Variable();

    /// <summary>One step of a duration: <c>1m</c>, <c>30s</c>, <c>500ms</c>.</summary>
    [GeneratedRegex(@"(?<number>\d+(?:\.\d+)?)(?<unit>ms|us|µs|ns|s|m|h)")]
    private static partial Regex DurationStep();

    /// <summary>What has been said of one line of the file: the gravest verdict, and every note.</summary>
    private sealed class LineMark
    {
        public string Verdict { get; set; } = "";

        public List<string> Notes { get; } = [];
    }

    /// <summary>The state of one reading: what the file's values are read with, and what was found on the way.</summary>
    private sealed class Reading(ComposeSource source)
    {
        private readonly List<string> _unsupported = [];
        private readonly List<string> _notNeeded = [];
        private readonly List<string> _warnings = [];

        /// <summary>The warnings about the file as a whole: no line of it carries them.</summary>
        private readonly List<string> _notes = [];
        private readonly Dictionary<string, ComposeNetwork> _usedNetworks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ComposeVolume> _usedVolumes = new(StringComparer.Ordinal);

        /// <summary>The line each value of the file is written on, by its path: <c>/services/web/ports/0</c>.</summary>
        private readonly Dictionary<string, int> _lineOf = new(StringComparer.Ordinal);

        private readonly Dictionary<int, LineMark> _marks = [];

        /// <summary>The variables the file asks for, as each was first read: the file's parameters.</summary>
        private readonly Dictionary<string, ComposeVariable> _variables = new(StringComparer.Ordinal);

        /// <summary>The variables a value asked for and nothing sets, with the path of the value.</summary>
        private readonly List<(string Name, string Path)> _unset = [];

        /// <summary>The path of the value being read, for <see cref="Resolve"/> to say where a variable was asked for.</summary>
        private string _reading = "";

        private string _project = "";
        private Dictionary<string, object?> _networks = new();
        private Dictionary<string, object?> _volumes = new();

        /// <summary>The file as plain values (maps, lists, texts with their variables resolved); null when it holds no services.</summary>
        public Dictionary<string, object?>? Load(string yaml)
        {
            var stream = new YamlStream();
            try
            {
                stream.Load(new StringReader(yaml));
            }
            catch (YamlException ex)
            {
                throw new ArgumentException($"The file is not valid YAML: {ex.Message}");
            }

            return stream.Documents.Count > 0 && Value(stream.Documents[0].RootNode, "") is Dictionary<string, object?> root && root.ContainsKey("services")
                ? root
                : null;
        }

        public ComposePlan Plan(Dictionary<string, object?> root, string file, string yaml)
        {
            var written = Text(root.GetValueOrDefault("name"));
            _project = ProjectName(written);
            Mark("/name", written == _project ? ComposeLine.Ok : ComposeLine.Converted, written == _project ? $"project {_project}" : $"→ project {_project}");
            _networks = Map(root.GetValueOrDefault("networks"));
            _volumes = Map(root.GetValueOrDefault("volumes"));
            foreach (var key in root.Keys)
            {
                if (key is "name" or "services" or "networks" or "volumes")
                {
                    continue;
                }

                if (key == "version")
                {
                    Mark("/version", ComposeLine.NotNeeded, "not used any more");
                }
                else if (key.StartsWith("x-", StringComparison.Ordinal))
                {
                    Mark($"/{key}", ComposeLine.NotNeeded, "an extension: read where a service takes it in");
                }
                else
                {
                    var later = LaterKeys.Contains(key);
                    Unsupported($"/{key}", later ? key + NotYet : key, later ? NotYetNote : "");
                }
            }

            var services = new List<ComposeService>();
            foreach (var pair in Map(root.GetValueOrDefault("services")))
            {
                if (pair.Value is Dictionary<string, object?> service)
                {
                    services.Add(Service(pair.Key, service));
                }
                else
                {
                    Warn($"/services/{pair.Key}", $"{pair.Key}: the service has no settings and was left out.", "no settings: left out");
                }
            }

            // Declared and used by no service, they are still the file's: Compose creates what it names.
            foreach (var key in _networks.Keys)
            {
                Network(key);
            }

            foreach (var key in _volumes.Keys)
            {
                Volume(key);
            }

            foreach (var name in _unset.Select(asked => asked.Name).Distinct().Order(StringComparer.Ordinal))
            {
                _warnings.Add($"The variable {name} is not set: read as empty.");
            }

            foreach (var unset in _unset)
            {
                Mark(unset.Path, ComposeLine.Warning, $"{unset.Name} is not set: read as empty");
            }

            var ordered = InStartOrder(services);
            return new ComposePlan(_project, file, ordered, _usedNetworks.Values.ToList(), _usedVolumes.Values.ToList(), _unsupported, _notNeeded, _warnings, Lines(yaml), _notes, _variables.Values.ToList());
        }

        // ---- what is said of the file, line by line

        /// <summary>The file's lines, each with what was said of it; a line nothing was said of carries no verdict.</summary>
        private List<ComposeLine> Lines(string yaml)
        {
            var texts = yaml.Replace("\r\n", "\n").Split('\n').ToList();
            if (texts.Count > 0 && texts[^1].Length == 0)
            {
                texts.RemoveAt(texts.Count - 1);
            }

            var lines = new List<ComposeLine>();
            for (var index = 0; index < texts.Count; index++)
            {
                lines.Add(_marks.TryGetValue(index + 1, out var mark)
                    ? new ComposeLine(index + 1, texts[index], mark.Verdict, string.Join("; ", mark.Notes))
                    : new ComposeLine(index + 1, texts[index], "", ""));
            }

            return lines;
        }

        /// <summary>How grave a verdict is: a line said two things of keeps the graver, and both notes.</summary>
        private static int Rank(string verdict) => verdict switch
        {
            ComposeLine.Unsupported => 5,
            ComposeLine.Warning => 4,
            ComposeLine.Converted => 3,
            ComposeLine.NotNeeded => 2,
            ComposeLine.Ok => 1,
            _ => 0,
        };

        /// <summary>Says something of the line a value is written on; a value the file does not write (a default) has no line, and nothing is said.</summary>
        private void Mark(string path, string verdict, string note = "")
        {
            if (!_lineOf.TryGetValue(path, out var line))
            {
                return;
            }

            if (!_marks.TryGetValue(line, out var mark))
            {
                mark = new LineMark();
                _marks[line] = mark;
            }

            if (Rank(verdict) > Rank(mark.Verdict))
            {
                mark.Verdict = verdict;
            }

            if (note.Length > 0 && !mark.Notes.Contains(note))
            {
                mark.Notes.Add(note);
            }
        }

        /// <summary>The same verdict for every line written under a value: the items of a list, the keys of a map.</summary>
        private void MarkUnder(string path, string verdict)
        {
            var prefix = path + "/";
            foreach (var under in _lineOf.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                Mark(under, verdict);
            }
        }

        private void Converted(string path, string becomes) => Mark(path, ComposeLine.Converted, $"→ {becomes}");

        /// <summary>Asked for and not given: in the list, and on its line and those under it.</summary>
        private void Unsupported(string path, string text, string note = "")
        {
            _unsupported.Add(text);
            Mark(path, ComposeLine.Unsupported, note);
            MarkUnder(path, ComposeLine.Unsupported);
        }

        private void Warn(string path, string text, string note)
        {
            _warnings.Add(text);
            Mark(path, ComposeLine.Warning, note);
        }

        /// <summary>A warning about the file as a whole, which no line carries.</summary>
        private void Note(string text)
        {
            _warnings.Add(text);
            _notes.Add(text);
        }

        /// <summary>The keys of a map the reader takes are fine as written; any other is named, so a key nobody thought of is said and not lost.</summary>
        private void ReadKeys(string path, Dictionary<string, object?> map, string owner, params string[] read)
        {
            foreach (var key in map.Keys)
            {
                if (read.Contains(key))
                {
                    Mark($"{path}/{key}", ComposeLine.Ok);
                    MarkUnder($"{path}/{key}", ComposeLine.Ok);
                }
                else
                {
                    Unsupported($"{path}/{key}", $"{owner}.{key}");
                }
            }
        }

        /// <summary>The path of one entry of a value that may be a list or stand alone: the list's item, or the value itself.</summary>
        private static string ItemPath(string path, object? value, int index) => value is List<object?> ? $"{path}/{index}" : path;

        // ---- the services

        /// <summary>Lower case, and nothing but letters, digits, <c>_</c> and <c>-</c>: what Compose asks of a project's name, since everything is named after it.</summary>
        private string ProjectName(string fromFile)
        {
            var given = source.Name.Trim().Length > 0 ? source.Name
                : fromFile.Length > 0 ? fromFile
                : source.Folder.Length > 0 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(source.Folder))
                : "";
            var name = string.Concat(given.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' ? c : '-')).TrimStart('-', '_');
            return name.Length > 0 ? name : "project";
        }

        private ComposeService Service(string name, Dictionary<string, object?> map)
        {
            var at = $"/services/{name}";
            var taken = new HashSet<string>(StringComparer.Ordinal);
            object? Take(string key)
            {
                taken.Add(key);
                return map.GetValueOrDefault(key);
            }

            var build = Build(name, $"{at}/build", Take("build"));
            var image = Text(Take("image"));
            if (image.Length == 0)
            {
                if (build is null)
                {
                    Warn(at, $"{name}: no image and no build.", "no image and no build");
                }
                else
                {
                    // Compose names an image it builds after the project and the service.
                    image = $"{_project}-{name}";
                }
            }

            var containerName = Text(Take("container_name"));
            var networks = Networks(name, at, Text(Take("network_mode")), Take("networks"));
            var health = Health(name, $"{at}/healthcheck", Take("healthcheck"));
            var limits = Limits(name, at, Text(Take("mem_limit")), Text(Take("cpus")), Text(Take("gpus")), Take("deploy"));
            var profiles = Texts(Take("profiles"));
            var grace = Text(Take("stop_grace_period"));
            var shm = Text(Take("shm_size"));
            var shmSize = MemorySize(shm);
            if (shmSize != shm)
            {
                Converted($"{at}/shm_size", shmSize);
            }

            var stopTimeout = grace.Length > 0 ? ((long)Math.Ceiling(Seconds(grace))).ToString(CultureInfo.InvariantCulture) : "";
            if (stopTimeout.Length > 0 && stopTimeout != grace)
            {
                Converted($"{at}/stop_grace_period", $"{stopTimeout} s");
            }

            var launch = new ContainerLaunchRequest
            {
                Image = image,
                Name = containerName.Length > 0 ? containerName : $"{_project}-{name}-1",
                Command = Words(Take("command")),
                Entrypoint = Words(Take("entrypoint")),
                Memory = limits.Memory,
                Cpus = limits.Cpus,
                Gpus = limits.Gpus,
                Publish = Ports(name, $"{at}/ports", Take("ports")),
                Volumes = Mounts(name, $"{at}/volumes", Take("volumes")),
                Workdir = Text(Take("working_dir")),
                Env = Environment(name, at, Take("env_file"), Take("environment")),
                Network = networks.Network,
                Ip = networks.Ip,
                NetworkAliases = networks.Aliases,
                ConnectNetworks = networks.Connect,
                User = Text(Take("user")),
                RestartPolicy = Restart(name, $"{at}/restart", Text(Take("restart"))),
                StopTimeout = stopTimeout,
                HealthCmd = health.Command,
                HealthInterval = health.Interval,
                HealthTimeout = health.Timeout,
                HealthRetries = health.Retries,
                HealthStartPeriod = health.StartPeriod,
                NoHealthcheck = health.Disabled,
                Labels = Pairs(Take("labels")),
                Hostname = Text(Take("hostname")),
                Domainname = Text(Take("domainname")),
                Dns = Texts(Take("dns")),
                DnsSearch = Texts(Take("dns_search")),
                DnsOptions = Texts(Take("dns_opt")),
                Tmpfs = Texts(Take("tmpfs")),
                ShmSize = shmSize,
                Ulimits = Ulimits(Take("ulimits")),
                StopSignal = Text(Take("stop_signal")),
            };
            var dependsOn = DependsOn(name, $"{at}/depends_on", Take("depends_on"));
            ExtraHosts(name, $"{at}/extra_hosts", Take("extra_hosts"));

            foreach (var key in map.Keys)
            {
                var path = $"{at}/{key}";
                if (taken.Contains(key))
                {
                    // Whatever its reader found graver stands; what is left was taken as written.
                    Mark(path, map[key] is string ? ComposeLine.Ok : "");
                    MarkUnder(path, ComposeLine.Ok);
                }
                else if (SilentKeys.Contains(key))
                {
                    Mark(path, ComposeLine.NotNeeded, "says what the image listens on: nothing to do here");
                    MarkUnder(path, ComposeLine.NotNeeded);
                }
                else if (!key.StartsWith("x-", StringComparison.Ordinal))
                {
                    var later = LaterKeys.Contains(key);
                    Unsupported(path, $"{name}: {key}{(later ? NotYet : "")}", later ? NotYetNote : "");
                }
            }

            var enabled = profiles.Count == 0 || profiles.Any(profile => source.Profiles.Contains(profile));
            if (enabled)
            {
                Mark(at, containerName.Length > 0 ? ComposeLine.Ok : ComposeLine.Converted, containerName.Length > 0 ? $"container {launch.Name}" : $"→ container {launch.Name}");
            }
            else
            {
                Mark(at, ComposeLine.Warning, $"left out: none of its profiles was asked for ({string.Join(", ", profiles)})");
            }

            return new ComposeService(name, launch, dependsOn, build, profiles, enabled);
        }

        /// <summary><c>build: ./app</c>, or its map with the context, the Dockerfile, the target stage and the arguments.</summary>
        private ComposeBuild? Build(string service, string at, object? value)
        {
            if (value is null)
            {
                return null;
            }

            var map = Map(value);
            var context = value is string text ? text : Text(map.GetValueOrDefault("context"));
            ReadKeys(at, map, $"{service}: build", "context", "dockerfile", "target", "args");
            var contextAt = value is string ? at : $"{at}/context";
            var folder = HostPath(service, contextAt, "build", context.Length > 0 ? context : ".");
            if (folder is null)
            {
                return null;
            }

            Converted(contextAt, folder);
            return new ComposeBuild(folder, Text(map.GetValueOrDefault("dockerfile")), Text(map.GetValueOrDefault("target")), Pairs(map.GetValueOrDefault("args")));
        }

        /// <summary><c>env_file</c> first, then <c>environment</c> over it, as Compose does; a name with no value takes the variable of that name, when there is one.</summary>
        private List<string> Environment(string service, string at, object? envFiles, object? environment)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var files = List(envFiles);
            for (var index = 0; index < files.Count; index++)
            {
                var entryAt = ItemPath($"{at}/env_file", envFiles, index);
                var options = files[index] as Dictionary<string, object?>;
                var path = options is null ? Text(files[index]) : Text(options.GetValueOrDefault("path"));
                if (path.Length == 0 || HostPath(service, entryAt, "env_file", path) is not { } full)
                {
                    continue;
                }

                if (source.ReadFile(full) is not { } text)
                {
                    if (options is null || !IsFalse(options.GetValueOrDefault("required")))
                    {
                        Warn(entryAt, $"{service}: env_file {path} is not there.", "the file is not there");
                    }

                    continue;
                }

                var read = ParseEnvFile(text);
                foreach (var pair in read)
                {
                    values[pair.Key] = pair.Value;
                }

                Mark(entryAt, ComposeLine.Ok, $"{read.Count} variable(s) read");
            }

            void Set(string key, string? value, string entryAt)
            {
                if (value is null && !source.Variables.TryGetValue(key, out value))
                {
                    Warn(entryAt, $"{service}: {key} has no value and there is no variable of that name: left out.", "no value, and no variable of that name: left out");
                    return;
                }

                values[key] = value ?? "";
            }

            if (environment is Dictionary<string, object?> map)
            {
                foreach (var pair in map)
                {
                    Set(pair.Key, pair.Value as string, $"{at}/environment/{pair.Key}");
                }
            }
            else
            {
                var entries = List(environment);
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = Text(entries[index]);
                    var equals = entry.IndexOf('=');
                    if (equals > 0)
                    {
                        Set(entry[..equals], entry[(equals + 1)..], $"{at}/environment/{index}");
                    }
                    else if (entry.Length > 0)
                    {
                        Set(entry, null, $"{at}/environment/{index}");
                    }
                }
            }

            return values.Select(pair => $"{pair.Key}={pair.Value}").ToList();
        }

        /// <summary><c>8080:80</c> as written, or the long form's <c>target</c>, <c>published</c>, <c>host_ip</c> and <c>protocol</c> as that one line.</summary>
        private List<string> Ports(string service, string at, object? value)
        {
            var ports = new List<string>();
            var entries = List(value);
            for (var index = 0; index < entries.Count; index++)
            {
                var entryAt = ItemPath(at, value, index);
                if (entries[index] is not Dictionary<string, object?> map)
                {
                    ports.Add(Text(entries[index]));
                    continue;
                }

                ReadKeys(entryAt, map, $"{service}: ports", "target", "published", "host_ip", "protocol", "mode", "name", "app_protocol");
                var target = Text(map.GetValueOrDefault("target"));
                if (target.Length == 0)
                {
                    Warn(entryAt, $"{service}: a port with no target was left out.", "no target: left out");
                    continue;
                }

                var hostIp = Text(map.GetValueOrDefault("host_ip"));
                var published = Text(map.GetValueOrDefault("published"));
                var protocol = Text(map.GetValueOrDefault("protocol"));
                var spec = $"{(hostIp.Length > 0 ? hostIp + ":" : "")}{(published.Length > 0 ? published + ":" : "")}{target}{(protocol.Length > 0 && protocol != "tcp" ? "/" + protocol : "")}";
                ports.Add(spec);
                Converted(entryAt, spec);
            }

            return ports;
        }

        /// <summary>Each mount as <c>source:target[:ro]</c>: a host folder made whole from the file's own, or a named volume under the project's name.</summary>
        private List<string> Mounts(string service, string at, object? value)
        {
            var mounts = new List<Bind>();
            var entries = List(value);
            for (var index = 0; index < entries.Count; index++)
            {
                var entryAt = ItemPath(at, value, index);
                string origin;
                string target;
                bool readOnly;
                if (entries[index] is Dictionary<string, object?> map)
                {
                    var type = Text(map.GetValueOrDefault("type"));
                    if (type is not ("bind" or "volume" or ""))
                    {
                        var later = type == "tmpfs";
                        Unsupported(entryAt, $"{service}: volume of type {type}{(later ? NotYet : "")}", later ? NotYetNote : "");
                        continue;
                    }

                    ReadKeys(entryAt, map, $"{service}: volumes", "type", "source", "target", "read_only", "bind", "volume", "consistency");
                    origin = Text(map.GetValueOrDefault("source"));
                    target = Text(map.GetValueOrDefault("target"));
                    readOnly = IsTrue(map.GetValueOrDefault("read_only"));
                }
                else
                {
                    (origin, target, readOnly) = SplitMount(Text(entries[index]));
                }

                if (origin.Length == 0)
                {
                    Unsupported(entryAt, $"{service}: volume {target} with no source (an anonymous volume){NotYet}", "an anonymous volume: not yet");
                    continue;
                }

                if (MountSource(service, entryAt, origin) is not { } resolved)
                {
                    continue;
                }

                var mount = new Bind(entryAt, resolved, target.TrimEnd('/'), readOnly, Path.IsPathFullyQualified(resolved));
                mounts.Add(mount);
                if (resolved != origin || entries[index] is not string)
                {
                    Converted(entryAt, mount.Spec);
                }
            }

            return Unlinked(service, mounts);
        }

        /// <summary>One mount of a service as it was read: the line it is written on, and whether its source is a folder of this machine.</summary>
        private sealed record Bind(string At, string Source, string Target, bool ReadOnly, bool Host)
        {
            public string Spec => $"{Source}:{Target}{(ReadOnly ? ":ro" : "")}";
        }

        /// <summary>
        /// The mounts as WSLC can make them. It does not follow a Windows link
        /// (a junction, a symbolic link) inside a mounted folder: a mount the
        /// file puts over one fails, since its mount point cannot be opened.
        /// So a link is mounted from its real folder, and a folder that holds
        /// one the file mounts over is not mounted whole but as its entries,
        /// one mount each, down to the link. What that costs is said: an entry
        /// added to that folder later is not in the container until the
        /// project is saved again.
        /// </summary>
        private List<string> Unlinked(string service, List<Bind> binds)
        {
            if (source.ListFolder is null)
            {
                return [.. binds.Select(bind => bind.Spec)];
            }

            var mounts = new List<string>();
            foreach (var bind in binds)
            {
                if (!bind.Host)
                {
                    mounts.Add(bind.Spec);
                    continue;
                }

                var real = bind with { Source = RealFolder(bind.Source) };
                if (binds.FirstOrDefault(other => IsUnder(other.Target, bind.Target) && LinkOnTheWay(real.Source, other.Target[(bind.Target.Length + 1)..])) is { } over)
                {
                    var apart = new List<string>();
                    Apart(real.Source, real.Target, real.ReadOnly, binds, apart);
                    mounts.AddRange(apart);
                    Warn(bind.At,
                        $"{service}: volume {bind.Source} is mounted as its {apart.Count} entries, one mount each, since {over.Target} stands on a link inside it and WSLC does not follow a link. An entry added to that folder later is in the container only once the project is saved again.",
                        $"→ {apart.Count} mounts, an entry each: {over.Target} stands on a link inside it, which WSLC does not follow. A new entry there needs the project saved again");
                    continue;
                }

                mounts.Add(real.Spec);
                if (real.Source != bind.Source)
                {
                    Converted(bind.At, real.Spec);
                }
            }

            return mounts;
        }

        /// <summary>A folder's entries each as a mount of its own, a link from its real folder; one the file mounts itself is left to that mount, and one with a link further in that the file mounts over is taken apart in turn.</summary>
        private void Apart(string folder, string target, bool readOnly, List<Bind> binds, List<string> mounts)
        {
            foreach (var entry in source.ListFolder!(folder) ?? [])
            {
                var at = $"{target}/{entry.Name}";
                if (binds.Any(other => other.Target == at))
                {
                    continue;
                }

                var own = Path.Combine(folder, entry.Name);
                if (entry.Link.Length == 0 && binds.Any(other => IsUnder(other.Target, at) && LinkOnTheWay(own, other.Target[(at.Length + 1)..])))
                {
                    Apart(own, at, readOnly, binds, mounts);
                }
                else
                {
                    mounts.Add($"{(entry.Link.Length > 0 ? entry.Link : own)}:{at}{(readOnly ? ":ro" : "")}");
                }
            }
        }

        private static bool IsUnder(string target, string parent) => target.StartsWith(parent + "/", StringComparison.Ordinal);

        /// <summary>The folder a path really is: where it leads when it is a link, itself otherwise.</summary>
        private string RealFolder(string path)
        {
            var whole = Path.TrimEndingDirectorySeparator(path);
            return Path.GetDirectoryName(whole) is { Length: > 0 } parent && Entry(parent, Path.GetFileName(whole)) is { Link.Length: > 0 } entry ? entry.Link : path;
        }

        /// <summary>Whether a link is met going from a folder down a path inside it, the path's own end included: what stops a mount over that path.</summary>
        private bool LinkOnTheWay(string folder, string inside)
        {
            foreach (var name in inside.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Entry(folder, name) is not { } entry)
                {
                    return false;
                }

                if (entry.Link.Length > 0)
                {
                    return true;
                }

                folder = Path.Combine(folder, name);
            }

            return false;
        }

        private HostEntry? Entry(string folder, string name) =>
            source.ListFolder!(folder)?.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary><c>source:target[:options]</c>; a Windows drive letter is not the separator, and one part alone is the target of an anonymous volume.</summary>
        private static (string Origin, string Target, bool ReadOnly) SplitMount(string spec)
        {
            var start = spec.Length >= 2 && char.IsLetter(spec[0]) && spec[1] == ':' ? 2 : 0;
            var colon = spec.IndexOf(':', start);
            if (colon < 0)
            {
                return ("", spec, false);
            }

            var rest = spec[(colon + 1)..];
            var last = rest.LastIndexOf(':');
            var options = last > 0 && !rest[(last + 1)..].Contains('/') ? rest[(last + 1)..] : "";
            var target = options.Length > 0 ? rest[..last] : rest;
            return (spec[..colon], target, options.Split(',').Contains("ro"));
        }

        /// <summary>A mount's source as <c>wslc</c> takes it; null when it cannot be one here, and it has been said why.</summary>
        private string? MountSource(string service, string at, string origin)
        {
            var isDrive = origin.Length >= 2 && char.IsLetter(origin[0]) && origin[1] == ':';
            if (isDrive || origin.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return origin;
            }

            if (origin.StartsWith('.') || origin.StartsWith('~'))
            {
                return HostPath(service, at, "volume", origin);
            }

            if (!origin.StartsWith('/'))
            {
                return Volume(origin).Name;
            }

            if (origin.EndsWith("docker.sock", StringComparison.Ordinal))
            {
                Unsupported(at, $"{service}: volume {origin} (WSLC has no such socket)", "WSLC has no such socket");
                return null;
            }

            Warn(at, $"{service}: volume {origin} is a Linux path; a bind mount on this machine starts at a Windows folder.", "a Linux path: a bind mount here starts at a Windows folder");
            return origin;
        }

        /// <summary>A path of the file made whole: relative ones start at the file's folder, which a pasted file does not have.</summary>
        private string? HostPath(string service, string at, string what, string path)
        {
            if (path.StartsWith('~'))
            {
                return Path.GetFullPath(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + path[1..]);
            }

            if (Path.IsPathFullyQualified(path))
            {
                return path;
            }

            if (source.Folder.Length == 0)
            {
                Unsupported(at, $"{service}: {what} {path} (a relative path needs the folder the file is in, and a pasted file has none)", "a relative path needs the file's folder, and a pasted file has none");
                return null;
            }

            return Path.GetFullPath(Path.Combine(source.Folder, path));
        }

        /// <summary>The primary network with its address and aliases, and the others to attach after; the service's own name is its first alias, which is how the others reach it.</summary>
        private (string Network, string Ip, List<string> Aliases, List<string> Connect) Networks(string service, string at, string networkMode, object? networks)
        {
            if (networkMode.Length > 0)
            {
                if (networkMode == "bridge")
                {
                    Mark($"{at}/network_mode", ComposeLine.Ok, "the default bridge");
                }
                else
                {
                    Unsupported($"{at}/network_mode", $"{service}: network_mode {networkMode}");
                }

                // The default bridge: no network of the project, and no names on it.
                return ("", "", new List<string>(), new List<string>());
            }

            var wanted = new List<(string Key, Dictionary<string, object?>? Settings, string At)>();
            if (networks is Dictionary<string, object?> map)
            {
                foreach (var pair in map)
                {
                    var settings = pair.Value as Dictionary<string, object?>;
                    wanted.Add((pair.Key, settings, $"{at}/networks/{pair.Key}"));
                    if (settings is not null)
                    {
                        ReadKeys($"{at}/networks/{pair.Key}", settings, $"{service}: networks.{pair.Key}", "aliases", "ipv4_address");
                    }
                }
            }
            else
            {
                var entries = List(networks);
                for (var index = 0; index < entries.Count; index++)
                {
                    if (Text(entries[index]) is { Length: > 0 } key)
                    {
                        wanted.Add((key, null, ItemPath($"{at}/networks", networks, index)));
                    }
                }
            }

            if (wanted.Count == 0)
            {
                wanted.Add((DefaultNetwork, null, ""));
            }

            var names = new List<string>();
            foreach (var entry in wanted)
            {
                var network = Network(entry.Key);
                names.Add(network.Name);
                if (network.Name != entry.Key)
                {
                    Converted(entry.At, $"network {network.Name}");
                }
            }

            var aliases = new List<string> { service };
            aliases.AddRange(Texts(wanted[0].Settings?.GetValueOrDefault("aliases")).Where(alias => alias != service));
            var connect = new List<string>();
            for (var index = 1; index < wanted.Count; index++)
            {
                var settings = wanted[index].Settings;
                if (Texts(settings?.GetValueOrDefault("aliases")).Count > 0)
                {
                    Warn($"{wanted[index].At}/aliases", $"{service}: aliases on {wanted[index].Key} are not set: only the first network takes them yet.", "not set yet: only the first network takes aliases");
                }

                var address = Text(settings?.GetValueOrDefault("ipv4_address"));
                connect.Add(address.Length > 0 ? $"{names[index]} {address}" : names[index]);
            }

            return (names[0], Text(wanted[0].Settings?.GetValueOrDefault("ipv4_address")), aliases, connect);
        }

        /// <summary>A network of the file by its key, as it is named on the machine; one the file does not declare is the project's own, as <c>default</c> is.</summary>
        private ComposeNetwork Network(string key)
        {
            if (_usedNetworks.TryGetValue(key, out var known))
            {
                return known;
            }

            var at = $"/networks/{key}";
            var map = Map(_networks.GetValueOrDefault(key));
            if (!_networks.ContainsKey(key) && key != DefaultNetwork)
            {
                Note($"The network {key} is used and not declared: taken as the project's own.");
            }

            ReadKeys(at, map, $"network {key}", "name", "external", "driver", "internal", "ipam");
            var driver = Text(map.GetValueOrDefault("driver"));
            if (driver.Length > 0 && driver != "bridge")
            {
                Unsupported($"{at}/driver", $"network {key}: driver {driver}");
            }

            var named = Named(key, map);
            Mark(at, named.External || named.Name == key ? ComposeLine.Ok : ComposeLine.Converted, named.External ? $"external: {named.Name} is used as it is" : $"→ network {named.Name}");
            var ipam = List(Map(map.GetValueOrDefault("ipam")).GetValueOrDefault("config")).OfType<Dictionary<string, object?>>().FirstOrDefault() ?? new Dictionary<string, object?>();
            var network = new ComposeNetwork(key, named.Name, Text(ipam.GetValueOrDefault("subnet")), Text(ipam.GetValueOrDefault("gateway")), IsTrue(map.GetValueOrDefault("internal")), named.External);
            _usedNetworks[key] = network;
            return network;
        }

        private ComposeVolume Volume(string key)
        {
            if (_usedVolumes.TryGetValue(key, out var known))
            {
                return known;
            }

            var at = $"/volumes/{key}";
            var map = Map(_volumes.GetValueOrDefault(key));
            if (!_volumes.ContainsKey(key))
            {
                Note($"The volume {key} is used and not declared: taken as the project's own.");
            }

            ReadKeys(at, map, $"volume {key}", "name", "external", "driver", "driver_opts");
            var named = Named(key, map);
            Mark(at, named.External || named.Name == key ? ComposeLine.Ok : ComposeLine.Converted, named.External ? $"external: {named.Name} is used as it is" : $"→ volume {named.Name}");
            var volume = new ComposeVolume(key, named.Name, Text(map.GetValueOrDefault("driver")), Pairs(map.GetValueOrDefault("driver_opts")), named.External);
            _usedVolumes[key] = volume;
            return volume;
        }

        /// <summary>The name on the machine: the file's own <c>name</c>, the key itself for an external one, <c>project_key</c> otherwise.</summary>
        private (string Name, bool External) Named(string key, Dictionary<string, object?> map)
        {
            var external = map.GetValueOrDefault("external");
            var isExternal = external is Dictionary<string, object?> || IsTrue(external);
            var own = Text(map.GetValueOrDefault("name"));
            var given = own.Length > 0 ? own : Text(Map(external).GetValueOrDefault("name"));
            return (given.Length > 0 ? given : isExternal ? key : $"{_project}_{key}", isExternal);
        }

        /// <summary>The health command as <c>--health-cmd</c> takes it, whether it is switched off, and its times.</summary>
        private (string Command, bool Disabled, string Interval, string Timeout, string Retries, string StartPeriod) Health(string service, string at, object? value)
        {
            if (value is not Dictionary<string, object?> map)
            {
                return ("", false, "", "", "", "");
            }

            ReadKeys(at, map, $"{service}: healthcheck", "test", "interval", "timeout", "retries", "start_period", "disable");
            var test = map.GetValueOrDefault("test");
            var words = Texts(test);
            if (IsTrue(map.GetValueOrDefault("disable")) || (test is not string && words.Count > 0 && words[0] == "NONE"))
            {
                return ("", true, "", "", "", "");
            }

            // CMD-SHELL carries one shell line; CMD carries words; a plain text is a shell line already.
            string command;
            if (test is string line)
            {
                command = line;
            }
            else if (words.Count > 0 && words[0] == "CMD-SHELL")
            {
                command = string.Join(' ', words.Skip(1));
            }
            else if (words.Count > 0 && words[0] == "CMD")
            {
                command = ShellWords.Join(words.Skip(1));
            }
            else
            {
                command = ShellWords.Join(words);
            }

            return (command, false, Time("interval"), Time("timeout"), Text(map.GetValueOrDefault("retries")), Time("start_period"));

            string Time(string key)
            {
                var given = Text(map.GetValueOrDefault(key));
                var duration = Duration(given);
                if (duration != given)
                {
                    Converted($"{at}/{key}", duration);
                }

                return duration;
            }
        }

        /// <summary>Memory and CPUs from the service or from <c>deploy.resources.limits</c>, and whether GPUs are asked for.</summary>
        private (string Memory, string Cpus, bool Gpus) Limits(string service, string at, string memLimit, string cpus, string gpus, object? deploy)
        {
            var deployAt = $"{at}/deploy";
            var map = Map(deploy);
            var resources = Map(map.GetValueOrDefault("resources"));
            var limits = Map(resources.GetValueOrDefault("limits"));
            var reservations = Map(resources.GetValueOrDefault("reservations"));
            ReadKeys(deployAt, map, $"{service}: deploy", "resources", "replicas");
            ReadKeys($"{deployAt}/resources", resources, $"{service}: deploy.resources", "limits", "reservations");
            ReadKeys($"{deployAt}/resources/limits", limits, $"{service}: deploy.resources.limits", "memory", "cpus");
            ReadKeys($"{deployAt}/resources/reservations", reservations, $"{service}: deploy.resources.reservations", "devices");

            var replicas = Text(map.GetValueOrDefault("replicas"));
            if (replicas.Length > 0 && replicas != "1")
            {
                Unsupported($"{deployAt}/replicas", $"{service}: deploy.replicas {replicas} (one container a service)", "one container a service");
            }

            var reserved = List(reservations.GetValueOrDefault("devices"))
                .OfType<Dictionary<string, object?>>()
                .Any(device => Texts(device.GetValueOrDefault("capabilities")).Contains("gpu"));
            if (reserved)
            {
                Converted($"{deployAt}/resources/reservations/devices", "every GPU (--gpus all)");
            }
            else if (reservations.ContainsKey("devices"))
            {
                Unsupported($"{deployAt}/resources/reservations/devices", $"{service}: deploy.resources.reservations.devices (only GPUs are passed in)", "only GPUs are passed in");
            }

            if (gpus.Length > 0 && gpus != "all")
            {
                Unsupported($"{at}/gpus", $"{service}: gpus {gpus} (WSLC passes every GPU or none)", "WSLC passes every GPU or none");
            }

            var own = memLimit.Length > 0;
            var given = own ? memLimit : Text(limits.GetValueOrDefault("memory"));
            var memory = MemorySize(given);
            if (memory != given)
            {
                Converted(own ? $"{at}/mem_limit" : $"{deployAt}/resources/limits/memory", memory);
            }

            return (memory, cpus.Length > 0 ? cpus : Text(limits.GetValueOrDefault("cpus")), gpus == "all" || reserved);
        }

        /// <summary><c>nofile: 65535</c>, or <c>nofile: { soft, hard }</c>, as <c>--ulimit</c> takes them: <c>name=soft[:hard]</c>.</summary>
        private static List<string> Ulimits(object? value)
        {
            var limits = new List<string>();
            foreach (var pair in Map(value))
            {
                if (pair.Value is Dictionary<string, object?> both)
                {
                    limits.Add($"{pair.Key}={Text(both.GetValueOrDefault("soft"))}:{Text(both.GetValueOrDefault("hard"))}");
                }
                else if (Text(pair.Value).Length > 0)
                {
                    limits.Add($"{pair.Key}={Text(pair.Value)}");
                }
            }

            return limits;
        }

        /// <summary><c>512M</c>, <c>1gb</c> as the CLI takes them: <c>512m</c>, <c>1g</c>.</summary>
        private static string MemorySize(string size)
        {
            var text = size.Trim().ToLowerInvariant();
            return text.Length > 2 && text[^1] == 'b' && char.IsLetter(text[^2]) ? text[..^1] : text;
        }

        private string Restart(string service, string at, string policy)
        {
            if (policy.Length == 0)
            {
                return RestartPolicyInfo.No;
            }

            if (RestartPolicyInfo.IsKnown(policy))
            {
                // WSLC restarts nothing: the agent does. A policy the file asks for is not taken as
                // written, then, but turned into something of the agent's, and said so.
                if (policy == RestartPolicyInfo.No)
                {
                    Mark(at, ComposeLine.Ok);
                }
                else
                {
                    Converted(at, "restarted by the agent, since WSLC has no restart policy");
                }

                return policy;
            }

            Unsupported(at, $"{service}: restart {policy} (the agent keeps no, unless-stopped and always)", "the agent keeps no, unless-stopped and always");
            return RestartPolicyInfo.No;
        }

        /// <summary>A list of names waits for each to have started; a map says what each is waited for.</summary>
        private List<ComposeDependency> DependsOn(string service, string at, object? value)
        {
            if (value is not Dictionary<string, object?> map)
            {
                return Texts(value).Select(name => new ComposeDependency(name, ComposeDependency.Started)).ToList();
            }

            var dependencies = new List<ComposeDependency>();
            foreach (var pair in map)
            {
                var settings = Map(pair.Value);
                ReadKeys($"{at}/{pair.Key}", settings, $"{service}: depends_on.{pair.Key}", "condition");
                var condition = Text(settings.GetValueOrDefault("condition"));
                dependencies.Add(new ComposeDependency(pair.Key, condition.Length > 0 ? condition : ComposeDependency.Started));
            }

            return dependencies;
        }

        /// <summary>An entry for a name WSLC answers itself is not needed; any other has no flag to go to.</summary>
        private void ExtraHosts(string service, string at, object? value)
        {
            var entries = new List<(string Entry, string At)>();
            if (value is Dictionary<string, object?> map)
            {
                foreach (var pair in map)
                {
                    entries.Add(($"{pair.Key}:{Text(pair.Value)}", $"{at}/{pair.Key}"));
                }
            }
            else
            {
                var items = List(value);
                for (var index = 0; index < items.Count; index++)
                {
                    entries.Add((Text(items[index]), ItemPath(at, value, index)));
                }
            }

            foreach (var (entry, entryAt) in entries)
            {
                var split = entry.IndexOfAny(HostSeparators);
                var host = split < 0 ? entry : entry[..split];
                var address = split < 0 ? "" : entry[(split + 1)..];
                if (HostNames.Contains(host) && address == "host-gateway")
                {
                    _notNeeded.Add($"{service}: extra_hosts {entry}");
                    Mark(entryAt, ComposeLine.NotNeeded, "WSLC answers this name itself");
                }
                else
                {
                    Unsupported(entryAt, $"{service}: extra_hosts {entry}", "wslc has no flag for it");
                }
            }
        }

        /// <summary>Each service after those it depends on, and otherwise as the file has them; a circle of dependencies keeps the file's order and is said.</summary>
        private List<ComposeService> InStartOrder(List<ComposeService> services)
        {
            var names = services.Select(service => service.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var service in services)
            {
                foreach (var missing in service.DependsOn.Where(dependency => !names.Contains(dependency.Service)))
                {
                    Note($"{service.Name}: depends on {missing.Service}, which is not in the file.");
                }
            }

            var ordered = new List<ComposeService>();
            var placed = new HashSet<string>(StringComparer.Ordinal);
            var waiting = services.ToList();
            while (waiting.Count > 0)
            {
                var ready = waiting.Where(service => service.DependsOn.All(dependency => placed.Contains(dependency.Service) || !names.Contains(dependency.Service))).ToList();
                if (ready.Count == 0)
                {
                    Note($"These services depend on each other in a circle and keep the file's order: {string.Join(", ", waiting.Select(service => service.Name))}.");
                    ordered.AddRange(waiting);
                    break;
                }

                ordered.AddRange(ready);
                foreach (var service in ready)
                {
                    placed.Add(service.Name);
                    waiting.Remove(service);
                }
            }

            return ordered;
        }

        /// <summary>A duration as the CLI takes one, a single number and unit: <c>1m30s</c> is <c>90s</c>.</summary>
        private static string Duration(string text)
        {
            if (text.Length == 0)
            {
                return "";
            }

            var milliseconds = (long)Math.Round(Seconds(text) * 1000);
            return milliseconds % 1000 == 0 ? $"{milliseconds / 1000}s" : $"{milliseconds}ms";
        }

        /// <summary>The seconds of <c>1m30s</c>; a bare number is seconds.</summary>
        private static double Seconds(string text)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bare))
            {
                return bare;
            }

            var seconds = 0.0;
            foreach (Match step in DurationStep().Matches(text))
            {
                var unit = step.Groups["unit"].Value switch
                {
                    "h" => 3600.0,
                    "m" => 60.0,
                    "s" => 1.0,
                    "ms" => 0.001,
                    "us" or "µs" => 0.000001,
                    _ => 0.000000001,
                };
                seconds += double.Parse(step.Groups["number"].Value, CultureInfo.InvariantCulture) * unit;
            }

            return seconds;
        }

        // ---- the file's values

        /// <summary>
        /// A YAML node as a plain value: a map, a list, a text with its
        /// variables resolved, or null; and the line it is written on, kept
        /// by its path. A merge key (<c>&lt;&lt;</c>) pours its map in under the
        /// keys written beside it, each poured key keeping the line it was
        /// written on.
        /// </summary>
        private object? Value(YamlNode node, string path)
        {
            if (node is YamlScalarNode scalar)
            {
                var isNull = scalar.Style == ScalarStyle.Plain && (scalar.Value is null || scalar.Value is "" or "~" or "null" or "Null" or "NULL");
                if (isNull)
                {
                    return null;
                }

                _reading = path;
                return Interpolate(scalar.Value ?? "");
            }

            if (node is YamlSequenceNode sequence)
            {
                var items = new List<object?>();
                foreach (var child in sequence.Children)
                {
                    var itemPath = $"{path}/{items.Count}";
                    _lineOf.TryAdd(itemPath, (int)child.Start.Line);
                    items.Add(Value(child, itemPath));
                }

                return items;
            }

            if (node is not YamlMappingNode mapping)
            {
                return null;
            }

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            var poured = new List<YamlNode>();
            foreach (var child in mapping.Children)
            {
                var name = (child.Key as YamlScalarNode)?.Value ?? "";
                if (name != "<<")
                {
                    var keyPath = $"{path}/{name}";
                    _lineOf[keyPath] = (int)child.Key.Start.Line;
                    values[name] = Value(child.Value, keyPath);
                }
                else if (child.Value is YamlSequenceNode many)
                {
                    poured.AddRange(many.Children);
                }
                else
                {
                    poured.Add(child.Value);
                }
            }

            // After the keys written here, so theirs are the values, and the lines, that stand.
            foreach (var merged in poured.OfType<YamlMappingNode>())
            {
                Pour(merged, path, values);
            }

            return values;
        }

        /// <summary>The keys of a merged map that the map itself does not write, each with the line it was written on in the map poured.</summary>
        private void Pour(YamlMappingNode merged, string path, Dictionary<string, object?> values)
        {
            foreach (var child in merged.Children)
            {
                var name = (child.Key as YamlScalarNode)?.Value ?? "";
                if (name == "<<" || values.ContainsKey(name))
                {
                    continue;
                }

                var keyPath = $"{path}/{name}";
                _lineOf[keyPath] = (int)child.Key.Start.Line;
                values[name] = Value(child.Value, keyPath);
            }
        }

        private string Interpolate(string text) => text.Contains('$') ? Variable().Replace(text, Resolve) : text;

        /// <summary>One <c>$</c> of a text: the variable's value, its default, or the stop a required one asks for.</summary>
        private string Resolve(Match match)
        {
            if (match.Groups["escaped"].Success)
            {
                return "$";
            }

            var name = match.Groups["name"].Success ? match.Groups["name"].Value : match.Groups["braced"].Value;
            var set = source.Variables.TryGetValue(name, out var found);
            var value = found ?? "";
            var filled = value.Length > 0;
            var argument = match.Groups["arg"].Value;
            string read;
            switch (match.Groups["op"].Value)
            {
                case ":-":
                    read = filled ? value : argument;
                    break;
                case "-":
                    read = set ? value : argument;
                    break;
                case ":+":
                    read = filled ? argument : "";
                    break;
                case "+":
                    read = set ? argument : "";
                    break;
                case ":?" when !filled:
                case "?" when !set:
                    throw new ArgumentException($"The file needs the variable {name}{(argument.Length > 0 ? $": {argument}" : "")}.");
                default:
                    if (!set)
                    {
                        _unset.Add((name, _reading));
                    }

                    read = value;
                    break;
            }

            // What it had where the file first names it: its own value, or the default given there.
            _variables.TryAdd(name, new ComposeVariable(name, match.Groups["op"].Value is ":+" or "+" ? value : read, set));
            return read;
        }

        private static string Text(object? value) => value as string ?? "";

        private static bool IsTrue(object? value) => Text(value).ToLowerInvariant() is "true" or "yes" or "on";

        private static bool IsFalse(object? value) => Text(value).ToLowerInvariant() is "false" or "no" or "off";

        private static Dictionary<string, object?> Map(object? value) => value as Dictionary<string, object?> ?? new Dictionary<string, object?>();

        /// <summary>A list, or the one value as a list of one.</summary>
        private static List<object?> List(object? value) => value switch
        {
            null => new List<object?>(),
            List<object?> list => list,
            _ => new List<object?> { value },
        };

        private static List<string> Texts(object? value) => List(value).Select(Text).Where(text => text.Length > 0).ToList();

        /// <summary>A command as one line the launch request splits like a shell: a text as written, a list as its words.</summary>
        private static string Words(object? value) => value is string line ? line : ShellWords.Join(Texts(value));

        /// <summary><c>KEY=value</c> lines from a map or a list; a name with no value is left out.</summary>
        private static List<string> Pairs(object? value)
        {
            if (value is Dictionary<string, object?> map)
            {
                return map.Where(pair => pair.Value is string).Select(pair => $"{pair.Key}={pair.Value}").ToList();
            }

            return Texts(value).Where(entry => entry.IndexOf('=') > 0).ToList();
        }
    }
}
