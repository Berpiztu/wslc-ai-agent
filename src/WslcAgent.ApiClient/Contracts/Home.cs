namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/home</c>: the overview cards. A part that failed says so and leaves the others standing.</summary>
public sealed record HomeOverview(
    string Version,
    string Session,
    HomeContainers Containers,
    HomeImages Images,
    HomeCount Networks,
    HomeCount Volumes,
    HomeRuntime Runtime);

public sealed record HomeContainers(int Running, int Total, bool Error);

public sealed record HomeImages(int Count, long SizeBytes, bool Error);

public sealed record HomeCount(int Count, bool Error);

/// <summary>Aggregate CPU and memory of the containers (<c>GET /api/v1/home/metrics/runtime</c>, and the overview's two cards).</summary>
/// <param name="CpuUsedPercent">Sum of the containers' CPU percentages.</param>
/// <param name="CpuTotalPercent">100 per logical CPU of the agent's machine.</param>
/// <param name="Timestamp">When the sample was taken (Unix milliseconds).</param>
/// <param name="MemoryTotalBytes">The memory the containers share — the session's, as <c>container stats</c> prints it after the slash — or 0 when no container runs to say it: what the memory alarm is a share of.</param>
public sealed record HomeRuntime(double CpuUsedPercent, int CpuTotalPercent, int CpuCount, long MemoryUsedBytes, long Timestamp, bool Error, long MemoryTotalBytes = 0);

/// <summary>
/// The Windows drive WSLC keeps its sessions' VHDX files on
/// (<c>GET /api/v1/home/metrics/disk</c>): what is used of it and its size,
/// what the Disk space card shows and its alarm watches — the disk that fills
/// and stops everything, not the virtual disks' allocation.
/// </summary>
/// <param name="Drive">The drive's root, <c>C:\</c>.</param>
/// <param name="Timestamp">When it was read (Unix milliseconds).</param>
public sealed record HomeDisk(string Drive, long UsedBytes, long TotalBytes, long Timestamp, bool Error);

/// <summary>Aggregate container disk and network I/O (<c>GET /api/v1/home/metrics/io</c>), as the CLI's cumulative counters.</summary>
public sealed record HomeIo(long DiskReadBytes, long DiskWriteBytes, long NetworkReceivedBytes, long NetworkSentBytes, long Timestamp, bool Error);
