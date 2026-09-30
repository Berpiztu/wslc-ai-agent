using WslcAgent.Mcp;

namespace WslcAgent.Server.Mcp;

/// <summary>
/// The MCP server's switches, read from what Settings saved. Read, not
/// remembered: the tool list and the approval gate ask on every request, so the
/// operator's change is in force on the next call without restarting the agent.
/// </summary>
public sealed class SavedMcpSwitches(McpSettingsStore store) : IMcpSwitches
{
    public bool Enabled => store.Get().Enabled;

    public bool AllowDestructiveTools => store.Get().AllowDestructiveTools;
}
