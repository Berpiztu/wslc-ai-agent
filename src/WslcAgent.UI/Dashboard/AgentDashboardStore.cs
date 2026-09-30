using System.Text.Json.Nodes;
using Berpiztu.Dashboard.Storage;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The dashboard, kept by the agent for each user (<c>/me/dashboard-v2</c>),
/// so every client shows the same one.
/// </summary>
public sealed class AgentDashboardStore(WslcAgentApi api) : IDashboardStore
{
    /// <summary>The one legend of every chart that stood before each chart had its own, its chart its source.</summary>
    private const string ChartLegend = "wslc.host-chart-legend";

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default) =>
        TransfersCardPieces.Upgrade(ChartsInTheirCards(OneLegendPerChart(await api.GetUserDashboardV2Async(cancellationToken))));

    public Task SaveAsync(string text, CancellationToken cancellationToken = default) =>
        api.SetUserDashboardV2Async(text, cancellationToken);

    /// <summary>
    /// A dashboard stored before each chart had a legend of its own: each
    /// chart legend becomes its chart's legend, whose
    /// subject is the host, as it showed. Anything else is left as it was.
    /// </summary>
    private static string? OneLegendPerChart(string? stored)
    {
        if (stored is null || !stored.Contains(ChartLegend, StringComparison.Ordinal)
            || Parse(stored) is not JsonObject layout || layout["objects"] is not JsonArray objects)
        {
            return stored;
        }

        foreach (var legend in objects.OfType<JsonObject>().Where(o => (string?)o["type"] == ChartLegend))
        {
            legend["type"] = $"wslc.host-{(string?)legend["source"] ?? HostCharts.Cpu}-legend";
            legend.Remove("source");
        }

        return layout.ToJsonString();
    }

    /// <summary>A dashboard stored while a chart was one object with parts, each such chart its card of three pieces again (<see cref="ChartCardPieces"/>), in each of its pages and views.</summary>
    private static string? ChartsInTheirCards(string? stored) =>
        stored is not null && ChartCardPieces.Needs(stored) ? DashboardPages.Each(stored, ChartCardPieces.Split) : stored;

    /// <summary>The stored text as JSON; null for one that is not, which the layout reads as an empty dashboard.</summary>
    private static JsonNode? Parse(string stored)
    {
        try
        {
            return JsonNode.Parse(stored);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
