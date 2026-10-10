using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>Host-level tools: the System overview and the CLI activity.</summary>
[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Name = "system_info", ReadOnly = true)]
    [Description("WSLC host overview: wslc version and runtime (kernel, Windows, session manager), the selected session, running sessions, the session VHDX files on disk, and image totals with the largest images. Also whether Compact VHDX is blocked by a running session.")]
    public static async Task<SystemInfoResult> SystemInfo(ISystemService system, CancellationToken cancellationToken = default)
    {
        var overview = await system.GetAsync(cancellationToken);
        var version = overview.Version.Trim();
        if (!version.StartsWith("wslc", StringComparison.OrdinalIgnoreCase))
        {
            version = $"wslc {version}";
        }

        var summary = $"{version}; selected session '{overview.SelectedSession}'; "
            + $"{overview.ImageCount} images ({Bytes.Humanize(overview.ImageTotalBytes)}); store {Bytes.Humanize(overview.Store.TotalBytes)}.";
        return new SystemInfoResult(summary, overview);
    }

    [McpServerTool(Name = "cli_activity", ReadOnly = true)]
    [Description("Recent wslc commands the agent ran, newest first, with status, exit code, output and the runtime's own error text — the ones running now, then the finished ones from the agent's log. Use it to explain a failure: it shows the exact command.")]
    public static CliActivityResult CliActivity(
        ICliActivityLog activity,
        [Description("How many commands, 1 to 200 (default 20).")] int limit = 20)
    {
        var traces = activity.Recent(Math.Clamp(limit, 1, 200));
        return new CliActivityResult($"{traces.Count} recent wslc commands.", traces.Count, traces);
    }

    [McpServerTool(Name = "recent_events", ReadOnly = true)]
    [Description("What happened lately in the session, newest first, as wslc reported it: containers started, stopped, killed or died (with their exit code), networks connected or disconnected, health changes, images' own events — each with its time, kind, action, id and name. Volumes report no events. The list starts empty when the agent starts or switches session.")]
    public static RecentEventsResult RecentEvents(
        IRecentEvents events,
        [Description("How many events, 1 to 100 (default 20).")] int limit = 20)
    {
        var recent = events.Recent(Math.Clamp(limit, 1, 100));
        return new RecentEventsResult($"{recent.Count} recent events.", recent.Count, recent);
    }
}

/// <summary>Result of <c>recent_events</c>.</summary>
public sealed record RecentEventsResult(string Summary, int Count, IReadOnlyList<WslcEventEntry> Events);

/// <summary>Result of <c>system_info</c>: one line to read, then the whole overview.</summary>
public sealed record SystemInfoResult(string Summary, SystemOverview Overview);

/// <summary>Result of <c>cli_activity</c>.</summary>
public sealed record CliActivityResult(string Summary, int Count, IReadOnlyList<CliTraceEntry> Traces);
