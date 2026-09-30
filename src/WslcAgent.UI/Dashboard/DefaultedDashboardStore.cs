using Berpiztu.Dashboard.Storage;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// Where the dashboard is kept, read with every blank page and view shown as
/// the default the agent ships (a user's dashboard left blank, on the agent
/// or on a device, landscape or portrait, shows the default's;
/// <see cref="DashboardPages.Filled"/>). What it shows is kept there only
/// once it is edited there.
/// </summary>
public sealed class DefaultedDashboardStore(IDashboardStore place, Func<Task<string?>> shipped) : IDashboardStore
{
    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default) =>
        DashboardPages.Filled(await place.LoadAsync(cancellationToken), await shipped());

    public Task SaveAsync(string text, CancellationToken cancellationToken = default) => place.SaveAsync(text, cancellationToken);
}
