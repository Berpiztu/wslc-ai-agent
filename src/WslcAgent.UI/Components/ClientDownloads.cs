using System.Net.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// The native client a browser is offered: the APK on an Android device, the
/// Windows installer on anything else; iOS has no client yet and is offered
/// nothing. One place for the download button and the System card's client
/// version, so both ask and fetch alike. The
/// agent says first whether it has the package, so a missing one is a notice
/// rather than a broken download.
/// </summary>
public static class ClientDownloads
{
    /// <summary>Which native client this device takes: <c>android</c>, <c>windows</c>, or another word for none.</summary>
    public static ValueTask<string> PlatformAsync(IJSRuntime js) => js.InvokeAsync<string>("wslcAgent.clientPlatform");

    /// <summary>Whether there is a native client for this platform at all.</summary>
    public static bool Offered(string platform) => platform is "android" or "windows";

    /// <summary>What the agent has for this platform; null when it could not say.</summary>
    public static async Task<ClientPackageInfo?> PackageAsync(WslcAgentApi api, string platform)
    {
        try
        {
            return await api.GetClientPackageAsync(platform);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>The package, fetched by the browser itself; a notice instead when the agent has none.</summary>
    public static async Task DownloadAsync(WslcAgentApi api, NavigationManager navigation, ISnackbar snackbar, string platform)
    {
        try
        {
            var package = await api.GetClientPackageAsync(platform);
            if (!package.Available)
            {
                snackbar.Add(package.Error.Length > 0 ? package.Error : "Client package is not available.", Severity.Error);
                return;
            }

            navigation.NavigateTo(api.ClientPackageDownloadUrl(platform).ToString(), forceLoad: true);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add(ex.Message.Length > 0 ? ex.Message : "Could not check the client package.", Severity.Error);
        }
    }

    /// <summary>What a platform's client is called on a button or a line.</summary>
    public static string Name(string platform) => platform == "android" ? "Android app" : "Windows client";
}
