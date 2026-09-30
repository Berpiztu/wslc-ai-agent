using System.Text.Json.Nodes;
using Berpiztu.Dashboard.Storage;
using Microsoft.JSInterop;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The dashboard's designs not saved yet, kept on this device
/// (<c>wslcAgent.dashboardV25Draft</c>): nothing reaches where the dashboard is kept without
/// Save, and a power cut or a lost connection loses nothing. One text holds
/// every draft, each under the name of the place, the page and the view it
/// is of — the server's System page in landscape, this device's User page
/// in portrait — so a draft is never opened on another. A private window
/// keeps no draft, and design still goes on for as long as the page is open.
/// </summary>
public sealed class DeviceDashboardDrafts(IJSRuntime js)
{
    /// <summary>The draft of one place's page in one view, as the designer reads and writes it.</summary>
    public IDashboardStore For(string place, string page, string view) => new Draft(this, $"{place}/{page}/{view}");

    private async Task<JsonObject> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await js.InvokeAsync<string?>("wslcAgent.dashboardV25Draft", cancellationToken) is { Length: > 0 } stored
                && JsonNode.Parse(stored) is JsonObject drafts
                    ? drafts
                    : [];
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Nothing kept, a private window, or a text that is not this one's: no draft.
            return [];
        }
    }

    private async Task WriteAsync(JsonObject drafts, CancellationToken cancellationToken)
    {
        try
        {
            await js.InvokeAsync<string?>("wslcAgent.dashboardV25Draft", cancellationToken, drafts.ToJsonString());
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Private windows: the design goes on, it is just not kept.
        }
    }

    /// <summary>One draft of the text: read out of it, written into it, every other draft left as it was; an empty text lets it go.</summary>
    private sealed class Draft(DeviceDashboardDrafts drafts, string name) : IDashboardStore
    {
        public async Task<string?> LoadAsync(CancellationToken cancellationToken = default) =>
            (await drafts.ReadAsync(cancellationToken))[name]?.GetValue<string>();

        public async Task SaveAsync(string text, CancellationToken cancellationToken = default)
        {
            var all = await drafts.ReadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                all.Remove(name);
            }
            else
            {
                all[name] = text;
            }

            await drafts.WriteAsync(all, cancellationToken);
        }
    }
}
