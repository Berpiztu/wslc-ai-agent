using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The Home page's data. Every probe fails on
/// its own and is reported as such, so one CLI error never blanks the page; the
/// metrics sum the containers' stats rows, and storage is the image catalog and
/// the session VHDX files (allocation, not free host disk).
/// </summary>
public sealed class HomeService(
    IWslcRunner wslc,
    ISelectedSession selected,
    IContainerService containers,
    IImageService images,
    INetworkService networks,
    IVolumeService volumes) : IHomeService
{
    public async Task<HomeOverview> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var containerTask = Probe(() => containers.ListAsync(all: true, cancellationToken: cancellationToken));
        var imageTask = Probe(() => images.ListAsync(cancellationToken));
        var networkTask = Probe(() => networks.ListAsync(cancellationToken));
        var volumeTask = Probe(() => volumes.ListAsync(cancellationToken));
        var versionTask = Probe(async () => (await wslc.RunAsync(["version"], cancellationToken: cancellationToken)).Stdout.Trim());
        await Task.WhenAll(containerTask, imageTask, networkTask, volumeTask, versionTask);

        var list = containerTask.Result;
        var aggregate = list?.Aggregate;
        return new HomeOverview(
            Version: versionTask.Result is { Length: > 0 } version ? version : "unavailable",
            Session: selected.Name,
            Containers: list is null ? new HomeContainers(0, 0, true) : new HomeContainers(list.Containers.Count(c => c.IsRunning), list.Containers.Count, false),
            Images: ImagesOf(imageTask.Result),
            Networks: networkTask.Result is { } n ? new HomeCount(n.Networks.Count, false) : new HomeCount(0, true),
            Volumes: volumeTask.Result is { } v ? new HomeCount(v.Count, false) : new HomeCount(0, true),
            Runtime: aggregate is null
                ? Runtime(0, 0, error: true)
                : Runtime(aggregate.CpuUsedPercent, aggregate.MemoryUsedBytes, error: false));
    }

    public async Task<HomeRuntime> RuntimeAsync(CancellationToken cancellationToken = default) =>
        await StatsRowsAsync(cancellationToken) is { } rows
            ? Runtime(
                Math.Round(rows.Sum(r => StatsParsing.Percent(r.GetString("CPUPerc"))), 2),
                rows.Sum(r => StatsParsing.UsedBytes(r.GetString("MemUsage"))),
                error: false,
                // Every row names the same limit after its slash: the session's memory.
                memoryTotal: rows.Select(r => StatsParsing.Pair(r.GetString("MemUsage")).Second).DefaultIfEmpty(0).Max())
            : Runtime(0, 0, error: true);

    public HomeDisk Disk()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var root = Path.GetPathRoot(SessionStoreReader.Read().BasePath);
            if (string.IsNullOrEmpty(root))
            {
                return new HomeDisk("", 0, 0, now, Error: true);
            }

            var drive = new DriveInfo(root);
            return new HomeDisk(drive.Name, drive.TotalSize - drive.TotalFreeSpace, drive.TotalSize, now, Error: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new HomeDisk("", 0, 0, now, Error: true);
        }
    }

    public async Task<HomeIo> IoAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (await StatsRowsAsync(cancellationToken) is not { } rows)
        {
            return new HomeIo(0, 0, 0, 0, now, Error: true);
        }

        var disk = rows.Select(r => StatsParsing.Pair(r.GetString("BlockIO"))).ToList();
        var network = rows.Select(r => StatsParsing.Pair(r.GetString("NetIO"))).ToList();
        return new HomeIo(disk.Sum(p => p.First), disk.Sum(p => p.Second), network.Sum(p => p.First), network.Sum(p => p.Second), now, Error: false);
    }

    private static HomeImages ImagesOf(ImageListResponse? list) =>
        list is null ? new HomeImages(0, 0, true) : new HomeImages(list.Aggregate.Count, list.Aggregate.SizeBytes, false);

    private static HomeRuntime Runtime(double cpu, long memory, bool error, long memoryTotal = 0)
    {
        var cpus = Math.Max(1, Environment.ProcessorCount);
        return new HomeRuntime(cpu, cpus * 100, cpus, memory, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), error, memoryTotal);
    }

    /// <summary><c>container stats --all</c>, or null when the CLI could not say.</summary>
    private Task<IReadOnlyList<JsonElement>?> StatsRowsAsync(CancellationToken cancellationToken) =>
        Probe<IReadOnlyList<JsonElement>>(async () =>
            WslcJson.ParseRows((await wslc.RunAsync(["container", "stats", "--all", "--format", "json"], cancellationToken: cancellationToken)).Stdout).ToList());

    /// <summary>The probe's answer, or null when the CLI failed, is missing or timed out: the card shows "Unavailable".</summary>
    private static async Task<T?> Probe<T>(Func<Task<T>> probe) where T : class
    {
        try
        {
            return await probe();
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            return null;
        }
    }
}
