using Berpiztu.Dashboard.Catalogue;
using Berpiztu.Dashboard.Model;
using MudBlazor;

namespace WslcAgent.UI.Dashboard.Cards;

/// <summary>
/// The cards made already, out of the objects that a card's parts became, so
/// each can be changed or moved after; on cells of about 17px.
/// <para>
/// A resource card — a container's, an image's, a volume's, a network's — is
/// laid out as the list pages draw theirs, measured against the
/// Containers, Images, Volumes and Networks screens: twenty-four cells across,
/// a list card's 414px; its header three rows (the list's 50px), its body
/// the rows its words take (a container's 118px seven, the others' six),
/// its actions two (33px); every piece at Medium, the list's own sizes; the
/// dials at the body's right, at its top, 16px down as the list's padding
/// puts them, a percent's ring a step smaller than its figure (the list's
/// 44px), a byte dial's at the list's 56px; the shortcuts at the left of the
/// actions and the own verbs at their right.
/// </para>
/// <para>
/// The System card, the chart cards and File transfers were laid out on
/// cells twice as large, now twice as many cells: the System card
/// thirty-two by ten; a chart card twenty-two cells wide, its summary and
/// its legend over its chart.
/// </para>
/// </summary>
public static class WslcCards
{
    /// <summary>The subgroup of the cards of what the user runs: containers, images, volumes, networks.</summary>
    public const string UserCards = "User";

    /// <summary>The subgroup of the cards of the system itself: the System card and the host's charts.</summary>
    public const string SystemCards = "System";

    /// <summary>A resource card's width, a list card's: twenty-four cells.</summary>
    private const int CardWidth = 24;

    /// <summary>A resource card's header, three rows: the list's 50px.</summary>
    private const int HeaderRows = 3;

    /// <summary>A resource card's actions, two rows: the list's 33px.</summary>
    private const int ActionRows = 2;

    /// <summary>A dial's cells across, at the body's right: a list card's dial and the gap beside it.</summary>
    private const int DialWidth = 4;

    /// <summary>How far down the body its dials stand: the list card's body padding, 16px.</summary>
    private const int DialMargin = 16;

    /// <summary>A percent's ring a step smaller than its figure: the list card's 44px dial beside its 11.5px text.</summary>
    private static readonly IReadOnlyDictionary<string, PartStyle> SmallRing =
        new Dictionary<string, PartStyle> { ["ring"] = new(Size: TypeSize.Small) };

    public static readonly CardTemplate Container = new("wslc.container-card", "Container card", Icons.Material.Filled.ViewInAr, UserCards,
        Resource("container", 7, dials: [("cpu", SmallRing), ("memory", SmallRing)], shortcuts: 14));

    public static readonly CardTemplate Image = new("wslc.image-card", "Image card", Icons.Material.Filled.Layers, UserCards,
        Resource("image", 6, dials: [], shortcuts: 10));

    public static readonly CardTemplate Volume = new("wslc.volume-card", "Volume card", Icons.Material.Filled.Storage, UserCards,
        Resource("volume", 6, dials: [("read", null), ("written", null)], shortcuts: 12));

    public static readonly CardTemplate Network = new("wslc.network-card", "Network card", Icons.Material.Filled.Lan, UserCards,
        Resource("network", 6, dials: [("received", null), ("sent", null)], shortcuts: 0));

    /// <summary>
    /// A resource card's pieces as the list's card lays them out: the header
    /// across, the details with the dials at their right, and the actions row
    /// — the shortcuts taking <paramref name="shortcuts"/> cells at its left,
    /// the own verbs the rest; a resource with no shortcuts has its verbs
    /// across. How each keeps to the card's edges as it widens is its kind's
    /// (DashboardObjectAttribute.Anchor).
    /// </summary>
    private static IReadOnlyList<TemplateItem> Resource(string resource, int bodyRows,
        IReadOnlyList<(string Piece, IReadOnlyDictionary<string, PartStyle>? Parts)> dials, int shortcuts)
    {
        var details = CardWidth - dials.Count * DialWidth;
        var actions = HeaderRows + bodyRows;
        List<TemplateItem> items =
        [
            new($"wslc.{resource}-header", 0, 0, CardWidth, HeaderRows),
            new($"wslc.{resource}-details", 0, HeaderRows, details, bodyRows),
            .. dials.Select((dial, i) => new TemplateItem($"wslc.{resource}-{dial.Piece}", details + i * DialWidth, HeaderRows, DialWidth, bodyRows,
                Parts: dial.Parts, Vertical: VerticalAlign.Top, MarginY: DialMargin)),
        ];
        if (shortcuts > 0)
        {
            items.Add(new($"wslc.{resource}-shortcuts", 0, actions, shortcuts, ActionRows));
        }

        items.Add(new($"wslc.{resource}-verbs", shortcuts, actions, CardWidth - shortcuts, ActionRows));
        return items;
    }

