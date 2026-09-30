using Berpiztu.Dashboard.Storage;
using Microsoft.JSInterop;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The dashboard kept on this device (<c>wslcAgent.dashboardV25</c>),
/// both its views as one text. A private window keeps
/// nothing, and the dashboard still works for as long as the page is open.
/// </summary>
public sealed class DeviceDashboardStore(IJSRuntime js) : IDashboardStore
{
    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return TransfersCardPieces.Upgrade(await js.InvokeAsync<string?>("wslcAgent.dashboardV25", cancellationToken));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task SaveAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            await js.InvokeAsync<string?>("wslcAgent.dashboardV25", cancellationToken, text);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Private windows: the dashboard is shown, it is just not remembered.
        }
    }
}
