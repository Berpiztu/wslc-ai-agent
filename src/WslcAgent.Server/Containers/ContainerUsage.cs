using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// What the containers on one resource add up to: how many there are, how many
/// run, and the disk and network they have moved (each container's whole
/// figures, as <c>stats</c> reports them, counted once per resource it is on).
/// </summary>
public sealed record UsageCount(int Containers, int Running, long DiskReadBytes, long DiskWriteBytes, long NetReceivedBytes, long NetSentBytes)
{
    public static readonly UsageCount Zero = new(0, 0, 0, 0, 0, 0);

    /// <summary>This plus one more container with these figures.</summary>
    public UsageCount Plus(bool running, long diskRead, long diskWrite, long received, long sent) =>
        new(Containers + 1, Running + (running ? 1 : 0), DiskReadBytes + diskRead, DiskWriteBytes + diskWrite, NetReceivedBytes + received, NetSentBytes + sent);
}

/// <summary>
/// What the containers (any state) currently reference: the "in use" dot of
/// images, volumes and networks, and the figures behind the dials on the
/// volume and network cards.
/// </summary>
/// <param name="ImageRefs">Image references as the container rows print them (<c>repo:tag</c>, <c>repo</c> or an id).</param>
/// <param name="VolumeUse">Per named volume mounted somewhere, the containers on it; bind paths are not volumes and are left out.</param>
/// <param name="NetworkUse">Per network name the containers are attached to, the containers on it.</param>
/// <param name="Totals">Every container together: what a resource's share is a share of.</param>
public sealed record ContainerUsage(
    IReadOnlySet<string> ImageRefs,
    IReadOnlyDictionary<string, UsageCount> VolumeUse,
    IReadOnlyDictionary<string, UsageCount> NetworkUse,
    UsageCount Totals)
{
    public static readonly ContainerUsage Empty = new(
        new HashSet<string>(), new Dictionary<string, UsageCount>(), new Dictionary<string, UsageCount>(), UsageCount.Zero);

    /// <summary>The count for a resource, by any of its keys (a network answers to its name and its id); zero when nothing uses it.</summary>
    public static UsageCount Of(IReadOnlyDictionary<string, UsageCount> use, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (key.Length > 0 && use.TryGetValue(key, out var count))
            {
                return count;
            }
        }

        return UsageCount.Zero;
    }
}

/// <summary>
/// One <c>container list --all</c> and one <c>container stats --all</c> per
/// page refresh answer "is it in use?", "by how many?" and "moving how much?"
/// for every resource page; 2.9.10 list rows carry <c>Image</c>,
/// <c>Mounts</c>, <c>Networks</c> and <c>State</c>, so no per-container
/// inspect is needed, and the stats rows join them by id or name as the
/// containers list does.
/// </summary>
public sealed partial class ContainerUsageScanner(IWslcRunner wslc, ILogger<ContainerUsageScanner> logger)
{
    /// <summary>A failing scan must not hide a list, so it degrades to "nothing in use"; failing stats only leave the figures at zero.</summary>
    public async Task<ContainerUsage> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var listTask = wslc.RunAsync(["container", "list", "--all", "--format", "json"], cancellationToken: cancellationToken);
            var statsTask = StatsRows.FetchAsync(wslc, all: true, logger, cancellationToken);
            await Task.WhenAll(listTask, statsTask);

            var images = new HashSet<string>(StringComparer.Ordinal);
            var volumes = new Dictionary<string, UsageCount>(StringComparer.Ordinal);
            var networks = new Dictionary<string, UsageCount>(StringComparer.Ordinal);
            var totals = UsageCount.Zero;
            foreach (var row in WslcJson.ParseRows(listTask.Result.Stdout))
            {
                var running = row.GetString("State").Trim().Equals("running", StringComparison.OrdinalIgnoreCase);
                var stats = StatsRows.Find(statsTask.Result, row.GetString("ID").Trim(), row.GetString("Names").Trim().TrimStart('/'));
                var (diskRead, diskWrite) = stats.ValueKind == JsonValueKind.Object ? StatsParsing.Pair(stats.GetString("BlockIO")) : (0, 0);
                var (received, sent) = stats.ValueKind == JsonValueKind.Object ? StatsParsing.Pair(stats.GetString("NetIO")) : (0, 0);

                totals = totals.Plus(running, diskRead, diskWrite, received, sent);
                Add(images, row.GetString("Image"));
                foreach (var token in Csv(row.GetString("Mounts")).Where(t => NamedVolume().IsMatch(t)))
                {
                    volumes[token] = volumes.GetValueOrDefault(token, UsageCount.Zero).Plus(running, diskRead, diskWrite, received, sent);
                }

                foreach (var token in Csv(row.GetString("Networks")))
                {
                    networks[token] = networks.GetValueOrDefault(token, UsageCount.Zero).Plus(running, diskRead, diskWrite, received, sent);
                }
            }

            return new ContainerUsage(images, volumes, networks, totals);
        }
        catch (WslcException ex)
        {
            logger.LogWarning("container usage unavailable: {Message}", ex.Message);
            return ContainerUsage.Empty;
        }
    }

    /// <summary>
    /// Names of the containers (any state, the agent's file helpers left out) whose
    /// <c>Networks</c> name one of <paramref name="networkKeys"/>: a network's name
    /// and its ids. What a network recreate disconnects and connects again.
    /// </summary>
    public async Task<IReadOnlyList<string>> AttachedToAsync(IReadOnlySet<string> networkKeys, CancellationToken cancellationToken = default)
    {
        var result = await wslc.RunAsync(["container", "list", "--all", "--format", "json"], cancellationToken: cancellationToken);
        return WslcJson.ParseRows(result.Stdout)
            .Where(row => !ContainerService.IsHelper(row.GetString("Names")) && Csv(row.GetString("Networks")).Any(networkKeys.Contains))
            .Select(row => row.GetString("Names").Trim().TrimStart('/'))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void Add(HashSet<string> set, string value)
    {
        if (value.Length > 0)
        {
            set.Add(value);
        }
    }

    private static IEnumerable<string> Csv(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A volume name; bind mounts print paths (<c>/mnt/…</c>, <c>C:\…</c>) which never match.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
    private static partial Regex NamedVolume();
}
