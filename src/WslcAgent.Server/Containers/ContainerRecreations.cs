using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The containers being recreated right now, so the list does not lose them
/// in the middle. A change to a container is a
/// recreate — stop, rehearse, remove, launch again — and a list read between
/// the remove and the launch had no row for it: it vanished from every client
/// and came back. While a recreate runs, its row stays, in the state
/// <see cref="State"/>: the container's own while it is still there, the last
/// one seen once it is gone, and the new one's as soon as that exists, under
/// the uid the registry keeps for it throughout, so nothing moves or doubles.
/// </summary>
public sealed class ContainerRecreations
{
    /// <summary>What a row says while its container is being recreated.</summary>
    public const string State = ContainerSummary.Recreating;

    private readonly Dictionary<string, Recreation> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ContainerSummary> _lastSeen = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>One recreate: the container it started from, the name it will have, its uid, and its row as last seen.</summary>
    private sealed record Recreation(string OldId, string NewName, int Uid, ContainerSummary? Last);

    /// <summary>A recreate of <paramref name="oldId"/> begins; <paramref name="newName"/> is what the new container will be called.</summary>
    public void Begin(string oldId, string newName, int uid)
    {
        lock (_gate)
        {
            _running[oldId] = new Recreation(oldId, newName, uid, _lastSeen.Values.FirstOrDefault(row => SameId(row.Id, oldId)));
        }
    }

    /// <summary>The recreate is over, done or undone: the list is wslc's own again.</summary>
    public void End(string oldId)
    {
        lock (_gate)
        {
            _running.Remove(oldId);
        }
    }

    /// <summary>
    /// The user's containers as <c>wslc</c> listed them, with every recreate
    /// under way shown as one row in <see cref="State"/>. A list of every
    /// container (<paramref name="complete"/>) is also remembered, so a
    /// recreate that begins has a row to show once its container is gone, and
    /// only such a list adds that row: a list of the running ones never had a
    /// stopped container in it.
    /// </summary>
    public IReadOnlyList<ContainerSummary> Overlay(IReadOnlyList<ContainerSummary> rows, bool complete)
    {
        lock (_gate)
        {
            if (_running.Count == 0)
            {
                if (complete)
                {
                    _lastSeen.Clear();
                    foreach (var row in rows)
                    {
                        _lastSeen[row.Id] = row;
                    }
                }

                return rows;
            }

            var shown = rows.ToList();
            foreach (var recreation in _running.Values)
            {
                // The new container once it exists, the old one until it goes.
                var index = shown.FindIndex(row => row.Name == recreation.NewName && !SameId(row.Id, recreation.OldId));
                if (index < 0)
                {
                    index = shown.FindIndex(row => SameId(row.Id, recreation.OldId));
                }

                if (index >= 0)
                {
                    shown[index] = Recreating(shown[index], recreation.Uid);
                }
                else if (complete && recreation.Last is { } last)
                {
                    shown.Add(Recreating(last, recreation.Uid));
                }
            }

            return shown;
        }
    }

    private static ContainerSummary Recreating(ContainerSummary row, int uid) => row with
    {
        State = State,
        Status = "Recreating…",
        CpuPercent = "",
        MemUsage = "",
        MemPercent = "",
        Uid = uid > 0 ? uid : row.Uid,
    };

    /// <summary>A list row's id is the short form, an inspect's the full one: either begins the other.</summary>
    private static bool SameId(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal));
}
