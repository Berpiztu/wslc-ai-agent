using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Storage;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The File transfers card's pieces (TransfersCardObject): its header's three
/// lines — title, count and hint — each an object of its own, the filter
/// beside them, the header of its lines and its lines. And a dashboard stored
/// while the card was one
/// object: each such object becomes its card again, over the cells it took —
/// its header's lines at the top, one row each where there is room and hidden
/// where there is none, so they can be shown again; the header of its lines
/// under them, its lines the rest — each piece with the object's size and
/// colours. One standing alone gets its card's frame round it. Read by both
/// places the dashboard is kept, the agent and this device.
/// </summary>
public static class TransfersCardPieces
{
    public const string Title = "wslc.host-transfers-title";
    public const string Count = "wslc.host-transfers-count";
    public const string Hint = "wslc.host-transfers-hint";
    public const string Filter = "wslc.host-transfers-filter";
    public const string Head = "wslc.host-transfers-head";
    public const string Lines = "wslc.host-transfers-lines";

    /// <summary>The object the card was, whose kind no longer exists.</summary>
    private const string Whole = "wslc.host-transfers";

    /// <summary>The stored text with every File transfers object made its card of pieces; the text as it was when there is none.</summary>
    public static string? Upgrade(string? stored) =>
        stored is not null && stored.Contains($"\"{Whole}\"", StringComparison.Ordinal) ? DashboardPages.Each(stored, Split) : stored;

    private static DashboardLayout Split(DashboardLayout layout) =>
        layout.Objects.Where(o => o.Type == Whole).ToList().Aggregate(layout, Split);

    private static DashboardLayout Split(DashboardLayout layout, ObjectInstance whole)
    {
        var group = whole.Group is { } id ? layout.FindGroup(id) : null;
        var groupId = group?.Id ?? Guid.NewGuid().ToString("N");
        var headerRows = whole.H >= 5 ? 2 : 1;
        var headRows = whole.H >= 3 ? 1 : 0;
        var linesRows = whole.H - headerRows - headRows;
        var piece = whole with { Group = groupId, Parts = null };
        ObjectInstance Made(string type, int row, int rows) =>
            piece with { Id = Guid.NewGuid().ToString("N"), Type = type, Y = whole.Y + row, H = rows };

        // The header's lines, one row each, the count first where there is
        // room for one line alone; what has no row is hidden at the top.
        string[] lines = headerRows >= 2 ? [Title, Count, Hint] : [Count, Title, Hint];
        var pieces = lines.Select((type, row) => row < headerRows ? Made(type, row, 1) : Made(type, 0, 1) with { Hidden = true }).ToList();
        pieces.Add(Made(Filter, 0, 1) with { Hidden = true });
        if (headRows > 0)
        {
            pieces.Add(Made(Head, headerRows, headRows));
        }

        if (linesRows > 0)
        {
            pieces.Add(Made(Lines, headerRows + headRows, linesRows));
        }

        var next = layout with { Objects = [.. layout.Objects.Where(o => o.Id != whole.Id)] };
        return group is not null
            ? pieces.Aggregate(next, (placed, made) => placed.With(made))
            : next.With(new DashboardGroup(groupId, whole.X, whole.Y, whole.W, whole.H), pieces);
    }
}
