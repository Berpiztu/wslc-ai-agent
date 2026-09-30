using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// Whether the agent hears wslc's events or the screens poll on their own
/// clock, for the System card's Events reading. The stream lives and dies with
/// the session, so the object that shows it reads it again when the agent
/// says the session changed.
/// </summary>
public sealed class EventStatusRead(WslcAgentApi api) : SharedRead<EventStatus>(TimeSpan.FromMinutes(10))
{
    protected override async Task<EventStatus?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetEventStatusAsync(cancellationToken);
}
