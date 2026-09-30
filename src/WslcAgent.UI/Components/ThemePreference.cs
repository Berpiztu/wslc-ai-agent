using Microsoft.JSInterop;

namespace WslcAgent.UI.Components;

/// <summary>
/// Light or dark, as this client last had it. The choice belongs to the client,
/// not to the agent: it lives in this device's storage
/// (<c>wslcAgent.theme</c>), exactly as the page zoom and the table-or-cards
/// choice of each list do, so closing the app and opening it again finds the
/// theme it was left in, and two clients of the same agent each keep their own.
/// </summary>
public sealed class ThemePreference(IJSRuntime js)
{
    /// <summary>Light until this device says otherwise, which is also what the first paint uses.</summary>
    public bool Dark { get; private set; }

    /// <summary>
    /// What this device remembers. Asked on the first render, when there is a
    /// browser to ask; a host that cannot answer yet keeps the light theme
    /// rather than losing the session.
    /// </summary>
    public async ValueTask<bool> ReadAsync()
    {
        try
        {
            Dark = await js.InvokeAsync<bool>("wslcAgent.theme");
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // No browser yet (a native client's web view before it loads) or no
            // storage (a private window): the theme still works, it is just not
            // remembered.
        }

        return Dark;
    }

    /// <summary>The user turned the theme over; this device remembers it from now on.</summary>
    public void Set(bool dark)
    {
        Dark = dark;
        _ = SaveAsync(dark);
    }

    private async Task SaveAsync(bool dark)
    {
        try
        {
            await js.InvokeAsync<bool>("wslcAgent.theme", dark);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // As above: the theme is applied all the same.
        }
    }
}