    /// <summary>The System card, its header and its client download left out.</summary>
    public static readonly CardTemplate SystemCard = new("wslc.system-card", "System card", Icons.Material.Filled.Computer, SystemCards,
    [
        new("wslc.system-agent", 0, 0, 6, 4, TypeSize.Large),
        new("wslc.system-client", 6, 0, 4, 4, TypeSize.Large),
        new("wslc.system-session", 10, 0, 10, 4),
        new("wslc.system-wslc", 20, 0, 6, 4, TypeSize.Large),
        new("wslc.system-windows", 26, 0, 6, 2, TypeSize.Small),
        new("wslc.system-kernel", 26, 2, 6, 4, TypeSize.Large),
        new("wslc.host-cpu-dial", 0, 4, 6, 6),
        new("wslc.host-memory-dial", 10, 4, 6, 6),
        new("wslc.host-disk-dial", 20, 4, 6, 6),
        new("wslc.system-events", 26, 6, 6, 4, TypeSize.Large),
    ]);

    /// <summary>
    /// A chart card: the summary at the top left, the
    /// legend at the top right, the chart under both, across the card; its
    /// subject, the host until another is chosen, chosen once on the card.
    /// </summary>
    private static CardTemplate Chart(string chart, string label, string icon,
        int summaryWidth, (int X, int W) legend, int chartRows, TypeSize chartSize = TypeSize.Medium) =>
        new($"wslc.{chart}-chart-card", $"{label} chart card", icon, SystemCards,
        [
            new($"wslc.host-{chart}-activity", 0, 0, summaryWidth, 4),
            new($"wslc.host-{chart}-legend", legend.X, 0, legend.W, 4),
            new($"wslc.host-{chart}-chart", 0, 4, 22, chartRows, chartSize),
        ]);

    public static readonly CardTemplate CpuChart = Chart(HostCharts.Cpu, "CPU", Icons.Material.Filled.ShowChart, 10, (12, 10), 16);

    public static readonly CardTemplate MemoryChart = Chart(HostCharts.Memory, "Memory", Icons.Material.Filled.StackedLineChart, 10, (10, 12), 18);

    public static readonly CardTemplate DiskChart = Chart(HostCharts.Disk, "Disk", Icons.Material.Filled.Timeline, 12, (12, 8), 16, TypeSize.Small);

    public static readonly CardTemplate NetworkChart = Chart(HostCharts.Network, "Network", Icons.Material.Filled.MultilineChart, 12, (14, 8), 16);

    /// <summary>
    /// The four chart cards in one, each at half its size: CPU and memory
    /// above, disk and network below; over them, on its first line, the name
    /// of what they show.
    /// </summary>
    public static readonly HalvedCardsTemplate ChartsCard = new("wslc.charts-card", "Charts card", Icons.Material.Filled.Insights,
        CardTemplate.CardsGroup, SystemCards, [CpuChart, MemoryChart, DiskChart, NetworkChart], PerRow: 2,
        Above: [new("wslc.chart-subject", 0, 0, 12, 2)]);

    /// <summary>
    /// The File transfers card: its header four rows — its three lines,
    /// title, count and hint, each an object of its own at the left, the
    /// count two rows for its larger type, and the container filter beside
    /// them —, the header of its lines two, its lines the rest (each with
    /// properties of its own, and none of them out of the card).
    /// </summary>
    public static readonly CardTemplate TransfersCard = new("wslc.transfers-card", "File transfers card", Icons.Material.Filled.ImportExport, SystemCards,
    [
        new(TransfersCardPieces.Title, 0, 0, 9, 1),
        new(TransfersCardPieces.Count, 0, 1, 9, 2),
        new(TransfersCardPieces.Hint, 0, 3, 9, 1),
        new(TransfersCardPieces.Filter, 9, 0, 7, 4),
        new(TransfersCardPieces.Head, 0, 4, 16, 2),
        new(TransfersCardPieces.Lines, 0, 6, 16, 4),
    ]);

    public static IEnumerable<IDashboardTemplate> All =>
        [Container, Image, Volume, Network, SystemCard, CpuChart, MemoryChart, DiskChart, NetworkChart, ChartsCard, TransfersCard];
}
