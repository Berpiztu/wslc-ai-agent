using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>The Home page's overview, for an assistant asked "how is it going?".</summary>
[McpServerToolType]
public static class HomeTools
{
    [McpServerTool(Name = "home_overview", ReadOnly = true)]
    [Description("At a glance: containers running and total, image count and catalog size, networks, volumes, the containers' aggregate CPU (percent of CpuTotalPercent, 100 per CPU) and memory, the wslc version and the selected session. A part that failed has Error=true.")]
    public static Task<HomeOverview> HomeOverview(IHomeService home, CancellationToken cancellationToken = default) =>
        home.OverviewAsync(cancellationToken);
}
