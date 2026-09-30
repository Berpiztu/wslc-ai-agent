using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>The <c>wslc</c> commands the agent ran, newest first, for the tool that explains a failure.</summary>
public interface ICliActivityLog
{
    IReadOnlyList<CliTraceEntry> Recent(int max);
}
