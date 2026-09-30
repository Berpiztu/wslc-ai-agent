using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace WslcAgent.Mcp;

/// <summary>
/// The two switches the operator has over this server, asked on every request so
/// a change reaches the running agent: serving at all, and offering the tools
/// that destroy something. The host decides where they are kept.
/// </summary>
public interface IMcpSwitches
{
    /// <summary>Serve MCP at all.</summary>
    bool Enabled { get; }

    /// <summary>Offer <c>remove_container</c>, <c>kill_container</c>, <c>exec_in_container</c> and <c>stop_session</c>.</summary>
    bool AllowDestructiveTools { get; }
}

/// <summary>
/// What a client is shown, and allowed, under those switches: with MCP off,
/// nothing at all; with the destructive tools off, every tool but those. The
/// filters run per request, so the operator's change needs no restart, and the
/// tools themselves ask again before acting — a client that cached the old list
/// is refused just the same.
/// </summary>
public static class McpSwitches
{
    private const string ServerOff = "The MCP server is switched off on this agent (Settings → MCP server). Nothing was changed, and no tool is available until the operator turns it on.";

    public static IMcpServerBuilder WithOperatorSwitches(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters => filters
            .AddListToolsFilter(next => async (context, cancellationToken) =>
            {
                var listed = await next(context, cancellationToken);
                var switches = Switches(context.Services);
                if (switches is null)
                {
                    return listed;
                }

                listed.Tools = switches switch
                {
                    { Enabled: false } => [],
                    { AllowDestructiveTools: false } => [.. listed.Tools.Where(tool => tool.Annotations?.DestructiveHint is not true)],
                    _ => listed.Tools,
                };
                return listed;
            })
            .AddCallToolFilter(next => async (context, cancellationToken) =>
                Switches(context.Services) is { Enabled: false }
                    ? new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = ServerOff }] }
                    : await next(context, cancellationToken)));

    private static IMcpSwitches? Switches(IServiceProvider? services) => services?.GetService<IMcpSwitches>();
}
