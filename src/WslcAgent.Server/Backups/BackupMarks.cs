using System.Collections.Concurrent;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Backups;

/// <summary>
/// The containers being backed up or restored now, by name, each with what is
/// done to it: the containers list carries it on each one's row
/// (<see cref="ContainerSummary.BackingUp"/>), as it carries a patch or an
/// image update, so everything that shows a container shows it turning: its
/// card's ring, its table row's bar, its slot in a project's window, the
/// dashboard's objects. Written by a container's backups as each job begins
/// and ends, its own or one a project's drives.
/// </summary>
public sealed class BackupMarks
{
    private readonly ConcurrentDictionary<string, string> _marks = new(StringComparer.Ordinal);

    public void Begin(string container, string kind) => _marks[container] = kind == BackupJob.Restore ? "Restoring" : "Backing up";

    public void End(string container) => _marks.TryRemove(container, out _);

    /// <summary>The containers list's rows, each being backed up or restored saying so.</summary>
    public IReadOnlyList<ContainerSummary> Annotate(IReadOnlyList<ContainerSummary> containers) =>
        _marks.IsEmpty ? containers : [.. containers.Select(row => _marks.TryGetValue(row.Name, out var doing) ? row with { BackingUp = doing } : row)];
}
