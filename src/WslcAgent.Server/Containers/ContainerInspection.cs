using System.Globalization;
using System.Text.Json;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// What the details view and the recreate flow read from
/// <c>wslc container inspect</c>: the header fields, the mounts and the
/// launch fields the container was created with, in one place for both.
/// </summary>
public sealed record ContainerInspection(
    string Id,
    string Name,
    string Image,
    string State,
    bool IsRunning,
    IReadOnlyList<string> Ports,
    IReadOnlyList<ContainerInspection.PortBinding> Binds,
    string Created,
    IReadOnlyList<MountInfo> Mounts,
    ContainerLaunchRequest Form,
    string Json)
{
    private static readonly string[] BuiltInStateNames = ["unknown", "created", "running", "exited", "deleted"];

    /// <summary>The first object of the CLI's inspect output (it prints a list).</summary>
    public static ContainerInspection Parse(WslcResult result)
    {
        var root = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new WslcException("container inspect printed no object", result);
        }

        var config = Child(root, "Config");
        var hostConfig = Child(root, "HostConfig");
        var networkSettings = Child(root, "NetworkSettings");
        var state = Child(root, "State");

        var id = root.GetString("Id");
        var name = root.GetString("Name").TrimStart('/');
        var image = ImageRef(root, config);
        var stateName = StateName(state, root);
        var ports = PortBindings(hostConfig, networkSettings, root);
        var mounts = MountsOf(root, hostConfig);
        var network = hostConfig.GetString("NetworkMode") is "default" or "" ? "" : hostConfig.GetString("NetworkMode");
        var form = ConfigFields(image, config) with
        {
            Name = name,
            Memory = BytesLimit(Prop(hostConfig, "Memory", out var mem) && mem.ValueKind == JsonValueKind.Number ? mem.GetInt64() : 0),
            Cpus = Cpus(hostConfig),
            Publish = ports.Select(PublishSpec).ToList(),
            Volumes = mounts.Where(m => m.Source.Length > 0).Select(m => $"{m.Source}:{m.Destination}{(m.Mode == "ro" ? ":ro" : "")}").ToList(),
            Network = network,
            Ip = StaticIp(networkSettings, network),
            NetworkAliases = Aliases(networkSettings, id, name, config.GetString("Hostname")),
            ConnectNetworks = ExtraNetworks(networkSettings, network),
            StopTimeout = FirstNumber(config, hostConfig, "StopTimeout"),
            Start = stateName == "running",
        };

        return new ContainerInspection(
            Id: id.Length > 12 ? id[..12] : id,
            Name: name,
            Image: image,
            State: stateName,
            IsRunning: stateName == "running",
            Ports: ports.Select(p => PortText.Display(p.HostPort, p.Container)).ToList(),
            Binds: ports,
            Created: root.GetString("Created"),
            Mounts: mounts,
            Form: form,
            Json: JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// What an image sets for every container made from it, read from the first
    /// object of <c>wslc image inspect</c>: its <c>Config</c> is the section a
    /// container's own carries, with what the user asked for on top.
    /// </summary>
    public static ContainerLaunchRequest ImageFields(string image, WslcResult result) =>
        ConfigFields(image, Child(WslcJson.ParseRows(result.Stdout).FirstOrDefault(), "Config"));

    /// <summary>The launch fields a <c>Config</c> section holds, an image's or a container's: variables, command, entrypoint, working folder, user and health check.</summary>
    private static ContainerLaunchRequest ConfigFields(string image, JsonElement config) => new()
    {
        Image = image,
        Command = CommandOf(config),
        Entrypoint = Words(config, "Entrypoint"),
        Workdir = config.GetString("WorkingDir"),
        Env = Strings(config, "Env"),
        User = config.GetString("User"),
        HealthCmd = HealthCommand(config, out var noHealthcheck),
        HealthInterval = Duration(config, "Interval"),
        HealthTimeout = Duration(config, "Timeout"),
        HealthStartPeriod = Duration(config, "StartPeriod"),
        HealthRetries = HealthRetries(config),
        NoHealthcheck = noHealthcheck,
    };

    /// <summary>A property of an object, or false: inspect leaves whole sections null (<c>Healthcheck</c>, <c>PortBindings</c>) and undefined elements throw on lookup.</summary>
    private static bool Prop(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        Prop(parent, name, out var child) && child.ValueKind == JsonValueKind.Object ? child : default;

    /// <summary><c>Config.Image</c> first (the reference the user typed); a digest only as a last resort.</summary>
    private static string ImageRef(JsonElement root, JsonElement config)
    {
        foreach (var candidate in new[] { config.GetString("Image"), config.GetString("ImageID"), root.GetString("Image") })
        {
            if (candidate.Length > 0 && !candidate.StartsWith("sha256:", StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return root.GetString("Image");
    }

    private static string StateName(JsonElement state, JsonElement root)
    {
        var status = state.GetString("Status");
        if (status.Length > 0)
        {
            return status.ToLowerInvariant();
        }

        if (Prop(root, "State", out var raw) && raw.ValueKind == JsonValueKind.Number && raw.TryGetInt32(out var code))
        {
            return code >= 0 && code < BuiltInStateNames.Length ? BuiltInStateNames[code] : "unknown";
        }

        return root.GetString("State").Length > 0 ? root.GetString("State").ToLowerInvariant() : "unknown";
    }

    /// <summary>One binding: the bind address when there is one (empty for every address), the host port, the container port as written (<c>80/tcp</c>).</summary>
    public sealed record PortBinding(string HostIp, string HostPort, string Container);

    /// <summary>
    /// The root <c>Ports</c> map (what WSLC 2.9.x fills), then <c>HostConfig.PortBindings</c>
    /// (what was asked) and <c>NetworkSettings.Ports</c> (what is live); the
    /// <c>com.microsoft.wsl.container.metadata</c> label is the last resort.
    /// </summary>
    private static List<PortBinding> PortBindings(JsonElement hostConfig, JsonElement networkSettings, JsonElement root)
    {
        var bindings = new List<PortBinding>();
        foreach (var source in new[] { Child(root, "Ports"), Child(hostConfig, "PortBindings"), Child(networkSettings, "Ports") })
        {
            if (source.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var port in source.EnumerateObject())
            {
                if (port.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var bind in port.Value.EnumerateArray())
                {
                    var hostIp = bind.GetString("HostIp");
                    bindings.Add(new PortBinding(hostIp is "0.0.0.0" or "::" ? "" : hostIp, bind.GetString("HostPort"), port.Name));
                }
            }

            if (bindings.Count > 0)
            {
                return bindings;
            }
        }

        var labels = Child(root, "Labels");
        var metadata = labels.ValueKind == JsonValueKind.Object
            ? string.Join(',', labels.EnumerateObject().Select(l => $"{l.Name}={l.Value.GetString()}"))
            : "";
        foreach (var rendered in ContainerService.ParsePorts("", metadata))
        {
            var arrow = rendered.IndexOf("->", StringComparison.Ordinal);
            if (arrow > 0)
            {
                bindings.Add(new PortBinding("", rendered[..arrow], rendered[(arrow + 2)..]));
            }
        }

        return bindings;
    }

    /// <summary><c>[ip:]hostPort:containerPort</c> as <c>--publish</c> takes it.</summary>
    private static string PublishSpec(PortBinding binding) =>
        $"{(binding.HostIp.Length > 0 ? binding.HostIp + ":" : "")}{binding.HostPort}:{binding.Container.Split('/')[0]}";

    private static List<MountInfo> MountsOf(JsonElement root, JsonElement hostConfig)
    {
        var mounts = new List<MountInfo>();
        if (Prop(root, "Mounts", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var mount in list.EnumerateArray())
            {
                var type = mount.GetString("Type").ToLowerInvariant();
                var source = type == "volume" ? mount.GetString("Name") : FirstNonEmpty(mount.GetString("Source"), mount.GetString("SourcePath"), mount.GetString("HostPath"));
                var destination = FirstNonEmpty(mount.GetString("Destination"), mount.GetString("Target"), mount.GetString("ContainerPath"));
                var readOnly = Prop(mount, "RW", out var rw) && rw.ValueKind == JsonValueKind.False
                    || Prop(mount, "ReadWrite", out var rwr) && rwr.ValueKind == JsonValueKind.False
                    || mount.GetString("Mode") == "ro";
                mounts.Add(new MountInfo(type.Length == 0 ? "bind" : type, source, destination, readOnly ? "ro" : "rw"));
            }
        }

        if (mounts.Count == 0)
        {
            foreach (var bind in Strings(hostConfig, "Binds"))
            {
                var target = LaunchArgs.MountTarget(bind);
                var source = bind[..Math.Max(0, bind.LastIndexOf(':' + target, StringComparison.Ordinal))];
                mounts.Add(new MountInfo("bind", source, target, bind.EndsWith(":ro", StringComparison.Ordinal) ? "ro" : "rw"));
            }
        }

        return mounts;
    }

    /// <summary><c>Config.Cmd</c> joined; a Dockerfile-style <c>ENTRYPOINT</c> first word means nothing usable.</summary>
    private static string CommandOf(JsonElement config)
    {
        var words = Strings(config, "Cmd");
        return words.Count > 0 && words[0].Equals("ENTRYPOINT", StringComparison.OrdinalIgnoreCase) ? "" : ShellWords.Join(words);
    }

    private static string Words(JsonElement config, string name) => ShellWords.Join(Strings(config, name));

    private static List<string> Strings(JsonElement element, string name)
    {
        if (!Prop(element, name, out var value))
        {
            return [];
        }

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").ToList(),
            JsonValueKind.String => [value.GetString() ?? ""],
            _ => [],
        };
    }

    private static string BytesLimit(long bytes)
    {
        if (bytes <= 0)
        {
            return "";
        }

        const long Gi = 1024L * 1024 * 1024;
        const long Mi = 1024L * 1024;
        return bytes % Gi == 0 ? $"{bytes / Gi}g" : bytes % Mi == 0 ? $"{bytes / Mi}m" : bytes.ToString(CultureInfo.InvariantCulture);
    }

    private static string Cpus(JsonElement hostConfig)
    {
        if (!Prop(hostConfig, "NanoCpus", out var nano) || nano.ValueKind != JsonValueKind.Number || nano.GetInt64() <= 0)
        {
            return "";
        }

        return (nano.GetInt64() / 1e9).ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Only an address the user asked for (<c>IPAMConfig</c>), never the assigned one, and never on built-in networks.</summary>
    private static string StaticIp(JsonElement networkSettings, string network)
    {
        if (!LaunchArgs.AllowsStaticIp(network))
        {
            return "";
        }

        var entry = Child(Child(networkSettings, "Networks"), network);
        return Child(entry, "IPAMConfig").GetString("IPv4Address");
    }

    /// <summary>Aliases the user set: the automatic ones (id, short id, name, hostname) are dropped.</summary>
    private static List<string> Aliases(JsonElement networkSettings, string id, string name, string hostname)
    {
        var automatic = new HashSet<string>(StringComparer.Ordinal) { id, id.Length > 12 ? id[..12] : id, name, hostname };
        var aliases = new List<string>();
        var networks = Child(networkSettings, "Networks");
        if (networks.ValueKind != JsonValueKind.Object)
        {
            return aliases;
        }

        foreach (var network in networks.EnumerateObject())
        {
            aliases.AddRange(Strings(network.Value, "Aliases").Where(a => a.Length > 0 && !automatic.Contains(a)));
        }

        return aliases.Distinct().ToList();
    }

    /// <summary>Every attached network but the primary one, with its requested static ip when any.</summary>
    private static List<string> ExtraNetworks(JsonElement networkSettings, string primary)
    {
        var extra = new List<string>();
        var networks = Child(networkSettings, "Networks");
        if (networks.ValueKind != JsonValueKind.Object)
        {
            return extra;
        }

        var primaryName = primary.Length == 0 ? "bridge" : primary;
        foreach (var network in networks.EnumerateObject())
        {
            if (network.Name == primaryName)
            {
                continue;
            }

            var ip = LaunchArgs.AllowsStaticIp(network.Name) ? Child(network.Value, "IPAMConfig").GetString("IPv4Address") : "";
            extra.Add(ip.Length > 0 ? $"{network.Name} {ip}" : network.Name);
        }

        return extra;
    }

    private static string FirstNumber(JsonElement config, JsonElement hostConfig, string name)
    {
        foreach (var element in new[] { config, hostConfig })
        {
            if (Prop(element, name, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt64().ToString(CultureInfo.InvariantCulture);
            }
        }

        return "";
    }

    /// <summary><c>Healthcheck.Test</c>: <c>["NONE"]</c> means disabled; <c>CMD</c>/<c>CMD-SHELL</c> carry the command after them.</summary>
    private static string HealthCommand(JsonElement config, out bool disabled)
    {
        disabled = false;
        var test = Strings(Child(config, "Healthcheck"), "Test");
        if (test.Count == 0)
        {
            return "";
        }

        if (test[0].Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            disabled = true;
            return "";
        }

        // CMD-SHELL carries one shell string; CMD carries words.
        return test[0] switch
        {
            "CMD-SHELL" => string.Join(' ', test.Skip(1)),
            "CMD" => ShellWords.Join(test.Skip(1)),
            _ => ShellWords.Join(test),
        };
    }

    /// <summary>Nanoseconds to <c>Ns</c> or <c>Nms</c>, as the CLI takes them.</summary>
    private static string Duration(JsonElement config, string name)
    {
        var health = Child(config, "Healthcheck");
        if (!Prop(health, name, out var value) || value.ValueKind != JsonValueKind.Number || value.GetInt64() <= 0)
        {
            return "";
        }

        var ns = value.GetInt64();
        return ns % 1_000_000_000 == 0 ? $"{ns / 1_000_000_000}s" : $"{ns / 1_000_000}ms";
    }

    private static string HealthRetries(JsonElement config)
    {
        var health = Child(config, "Healthcheck");
        return Prop(health, "Retries", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt64() > 0
            ? value.GetInt64().ToString(CultureInfo.InvariantCulture)
            : "";
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => v.Length > 0) ?? "";
}
