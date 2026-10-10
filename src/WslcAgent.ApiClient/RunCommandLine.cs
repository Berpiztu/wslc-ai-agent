using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.ApiClient;

/// <summary>What the Fill bar understood from a pasted run line.</summary>
/// <param name="Request">The fields it filled; null when no image was found.</param>
/// <param name="Unsupported">Flags the form has no field for (they are reported, not silently dropped).</param>
/// <param name="Error">Why nothing could be filled.</param>
/// <param name="FromDocker">The line was a <c>docker run</c>, read as the wslc one.</param>
public sealed record RunCommandParse(ContainerLaunchRequest? Request, IReadOnlyList<string> Unsupported, string Error, bool FromDocker = false)
{
    /// <summary>
    /// Flags that ask for what WSLC gives every container already: named so the
    /// user knows they were read, and no reason to flag the line.
    /// </summary>
    public IReadOnlyList<string> NotNeeded { get; init; } = [];

    /// <summary>The status line: what was read, what was filled, what has no field here.</summary>
    public string Summary => Request is null
        ? Error
        : (FromDocker ? "Read as a wslc run. " : "")
          + $"Filled: image {Request.Image}"
          + (Request.Name.Length > 0 ? $", name {Request.Name}" : "")
          + (Request.Publish.Count > 0 ? $", {Request.Publish.Count} port(s)" : "")
          + (Request.Volumes.Count > 0 ? $", {Request.Volumes.Count} volume(s)" : "")
          + (Request.Network.Length > 0 || Request.ConnectNetworks.Count > 0 ? $", {1 + Request.ConnectNetworks.Count} network(s)" : "")
          + (Request.Env.Count > 0 ? $", {Request.Env.Count} env var(s)" : "")
          + (Request.Command.Length > 0 ? ", command" : "")
          + "."
          + (NotNeeded.Count > 0 ? $" Not needed here, WSLC containers reach the host by that name already: {string.Join(", ", NotNeeded)}." : "")
          + (Unsupported.Count > 0 ? $" Not supported here: {string.Join(", ", Unsupported)}." : "");
}

/// <summary>
/// Reads a pasted <c>wslc run …</c> line (or a <c>docker run …</c> one, read as
/// wslc) into the launch fields: value flags
/// to their fields, list flags accumulated, the first bare token is the image
/// and everything after it the command.
/// </summary>
public static partial class RunCommandLine
{
    /// <summary>Switches the form has no field for: named in the status.</summary>
    private static readonly HashSet<string> UnsupportedSwitches = ["-i", "-t", "-it", "-ti", "--interactive", "--tty", "--rm", "-P", "--publish-all", "--privileged"];

    /// <summary>
    /// The names a WSLC container reaches its host by, with no flag (seen in
    /// wslc 3.0.2): docker's <c>--add-host=host.docker.internal:host-gateway</c>
    /// asks for what is already there.
    /// </summary>
    private static readonly HashSet<string> HostNames = new(StringComparer.OrdinalIgnoreCase) { "host.docker.internal", "host.wslc.internal" };

    private const string HostGateway = "host-gateway";

    /// <summary>Every flag the form fills from a value.</summary>
    private static readonly HashSet<string> ValueFlags =
    [
        "--name", "-w", "--workdir", "-m", "--memory", "--cpus", "--restart", "--stop-timeout", "--health-cmd", "--health-interval",
        "--health-timeout", "--health-retries", "--health-start-period", "--ip", "-p", "--publish", "-v", "--volume", "-e", "--env",
        "--network", "--net", "--network-alias", "--entrypoint", "-u", "--user", "--public-name", "--gpus",
        "--label", "-l", "--hostname", "-h", "--domainname", "--dns", "--dns-search", "--dns-option", "--tmpfs", "--shm-size", "--ulimit", "--stop-signal",
    ];

