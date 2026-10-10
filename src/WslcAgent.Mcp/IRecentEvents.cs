using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>The last events the agent heard from <c>wslc events</c>, newest first, for the tool that says what happened.</summary>
public interface IRecentEvents
{
    IReadOnlyList<WslcEventEntry> Recent(int max);
}
