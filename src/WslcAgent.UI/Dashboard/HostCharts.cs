using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>What a chart draws: its title, how it writes a value, its lines and their history, its last value, and the ceiling its axis grows to.</summary>
public sealed record HostChartData(
    string Title,
    string Format,
    double? YMax,
    IReadOnlyList<LineChart.Line> Series,
    IReadOnlyList<long> Times,
    IReadOnlyList<double[]> Values,
    string Value,
    double ForceYMax = 0);

/// <summary>
/// The four charts — CPU, memory, disk, network — each worked out once from
/// the history its read keeps: the host's, or a container's, as its Stats
/// tab does. A chart object draws it,
/// and the legend and the summary of the same chart and subject write their
/// lines and values from the same.
/// </summary>
public static class HostCharts
{
    public const string Cpu = "cpu";

    public const string Memory = "memory";

    public const string Disk = "disk";

    public const string Network = "network";

    /// <summary>Whether this chart draws the host's runtime (CPU, memory) rather than its I/O (disk, network).</summary>
    public static bool FromRuntime(string chart) => chart is Cpu or Memory;

    /// <summary>The host's chart of this key.</summary>
    public static HostChartData Of(string chart, HostRuntimeRead runtime, HostIoRead io) => chart switch
    {
        Cpu => new("CPU", "percent", 100, HostSeries.Cpu, Times(runtime.History),
            [[.. runtime.History.Select(s => s.CpuUsedPercent)]],
            runtime.Value is { Error: false } r ? Readings.Cpu(r) : "—"),
        Memory => new("Memory", "bytes", null, HostSeries.Memory, Times(runtime.History),
            [[.. runtime.History.Select(s => (double)s.MemoryUsedBytes)]],
            runtime.Value is { Error: false } m ? Bytes.Humanize(m.MemoryUsedBytes) : "—"),
        Disk => new("Disk", "bytes", null, HostSeries.Disk, Times(io.History),
            [[.. io.History.Select(s => (double)s.DiskReadBytes)], [.. io.History.Select(s => (double)s.DiskWriteBytes)]],
            DiskActivity(io.Value)),
        _ => new("Network", "bytes", null, HostSeries.Network, Times(io.History),
            [[.. io.History.Select(s => (double)s.NetworkReceivedBytes)], [.. io.History.Select(s => (double)s.NetworkSentBytes)]],
            NetworkActivity(io.Value)),
    };

    /// <summary>A container's chart of this key, as its Stats tab draws it: its memory's axis grows to the container's limit.</summary>
    public static HostChartData Of(string chart, ContainerStatsRead read)
    {
        var history = read.History.Select(sample => sample.Stats).ToList();
        long[] times = [.. read.History.Select(sample => sample.Timestamp)];
        var last = read.Value?.Stats;
        return chart switch
        {
            Cpu => new("CPU", "percent", 100, ContainerSeries.Cpu, times,
                [[.. history.Select(s => s.CpuPercent)]], ContainerSeries.CpuText(last)),
            Memory => new("Memory", "bytes", null, ContainerSeries.Memory, times,
                [[.. history.Select(s => (double)s.MemoryUsedBytes)]], ContainerSeries.MemoryText(last),
                history.LastOrDefault(s => s.MemoryLimitBytes > 0)?.MemoryLimitBytes ?? 0),
            Disk => new("Disk", "bytes", null, ContainerSeries.Disk, times,
                [[.. history.Select(s => (double)s.DiskReadBytes)], [.. history.Select(s => (double)s.DiskWriteBytes)]], ContainerSeries.DiskText(last)),
            _ => new("Network", "bytes", null, ContainerSeries.Network, times,
                [[.. history.Select(s => (double)s.NetworkReceivedBytes)], [.. history.Select(s => (double)s.NetworkSentBytes)]], ContainerSeries.NetworkText(last)),
        };
    }

    /// <summary>What the containers read and wrote, "1.2 GB / 300 MB"; a dash until it is known.</summary>
    public static string DiskActivity(HomeIo? io) =>
        io is { Error: false } d ? $"{Bytes.Humanize(d.DiskReadBytes)} / {Bytes.Humanize(d.DiskWriteBytes)}" : "—";

    /// <summary>What the containers received and sent; a dash until it is known.</summary>
    public static string NetworkActivity(HomeIo? io) =>
        io is { Error: false } n ? $"{Bytes.Humanize(n.NetworkReceivedBytes)} / {Bytes.Humanize(n.NetworkSentBytes)}" : "—";

    private static long[] Times(IEnumerable<HomeRuntime> history) => [.. history.Select(s => s.Timestamp)];

    private static long[] Times(IEnumerable<HomeIo> history) => [.. history.Select(s => s.Timestamp)];
}
