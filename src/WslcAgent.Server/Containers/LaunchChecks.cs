using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Publishing;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// What the launch form asks before it runs, creates or recreates: the fields
/// against each other (a published port the command does not listen on, a
/// variable and the command naming different ports) and against the machine
/// (a name in use, a host port taken, a network that is not there, a host path
/// that does not exist). Errors stop the launch; warnings are shown and
/// confirmed, since a product may well listen where its command does not say.
/// Every message quotes the value in full: the field it sits in may be cut
/// short on a narrow screen, which is how a wrong port went unseen.
/// </summary>
public sealed partial class LaunchChecks(
    IContainerService containers,
    INetworkService networks,
    IImageService images,
    PublishingService publishing,
    ILogger<LaunchChecks> logger) : ILaunchChecks
{
    public async Task<LaunchCheck> CheckAsync(ContainerLaunchRequest request, string source, CancellationToken cancellationToken = default)
    {
        var findings = new Findings();
        Inspect(request, findings);
        await AgainstContainersAsync(request, source, findings, cancellationToken);
        await AgainstNetworksAsync(request, findings, cancellationToken);
        await AgainstImagesAsync(request, findings, cancellationToken);
        await AgainstPublishingAsync(request, source, findings, cancellationToken);
        return findings.ToCheck();
    }

    /// <summary>The form against itself, no machine asked: what the tests read.</summary>
    internal static LaunchCheck Inspect(ContainerLaunchRequest request)
    {
        var findings = new Findings();
        Inspect(request, findings);
        return findings.ToCheck();
    }

    private static void Inspect(ContainerLaunchRequest request, Findings findings)
    {
        Shapes(request, findings);
        Volumes(request, findings);
        Ports(request, findings);
    }

    /// <summary>Each value in the shape the CLI takes it, said before the CLI says it in its own words.</summary>
    private static void Shapes(ContainerLaunchRequest request, Findings findings)
    {
        if (request.Name.Length > 0 && !ContainerName().IsMatch(request.Name))
        {
            findings.Error(LaunchFields.Name, $"'{request.Name}' is not a container name: letters, digits, '_', '.' and '-', starting with a letter or digit.");
        }

        if (request.Memory.Length > 0 && !MemorySize().IsMatch(request.Memory))
        {
            findings.Error(LaunchFields.Memory, $"'{request.Memory}' is not a memory size. Example: 512m, 2g.");
        }

        if (request.Cpus.Length > 0 && (!double.TryParse(request.Cpus, NumberStyles.Float, CultureInfo.InvariantCulture, out var cpus) || cpus <= 0))
        {
            findings.Error(LaunchFields.Cpus, $"'{request.Cpus}' is not a CPU count. Example: 1.5.");
        }

        if (request.ShmSize.Length > 0 && !MemorySize().IsMatch(request.ShmSize))
        {
            findings.Error(LaunchFields.ShmSize, $"'{request.ShmSize}' is not a size. Example: 64m, 1g.");
        }

        foreach (var label in request.Labels.Where(label => label.IndexOf('=') <= 0))
        {
            findings.Error(LaunchFields.Labels, $"'{label}' has no '=': a label is key=value.");
        }

        foreach (var limit in request.Ulimits.Where(limit => !Ulimit().IsMatch(limit)))
        {
            findings.Error(LaunchFields.Ulimits, $"'{limit}' is not a limit: name=soft[:hard], -1 for unlimited. Example: nofile=1024:2048.");
        }

        if (request.StopTimeout.Length > 0 && (!int.TryParse(request.StopTimeout, out var stop) || stop < -1))
        {
            findings.Error(LaunchFields.StopTimeout, $"'{request.StopTimeout}' is not a stop timeout: seconds, 0 immediate, -1 never.");
        }

        if (request.RestartPolicy.Length > 0 && !RestartPolicyInfo.IsKnown(request.RestartPolicy))
        {
            findings.Error(LaunchFields.RestartPolicy, $"'{request.RestartPolicy}' is not a restart policy: no, unless-stopped or always.");
        }

        if (!request.NoHealthcheck)
        {
            foreach (var (field, value) in new[] { (LaunchFields.HealthInterval, request.HealthInterval), (LaunchFields.HealthTimeout, request.HealthTimeout), (LaunchFields.HealthStartPeriod, request.HealthStartPeriod) })
            {
                if (value.Length > 0 && !Duration().IsMatch(value))
                {
                    findings.Error(field, $"'{value}' is not a duration. Example: 30s, 5m, 500ms.");
                }
            }

            if (request.HealthRetries.Length > 0 && (!int.TryParse(request.HealthRetries, out var retries) || retries < 0))
            {
                findings.Error(LaunchFields.HealthRetries, $"'{request.HealthRetries}' is not a count of retries.");
            }
        }

        foreach (var pair in request.Env)
        {
            if (!pair.Contains('='))
            {
                findings.Error(LaunchFields.Env, $"'{pair}' has no '=': a variable is KEY=value.");
            }
        }

        foreach (var key in request.Env.Where(e => e.Contains('=')).Select(e => e[..e.IndexOf('=')].Trim()).GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            findings.Warn(LaunchFields.Env, $"{key} is set more than once; the last value wins.");
        }

        if (request.Ip.Length > 0 && !LaunchArgs.AllowsStaticIp(request.Network))
        {
            findings.Warn(LaunchFields.Networks, $"The static IP {request.Ip} is dropped: {(request.Network.Length > 0 ? request.Network : "bridge")} is a built-in network, and WSLC assigns addresses there.");
        }
    }

    /// <summary>A host path bound into the container has to exist on this machine; the CLI refuses it later with less to say.</summary>
    private static void Volumes(ContainerLaunchRequest request, Findings findings)
    {
        foreach (var spec in request.Volumes)
        {
            var target = LaunchArgs.MountTarget(spec);
            var cut = spec.LastIndexOf(':' + target, StringComparison.Ordinal);
            var source = cut > 0 ? spec[..cut] : spec;
            if (LaunchArgs.LooksLikeHostPath(source) && !Path.Exists(Environment.ExpandEnvironmentVariables(source)))
            {
                findings.Error(LaunchFields.Volumes, $"Host path '{source}' does not exist on this machine (row {spec}).");
            }
        }
    }

    /// <summary>
    /// The published container ports against the ports the command, the variables
    /// and the health command name: a mismatch is what a wrong <c>--port</c> looks
    /// like from outside, and the container is "up" while nothing answers.
    /// </summary>
    private static void Ports(ContainerLaunchRequest request, Findings findings)
    {
        var published = new List<(string Spec, int Host, int Container)>();
        foreach (var spec in request.Publish)
        {
            var match = PublishSpec().Match(spec);
            if (!match.Success)
            {
                findings.Error(LaunchFields.Publish, $"'{spec}' is not a port publication: [ip:]hostPort:containerPort[/udp].");
                continue;
            }

            if (match.Groups["host"].Success && !match.Groups["range"].Success)
            {
                published.Add((spec, int.Parse(match.Groups["host"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["container"].Value, CultureInfo.InvariantCulture)));
            }
        }

        foreach (var taken in published.GroupBy(p => p.Host).Where(g => g.Count() > 1))
        {
            findings.Error(LaunchFields.Publish, $"Host port {taken.Key} is published twice: {string.Join(", ", taken.Select(p => p.Spec))}.");
        }

        var command = CommandPorts(request.Command);
        var variables = VariablePorts(request.Env);
        var health = HealthPorts(request.HealthCmd);
        var named = command.Select(c => c.Port).Concat(variables.Select(v => v.Port)).Concat(health).ToHashSet();
        var containerPorts = published.Select(p => p.Container).ToHashSet();
        var says = Says(command, variables);

        if (named.Count > 0)
        {
            foreach (var port in published.Where(p => !named.Contains(p.Container)).Select(p => p.Container).Distinct())
            {
                findings.Warn(LaunchFields.Publish, $"Publish reaches container port {port}, but nothing says the container listens there: {says}.");
            }
        }

        if (containerPorts.Count > 0)
        {
            foreach (var (flag, port) in command.Where(c => !containerPorts.Contains(c.Port)))
            {
                findings.Warn(LaunchFields.Command, $"The command says '{flag} {port}', and port {port} is not published (published container ports: {string.Join(", ", containerPorts.Order())}).");
            }
        }

        foreach (var (key, port) in variables)
        {
            var other = command.FirstOrDefault(c => c.Port != port && Related(key, c.Flag));
            if (other.Flag is not null)
            {
                findings.Warn(LaunchFields.Env, $"{key}={port} but the command says '{other.Flag} {other.Port}': one of the two is not the port the container uses.");
            }
        }
    }

    /// <summary>A variable and a flag about the same thing: both say "port", and their other words overlap (GATEWAY_PORT and --port; PORT and --port).</summary>
    private static bool Related(string key, string flag)
    {
        var flagWords = flag.TrimStart('-').Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).Where(w => w != "port").ToHashSet();
        var keyWords = key.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).Where(w => w != "port").ToHashSet();
        return flagWords.Count == 0 || keyWords.Overlaps(flagWords);
    }

    private static string Says(List<(string Flag, int Port)> command, List<(string Key, int Port)> variables)
    {
        var parts = new List<string>();
        if (command.Count > 0)
        {
            parts.Add($"the command says {string.Join(" and ", command.Select(c => $"'{c.Flag} {c.Port}'"))}");
        }

        if (variables.Count > 0)
        {
            parts.Add($"the variables say {string.Join(", ", variables.Select(v => $"{v.Key}={v.Port}"))}");
        }

        return string.Join("; ", parts);
    }

    /// <summary><c>--port 18782</c>, <c>--http-port=8080</c>, <c>-port 9000</c>: a flag with "port" in it and a number after it.</summary>
    private static List<(string Flag, int Port)> CommandPorts(string command) =>
        PortFlag().Matches(command).Select(m => (m.Groups["flag"].Value, int.Parse(m.Groups["port"].Value, CultureInfo.InvariantCulture))).ToList();

    /// <summary><c>OPENCLAW_GATEWAY_PORT=18789</c>: a variable with PORT in its name and a number as its value.</summary>
    private static List<(string Key, int Port)> VariablePorts(IReadOnlyList<string> env)
    {
        var ports = new List<(string, int)>();
        foreach (var pair in env)
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = pair[..eq].Trim();
            if (key.Contains("PORT", StringComparison.OrdinalIgnoreCase) && int.TryParse(pair[(eq + 1)..].Trim(), out var port) && port is > 0 and < 65536)
            {
                ports.Add((key, port));
            }
        }

        return ports;
    }

    /// <summary><c>curl -f http://localhost:8080/</c>: the port in a health command's address.</summary>
    private static List<int> HealthPorts(string healthCmd) =>
        AddressPort().Matches(healthCmd).Select(m => int.Parse(m.Groups["port"].Value, CultureInfo.InvariantCulture)).ToList();

    /// <summary>A name in use, and a host port another running container holds or a program on this machine listens on.</summary>
    private async Task AgainstContainersAsync(ContainerLaunchRequest request, string source, Findings findings, CancellationToken cancellationToken)
    {
        IReadOnlyList<ContainerSummary> all;
        try
        {
            all = (await containers.ListAsync(all: true, helpers: true, cancellationToken)).Containers;
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            findings.Warn(LaunchFields.General, $"Could not check the name and the ports against the containers: {ex.Message}");
            return;
        }

        var self = source.Length == 0 ? null : all.FirstOrDefault(c => c.Name == source || c.Id == source || source.StartsWith(c.Id, StringComparison.Ordinal));
        var others = all.Where(c => c != self).ToList();
        if (request.Name.Length > 0 && others.FirstOrDefault(c => c.Name.Equals(request.Name, StringComparison.Ordinal)) is { } holder)
        {
            findings.Error(LaunchFields.Name, $"'{request.Name}' is already the name of container {holder.Id} ({holder.State}). Remove or rename it, or pick another name.");
        }

        var ownPorts = self?.Ports.Select(HostPortOf).ToHashSet() ?? [];
        var hostPorts = request.Publish.Select(spec => PublishSpec().Match(spec)).Where(m => m.Success && m.Groups["host"].Success && !m.Groups["range"].Success)
            .Select(m => int.Parse(m.Groups["host"].Value, CultureInfo.InvariantCulture)).Distinct().ToList();
        var listening = Listening();
        foreach (var port in hostPorts)
        {
            var running = others.FirstOrDefault(c => c.State == "running" && c.Ports.Any(p => HostPortOf(p) == port));
            if (running is not null)
            {
                findings.Error(LaunchFields.Publish, $"Host port {port} is already published by container {running.Name}.");
            }
            else if (!ownPorts.Contains(port) && listening.Contains(port))
            {
                findings.Error(LaunchFields.Publish, $"Host port {port} is in use on this machine by a program that is not a container.");
            }
        }
    }

    /// <summary>Every network the form names has to exist: the CLI refuses one that does not after the container was created.</summary>
    private async Task AgainstNetworksAsync(ContainerLaunchRequest request, Findings findings, CancellationToken cancellationToken)
    {
        var wanted = request.ConnectNetworks.Select(line => line.Split(' ', 2)[0].Trim()).Prepend(request.Network.Trim())
            .Where(n => n.Length > 0 && !NetworkSummary.IsBuiltIn(n)).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        try
        {
            var known = (await networks.ListAsync(cancellationToken)).Networks.Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var name in wanted.Where(n => !known.Contains(n)))
            {
                findings.Error(LaunchFields.Networks, $"Network '{name}' does not exist. Create it in Networks, or pick one with ⋮.");
            }
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            findings.Warn(LaunchFields.General, $"Could not check the networks: {ex.Message}");
        }
    }

    /// <summary>An image that is not local is pulled first: minutes, said before the wait.</summary>
    private async Task AgainstImagesAsync(ContainerLaunchRequest request, Findings findings, CancellationToken cancellationToken)
    {
        try
        {
            if (!ContainerLaunches.IsLocal((await images.ListAsync(cancellationToken)).Images, request.Image))
            {
                findings.Warn(LaunchFields.Image, $"Image '{request.Image}' is not on this machine: it will be pulled first, which can take minutes.");
            }
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            findings.Warn(LaunchFields.General, $"Could not check the image: {ex.Message}");
        }
    }

    /// <summary>The public names as the launch will validate them, said here instead of after the container is gone.</summary>
    private async Task AgainstPublishingAsync(ContainerLaunchRequest request, string source, Findings findings, CancellationToken cancellationToken)
    {
        try
        {
            await publishing.ValidateRowsAsync(request.Name.Length > 0 ? request.Name : source, request, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            findings.Error(LaunchFields.PublicNames, ex.Message);
        }
    }

    /// <summary>The TCP ports something on this machine listens on; none when the system will not say.</summary>
    private HashSet<int> Listening()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            logger.LogDebug("launch check: listeners unavailable: {Message}", ex.Message);
            return [];
        }
    }

    /// <summary>The host side of a list row's <c>host->container</c>; -1 when it has none.</summary>
    private static int HostPortOf(string rendered)
    {
        var arrow = rendered.IndexOf("->", StringComparison.Ordinal);
        return arrow > 0 && int.TryParse(rendered[..arrow], out var port) ? port : -1;
    }

    private sealed class Findings
    {
        private readonly List<LaunchFinding> _errors = [];
        private readonly List<LaunchFinding> _warnings = [];

        public void Error(string field, string message) => _errors.Add(new LaunchFinding(field, message));

        public void Warn(string field, string message) => _warnings.Add(new LaunchFinding(field, message));

        public LaunchCheck ToCheck() => new(_errors, _warnings);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
    private static partial Regex ContainerName();

    [GeneratedRegex(@"^\d+(\.\d+)?[bkmgt]?$", RegexOptions.IgnoreCase)]
    private static partial Regex MemorySize();

    [GeneratedRegex(@"^\d+(ms|s|m|h)$")]
    private static partial Regex Duration();

    [GeneratedRegex(@"^[a-z]+=-?\d+(:-?\d+)?$")]
    private static partial Regex Ulimit();

    /// <summary><c>[ip:]hostPort[-to]:containerPort[-to][/proto]</c>, or a container port alone (a random host port).</summary>
    [GeneratedRegex(@"^(?:(?:\d{1,3}\.){3}\d{1,3}:)?(?:(?<host>\d{1,5})(?<range>-\d{1,5})?:)?(?<container>\d{1,5})(?:-\d{1,5})?(?:/(?:tcp|udp|sctp))?$", RegexOptions.IgnoreCase)]
    private static partial Regex PublishSpec();

    [GeneratedRegex(@"(?<flag>--?(?:[A-Za-z][\w-]*)?port[\w-]*)(?:=|\s+)(?<port>\d{1,5})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PortFlag();

    [GeneratedRegex(@"(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1\]):(?<port>\d{1,5})\b")]
    private static partial Regex AddressPort();
}
