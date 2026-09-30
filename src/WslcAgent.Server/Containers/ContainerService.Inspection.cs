using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>Stats, the inspect JSON as a file, and a launch form read back from a JSON the user loaded.</summary>
public sealed partial class ContainerService
{
    private static readonly JsonSerializerOptions LaunchRequestJson = new(JsonSerializerDefaults.Web);

    public async Task<ContainerStats> StatsAsync(string container, CancellationToken cancellationToken = default)
    {
        var result = await wslc.RunAsync(["container", "stats", WslcArgs.Require(container, "container"), "--format", "json"], cancellationToken: cancellationToken);
        var row = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
        if (row.ValueKind != JsonValueKind.Object)
        {
            throw new WslcException("container stats printed no row", result);
        }

        var (memoryUsed, memoryLimit) = StatsParsing.Pair(row.GetString("MemUsage"));
        var (diskRead, diskWrite) = StatsParsing.Pair(row.GetString("BlockIO"));
        var (received, sent) = StatsParsing.Pair(row.GetString("NetIO"));
        var id = row.GetString("ID");
        return new ContainerStats(
            id.Length > 12 ? id[..12] : id,
            row.GetString("Name"),
            StatsParsing.Percent(row.GetString("CPUPerc")),
            memoryUsed,
            memoryLimit,
            StatsParsing.Percent(row.GetString("MemPerc")),
            diskRead,
            diskWrite,
            received,
            sent,
            row.TryGetProperty("PIDs", out var pids) && pids.TryGetInt32(out var count) ? count : 0,
            row.GetString("MemUsage"),
            row.GetString("BlockIO"),
            row.GetString("NetIO"));
    }

    public async Task<(string Name, string Json)> InspectJsonAsync(string container, CancellationToken cancellationToken = default)
    {
        var inspection = await InspectAsync(WslcArgs.Require(container, "container"), cancellationToken);
        return (inspection.Name.Length > 0 ? inspection.Name : inspection.Id, inspection.Json);
    }

    /// <summary>
    /// An inspect output (the CLI's list with one object, or that object alone)
    /// gives the form the container was created with; anything else must be a
    /// launch request as this API exports it.
    /// </summary>
    public ContainerLaunchRequest LaunchFormFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("The file is empty.", nameof(json));
        }

        JsonElement first;
        try
        {
            first = WslcJson.ParseRows(json).FirstOrDefault();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"The file is not JSON: {ex.Message}", nameof(json));
        }

        if (first.ValueKind == JsonValueKind.Object && (first.TryGetProperty("Config", out _) || first.TryGetProperty("HostConfig", out _)))
        {
            return ContainerInspection.Parse(new WslcResult(["container", "inspect"], 0, json, "", TimeSpan.Zero)).Form;
        }

        return LaunchRequestOf(first) is { Image.Length: > 0 } request
            ? request
            : throw new ArgumentException("The JSON is neither a container inspect output nor an exported launch request.", nameof(json));
    }

    /// <summary>An object without the required fields is not a request, not an error.</summary>
    private static ContainerLaunchRequest? LaunchRequestOf(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return element.Deserialize<ContainerLaunchRequest>(LaunchRequestJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
