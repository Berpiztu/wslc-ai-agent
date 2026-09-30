using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Networks;

/// <summary>
/// The network map's data: every
/// network, and one inspect per container (running or stopped, the file helpers
/// left out) for the networks it is on and its address on each. Eight inspects
/// at a time; a container that cannot be inspected is left off the map.
/// </summary>
public sealed class NetworkTopologyReader(IWslcRunner wslc, INetworkService networks) : INetworkTopology
{
    private const int Parallel = 8;

    /// <summary>The MCP tool asks for the same map the Networks page draws.</summary>
    public Task<NetworkTopology> TopologyAsync(CancellationToken cancellationToken = default) => ReadAsync(cancellationToken);

    public async Task<NetworkTopology> ReadAsync(CancellationToken cancellationToken = default)
    {
        var networkList = await networks.ListAsync(cancellationToken);
        var rows = await ContainerRowsAsync(cancellationToken);

        using var gate = new SemaphoreSlim(Parallel);
        var scanned = await Task.WhenAll(rows.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await ContainerAsync(id, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));

        return new NetworkTopology(networkList.Networks, scanned.OfType<TopologyContainer>().ToList());
    }

    /// <summary>
    /// The networks a container is on, from <c>NetworkSettings.Networks</c>, plus
    /// <c>HostConfig.NetworkMode</c> when it names one the list lacks. The address is
    /// the requested static one, else the assigned one, without its prefix length.
    /// </summary>
    internal static IReadOnlyList<TopologyAttachment> Attachments(JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var attachments = new List<TopologyAttachment>();
        if (root.TryGetProperty("NetworkSettings", out var settings) && settings.ValueKind == JsonValueKind.Object
            && settings.TryGetProperty("Networks", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var network in map.EnumerateObject())
            {
                var name = network.Name.Trim();
                if (name.Length == 0 || name == "default" || !seen.Add(name))
                {
                    continue;
                }

                attachments.Add(new TopologyAttachment(name, EndpointIp(network.Value)));
            }
        }

        if (root.TryGetProperty("HostConfig", out var host) && host.ValueKind == JsonValueKind.Object)
        {
            var mode = host.GetString("NetworkMode").Trim();
            if (mode.Length > 0 && mode != "default" && !mode.Contains(':') && !seen.Contains(mode))
            {
                attachments.Add(new TopologyAttachment(mode, ""));
            }
        }

        return attachments;
    }

    private static string EndpointIp(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        var requested = entry.TryGetProperty("IPAMConfig", out var ipam) && ipam.ValueKind == JsonValueKind.Object
            ? ipam.GetString("IPv4Address").Trim()
            : "";
        var address = requested.Length > 0 ? requested : entry.GetString("IPAddress").Trim();
        return address.Split('/', 2)[0];
    }

    private async Task<IReadOnlyList<string>> ContainerRowsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await wslc.RunAsync(["container", "list", "--all", "--format", "json"], cancellationToken: cancellationToken);
            return WslcJson.ParseRows(result.Stdout)
                .Where(row => !ContainerService.IsHelper(row.GetString("Names")))
                .Select(row => row.GetString("ID").Trim() is { Length: > 0 } id ? id : row.GetString("Names").Trim())
                .Where(id => id.Length > 0)
                .ToList();
        }
        catch (WslcException)
        {
            return [];
        }
    }

    private async Task<TopologyContainer?> ContainerAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await wslc.RunAsync(["container", "inspect", id, "--format", "json"], cancellationToken: cancellationToken);
            var inspection = ContainerInspection.Parse(result);
            var root = WslcJson.ParseRows(result.Stdout).First();
            var fullId = inspection.Id.Length > 0 ? inspection.Id : id;
            return new TopologyContainer(
                inspection.Name.Length > 0 ? inspection.Name : fullId[..Math.Min(12, fullId.Length)],
                fullId,
                inspection.State,
                Attachments(root));
        }
        catch (Exception ex) when (ex is WslcException or JsonException)
        {
            return null;
        }
    }
}
