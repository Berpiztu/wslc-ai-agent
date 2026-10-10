using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Projects;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// Containers as <c>wslc container list --format json</c> reports them, each
/// merged with its <c>wslc container stats</c> row and its restart policy;
/// the lifecycle verbs; create, run and recreate from the launch fields;
/// details from <c>inspect</c>; logs.
/// </summary>
public sealed partial class ContainerService(
    IWslcRunner wslc,
    INetworkService networks,
    RestartPolicyStore policies,
    Publishing.PublicationStore publications,
    Publishing.PublishingService publishing,
    ContainerRestarter restarter,
    Resources.ResourceRegistry registry,
    ContainerRecreations recreations,
    ImageUpdatesUnderWay updates,
    WslcEvents events,
    ProjectDependencies dependencies,
    ILogger<ContainerService> logger) : IContainerService
{
    private const string MetadataLabel = "com.microsoft.wsl.container.metadata=";

    /// <summary>The throwaway name a recreate tries its settings under first; hidden from the list like the Files helpers.</summary>
    internal const string RehearsalPrefix = "wslc-agent-rehearsal-";
    private static readonly string[] HelperPrefixes = [FilesHelpers.ImagePrefix, FilesHelpers.VolumePrefix, RehearsalPrefix, "wslc-dash-files-", "wslc-dash-volfiles-"];
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(60);

    public async Task<ContainerListResponse> ListAsync(bool all, bool helpers = false, CancellationToken cancellationToken = default)
    {
        var listTask = wslc.RunAsync(Scoped(["container", "list"], all), cancellationToken: cancellationToken);
        var statsTask = StatsAsync(all, cancellationToken);
        await Task.WhenAll(listTask, statsTask);

        var stats = statsTask.Result;
        var rows = WslcJson.ParseRows(listTask.Result.Stdout).Select(row => WithStats(ToSummary(row), stats)).ToList();
        // Every read is the registry's too: a list of every container is the
        // whole of them, so it also says which are gone; the agent's own
        // helpers are not the user's and are never entered.
        var owned = rows.Where(c => !IsHelper(c.Name)).ToList();
        var uids = registry.Reconcile(Resources.ResourceRegistry.Container, [.. owned.Select(c => (c.Id, c.Name))], complete: all);
        var containers = recreations.Overlay([.. (helpers ? rows : owned).Select(c => c with { Uid = uids.GetValueOrDefault(c.Id) })], complete: all);
        var annotated = updates.Annotate(publications.Annotate(policies.Annotate(containers)));
        return new ContainerListResponse(annotated, Aggregate(annotated));
    }

    /// <summary><c>wslc container start</c>; a project's container is not started while a service its own depends on is not running (409).</summary>
    public async Task StartAsync(string container, CancellationToken cancellationToken = default)
    {
        await dependencies.RequireRunningAsync(WslcArgs.Require(container, "container"), cancellationToken);
        await wslc.RunAsync(["container", "start", WslcArgs.Require(container, "container")], cancellationToken: cancellationToken);
        policies.SetDesired(container, RestartPolicyInfo.Running);
    }

    /// <summary><c>wslc container stop</c>; a project's container is not stopped while another of the project that depends on it still runs (409).</summary>
    public async Task StopAsync(string container, CancellationToken cancellationToken = default)
    {
        await dependencies.RequireNoDependentsAsync(WslcArgs.Require(container, "container"), cancellationToken);
        await wslc.RunAsync(["container", "stop", WslcArgs.Require(container, "container")], TimeSpan.FromSeconds(90), cancellationToken);
        policies.SetDesired(container, RestartPolicyInfo.Stopped);
    }

    /// <summary>The restart verb lives in <see cref="ContainerRestarter"/>; a project's container is not restarted while a service its own depends on is not running (409), as it is not started.</summary>
    public async Task RestartAsync(string container, CancellationToken cancellationToken = default)
    {
        await dependencies.RequireRunningAsync(WslcArgs.Require(container, "container"), cancellationToken);
        await restarter.RestartAsync(container, cancellationToken);
    }

    public async Task KillAsync(string container, CancellationToken cancellationToken = default)
    {
        await wslc.RunAsync(["container", "kill", WslcArgs.Require(container, "container")], cancellationToken: cancellationToken);
        policies.SetDesired(container, RestartPolicyInfo.Stopped);
    }

    public async Task RemoveAsync(string container, bool force = false, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "container", "rm" }.Flag("--force", force);
        args.Add(WslcArgs.Require(container, "container"));
        await wslc.RunAsync(args, cancellationToken: cancellationToken);
        policies.Remove(container);
        registry.Removed(Resources.ResourceRegistry.Container, container);
    }

    public Task<ContainerCreated> CreateAsync(ContainerLaunchRequest request, CancellationToken cancellationToken = default) =>
        LaunchAsync(request, start: false, cancellationToken);

    public Task<ContainerCreated> RunAsync(ContainerLaunchRequest request, CancellationToken cancellationToken = default) =>
        LaunchAsync(request, start: true, cancellationToken);

    /// <summary>
    /// The recreate — snapshot by inspect, stop (best effort),
    /// remove --force, launch the new one, launch the snapshot again if that
    /// fails — with a rehearsal before the destructive step: the new settings
    /// are launched first under a throwaway name, ports, volumes and networks
    /// included, while the container is only stopped. An entrypoint that is not
    /// in the image, a port in use or a network that is gone fail there, and the
    /// container is started again as it was. Only a rehearsal that passed
    /// removes it, and the restore stays as the net behind that.
    /// </summary>
    public async Task<ContainerCreated> RecreateAsync(string container, ContainerLaunchRequest request, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var snapshot = await InspectAsync(id, cancellationToken);
        // The container does not exist for a moment in the middle: its entry in
        // the registry — the uid a dashboard card points at — is held through
        // it, and given the new container at the end, whatever its name.
        registry.Hold(snapshot.Id);
        // Its row stays in the list, saying so, until the recreate is over
        // (ContainerRecreations); every client is told at both ends, since
        // wslc announces neither the remove nor a create that does not start.
        var newName = request.Name.Trim().Length > 0 ? request.Name.Trim() : snapshot.Name;
        recreations.Begin(snapshot.Id, newName, registry.UidOf(Resources.ResourceRegistry.Container, snapshot.Id, snapshot.Name));
        events.Publish(new ChangeNotice([ChangeNotice.Container]));
        try
        {
            var created = await RecreateHeldAsync(id, snapshot, request, cancellationToken);
            registry.Recreated(Resources.ResourceRegistry.Container, snapshot.Id, created.Id, request.Name.Trim());
            return created;
        }
        finally
        {
            recreations.End(snapshot.Id);
            registry.Release(snapshot.Id);
            events.Publish(new ChangeNotice([ChangeNotice.Container]));
        }
    }

    /// <summary>The recreate itself, its registry entry held by <see cref="RecreateAsync"/>.</summary>
    private async Task<ContainerCreated> RecreateHeldAsync(string id, ContainerInspection snapshot, ContainerLaunchRequest request, CancellationToken cancellationToken)
    {
        var previousPolicy = policies.Get(id);
        var launch = request with { RestartPolicy = request.RestartPolicy.Length == 0 ? previousPolicy.Policy : request.RestartPolicy };
        // Public names that cannot be applied fail here, before the container is
        // touched: a save that fails changes nothing.
        await publishing.ValidateRowsAsync(TargetOf(request, snapshot.Name), request, cancellationToken);

        try
        {
            await wslc.RunAsync(["container", "stop", id], TimeSpan.FromSeconds(90), cancellationToken);
        }
        catch (WslcException ex)
        {
            logger.LogInformation("recreate: stop of {Container} skipped: {Message}", id, ex.Message);
        }

        await RehearseAsync(id, snapshot, launch, cancellationToken);

        await wslc.RunAsync(["container", "rm", "--force", id], cancellationToken: cancellationToken);
        policies.Remove(id);

        try
        {
            return await LaunchAsync(launch, request.Start, cancellationToken);
        }
        catch (WslcException failure)
        {
            var restored = await RestoreAsync(snapshot, previousPolicy.Policy, cancellationToken);
            var outcome = restored ? "The previous container was restored so you can inspect logs and fix the settings." : "The previous container could not be restored either.";
            throw new WslcException($"Recreate failed: {failure.Message}. {outcome}", failure.Result)
            {
                Fields = LaunchErrors.WithNote(failure.Fields, outcome),
            };
        }
    }

    /// <summary>The name a container's rehearsal runs under: one per container, so one left by an interrupted recreate is found and removed by the next.</summary>
    internal static string RehearsalName(string container) =>
        RehearsalPrefix + Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(container)))[..12];

    /// <summary>
    /// The new settings launched under the rehearsal name exactly as the real
    /// launch will do it (run or create per <c>start</c>, then the extra
    /// networks) and removed again. When that fails the container, still there,
    /// is started again if it was running, and the error says nothing changed.
    /// </summary>
    private async Task RehearseAsync(string id, ContainerInspection snapshot, ContainerLaunchRequest request, CancellationToken cancellationToken)
    {
        var name = RehearsalName(snapshot.Name.Length > 0 ? snapshot.Name : id);
        var rehearsal = request with { Name = name, PublicNames = [] };
        await RemoveQuietlyAsync(name, cancellationToken);
        try
        {
            await RunOrCreateAsync(rehearsal, request.Start, cancellationToken);
        }
        catch (WslcException failure)
        {
            await RemoveQuietlyAsync(name, cancellationToken);
            var back = snapshot.IsRunning ? await StartQuietlyAsync(id, cancellationToken) : "";
            throw new WslcException($"Recreate aborted: {failure.Message}. {snapshot.Name} was not changed{back}.", failure.Result)
            {
                Fields = LaunchErrors.WithNote(failure.Fields, $"{snapshot.Name} was not changed{back}."),
            };
        }

        await RemoveQuietlyAsync(name, cancellationToken);
    }

    /// <summary>The container back up after a rehearsal that failed; what to add to the error when it would not start.</summary>
    private async Task<string> StartQuietlyAsync(string container, CancellationToken cancellationToken)
    {
        try
        {
            await wslc.RunAsync(["container", "start", container], cancellationToken: cancellationToken);
            return "";
        }
        catch (WslcException ex)
        {
            logger.LogError("recreate: could not start {Container} again: {Message}", container, ex.Message);
            return $" but could not be started again: {ex.Message}";
        }
    }

    /// <summary>A remove that may find nothing there: a rehearsal that never got created, or one an interrupted recreate left behind.</summary>
    private async Task RemoveQuietlyAsync(string container, CancellationToken cancellationToken)
    {
        try
        {
            await wslc.RunAsync(["container", "rm", "--force", container], cancellationToken: cancellationToken);
        }
        catch (WslcException ex)
        {
            logger.LogDebug("recreate: remove of {Container} skipped: {Message}", container, ex.Message);
        }
    }

    public async Task<ContainerDetails> DetailsAsync(string container, CancellationToken cancellationToken = default)
    {
        var inspection = await InspectAsync(WslcArgs.Require(container, "container"), cancellationToken);
        var policy = policies.Get(inspection.Name.Length > 0 ? inspection.Name : inspection.Id);
        return new ContainerDetails(
            inspection.Id, inspection.Name, inspection.Image, inspection.State, inspection.IsRunning, inspection.Ports, inspection.Created,
            inspection.Mounts, inspection.Form with { RestartPolicy = policy.Policy, PublicNames = publishing.RowsOf(inspection.Name) }, policy, inspection.Json, publications.ForContainer(inspection.Name),
            await UidOfAsync(inspection, cancellationToken));
    }

    /// <summary>
    /// The container's number in the registry. One the registry has not seen
    /// yet — its details opened before any list was read — is entered by a
    /// read of the list, which is how every container is entered. A list that
    /// cannot be read leaves it at 0: the details stand without it, only Pin
    /// to dashboard waits for the next read.
    /// </summary>
    private async Task<int> UidOfAsync(ContainerInspection inspection, CancellationToken cancellationToken)
    {
        var uid = registry.UidOf(Resources.ResourceRegistry.Container, inspection.Id, inspection.Name);
        if (uid != 0)
        {
            return uid;
        }

        try
        {
            await ListAsync(all: true, cancellationToken: cancellationToken);
        }
        catch (WslcException ex)
        {
            logger.LogInformation("details: the list that numbers {Container} could not be read: {Message}", inspection.Name, ex.Message);
        }

        return registry.UidOf(Resources.ResourceRegistry.Container, inspection.Id, inspection.Name);
    }

    public async Task<ContainerLogs> LogsAsync(string container, int tail, bool timestamps, CancellationToken cancellationToken = default, string since = "")
    {
        var args = new List<string> { "container", "logs" }
            .Option("--tail", tail > 0 ? tail.ToString() : "")
            .Flag("--timestamps", timestamps)
            .Option("--since", since);
        args.Add(WslcArgs.Require(container, "container"));
        var result = await wslc.RunAsync(args, cancellationToken: cancellationToken);
        var text = result.Stderr.Length > 0 ? result.Stderr.TrimEnd() + "\n" + result.Stdout : result.Stdout;
        return new ContainerLogs(text);
    }

    public RestartPolicyInfo GetRestartPolicy(string container) => policies.Get(WslcArgs.Require(container, "container"));

    public async Task<RestartPolicyInfo> SetRestartPolicyAsync(string container, string policy, CancellationToken cancellationToken = default)
    {
        var inspection = await InspectAsync(WslcArgs.Require(container, "container"), cancellationToken);
        policies.Set(inspection.Name, inspection.Id, policy, inspection.IsRunning ? RestartPolicyInfo.Running : RestartPolicyInfo.Stopped);
        return policies.Get(inspection.Name.Length > 0 ? inspection.Name : inspection.Id);
    }

    /// <summary>Create or run with the extra networks, then enrol the restart policy and publish the names.</summary>
    private async Task<ContainerCreated> LaunchAsync(ContainerLaunchRequest request, bool start, CancellationToken cancellationToken)
    {
        await publishing.ValidateRowsAsync(request.Name.Trim(), request, cancellationToken);
        var created = await RunOrCreateAsync(request, start, cancellationToken);
        policies.Set(request.Name, created.Id, request.RestartPolicy.Length == 0 ? RestartPolicyInfo.No : request.RestartPolicy,
            start ? RestartPolicyInfo.Running : RestartPolicyInfo.Stopped);
        // The names after the container exists: the store's rows against the form's,
        // the difference published or unpublished, the proxy restarted if the map moved.
        await publishing.ApplyAsync(TargetOf(request, created.Id), request.PublicNames, cancellationToken);
        return created;
    }

    /// <summary><c>run</c> or <c>create</c>, then the extra networks: what a rehearsal and the real launch share. A failure says which fields it is about.</summary>
    private async Task<ContainerCreated> RunOrCreateAsync(ContainerLaunchRequest request, bool start, CancellationToken cancellationToken)
    {
        try
        {
            var args = start ? LaunchArgs.Run(request) : LaunchArgs.Create(request);
            var result = await wslc.RunAsync(args, start ? RunTimeout : null, cancellationToken);
            var id = ContainerIdFrom(result.Stdout);
            var notes = await ConnectExtraNetworksAsync(TargetOf(request, id), request.ConnectNetworks, cancellationToken);
            return new ContainerCreated(id, notes);
        }
        catch (WslcException failure)
        {
            throw LaunchErrors.WithFields(failure, request);
        }
    }

    /// <summary>The name the form gives the container, or the fallback (its id, its previous name) when it gives none.</summary>
    private static string TargetOf(ContainerLaunchRequest request, string fallback) =>
        request.Name.Trim().Length > 0 ? request.Name.Trim() : fallback;

    /// <summary>
    /// The snapshot's own launch again, its public names and restart policy
    /// with it (the form read from inspect carries neither), dropping a static
    /// ip its network no longer allows.
    /// </summary>
    private async Task<bool> RestoreAsync(ContainerInspection snapshot, string policy, CancellationToken cancellationToken)
    {
        try
        {
            var form = snapshot.Form with
            {
                Ip = LaunchArgs.AllowsStaticIp(snapshot.Form.Network) ? snapshot.Form.Ip : "",
                RestartPolicy = policy,
                PublicNames = publishing.RowsOf(snapshot.Name),
            };
            await LaunchAsync(form, snapshot.IsRunning, cancellationToken);
            return true;
        }
        catch (WslcException ex)
        {
            logger.LogError("recreate: could not restore {Container}: {Message}", snapshot.Name, ex.Message);
            return false;
        }
    }

    /// <summary><c>network connect [--ip IP] NETWORK CONTAINER</c> per extra line; a refused static ip is retried without it and noted.</summary>
    private async Task<IReadOnlyList<string>> ConnectExtraNetworksAsync(string container, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        foreach (var line in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var network = parts[0];
            var ip = parts.Length > 1 && Ipv4().IsMatch(parts[1]) ? parts[1] : "";
            try
            {
                await networks.ConnectAsync(network, container, ip, cancellationToken);
            }
            catch (WslcException ex) when (ip.Length > 0)
            {
                await networks.ConnectAsync(network, container, "", cancellationToken);
                notes.Add($"Could not pin {ip} on {network}: {ex.Message}. Connected without a static IP.");
            }
        }

        return notes;
    }

    private async Task<ContainerInspection> InspectAsync(string container, CancellationToken cancellationToken)
    {
        var result = await wslc.RunAsync(["container", "inspect", container, "--format", "json"], cancellationToken: cancellationToken);
        var inspection = ContainerInspection.Parse(result);
        return inspection with { Form = inspection.Form with { Gpus = await HasGpusAsync(inspection.Id, cancellationToken) } };
    }

    /// <summary>
    /// Whether the container was created with <c>--gpus all</c>. Inspect does
    /// not say; only the list row's metadata label does, so the row is read
    /// for it. A recreate built from inspect alone once brought a speech
    /// model back without its GPU, and it failed on start with no libcuda.
    /// </summary>
    private async Task<bool> HasGpusAsync(string id, CancellationToken cancellationToken)
    {
        WslcResult list;
        try
        {
            list = await wslc.RunAsync(Scoped(["container", "list"], all: true), cancellationToken: cancellationToken);
        }
        catch (WslcException ex)
        {
            logger.LogWarning("container {Id}: the list could not be read for its GPU flag: {Message}", id, ex.Message);
            return false;
        }

        return WslcJson.ParseRows(list.Stdout)
            .Where(row => FirstNonEmpty(row.GetString("ID"), row.GetString("Id")) is { Length: > 0 } rowId
                && (rowId.StartsWith(id, StringComparison.OrdinalIgnoreCase) || id.StartsWith(rowId, StringComparison.OrdinalIgnoreCase)))
            .Select(row => HasGpus(row.GetString("Labels")))
            .FirstOrDefault();
    }

    /// <summary>The CLI prints the new id (or the name) on its last line.</summary>
    internal static string ContainerIdFrom(string stdout)
    {
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (HexId().IsMatch(lines[i]) || NameToken().IsMatch(lines[i]))
            {
                return lines[i].Length > 12 && HexId().IsMatch(lines[i]) ? lines[i][..12] : lines[i];
            }
        }

        return "";
    }

    /// <summary>The temporary helper containers the Files views spin up, hidden unless asked for.</summary>
    internal static bool IsHelper(string name) => HelperPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));

    /// <summary>Stats rows keyed by full id. A failing stats call must not hide the list, so it degrades to no stats.</summary>
    private Task<IReadOnlyList<JsonElement>> StatsAsync(bool all, CancellationToken cancellationToken) =>
        StatsRows.FetchAsync(wslc, all, logger, cancellationToken);

    /// <summary><c>[--all]</c> before <c>--format json</c>, the same shape for list and stats.</summary>
    private static List<string> Scoped(List<string> command, bool all)
    {
        if (all)
        {
            command.Add("--all");
        }

        command.AddRange(["--format", "json"]);
        return command;
    }

    /// <summary>The list row with its stats row's measures, when it has one.</summary>
    internal static ContainerSummary WithStats(ContainerSummary container, IReadOnlyList<JsonElement> stats)
    {
        var row = StatsRows.Find(stats, container.Id, container.Name);
        if (row.ValueKind != JsonValueKind.Object)
        {
            return container;
        }

        var (diskRead, diskWrite) = StatsParsing.Pair(row.GetString("BlockIO"));
        var (received, sent) = StatsParsing.Pair(row.GetString("NetIO"));
        return container with
        {
            CpuPercent = row.GetString("CPUPerc"),
            MemUsage = row.GetString("MemUsage"),
            MemPercent = row.GetString("MemPerc"),
            DiskIo = row.GetString("BlockIO"),
            NetIo = row.GetString("NetIO"),
            DiskIoBytes = diskRead + diskWrite,
            NetIoBytes = received + sent,
        };
    }

    internal static ContainerAggregate Aggregate(IReadOnlyList<ContainerSummary> containers) =>
        new(
            CpuUsedPercent: Math.Round(containers.Sum(c => StatsParsing.Percent(c.CpuPercent)), 2),
            CpuCount: Math.Max(1, Environment.ProcessorCount),
            MemoryUsedBytes: containers.Sum(c => StatsParsing.UsedBytes(c.MemUsage)));

    /// <summary>
    /// Map one list row. 2.9.10+ rows are Docker-shaped: <c>ID</c> (12 chars),
    /// <c>Names</c> (comma-separated), <c>State</c> as text, <c>Ports</c> as
    /// rendered text that WSLC leaves empty; the real port bindings sit in the
    /// <c>com.microsoft.wsl.container.metadata</c> label, so they come from there.
    /// Older rows with <c>Id</c>/<c>Name</c> map the same way.
    /// </summary>
    internal static ContainerSummary ToSummary(JsonElement row)
    {
        var id = FirstNonEmpty(row.GetString("ID"), row.GetString("Id"));
        var names = FirstNonEmpty(row.GetString("Names"), row.GetString("Name"));
        var name = names.Split(',', 2)[0].Trim().TrimStart('/');
        var state = row.GetString("State").Trim().ToLowerInvariant();
        var labels = row.GetString("Labels");
        var ports = ParsePorts(row.GetString("Ports"), labels);
        var (project, service, _) = ProjectLabels.Of(labels);
        var platform = row.TryGetProperty("Platform", out var p) && p.ValueKind == JsonValueKind.Object
            ? $"{p.GetString("os")}/{p.GetString("architecture")}".Trim('/')
            : "";

        return new ContainerSummary(
            Id: id,
            Name: name,
            Image: row.GetString("Image"),
            State: state.Length == 0 ? "unknown" : state,
            Status: row.GetString("Status"),
            Command: row.GetString("Command").Trim().Trim('"'),
            CreatedAt: row.GetString("CreatedAt"),
            RunningFor: row.GetString("RunningFor"),
            Ports: ports,
            Networks: row.GetString("Networks"),
            Size: row.GetString("Size"),
            HealthStatus: row.GetString("HealthStatus"),
            Platform: platform,
            Project: project,
            Service: service);
    }

    /// <summary>
    /// Port bindings as <c>hostPort->containerPort</c>,
    /// no bind address and no protocol. Prefers the rendered <c>Ports</c> text
    /// when WSLC fills it (<c>0.0.0.0:8080->80/tcp</c>); otherwise reads the
    /// metadata label (<c>{"V1":{"Ports":[{"BindingAddress","HostPort","ContainerPort","Protocol"}]}}</c>).
    /// </summary>
    internal static IReadOnlyList<string> ParsePorts(string rendered, string labels)
    {
        if (!string.IsNullOrWhiteSpace(rendered))
        {
            return rendered.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(PortText.Display)
                .Distinct()
                .ToList();
        }

        var start = labels.IndexOf(MetadataLabel, StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        var json = ExtractJsonObject(labels, start + MetadataLabel.Length);
        if (json is null)
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("V1", out var v1) || !v1.TryGetProperty("Ports", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return list.EnumerateArray()
                .Select(binding => PortText.Display(binding.GetString("HostPort"), binding.GetString("ContainerPort")))
                .Distinct()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The metadata label's <c>Flags</c> bit WSLC sets for <c>--gpus all</c>.</summary>
    private const int GpusFlag = 2;

    /// <summary>Whether the metadata label (<c>{"V1":{"Flags":…}}</c>) says the container has every GPU.</summary>
    internal static bool HasGpus(string labels)
    {
        var start = labels.IndexOf(MetadataLabel, StringComparison.Ordinal);
        var json = start < 0 ? null : ExtractJsonObject(labels, start + MetadataLabel.Length);
        if (json is null)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("V1", out var v1)
                && v1.TryGetProperty("Flags", out var flags)
                && flags.ValueKind == JsonValueKind.Number
                && (flags.GetInt32() & GpusFlag) != 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The label value is JSON embedded in a comma-separated list; cut it at its matching brace.</summary>
    private static string? ExtractJsonObject(string text, int start)
    {
        if (start >= text.Length || text[start] != '{')
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

    [GeneratedRegex("^[0-9a-fA-F]{12,64}$")]
    private static partial Regex HexId();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex NameToken();

    [GeneratedRegex(@"^(?:\d{1,3}\.){3}\d{1,3}$")]
    private static partial Regex Ipv4();
}
