using System.Net.Http;
using Berpiztu.Dashboard.Storage;
using Microsoft.JSInterop;
using WslcAgent.ApiClient;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>Where the dashboard is kept, this device's choice.</summary>
public enum DashboardScope
{
    /// <summary>On this device alone (<see cref="DeviceDashboardStore"/>), as the theme and the table-or-cards choice are.</summary>
    Device,

    /// <summary>With the user, on the agent (<see cref="AgentDashboardStore"/>): the phone and the desktop open the same one.</summary>
    User,
}

/// <summary>
/// Where this device keeps the dashboard: with the user on the agent
/// (<see cref="AgentDashboardStore"/>), the same on every client, or on this
/// device alone (<see cref="DeviceDashboardStore"/>). The choice is the
/// device's, remembered on it
/// (<c>wslcAgent.dashboardV2Scope</c>); a device that never chose keeps it
/// with the user. It remembers too which of the dashboard's pages the
/// device looked at last (<c>wslcAgent.dashboardV2Page</c>).
/// </summary>
public sealed class DashboardPlace(IJSRuntime js, WslcAgentApi api, AgentDashboardStore user, DeviceDashboardStore device)
{
    private const string Device = "device";

    private const string User = "user";

    /// <summary>How the main page is remembered, beside the named ones.</summary>
    private const string MainPage = "main";

    private Task? _reading;

    /// <summary>The default the agent ships, read once.</summary>
    private Task<string?>? _shipped;

    public DashboardScope Scope { get; private set; } = DashboardScope.User;

    /// <summary>
    /// The view the dashboard is being designed in, landscape or portrait, whose
    /// alarms the status bar shows instead of those of the view the window's
    /// width asks for; null out of design.
    /// </summary>
    public string? Designed { get; private set; }

    /// <summary>The view designed changed, or design ended.</summary>
    public event Action? DesignedChanged;

    /// <summary>The view being designed now; null when design ends.</summary>
    public void Design(string? view)
    {
        if (view == Designed)
        {
            return;
        }

        Designed = view;
        DesignedChanged?.Invoke();
    }

    /// <summary>The place the dashboard is kept now, its blank pages and views showing the default (<see cref="DefaultedDashboardStore"/>).</summary>
    public IDashboardStore Store => new DefaultedDashboardStore(Of(Scope), ShippedAsync);

    /// <summary>The other place, which Copy writes to.</summary>
    public DashboardScope Other => Scope == DashboardScope.User ? DashboardScope.Device : DashboardScope.User;

    /// <summary>The store of a place, as it holds it: what Copy writes over, and asks about first.</summary>
    public IDashboardStore Of(DashboardScope scope) => scope == DashboardScope.User ? user : device;

    /// <summary>The device's choice, read once.</summary>
    public Task LoadAsync() => _reading ??= ReadAsync();

    /// <summary>
    /// Where this device keeps it from now on, remembered on the device.
    /// Switching shows what that place holds and writes nothing to it: Copy
    /// is the one verb that does.
    /// </summary>
    public async Task UseAsync(DashboardScope scope)
    {
        Scope = scope;
        try
        {
            await js.InvokeAsync<string?>("wslcAgent.dashboardV2Scope", scope == DashboardScope.User ? User : Device);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Private windows: the choice holds for this run.
        }
    }

    /// <summary>The page this device looked at last, which opens again; null for the main page, or where nothing is remembered.</summary>
    public async Task<string?> LastPageAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("wslcAgent.dashboardV2Page") is { Length: > 0 } page && page != MainPage ? page : null;
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>The page on screen, remembered on the device.</summary>
    public async Task RememberPageAsync(string? page)
    {
        try
        {
            await js.InvokeAsync<string?>("wslcAgent.dashboardV2Page", page ?? MainPage);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Private windows: the page opens as the main one next time.
        }
    }

    /// <summary>The default the agent ships; null where there is none, or the agent cannot say.</summary>
    private Task<string?> ShippedAsync() => _shipped ??= ReadShippedAsync();

    private async Task<string?> ReadShippedAsync()
    {
        try
        {
            return await api.GetDefaultDashboardV2Async() is { Length: > 0 } text ? text : null;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            return null;
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            Scope = await js.InvokeAsync<string?>("wslcAgent.dashboardV2Scope") == Device ? DashboardScope.Device : DashboardScope.User;
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            Scope = DashboardScope.User;
        }
    }
}
