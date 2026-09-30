using System.Text.Json;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The rows of <c>container stats --format json</c>, fetched once and joined to
/// list rows: the containers list draws its CPU, memory, disk and network from
/// them, and the usage scanner sums the disk and network of the containers on
/// each volume and network. Stats that fail leave no rows rather than hiding a
/// list.
/// </summary>
public static class StatsRows
{
    public static async Task<IReadOnlyList<JsonElement>> FetchAsync(IWslcRunner wslc, bool all, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var args = all ? new[] { "container", "stats", "--all", "--format", "json" } : ["container", "stats", "--format", "json"];
            var result = await wslc.RunAsync(args, cancellationToken: cancellationToken);
            return WslcJson.ParseRows(result.Stdout);
        }
        catch (WslcException ex)
        {
            logger.LogWarning("container stats unavailable: {Message}", ex.Message);
            return [];
        }
    }

    /// <summary>The stats row whose full id starts with a list row's short id, or the same name; <c>default</c> when there is none.</summary>
    public static JsonElement Find(IReadOnlyList<JsonElement> stats, string id, string name) =>
        stats.FirstOrDefault(s =>
            (id.Length > 0 && s.GetString("ID").StartsWith(id, StringComparison.OrdinalIgnoreCase))
            || (name.Length > 0 && s.GetString("Name") == name));
}
