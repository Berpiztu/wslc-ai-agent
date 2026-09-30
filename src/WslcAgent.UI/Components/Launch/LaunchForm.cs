using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components.Launch;

/// <summary>Which of its three surfaces the launch form is: Run, Create, or View &amp; edit (recreate).</summary>
public enum LaunchMode
{
    Run,
    Create,
    Edit,
}

/// <summary>One Volumes row: a host folder (bind) or a managed volume, its target, read-only.</summary>
public sealed class MountRow
{
    public const string Bind = "bind";
    public const string Volume = "volume";

    public string Mode { get; set; } = Bind;

    public string Source { get; set; } = "";

    public string Target { get; set; } = "";

    public bool ReadOnly { get; set; }

    /// <summary><c>source:target[:ro]</c>; an empty target is <c>/workspace</c>.</summary>
    public string Spec => Source.Trim().Length == 0 ? "" : $"{Source.Trim()}:{(Target.Trim().Length == 0 ? "/workspace" : Target.Trim())}{(ReadOnly ? ":ro" : "")}";

    /// <summary>The reverse of <see cref="Spec"/>; a Windows drive letter is not the separator.</summary>
    public static MountRow Parse(string spec)
    {
        var start = spec.Length >= 2 && char.IsLetter(spec[0]) && spec[1] == ':' ? 2 : 0;
        var colon = spec.IndexOf(':', start);
        var source = colon < 0 ? spec : spec[..colon];
        var rest = colon < 0 ? "" : spec[(colon + 1)..];
        var readOnly = rest.EndsWith(":ro", StringComparison.Ordinal);
        var target = readOnly || rest.EndsWith(":rw", StringComparison.Ordinal) ? rest[..^3] : rest;
        var isPath = start == 2 || source.StartsWith('/') || source.StartsWith('\\') || source.StartsWith('~') || source.StartsWith('.');
        return new MountRow { Mode = isPath ? Bind : Volume, Source = source, Target = target, ReadOnly = readOnly };
    }
}

/// <summary>One Networks row: the first is the primary network (<c>--network</c>), the rest attach after creation.</summary>
public sealed class NetworkRow
{
    public string Name { get; set; } = "";

    public string Ip { get; set; } = "";

    /// <summary>Aliases separated by commas or spaces (first row only).</summary>
    public string Aliases { get; set; } = "";

    public bool AllowsStaticIp => Name.Trim().Length > 0 && !NetworkSummary.IsBuiltIn(Name.Trim());
}

/// <summary>
/// The editable state behind the launch form, one property per form field,
/// converted to and from the API's <see cref="ContainerLaunchRequest"/>.
/// </summary>
public sealed class LaunchForm
{
    public const string KeepRunningCommand = "sleep infinity";

    public string ImageName { get; set; } = "";

    public string ImageTag { get; set; } = "";

    public string Name { get; set; } = "";

    public string Command { get; set; } = "";

    public string Memory { get; set; } = "";

    public string Cpus { get; set; } = "";

    /// <summary>One <c>--publish</c> per value; the field shows them on one line and edits them as rows.</summary>
    public List<string> Publish { get; } = [];

    public string Workdir { get; set; } = "";

    /// <summary>One <c>--env</c> per value, <c>KEY=value</c>; edited like <see cref="Publish"/>.</summary>
    public List<string> Env { get; } = [];

    public string User { get; set; } = "";

    public string Entrypoint { get; set; } = "";

    public string RestartPolicy { get; set; } = RestartPolicyInfo.No;

    /// <summary>One public name per value, <c>containerPort:prefix</c>; edited like <see cref="Publish"/>. The agent's, not a flag.</summary>
    public List<string> PublicNames { get; } = [];

    public string StopTimeout { get; set; } = "";

    public string HealthCmd { get; set; } = "";

    public string HealthInterval { get; set; } = "";

    public string HealthTimeout { get; set; } = "";

    public string HealthRetries { get; set; } = "";

    public string HealthStartPeriod { get; set; } = "";

    public bool NoHealthcheck { get; set; }

    public List<MountRow> Mounts { get; } = [];

    public List<NetworkRow> Networks { get; } = [];

    /// <summary>Start after create / recreate: the rail's Run verb sets it, its Save verb clears it.</summary>
    public bool Start { get; set; } = true;

    public bool KeepRunning => Command.Trim() == KeepRunningCommand;

    /// <summary><c>repo[:tag]</c> from the two image fields.</summary>
    public string Image => ImageTag.Trim().Length > 0 ? $"{ImageName.Trim()}:{ImageTag.Trim()}" : ImageName.Trim();

    /// <summary>The same reference as one field (Create and View &amp; edit show one; Run shows Image and Tag).</summary>
    public string ImageRef
    {
        get => Image;
        set => (ImageName, ImageTag) = SplitImage(value);
    }

