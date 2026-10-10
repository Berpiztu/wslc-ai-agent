using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The last events the agent heard from <c>wslc events</c>, for the Recent
/// events object. Read again at once when the agent says something changed;
/// the interval only covers a notice that never came.
/// </summary>
public sealed class RecentEventsRead(WslcAgentApi api) : SharedRead<IReadOnlyList<WslcEventEntry>>(TimeSpan.FromMinutes(1))
{
    /// <summary>How many it asks for: the object shows what fits, newest first.</summary>
    public const int Shown = 50;

    protected override async Task<IReadOnlyList<WslcEventEntry>?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetRecentEventsAsync(Shown, cancellationToken);
}
