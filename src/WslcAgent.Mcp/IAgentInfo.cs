namespace WslcAgent.Mcp;

/// <summary>What the MCP tools may say about the agent hosting them.</summary>
public interface IAgentInfo
{
    string Version { get; }

    /// <summary>The id of the running assembly: a new one with every compilation, where the version stays the same for months.</summary>
    string Build { get; }
}
