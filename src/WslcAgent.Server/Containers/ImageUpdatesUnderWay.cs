using System.Collections.Concurrent;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The containers whose image update is pulling its new image, and which image:
/// the list marks those rows (<see cref="ContainerSummary.UpdatingTo"/>), so the
/// container asked for shows the pull, not only the image it pulls. Once the
/// pull is over the recreate takes the row (<see cref="ContainerRecreations"/>).
/// </summary>
public sealed class ImageUpdatesUnderWay
{
    private readonly ConcurrentDictionary<string, string> _pulling = new(StringComparer.Ordinal);

    public void Begin(string containerId, string image) => _pulling[containerId] = image;

    public void End(string containerId) => _pulling.TryRemove(containerId, out _);

    /// <summary>The rows with the image each one's update pulls; the rest as they are.</summary>
    public IReadOnlyList<ContainerSummary> Annotate(IReadOnlyList<ContainerSummary> rows)
    {
        if (_pulling.IsEmpty)
        {
            return rows;
        }

        return [.. rows.Select(row => ImageOf(row.Id) is { } image ? row with { UpdatingTo = image } : row)];
    }

    /// <summary>The list's short id and the inspect's long one name the same container.</summary>
    private string? ImageOf(string rowId) =>
        rowId.Length == 0 ? null
        : _pulling.FirstOrDefault(update => update.Key.StartsWith(rowId, StringComparison.Ordinal) || rowId.StartsWith(update.Key, StringComparison.Ordinal)).Value;
}
