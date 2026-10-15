using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// A Compose project's stats as one container's: its containers' CPU, memory,
/// disk and network added up, sampled on the dashboard's beat for the charts,
/// legends and readings given that project as their subject, its last two
/// minutes kept as a container's are. It asks the agent nothing of its own:
/// it adds up what the containers' list already says of each of them, which
/// the chart showing it keeps reading (<see cref="ChartObject"/>), instead of
/// one stats call per container. A sample is the list as it stands, so it is
/// at most one beat behind it. Not taken while the session is stopped, nor
/// before the list is read, nor while the project has no container.
/// </summary>
public sealed class ProjectStatsRead(ContainerRows rows, SessionState session, string project)
    : SampledRead<TimedStats>(TimeSpan.FromSeconds(5))
{
    protected override bool CanRead => session.Active;

    protected override Task<TimedStats?> ReadAsync(CancellationToken cancellationToken)
    {
        var members = (rows.Rows ?? []).Where(container => container.Project == project).ToList();
        return Task.FromResult(members.Count == 0 ? null : new TimedStats(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Sum(members)));
    }

    protected override bool Failed(TimedStats sample) => false;

    /// <summary>
    /// The containers as one: what each uses added up. The memory's limit is
    /// not added: each container's is the machine's, so the project's is the
    /// largest of them, which its chart's axis grows to as a container's does.
    /// </summary>
    private ContainerStats Sum(IReadOnlyList<ContainerSummary> members)
    {
        var memory = members.Select(member => StatsParsing.Pair(member.MemUsage)).ToList();
        var disk = members.Select(member => StatsParsing.Pair(member.DiskIo)).ToList();
        var network = members.Select(member => StatsParsing.Pair(member.NetIo)).ToList();
        return new ContainerStats(
            Id: "",
            Name: project,
            CpuPercent: members.Sum(member => StatsParsing.Percent(member.CpuPercent)),
            MemoryUsedBytes: memory.Sum(pair => pair.First),
            MemoryLimitBytes: memory.Count > 0 ? memory.Max(pair => pair.Second) : 0,
            MemoryPercent: members.Sum(member => StatsParsing.Percent(member.MemPercent)),
            DiskReadBytes: disk.Sum(pair => pair.First),
            DiskWriteBytes: disk.Sum(pair => pair.Second),
            NetworkReceivedBytes: network.Sum(pair => pair.First),
            NetworkSentBytes: network.Sum(pair => pair.Second),
            Pids: 0,
            MemUsage: "",
            DiskIo: "",
            NetIo: "");
    }
}

/// <summary>Every project's stats read, one per project however many objects show it.</summary>
public sealed class ProjectStatsReads(ContainerRows rows, SessionState session) : IDisposable
{
    private readonly Dictionary<string, ProjectStatsRead> _reads = new(StringComparer.Ordinal);

    /// <summary>The read of the project of this name, made the first time it is asked for.</summary>
    public ProjectStatsRead For(string project)
    {
        if (!_reads.TryGetValue(project, out var read))
        {
            read = new ProjectStatsRead(rows, session, project);
            _reads[project] = read;
        }

        return read;
    }

    public void Dispose()
    {
        foreach (var read in _reads.Values)
        {
            read.Dispose();
        }
    }
}