    /// <summary>Every field from another form (the Fill bar's result), the rows included.</summary>
    public void CopyFrom(LaunchForm other)
    {
        ImageName = other.ImageName;
        ImageTag = other.ImageTag;
        Name = other.Name;
        Command = other.Command;
        Memory = other.Memory;
        Cpus = other.Cpus;
        Workdir = other.Workdir;
        User = other.User;
        Entrypoint = other.Entrypoint;
        RestartPolicy = other.RestartPolicy;
        StopTimeout = other.StopTimeout;
        HealthCmd = other.HealthCmd;
        HealthInterval = other.HealthInterval;
        HealthTimeout = other.HealthTimeout;
        HealthRetries = other.HealthRetries;
        HealthStartPeriod = other.HealthStartPeriod;
        NoHealthcheck = other.NoHealthcheck;
        Publish.Clear();
        Publish.AddRange(other.Publish);
        Env.Clear();
        Env.AddRange(other.Env);
        PublicNames.Clear();
        PublicNames.AddRange(other.PublicNames);
        Mounts.Clear();
        Mounts.AddRange(other.Mounts);
        Networks.Clear();
        Networks.AddRange(other.Networks);
    }

    public static LaunchForm From(ContainerLaunchRequest request)
    {
        var form = new LaunchForm
        {
            Name = request.Name,
            Command = request.Command,
            Memory = request.Memory,
            Cpus = request.Cpus,
            Workdir = request.Workdir,
            User = request.User,
            Entrypoint = request.Entrypoint,
            RestartPolicy = RestartPolicyInfo.IsKnown(request.RestartPolicy) ? request.RestartPolicy : RestartPolicyInfo.No,
            StopTimeout = request.StopTimeout,
            HealthCmd = request.HealthCmd,
            HealthInterval = request.HealthInterval,
            HealthTimeout = request.HealthTimeout,
            HealthRetries = request.HealthRetries,
            HealthStartPeriod = request.HealthStartPeriod,
            NoHealthcheck = request.NoHealthcheck,
            Start = request.Start,
        };
        (form.ImageName, form.ImageTag) = SplitImage(request.Image);
        form.Publish.AddRange(request.Publish);
        form.Env.AddRange(request.Env);
        form.PublicNames.AddRange(request.PublicNames);
        form.Mounts.AddRange(request.Volumes.Select(MountRow.Parse));
        if (request.Network.Length > 0 || request.Ip.Length > 0 || request.NetworkAliases.Count > 0)
        {
            form.Networks.Add(new NetworkRow { Name = request.Network, Ip = request.Ip, Aliases = string.Join(", ", request.NetworkAliases) });
        }

        foreach (var line in request.ConnectNetworks)
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            form.Networks.Add(new NetworkRow { Name = parts[0], Ip = parts.Length > 1 ? parts[1] : "" });
        }

        return form;
    }

    public ContainerLaunchRequest ToRequest()
    {
        var primary = Networks.FirstOrDefault(n => n.Name.Trim().Length > 0);
        var extras = Networks.Where(n => n.Name.Trim().Length > 0 && n != primary);
        return new ContainerLaunchRequest
        {
            Image = Image,
            Name = Name.Trim(),
            Command = Command.Trim(),
            Entrypoint = Entrypoint.Trim(),
            Memory = Memory.Trim(),
            Cpus = Cpus.Trim(),
            Publish = Kept(Publish),
            Volumes = Mounts.Select(m => m.Spec).Where(s => s.Length > 0).ToList(),
            Workdir = Workdir.Trim(),
            Env = Kept(Env),
            Network = primary?.Name.Trim() ?? "",
            Ip = primary is { AllowsStaticIp: true } ? primary.Ip.Trim() : "",
            NetworkAliases = primary is null ? [] : primary.Aliases.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ConnectNetworks = extras.Select(n => n.AllowsStaticIp && n.Ip.Trim().Length > 0 ? $"{n.Name.Trim()} {n.Ip.Trim()}" : n.Name.Trim()).ToList(),
            User = User.Trim(),
            RestartPolicy = RestartPolicy,
            PublicNames = Kept(PublicNames),
            StopTimeout = StopTimeout.Trim(),
            HealthCmd = HealthCmd.Trim(),
            HealthInterval = HealthInterval.Trim(),
            HealthTimeout = HealthTimeout.Trim(),
            HealthRetries = HealthRetries.Trim(),
            HealthStartPeriod = HealthStartPeriod.Trim(),
            NoHealthcheck = NoHealthcheck,
            Start = Start,
        };
    }

    /// <summary>The tag after the last colon, unless that colon belongs to a registry port.</summary>
    public static (string Name, string Tag) SplitImage(string image)
    {
        var text = image.Trim();
        var colon = text.LastIndexOf(':');
        if (colon <= 0 || text[(colon + 1)..].Contains('/'))
        {
            return (text, "");
        }

        return (text[..colon], text[(colon + 1)..]);
    }

    /// <summary>A multi-value field without its blank rows.</summary>
    private static List<string> Kept(IEnumerable<string> values) =>
        [.. values.Select(v => v.Trim()).Where(v => v.Length > 0)];
}