    public static RunCommandParse Parse(string text)
    {
        if (text.Trim().Length == 0)
        {
            return new RunCommandParse(null, [], "Paste a command first.");
        }

        var words = ShellWords.Split(Normalize(text)).ToList();
        var fromDocker = SkipCommandPrefix(words, out var start);

        string image = "", name = "", workdir = "", memory = "", cpus = "", restart = "no", stopTimeout = "", ip = "";
        string healthCmd = "", healthInterval = "", healthTimeout = "", healthRetries = "", healthStartPeriod = "";
        string entrypoint = "", user = "", hostname = "", domainname = "", shmSize = "", stopSignal = "";
        var labels = new List<string>();
        var dns = new List<string>();
        var dnsSearch = new List<string>();
        var dnsOptions = new List<string>();
        var tmpfs = new List<string>();
        var ulimits = new List<string>();
        var publicNames = new List<string>();
        var publish = new List<string>();
        var volumes = new List<string>();
        var env = new List<string>();
        var networks = new List<string>();
        var aliases = new List<string>();
        var unsupported = new List<string>();
        var notNeeded = new List<string>();
        var noHealthcheck = false;
        var gpus = false;
        var command = new List<string>();

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (image.Length > 0)
            {
                command.Add(word);
                continue;
            }

            if (!word.StartsWith('-'))
            {
                image = word;
                continue;
            }

            var (flag, inlineValue) = SplitFlag(word);
            if (UnsupportedSwitches.Contains(flag))
            {
                unsupported.Add(flag);
                continue;
            }

            if (flag is "-d" or "--detach")
            {
                continue;
            }

            if (flag == "--no-healthcheck")
            {
                noHealthcheck = true;
                continue;
            }

            // An unknown flag takes a value only when the next word is not a flag; both are named.
            if (!ValueFlags.Contains(flag))
            {
                var written = inlineValue is null && i + 1 < words.Count && !words[i + 1].StartsWith('-')
                    ? $"{flag} {words[++i]}"
                    : word;
                (IsHostAlreadyNamed(flag, inlineValue ?? written[flag.Length..].Trim()) ? notNeeded : unsupported).Add(written);
                continue;
            }

            string value;
            if (inlineValue is not null)
            {
                value = inlineValue;
            }
            else if (i + 1 < words.Count)
            {
                value = words[++i];
            }
            else
            {
                return new RunCommandParse(null, unsupported, $"{flag} has no value.", fromDocker) { NotNeeded = notNeeded };
            }

            switch (flag)
            {
                case "--name": name = value; break;
                case "-w" or "--workdir": workdir = value; break;
                case "-m" or "--memory": memory = value; break;
                case "--cpus": cpus = value; break;
                // WSLC passes every GPU or none: a device list is named, not taken as all.
                case "--gpus" when value == "all": gpus = true; break;
                case "--gpus": unsupported.Add($"--gpus {value}"); break;
                case "--restart" when RestartPolicyInfo.IsKnown(value): restart = value; break;
                case "--restart": unsupported.Add($"{flag} {value}"); break;
                case "--stop-timeout": stopTimeout = value; break;
                case "--health-cmd": healthCmd = value; break;
                case "--health-interval": healthInterval = value; break;
                case "--health-timeout": healthTimeout = value; break;
                case "--health-retries": healthRetries = value; break;
                case "--health-start-period": healthStartPeriod = value; break;
                case "--ip": ip = value; break;
                case "-p" or "--publish": publish.Add(value); break;
                case "-v" or "--volume": volumes.Add(value); break;
                case "-e" or "--env": env.Add(value); break;
                case "--network" or "--net": networks.Add(value); break;
                case "--network-alias": aliases.Add(value); break;
                case "--entrypoint": entrypoint = value; break;
                case "-u" or "--user": user = value; break;
                case "--public-name": publicNames.Add(value); break;
                case "-l" or "--label": labels.Add(value); break;
                case "-h" or "--hostname": hostname = value; break;
                case "--domainname": domainname = value; break;
                case "--dns": dns.Add(value); break;
                case "--dns-search": dnsSearch.Add(value); break;
                case "--dns-option": dnsOptions.Add(value); break;
                case "--tmpfs": tmpfs.Add(value); break;
                case "--shm-size": shmSize = value; break;
                case "--ulimit": ulimits.Add(value); break;
                case "--stop-signal": stopSignal = value; break;
                default:
                    unsupported.Add($"{flag} {value}");
                    break;
            }
        }

        if (image.Length == 0)
        {
            return new RunCommandParse(null, unsupported, "No image reference found.", fromDocker) { NotNeeded = notNeeded };
        }

