using System.Globalization;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// A container's four charts as its Stats tab draws them — their lines, as
/// the legends name them, and each one's latest value as its header writes
/// it — shared by that tab and the dashboard's charts given the container as their
/// subject, so the two say the same.
/// </summary>
public static class ContainerSeries
{
    public static readonly LineChart.Line[] Cpu = [new("CPU")];

    public static readonly LineChart.Line[] Memory = [new("Memory")];

    public static readonly LineChart.Line[] Disk = [new("Data read"), new("Data write", Secondary: true)];

    public static readonly LineChart.Line[] Network = [new("Data received"), new("Data sent", Secondary: true)];

    public static string CpuText(ContainerStats? stats) =>
        stats is null ? "—" : $"{stats.CpuPercent.ToString("0.00", CultureInfo.InvariantCulture)}%";

    public static string MemoryText(ContainerStats? stats) =>
        stats is null ? "—" : $"{ChartBytes.Compact(stats.MemoryUsedBytes)} / {(stats.MemoryLimitBytes > 0 ? ChartBytes.Compact(stats.MemoryLimitBytes) : "—")}";

    public static string DiskText(ContainerStats? stats) =>
        stats is null ? "—" : $"{ChartBytes.Compact(stats.DiskReadBytes)} / {ChartBytes.Compact(stats.DiskWriteBytes)}";

    public static string NetworkText(ContainerStats? stats) =>
        stats is null ? "—" : $"{ChartBytes.Compact(stats.NetworkReceivedBytes)} / {ChartBytes.Compact(stats.NetworkSentBytes)}";
}
