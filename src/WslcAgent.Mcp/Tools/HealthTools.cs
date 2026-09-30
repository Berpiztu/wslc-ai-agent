using System.ComponentModel;
using ModelContextProtocol.Server;

namespace WslcAgent.Mcp.Tools;

/// <summary>Tools that report on the agent itself.</summary>
[McpServerToolType]
public static class HealthTools
{
    [McpServerTool(Name = "health")]
    [Description("Report whether the wslc-agent server is up and which version it runs. Call it first.")]
    public static HealthResult Health(IAgentInfo info) => new("ok", info.Version);
}

/// <summary>Result of the <c>health</c> tool.</summary>
public sealed record HealthResult(string Status, string Version);
