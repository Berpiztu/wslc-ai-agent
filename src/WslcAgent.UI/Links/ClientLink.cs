using Microsoft.JSInterop;
using MudBlazor;

namespace WslcAgent.UI.Links;

/// <summary>
/// The one way the application opens an address that is not one of its own
/// screens. Two callers have it — the Remote way in of Open with browser, and
/// the Open button of a publication — and both have to go the same way, or one
/// of them takes the native clients down again.
/// </summary>
public static class ClientLink
{
    public static async Task OpenAsync(IClientLinks links, IJSRuntime js, ISnackbar snackbar, string url)
    {
        try
        {
            if (links.Supported)
            {
                await links.OpenAsync(url);
            }
            else
            {
                await js.InvokeVoidAsync("open", url, "_blank");
            }

            snackbar.Add($"Opened {url}", Severity.Success);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or NotSupportedException)
        {
            // A device with nothing to open pages with, or a web view that
            // refused: the address is worth showing anyway, it can be copied.
            snackbar.Add($"Could not open {url}: {ex.Message}", Severity.Error);
        }
    }
}
