using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The agent's version and whether it is a development one, for the System
/// card's Agent reading. It does not change while the agent runs, so it is
/// read when an object first follows it and then seldom.
/// </summary>
public sealed class AgentHealthRead(WslcAgentApi api) : SharedRead<HealthResponse>(TimeSpan.FromMinutes(10))
{
    protected override Task<HealthResponse?> ReadAsync(CancellationToken cancellationToken) =>
        api.GetHealthAsync(cancellationToken);
}
