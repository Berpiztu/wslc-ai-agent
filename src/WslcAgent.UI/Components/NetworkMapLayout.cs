namespace WslcAgent.UI.Components;

/// <summary>
/// Where the network map puts its boxes: networks in one row of hubs, containers in a row of
/// nodes with one address line per network they are on, spokes between them.
/// The fit goes in order: boxes shrink to their minimum width, then the whole
/// drawing scales down to <see cref="MinScale"/>, and only then the card
/// scrolls; with room to spare the drawing scales up to <see cref="MaxScale"/>.
/// </summary>
public sealed record NetworkMapLayout(
    double HubWidth,
    double NodeWidth,
    double NodeHeight,
    double CanvasWidth,
    double CanvasHeight,
    double ContentWidth,
    double ContentHeight,
    double HubY,
    double NodeY,
    double HubRowStart,
    double NodeRowStart)
{
    public const double HubHeight = 46;
    public const double RowHeight = 13;
    public const double Gap = 12;
    private const double Pad = 16;
    private const double PadY = 6;
    private const double HubMinWidth = 88;
    private const double HubMaxWidth = 168;
    private const double NodeMinWidth = 96;
    private const double NodeMaxWidth = 196;
    private const double MinSpoke = 20;
    private const double MaxSpoke = 90;
    private const double MinScale = 0.6;
    private const double MaxScale = 2;
    private const double Scrollbar = 16;

    public double HubX(int index) => HubRowStart + (HubWidth / 2) + (index * (HubWidth + Gap));

    public double NodeX(int index) => NodeRowStart + (NodeWidth / 2) + (index * (NodeWidth + Gap));

    /// <summary>The layout for a card of this size and the scale its drawing is shown at.</summary>
    public static (NetworkMapLayout Layout, double Scale) Fit(double width, double height, int networks, int containers, int ipRows, bool containersOnTop)
    {
        var (layout, scale) = Measure(width, height, networks, containers, ipRows, containersOnTop);
        if (layout.CanvasWidth * scale > width + 0.5)
        {
            // A horizontal scrollbar eats card height; do not bring a vertical one too.
            return Measure(width, Math.Max(60, height - Scrollbar), networks, containers, ipRows, containersOnTop);
        }

        if (layout.CanvasHeight * scale > height + 0.5)
        {
            return Measure(Math.Max(120, width - Scrollbar), height, networks, containers, ipRows, containersOnTop);
        }

        return (layout, scale);
    }

    /// <summary>Approximate ellipsis, so a shrunk box never overflows its label.</summary>
    public static string FitText(string text, double boxWidth, double fontSize)
    {
        var maxChars = Math.Max(3, (int)Math.Floor((boxWidth - 10) / (fontSize * 0.58)));
        return text.Length > maxChars ? text[..(maxChars - 1)] + "…" : text;
    }

    private static (NetworkMapLayout Layout, double Scale) Measure(double width, double height, int networks, int containers, int ipRows, bool containersOnTop)
    {
        var first = Create(width, height, networks, containers, ipRows, containersOnTop);
        var sx = first.CanvasWidth > width ? width / first.CanvasWidth : 1;
        var sy = first.ContentHeight > height ? height / first.ContentHeight : 1;
        if (sx >= 1 && sy >= 1)
        {
            var up = Math.Min(MaxScale, Math.Min(width / first.ContentWidth, height / first.ContentHeight));
            return up > 1.05
                ? (Create(width / up, height / up, networks, containers, ipRows, containersOnTop), up)
                : (first, 1);
        }

        var scale = Math.Max(MinScale, Math.Min(sx, sy));
        return (Create(width / scale, height / scale, networks, containers, ipRows, containersOnTop), scale);
    }

    private static NetworkMapLayout Create(double width, double height, int networks, int containers, int ipRows, bool containersOnTop)
    {
        var nodeHeight = 20 + (ipRows * RowHeight);
        var available = width - (Pad * 2);
        var hubWidth = FitWidth(networks, HubMinWidth, HubMaxWidth, available);
        var nodeWidth = FitWidth(containers, NodeMinWidth, NodeMaxWidth, available);
        var hubRow = RowWidth(networks, hubWidth);
        var nodeRow = RowWidth(containers, nodeWidth);
        var canvasWidth = Math.Max(width, Math.Max(hubRow, nodeRow) + (Pad * 2));
        var contentHeight = HubHeight + nodeHeight + MinSpoke + (PadY * 2);
        var canvasHeight = Math.Max(height, contentHeight);
        var groupHeight = Math.Min(canvasHeight - (PadY * 2), HubHeight + nodeHeight + MaxSpoke);
        var groupTop = (canvasHeight - groupHeight) / 2;
        return new NetworkMapLayout(
            hubWidth,
            nodeWidth,
            nodeHeight,
            canvasWidth,
            canvasHeight,
            Math.Max(hubRow, nodeRow) + (Pad * 2),
            contentHeight,
            containersOnTop ? groupTop + groupHeight - (HubHeight / 2) : groupTop + (HubHeight / 2),
            containersOnTop ? groupTop + (nodeHeight / 2) : groupTop + groupHeight - (nodeHeight / 2),
            (canvasWidth - hubRow) / 2,
            (canvasWidth - nodeRow) / 2);
    }

    private static double FitWidth(int count, double min, double max, double available) =>
        count <= 0 ? max : Math.Min(max, Math.Max(min, (available - (Gap * (count - 1))) / count));

    private static double RowWidth(int count, double boxWidth) => count <= 0 ? 0 : (count * boxWidth) + (Gap * (count - 1));
}