        var request = new ContainerLaunchRequest
        {
            // A create line prepares the container and leaves it stopped.
            Start = start,
            Image = image,
            Name = name,
            Command = ShellWords.Join(command),
            Entrypoint = entrypoint,
            Memory = memory,
            Cpus = cpus,
            Publish = publish,
            Volumes = volumes,
            Workdir = workdir,
            Env = env,
            Network = networks.Count > 0 ? networks[0] : "",
            Ip = ip,
            NetworkAliases = aliases,
            ConnectNetworks = networks.Skip(1).ToList(),
            User = user,
            RestartPolicy = restart,
            PublicNames = publicNames,
            StopTimeout = stopTimeout,
            HealthCmd = healthCmd,
            HealthInterval = healthInterval,
            HealthTimeout = healthTimeout,
            HealthRetries = healthRetries,
            HealthStartPeriod = healthStartPeriod,
            NoHealthcheck = noHealthcheck,
            Gpus = gpus,
            Labels = labels,
            Hostname = hostname,
            Domainname = domainname,
            Dns = dns,
            DnsSearch = dnsSearch,
            DnsOptions = dnsOptions,
            Tmpfs = tmpfs,
            ShmSize = shmSize,
            Ulimits = ulimits,
            StopSignal = stopSignal,
        };
        return new RunCommandParse(request, unsupported, "", fromDocker) { NotNeeded = notNeeded };
    }

    /// <summary>
    /// The line that would run this container again, in the flags
    /// <see cref="Parse"/> reads back: copy it, paste it in the Fill bar and the
    /// form comes back as it was. Every field the form has is written — a copy
    /// that carried only the name, the ports and the image recreated a
    /// different container, quietly.
    /// </summary>
    /// <param name="request">The container's launch fields.</param>
    /// <param name="fullVariables">
    /// Also write what only the agent keeps — the restart policy and the public
    /// names — as the pseudo-flags <see cref="Parse"/> reads back (<c>--restart</c>,
    /// <c>--public-name</c>). Off, the line is what <c>wslc</c> itself understands.
    /// </param>
    public static string Write(ContainerLaunchRequest request, bool fullVariables = false)
    {
        var words = new List<string> { "wslc", "run" };
        if (request.Start)
        {
            words.Add("-d");
        }

        void Flag(string flag, string value)
        {
            if (value.Length > 0)
            {
                words.AddRange([flag, value]);
            }
        }

        void Each(string flag, IReadOnlyList<string> values)
        {
            foreach (var value in values.Where(value => value.Length > 0))
            {
                words.AddRange([flag, value]);
            }
        }

        Flag("--name", request.Name);
        Flag("--entrypoint", request.Entrypoint);
        Flag("-m", request.Memory);
        Flag("--cpus", request.Cpus);
        Flag("--gpus", request.Gpus ? "all" : "");
        Each("-p", request.Publish);
        Each("-v", request.Volumes);
        Flag("-w", request.Workdir);
        Each("-e", request.Env);
        Flag("--network", request.Network);
        Each("--network", request.ConnectNetworks);
        Flag("--ip", request.Ip);
        Each("--network-alias", request.NetworkAliases);
        Flag("--hostname", request.Hostname);
        Flag("--domainname", request.Domainname);
        Each("--dns", request.Dns);
        Each("--dns-search", request.DnsSearch);
        Each("--dns-option", request.DnsOptions);
        Each("--tmpfs", request.Tmpfs);
        Flag("--shm-size", request.ShmSize);
        Each("--ulimit", request.Ulimits);
        Each("--label", request.Labels);
        Flag("-u", request.User);
        Flag("--stop-signal", request.StopSignal);

        // The policy and the public names are the agent's own, not CLI flags:
        // written only for another agent, and only a policy that says something
        // is worth writing — "no" is what a line without it means.
        if (fullVariables)
        {
            if (request.RestartPolicy.Length > 0 && request.RestartPolicy != "no")
            {
                words.AddRange(["--restart", request.RestartPolicy]);
            }

            Each("--public-name", request.PublicNames);
        }

        Flag("--stop-timeout", request.StopTimeout);
        if (request.NoHealthcheck)
        {
            words.Add("--no-healthcheck");
        }
        else
        {
            Flag("--health-cmd", request.HealthCmd);
            Flag("--health-interval", request.HealthInterval);
            Flag("--health-timeout", request.HealthTimeout);
            Flag("--health-retries", request.HealthRetries);
            Flag("--health-start-period", request.HealthStartPeriod);
        }

        words.Add(request.Image);
        words.AddRange(ShellWords.Split(request.Command));
        return ShellWords.Join(words);
    }

    /// <summary>Unquoted <c>#</c> comments dropped, <c>\</c> line continuations folded, stray lone backslashes removed (Windows paths keep theirs).</summary>
    private static string Normalize(string text)
    {
        var joined = LineContinuation().Replace(text, " ");
        var lines = joined.Split('\n').Select(line => Comment().Replace(line, ""));
        return string.Join(' ', lines).Replace(" \\ ", " ");
    }

    /// <summary><c>wslc|docker[.exe] [container] run|create</c> is a prefix, not part of the fields; true when it was docker, and false in <paramref name="start"/> when the verb was create.</summary>
    private static bool SkipCommandPrefix(List<string> words, out bool start)
    {
        var fromDocker = false;
        start = true;
        if (words.Count > 0 && words[0].ToLowerInvariant() is "wslc" or "docker" or "wslc.exe" or "docker.exe")
        {
            fromDocker = words[0].StartsWith("docker", StringComparison.OrdinalIgnoreCase);
            words.RemoveAt(0);
        }

        if (words.Count > 0 && words[0] == "container")
        {
            words.RemoveAt(0);
        }

        if (words.Count > 0 && words[0] is "run" or "create")
        {
            start = words[0] == "run";
            words.RemoveAt(0);
        }

        return fromDocker;
    }

    /// <summary><c>--add-host name:host-gateway</c> for a name WSLC answers itself; any other host entry has no equivalent.</summary>
    private static bool IsHostAlreadyNamed(string flag, string value)
    {
        if (flag != "--add-host")
        {
            return false;
        }

        var colon = value.IndexOf(':');
        return colon > 0 && HostNames.Contains(value[..colon]) && value[(colon + 1)..] == HostGateway;
    }

    private static (string Flag, string? Value) SplitFlag(string word)
    {
        // --flag=value and -p=value both appear in the wild.
        var eq = word.IndexOf('=');
        return eq > 1 ? (word[..eq], word[(eq + 1)..]) : (word, null);
    }

    /// <summary>A line broken with <c>\</c> (bash, docker's docs) or with <c>`</c> (PowerShell) is one line.</summary>
    [GeneratedRegex(@"[\\`]\r?\n")]
    private static partial Regex LineContinuation();

    [GeneratedRegex(@"(^|\s)#.*$")]
    private static partial Regex Comment();
}
