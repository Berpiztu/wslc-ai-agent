namespace WslcAgent.UI.Components;

/// <summary>
/// The order of a list the user has not sorted by a column. <c>wslc</c> lists
/// what was created or started last first: a container started, or recreated
/// by a change to it, and an image pulled again jumped to the top. So a list
/// whose rows all carry the agent's own number (its resource registry: given
/// in the order things first appeared, kept through a recreate, kept on disk)
/// is shown in that order: every row keeps its place, on every client and
/// after a reload, and something new comes last. A list without those numbers
/// keeps the order it was shown in while the application runs, a new row at
/// the place the agent gives it. A sort the user sets on a column is applied
/// over either.
/// </summary>
public sealed class ListOrder
{
    private readonly Dictionary<string, IReadOnlyList<string>> _lists = new(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="incoming"/> in the order of <paramref name="ageOf"/>,
    /// the agent's number for each row, when every row has one (above 0);
    /// otherwise in the order <paramref name="list"/> was last shown in.
    /// <paramref name="placeOf"/> is the identity a row keeps through what
    /// moved it — a container's recreate changes its id, not its number in the
    /// agent's registry. An empty answer (a session stopped, a list emptied)
    /// leaves the order remembered for when the rows come back.
    /// </summary>
    public IReadOnlyList<T> Keep<T>(string list, IReadOnlyList<T> incoming, Func<T, string> placeOf, Func<T, int> ageOf)
    {
        if (incoming.Count == 0)
        {
            return incoming;
        }

        var ordered = incoming.All(item => ageOf(item) > 0) ? incoming.OrderBy(ageOf).ToList()
            : _lists.TryGetValue(list, out var before) ? Arrange(before, incoming, placeOf)
            : incoming;
        _lists[list] = [.. ordered.Select(placeOf)];
        return ordered;
    }

    /// <summary>
    /// The new rows stay at the indexes the agent put them at; the known ones
    /// fill the other slots in the order they had.
    /// </summary>
    private static IReadOnlyList<T> Arrange<T>(IReadOnlyList<string> before, IReadOnlyList<T> incoming, Func<T, string> placeOf)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var place in before)
        {
            rank.TryAdd(place, rank.Count);
        }

        var result = new T[incoming.Count];
        var taken = new bool[incoming.Count];
        for (var i = 0; i < incoming.Count; i++)
        {
            if (!rank.ContainsKey(placeOf(incoming[i])))
            {
                result[i] = incoming[i];
                taken[i] = true;
            }
        }

        var slot = 0;
        foreach (var known in incoming.Where(item => rank.ContainsKey(placeOf(item))).OrderBy(item => rank[placeOf(item)]))
        {
            while (taken[slot])
            {
                slot++;
            }

            result[slot++] = known;
        }

        return result;
    }
}
