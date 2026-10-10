using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Projects;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// <c>wslc container run|create</c> argument lists from the launch fields, in
/// a fixed flag order. The restart policy is not a CLI flag and is
/// left to <see cref="RestartPolicyStore"/>.
/// </summary>
public static class LaunchArgs
{
    /// <summary><c>container run --detach OPTIONS image [command…]</c>.</summary>
    public static List<string> Run(ContainerLaunchRequest request) =>
        Build(["container", "run", "--detach"], request);

    /// <summary><c>container create OPTIONS image [command…]</c>.</summary>
    public static List<string> Create(ContainerLaunchRequest request) =>
        Build(["container", "create"], request);

    private static List<string> Build(List<string> args, ContainerLaunchRequest request)
    {
        var image = WslcArgs.Require(request.Image, "image");
        var volumes = ResolveVolumes(request);
        var workdir = ResolveWorkdir(request.Workdir, volumes);
        var entrypoint = ShellWords.Split(request.Entrypoint);

        args.Option("--name", request.Name)
            .Option("--memory", request.Memory)
            .Option("--cpus", request.Cpus)
            .Option("--gpus", request.Gpus ? "all" : "");
        Repeat(args, "--publish", request.Publish);
        Repeat(args, "--volume", volumes);
        args.Option("--workdir", workdir);
        Repeat(args, "--env", request.Env);
        args.Option("--entrypoint", entrypoint.Count > 0 ? entrypoint[0] : "")
            .Option("--network", request.Network)
            .Option("--ip", AllowsStaticIp(request.Network) ? request.Ip : "");
        Repeat(args, "--network-alias", request.NetworkAliases);
        args.Option("--hostname", request.Hostname)
            .Option("--domainname", request.Domainname);
        Repeat(args, "--dns", request.Dns);
        Repeat(args, "--dns-search", request.DnsSearch);
        Repeat(args, "--dns-option", request.DnsOptions);
        Repeat(args, "--tmpfs", request.Tmpfs);
        args.Option("--shm-size", request.ShmSize);
        Repeat(args, "--ulimit", request.Ulimits);
        Repeat(args, "--label", request.Labels.Where(label => !LaunchMemo.IsOwn(label)));

        // A container a Compose file brought up says so on itself: its project, its
        // service, and the service as its file had it when it was made.
        var (project, service) = ProjectLabels.Split(request.Project);
        args.Option("--label", project.Length > 0 ? $"{ProjectLabels.Project}={project}" : "")
            .Option("--label", project.Length > 0 ? $"{ProjectLabels.Service}={service}" : "")
            .Option("--label", project.Length > 0 && request.ProjectConfig.Length > 0 ? $"{ProjectLabels.Config}={request.ProjectConfig}" : "");

        // What inspect will not say back goes on the container itself, for whoever reads it next.
        args.Option("--label", LaunchMemo.Of(request))
            .Option("--user", request.User)
            .Option("--stop-signal", request.StopSignal)
            .Option("--stop-timeout", request.StopTimeout);
        if (request.NoHealthcheck)
        {
            args.Flag("--no-healthcheck", true);
        }
        else
        {
            args.Option("--health-cmd", request.HealthCmd)
                .Option("--health-interval", request.HealthInterval)
                .Option("--health-timeout", request.HealthTimeout)
                .Option("--health-retries", request.HealthRetries)
                .Option("--health-start-period", request.HealthStartPeriod);
        }

        args.Add(image);
        // --entrypoint names one executable, as it does for Docker: an image's
        // ENTRYPOINT ["tini", "-s", "--"] read back by inspect is that executable
        // and its arguments, which go where the CLI takes arguments, before the command.
        args.AddRange(entrypoint.Skip(1));
        args.AddRange(ShellWords.Split(request.Command));
        return args;
    }

    /// <summary>WSLC accepts <c>--ip</c> only on a user-defined network.</summary>
    public static bool AllowsStaticIp(string network) =>
        network.Trim().Length > 0 && !NetworkSummary.IsBuiltIn(network.Trim());

    /// <summary>A host-looking workdir with no volume becomes the bind <c>workdir:/workspace</c>.</summary>
    private static List<string> ResolveVolumes(ContainerLaunchRequest request)
    {
        var volumes = request.Volumes.Where(v => v.Trim().Length > 0).Select(v => v.Trim()).ToList();
        if (volumes.Count == 0 && LooksLikeHostPath(request.Workdir))
        {
            volumes.Add($"{request.Workdir.Trim()}:/workspace");
        }

        return volumes;
    }

    /// <summary>Host paths cannot be a container workdir: they map to the first volume's target (or <c>/workspace</c>); container paths get a leading slash.</summary>
    private static string ResolveWorkdir(string workdir, IReadOnlyList<string> volumes)
    {
        var text = workdir.Trim();
        if (text.Length == 0)
        {
            return "";
        }

        if (LooksLikeHostPath(text))
        {
            return volumes.Count > 0 ? MountTarget(volumes[0]) : "/workspace";
        }

        return text.StartsWith('/') ? text : "/" + text;
    }

    /// <summary><c>X:\…</c>, <c>~</c>, <c>$VAR</c>, <c>%VAR%</c> and UNC paths are host paths.</summary>
    public static bool LooksLikeHostPath(string value)
    {
        var text = value.Trim();
        return text.Length >= 2 && (char.IsLetter(text[0]) && text[1] == ':' || text[0] is '~' or '$' or '%' || text.StartsWith("\\\\"));
    }

    /// <summary>The container side of <c>source:target[:ro]</c>; a Windows drive letter is not a separator.</summary>
    public static string MountTarget(string spec)
    {
        var start = spec.Length >= 2 && char.IsLetter(spec[0]) && spec[1] == ':' ? 2 : 0;
        var colon = spec.IndexOf(':', start);
        if (colon < 0)
        {
            return "/workspace";
        }

        var rest = spec[(colon + 1)..];
        var mode = rest.LastIndexOf(':');
        var target = mode > 0 && rest[(mode + 1)..] is "ro" or "rw" ? rest[..mode] : rest;
        return target.Length > 0 ? target : "/workspace";
    }

    private static void Repeat(List<string> args, string flag, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            args.Option(flag, value);
        }
    }
}
