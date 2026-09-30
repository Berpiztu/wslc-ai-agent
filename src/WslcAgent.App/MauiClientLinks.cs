using WslcAgent.UI.Links;

namespace WslcAgent.App;

/// <summary>
/// A link opened where the user is: the device's own browser, as a separate
/// application. The web view cannot do it — a BlazorWebView has no second
/// window, and asking for one takes MAUI's chrome client through
/// <c>Uri.parse(null)</c> and kills the process, which is what tapping Remote
/// did on Android.
/// </summary>
internal sealed class MauiClientLinks : IClientLinks
{
    public bool Supported => true;

    /// <summary>
    /// The system launcher first: it is what "open this address" means to the
    /// device, and it lands in the browser itself rather than in a tab drawn
    /// over this application (Android's in-app Custom Tab, with its own bar and
    /// a menu to open it in Chrome — one step too many). Where the launcher is
    /// not available it falls back to MAUI's browser, and only then to its
    /// in-app mode, so a link always opens somewhere.
    /// </summary>
    public async Task OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        var address = new Uri(url);
        if (await Launcher.Default.TryOpenAsync(address))
        {
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(address, BrowserLaunchMode.External);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or NotSupportedException or PlatformNotSupportedException)
        {
            await Browser.Default.OpenAsync(address, BrowserLaunchMode.SystemPreferred);
        }
    }
}
