using Berpiztu.Dashboard.Model;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// A dashboard stored while a chart was one object whose summary, legend and
/// lines were parts of it: each such chart becomes its card again, three
/// pieces over the cells it took, so they are moved and changed inside the
/// card — the
/// summary at the top left, the legend at the top right, the chart under
/// both — each piece as the part was: its size, its lines' look, hidden
/// where the part was. A chart standing alone gets its card's frame round it.
/// A chart of that time with none of its parts changed cannot be told from
/// a chart piece, and stays one.
/// </summary>
internal static class ChartCardPieces
{
    private static readonly string[] Charts = [HostCharts.Cpu, HostCharts.Memory, HostCharts.Disk, HostCharts.Network];

    private static readonly string[] SummaryLines = ["label", "value", "hint"];

    private const string Legend = "legend";

    private const string Lines = "chart";

    /// <summary>Whether the stored text may hold a chart made one object: a chart with parts.</summary>
    public static bool Needs(string stored) => stored.Contains("\"parts\"", StringComparison.Ordinal)
        && Charts.Any(chart => stored.Contains(ChartType(chart), StringComparison.Ordinal));

    /// <summary>The dashboard with every chart made one object split into its card's three pieces.</summary>
    public static DashboardLayout Split(DashboardLayout layout) =>
        layout.Objects.Where(o => ChartOf(o) is not null && o.Parts is { Count: > 0 }).ToList()
            .Aggregate(layout, (next, chart) => Split(next, chart, ChartOf(chart)!));

    private static DashboardLayout Split(DashboardLayout layout, ObjectInstance whole, string chart)
    {
        if (whole is not { W: >= 2, H: >= 3 })
        {
            return layout.With(whole with { Parts = null });
        }

        var group = whole.Group is { } id ? layout.FindGroup(id) : null;
        var groupId = group?.Id ?? Guid.NewGuid().ToString("N");
        var summaryWidth = (whole.W + 1) / 2;
        var legendStyle = whole.PartOf(Legend);
        var linesStyle = whole.PartOf(Lines);
        var summary = new ObjectInstance(Guid.NewGuid().ToString("N"), $"wslc.host-{chart}-activity", whole.X, whole.Y, summaryWidth, 2,
            whole.Source, whole.Size, Group: groupId, Background: whole.Background, Foreground: whole.Foreground,
            Parts: SummaryParts(whole), Hidden: SummaryLines.All(line => whole.PartOf(line).Hidden == true) ? true : null);
        var legend = summary with
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = $"wslc.host-{chart}-legend",
            X = whole.X + summaryWidth,
            W = whole.W - summaryWidth,
            Size = Stepped(whole.Size, legendStyle.Size),
            Parts = null,
            Hidden = legendStyle.Hidden,
        };
        var lines = whole with
        {
            Y = whole.Y + 2,
            H = whole.H - 2,
            Size = Stepped(whole.Size, linesStyle.Size),
            Group = groupId,
            Parts = null,
            Hidden = linesStyle.Hidden,
        };

        var next = layout with { Objects = [.. layout.Objects.Where(o => o.Id != whole.Id)] };
        return group is not null
            ? next.With(summary).With(legend).With(lines)
            : next.With(new DashboardGroup(groupId, whole.X, whole.Y, whole.W, whole.H), [summary, legend, lines]);
    }

    /// <summary>The summary's three lines as they were, their size, colour and weight, and whether each is shown.</summary>
    private static IReadOnlyDictionary<string, PartStyle>? SummaryParts(ObjectInstance whole)
    {
        var parts = SummaryLines.Where(line => !whole.PartOf(line).IsDesign).ToDictionary(line => line, whole.PartOf);
        return parts.Count > 0 ? parts : null;
    }

    /// <summary>An object's size stepped by what its part was: a Large part on a Small object is a Medium piece.</summary>
    private static TypeSize Stepped(TypeSize size, TypeSize? part) =>
        (TypeSize)Math.Clamp((int)size + (int)(part ?? TypeSize.Medium) - (int)TypeSize.Medium, (int)TypeSize.Small, (int)TypeSize.Large);

    private static string? ChartOf(ObjectInstance o) => Charts.FirstOrDefault(chart => o.Type == ChartType(chart));

    private static string ChartType(string chart) => $"wslc.host-{chart}-chart";
}
