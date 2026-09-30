namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// One sample of <c>GET /api/v1/containers/{id}/stats</c>: the CLI's stats row
/// read back into numbers (the Stats view charts them) plus the strings the
/// CLI printed, for display.
/// </summary>
public sealed record ContainerStats(
    string Id,
    string Name,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryLimitBytes,
    double MemoryPercent,
    long DiskReadBytes,
    long DiskWriteBytes,
    long NetworkReceivedBytes,
    long NetworkSentBytes,
    int Pids,
    string MemUsage,
    string DiskIo,
    string NetIo);
