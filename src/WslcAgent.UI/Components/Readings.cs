using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>How a reading is written: the muted dash every list and card shows when there is none, and the figures of a volume's or a network's dials.</summary>
public static class Readings
{
    public static string Dash(string value) => string.IsNullOrEmpty(value) ? "—" : value;

    /// <summary>The containers' CPU as Home writes it: what they use of all there is, "12.00% / 800%".</summary>
    public static string Cpu(HomeRuntime runtime) =>
        $"{runtime.CpuUsedPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}% / {runtime.CpuTotalPercent}%";

    /// <summary>The figure inside a traffic dial, compact; nothing for a resource no container uses, so the dial shows its dash.</summary>
    public static string Traffic(bool inUse, long bytes) => inUse ? ChartBytes.Compact(bytes) : "";

    /// <summary>A traffic dial's tooltip: what the resource's containers moved, of what every container did.</summary>
    /// <param name="where">How the containers relate to the resource: "that mount this volume", "on this network".</param>
    public static string TrafficTitle(long bytes, string verb, int containers, string where, long total) =>
        $"{ChartBytes.Compact(bytes)} {verb} by the {containers} container{(containers == 1 ? "" : "s")} {where}, of {ChartBytes.Compact(total)} by all";
}
