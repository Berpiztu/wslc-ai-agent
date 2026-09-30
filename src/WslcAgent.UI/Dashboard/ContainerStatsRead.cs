using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>A container's stats as sampled, with when: the agent's reading carries no time of its own.</summary>
public sealed record TimedStats(long Timestamp, ContainerStats Stats);

/// <summary>
/// One container's stats — its CPU, its memory and limit, what it has read
/// and written, received and sent — sampled on the dashboard's beat for the
/// charts, legends and readings given that container as their subject, its
/// last two minutes kept as the host's are.
/// Not asked while the session is stopped.
/// </summary>
public sealed class ContainerStatsRead(WslcAgentApi api, SessionState session, string container)
    : SampledRead<TimedStats>(TimeSpan.FromSeconds(5))
{
    protected override bool CanRead => session.Active;

    protected override async Task<TimedStats?> ReadAsync(CancellationToken cancellationToken) =>
        new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), await api.GetContainerStatsAsync(container, cancellationToken));

    protected override bool Failed(TimedStats sample) => false;
}

/// <summary>Every container's stats read, one per container however many objects show it.</summary>
public sealed class ContainerStatsReads(WslcAgentApi api, SessionState session) : IDisposable
{
    private readonly Dictionary<string, ContainerStatsRead> _reads = [];

    /// <summary>The read of the container of this id, made the first time it is asked for.</summary>
    public ContainerStatsRead For(string container)
    {
        if (!_reads.TryGetValue(container, out var read))
        {
            read = new ContainerStatsRead(api, session, container);
            _reads[container] = read;
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
