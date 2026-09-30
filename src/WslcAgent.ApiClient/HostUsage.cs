using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.ApiClient;

/// <summary>
/// The host's readings as percents, worked out in one place for the dashboard,
/// its alarms and the agent's notifications alike, so what turns a dial red
/// and what wakes the user are the same number.
/// </summary>
public static class HostUsage
{
    /// <summary>The host's CPU used of all there is, in percent; null while it is not known.</summary>
    public static double? CpuPercent(HomeRuntime? runtime) =>
        runtime is { Error: false, CpuTotalPercent: > 0 } r ? 100.0 * r.CpuUsedPercent / r.CpuTotalPercent : null;

    /// <summary>The containers' memory used of the session's, in percent; null while it is not known.</summary>
    public static double? MemoryPercent(HomeRuntime? runtime) =>
        runtime is { Error: false, MemoryTotalBytes: > 0 } m ? 100.0 * m.MemoryUsedBytes / m.MemoryTotalBytes : null;

    /// <summary>The drive's used share, in percent; null while it is not known.</summary>
    public static double? DiskPercent(HomeDisk? disk) =>
        disk is { Error: false, TotalBytes: > 0 } d ? 100.0 * d.UsedBytes / d.TotalBytes : null;
}
