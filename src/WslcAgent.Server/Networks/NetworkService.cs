using System.Text.Json;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Networks;

/// <summary>
/// Networks as <c>wslc network list --format json</c> reports them, each
/// completed with the subnet and gateway from <c>network inspect</c> and
/// marked with container usage.
/// </summary>
public sealed class NetworkService(IWslcRunner wslc, ContainerUsageScanner usage, Resources.ResourceRegistry registry, ILogger<NetworkService> logger) : INetworkService
{
    public async Task<NetworkListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        var listTask = wslc.RunAsync(["network", "list", "--format", "json"], cancellationToken: cancellationToken);
        var usageTask = usage.ScanAsync(cancellationToken);
        await Task.WhenAll(listTask, usageTask);

        var networks = new List<NetworkSummary>();
        foreach (var row in WslcJson.ParseRows(listTask.Result.Stdout))
        {
            var summary = ToSummary(row, usageTask.Result);
            var (subnet, gateway) = await IpamAsync(summary.Name, cancellationToken);
            networks.Add(summary with { Subnet = subnet, Gateway = gateway });
        }

        // Every read is the registry's too, a network by its id and its name.
        var uids = registry.Reconcile(Resources.ResourceRegistry.Network, [.. networks.Select(n => (n.Id, n.Name))], complete: true);
        networks = [.. networks.Select(n => n with { Uid = uids.GetValueOrDefault(n.Id) })];
        var totals = usageTask.Result.Totals;
        return new NetworkListResponse(networks, networks.Count, totals.NetReceivedBytes, totals.NetSentBytes);
    }

    public Task CreateAsync(CreateNetworkRequest request, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(CreateArgs(request), cancellationToken: cancellationToken);

    public Task ConnectAsync(string network, string container, string ip, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(ConnectArgs(network, container, ip), cancellationToken: cancellationToken);

    public Task DisconnectAsync(string network, string container, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(["network", "disconnect", WslcArgs.Require(network, "network name"), WslcArgs.Require(container, "container")], cancellationToken: cancellationToken);

    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        await RemoveFromWslcAsync(name, cancellationToken);
        registry.Removed(Resources.ResourceRegistry.Network, name);
    }

    /// <summary>The network gone from WSLC and nothing more: a recreate removes the old one but keeps its entry in the registry for the new.</summary>
    private Task RemoveFromWslcAsync(string name, CancellationToken cancellationToken) =>
        wslc.RunAsync(["network", "remove", WslcArgs.Require(name, "network name")], cancellationToken: cancellationToken);

    /// <summary><c>-f</c>: see <see cref="Images.ImageService.PruneAsync"/> (the 2.9.12 confirmation prompt).</summary>
    public async Task<CommandOutput> PruneAsync(CancellationToken cancellationToken = default) =>
        (await wslc.RunAsync(["network", "prune", "-f"], cancellationToken: cancellationToken)).Output;

    public async Task<NetworkDetails> DetailsAsync(string name, CancellationToken cancellationToken = default)
    {
        var item = await InspectAsync(WslcArgs.Require(name, "network name"), cancellationToken);
        var containers = item.TryGetProperty("Containers", out var attached) && attached.ValueKind == JsonValueKind.Object
            ? attached.EnumerateObject()
                .Select(c => (Name: c.Value.GetString("Name"), Ip: c.Value.GetString("IPv4Address")))
                .Where(c => c.Name.Length > 0)
                .Select(c => $"{c.Name} ({(c.Ip.Length > 0 ? c.Ip : "?")})")
                .ToList()
            : [];
        var itemName = item.GetString("Name").Trim();
        return new NetworkDetails(
            itemName.Length > 0 ? itemName : name,
            item.GetString("Id"),
            item.GetString("Scope"),
            item.GetString("Created"),
            containers,
            FormOf(item, name),
            NetworkSummary.IsBuiltIn((itemName.Length > 0 ? itemName : name).ToLowerInvariant()),
            JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// View &amp; edit's Save, a recreate: create-time settings cannot
    /// change in place, so the network is replaced and its containers are connected
    /// to the replacement. Renamed: the new one is created first, the containers
    /// move, then the old one goes. Same name: the containers are disconnected, the
    /// old one goes, the new one is created; if that fails the previous settings are
    /// restored and the error says so. Built-in networks are refused.
    /// </summary>
    public async Task<RecreateNetworkResult> RecreateAsync(string source, CreateNetworkRequest request, CancellationToken cancellationToken = default)
    {
        var origin = source.Trim();
        var dest = request.Name.Trim().Length > 0 ? request.Name.Trim() : origin;
        if (origin.Length == 0)
        {
            throw new ArgumentException("Network name is required", nameof(source));
        }

        if (NetworkSummary.IsBuiltIn(origin.ToLowerInvariant()))
        {
            throw new ArgumentException($"Cannot recreate the built-in {origin} network", nameof(source));
        }

        if (NetworkSummary.IsBuiltIn(dest.ToLowerInvariant()))
        {
            throw new ArgumentException($"Cannot replace a built-in network named {dest}", nameof(request));
        }

        JsonElement item;
        try
        {
            item = await InspectAsync(origin, cancellationToken);
        }
        catch (WslcException ex)
        {
            throw new WslcException($"Cannot recreate: failed to inspect network {origin}: {ex.Message}", ex.Result);
        }

        // The network does not exist for a moment in the middle, and may come
        // back under another name: its entry in the registry — the uid a
        // dashboard card points at — is held through it and given the new
        // network at the end.
        var held = item.GetString("Id").Trim();
        registry.Hold(held);
        try
        {
            var result = await ReplaceAsync(origin, dest, request, item, cancellationToken);
            await KeepUidAsync(held, dest, cancellationToken);
            return result;
        }
        finally
        {
            registry.Release(held);
        }
    }

    /// <summary>The replacement's id given the old network's registry entry; a replacement that cannot be inspected is left to the next read, which finds it by name.</summary>
    private async Task KeepUidAsync(string oldId, string dest, CancellationToken cancellationToken)
    {
        try
        {
            var fresh = await InspectAsync(dest, cancellationToken);
            registry.Recreated(Resources.ResourceRegistry.Network, oldId, fresh.GetString("Id").Trim(), dest);
        }
        catch (WslcException ex)
        {
            logger.LogInformation("recreate: {Network} could not be inspected to keep its uid: {Message}", dest, ex.Message);
        }
    }

    /// <summary>The recreate itself — the new network, the containers moved, the old one gone — its registry entry held by <see cref="RecreateAsync"/>.</summary>
    private async Task<RecreateNetworkResult> ReplaceAsync(string origin, string dest, CreateNetworkRequest request, JsonElement item, CancellationToken cancellationToken)
    {
        var snapshot = FormOf(item, origin);
        var keys = new HashSet<string>(StringComparer.Ordinal) { origin };
        var id = item.GetString("Id").Trim();
        if (id.Length > 0)
        {
            keys.Add(id);
            keys.Add(id.Length > 12 ? id[..12] : id);
        }

        var attached = await usage.AttachedToAsync(keys, cancellationToken);
        var replacement = request with { Name = dest };

        if (dest != origin)
        {
            await CreateAsync(replacement, cancellationToken);
            var failed = await ConnectAllAsync(dest, attached, cancellationToken);
            await DisconnectAllAsync(origin, attached, cancellationToken);
            try
            {
                await RemoveFromWslcAsync(origin, cancellationToken);
            }
            catch (WslcException ex)
            {
                throw new WslcException($"Replacement {dest} exists, but the previous network {origin} could not be removed: {ex.Message}", ex.Result);
            }

            return new RecreateNetworkResult(dest, attached.Count - failed.Count, failed);
        }

        await DisconnectAllAsync(origin, attached, cancellationToken);
        try
        {
            await RemoveFromWslcAsync(origin, cancellationToken);
        }
        catch (WslcException ex)
        {
            await ConnectAllAsync(origin, attached, cancellationToken);
            throw new WslcException($"Cannot recreate: failed to remove network {origin}: {ex.Message}", ex.Result);
        }

        try
        {
            await CreateAsync(replacement, cancellationToken);
        }
        catch (WslcException createError)
        {
            var note = " Previous network was restored so you can inspect and fix settings.";
            try
            {
                await CreateAsync(snapshot with { Option = "" }, cancellationToken);
                await ConnectAllAsync(origin, attached, cancellationToken);
            }
            catch (Exception ex) when (ex is WslcException or ArgumentException)
            {
                note = " Previous network could not be restored.";
            }

            throw new WslcException($"Cannot recreate: failed to create replacement: {createError.Message}.{note}", createError.Result);
        }

        var connectFailed = await ConnectAllAsync(dest, attached, cancellationToken);
        return new RecreateNetworkResult(dest, attached.Count - connectFailed.Count, connectFailed);
    }

    /// <summary>The containers that could not be connected; a failure does not stop the others.</summary>
    private async Task<List<string>> ConnectAllAsync(string network, IReadOnlyList<string> containers, CancellationToken cancellationToken)
    {
        var failed = new List<string>();
        foreach (var container in containers)
        {
            try
            {
                await ConnectAsync(network, container, "", cancellationToken);
            }
            catch (WslcException)
            {
                failed.Add(container);
            }
        }

        return failed;
    }

    private async Task DisconnectAllAsync(string network, IReadOnlyList<string> containers, CancellationToken cancellationToken)
    {
        foreach (var container in containers)
        {
            try
            {
                await DisconnectAsync(network, container, cancellationToken);
            }
            catch (WslcException)
            {
                // Already gone from it: nothing to undo.
            }
        }
    }

    private async Task<JsonElement> InspectAsync(string name, CancellationToken cancellationToken)
    {
        var result = await wslc.RunAsync(["network", "inspect", name, "--format", "json"], cancellationToken: cancellationToken);
        var item = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
        return item.ValueKind == JsonValueKind.Object
            ? item
            : throw new WslcException($"network inspect {name} returned no network data", result);
    }

    /// <summary>The settings a network was created with, as the edit form shows them.</summary>
    internal static CreateNetworkRequest FormOf(JsonElement item, string fallbackName)
    {
        var ipam = item.TryGetProperty("IPAM", out var i) && i.TryGetProperty("Config", out var configs) && configs.ValueKind == JsonValueKind.Array
            ? configs.EnumerateArray().FirstOrDefault()
            : default;
        var name = item.GetString("Name").Trim();
        var driver = item.GetString("Driver").Trim();
        return new CreateNetworkRequest(
            name.Length > 0 ? name : fallbackName,
            driver.Length > 0 ? driver : "bridge",
            ipam.ValueKind == JsonValueKind.Object ? ipam.GetString("Subnet") : "",
            ipam.ValueKind == JsonValueKind.Object ? ipam.GetString("Gateway") : "",
            ipam.ValueKind == JsonValueKind.Object ? ipam.GetString("IPRange") : "",
            item.TryGetProperty("Internal", out var internalFlag) && internalFlag.ValueKind == JsonValueKind.True,
            Pairs(item, "Labels"),
            Pairs(item, "Options"));
    }

    /// <summary><c>{"a":"1","b":"2"}</c> → <c>a=1, b=2</c>: commas between pairs, which the create splits again.</summary>
    private static string Pairs(JsonElement item, string property) =>
        item.TryGetProperty(property, out var map) && map.ValueKind == JsonValueKind.Object
            ? ValueList.Join(map.EnumerateObject().Select(p => $"{p.Name}={(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())}"))
            : "";

    internal static List<string> CreateArgs(CreateNetworkRequest request)
    {
        var name = WslcArgs.Require(request.Name, "network name");
        var args = new List<string> { "network", "create" }
            .Option("--driver", request.Driver)
            .Option("--subnet", request.Subnet)
            .Option("--gateway", request.Gateway)
            .Option("--ip-range", request.IpRange)
            .Flag("--internal", request.Internal)
            .Pairs("--label", request.Label)
            .Pairs("--opt", request.Option);
        args.Add(name);
        return args;
    }

    /// <summary>WSLC accepts <c>--ip</c> only on a user-defined network; on bridge/host/none it is dropped.</summary>
    internal static List<string> ConnectArgs(string network, string container, string ip)
    {
        var networkName = WslcArgs.Require(network, "network name");
        var args = new List<string> { "network", "connect" }
            .Option("--ip", NetworkSummary.IsBuiltIn(networkName) ? "" : ip);
        args.Add(networkName);
        args.Add(WslcArgs.Require(container, "container"));
        return args;
    }

    internal static NetworkSummary ToSummary(JsonElement row, ContainerUsage usage)
    {
        var id = row.GetString("ID").Trim();
        var name = row.GetString("Name").Trim();
        var use = ContainerUsage.Of(usage.NetworkUse, name, id);
        return new NetworkSummary(
            Id: id.Length > 12 ? id[..12] : id,
            Name: name,
            Driver: row.GetString("Driver"),
            Scope: row.GetString("Scope"),
            Subnet: "",
            Gateway: "",
            Internal: row.GetString("Internal").Equals("true", StringComparison.OrdinalIgnoreCase),
            InUse: use.Containers > 0,
            Containers: use.Containers,
            Running: use.Running,
            NetReceivedBytes: use.NetReceivedBytes,
            NetSentBytes: use.NetSentBytes);
    }

    /// <summary>First IPAM config of <c>network inspect</c>; an inspect failure leaves both empty rather than hiding the row.</summary>
    private async Task<(string Subnet, string Gateway)> IpamAsync(string name, CancellationToken cancellationToken)
    {
        if (name.Length == 0)
        {
            return ("", "");
        }

        try
        {
            var result = await wslc.RunAsync(["network", "inspect", name, "--format", "json"], cancellationToken: cancellationToken);
            var item = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("IPAM", out var ipam)
                || !ipam.TryGetProperty("Config", out var configs)
                || configs.ValueKind != JsonValueKind.Array)
            {
                return ("", "");
            }

            var first = configs.EnumerateArray().FirstOrDefault();
            return (first.GetString("Subnet"), first.GetString("Gateway"));
        }
        catch (Exception ex) when (ex is WslcException or JsonException)
        {
            logger.LogWarning("network inspect {Name} unavailable: {Message}", name, ex.Message);
            return ("", "");
        }
    }
}
