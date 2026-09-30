using Microsoft.Extensions.Logging;
using WslcAgent.ApiClient;
using WslcAgent.UI;
using WslcAgent.UI.Access;
using WslcAgent.UI.Files;
using WslcAgent.UI.Updates;

namespace WslcAgent.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AllowPlainAgentConnections();
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<IClientUpdates, MauiClientUpdates>();
        builder.Services.AddSingleton<IClientFiles, MauiClientFiles>();
        builder.Services.AddSingleton<WslcAgent.UI.Links.IClientLinks, MauiClientLinks>();
        builder.Services.AddSingleton<WslcAgent.UI.Lifetime.IClientLifetime, MauiClientLifetime>();
#if ANDROID
        // Pushed through Firebase to the phone, running or not.
        builder.Services.AddSingleton<WslcAgent.UI.Notifications.IClientNotifications, AndroidClientNotifications>();
#elif WINDOWS
        // Read from the agent's feed while the client runs, when the agent is on another machine.
        builder.Services.AddSingleton<WslcAgent.UI.Notifications.IClientNotifications, WindowsClientNotifications>();
#endif
        builder.Services.AddWslcAgentUi();

        // The shared UI calls the agent through this client, exactly as the
        // browser build does at its own origin. The address comes from the
        // debug scripts, the installer or the app preference (AgentAddress).
        var agentUrl = AgentAddress.Resolve();
        builder.Services.AddSingleton<IAgentTokenStore, PreferencesTokenStore>();
        builder.Services.AddSingleton<IClientServers, MauiClientServers>();
        builder.Services.AddScoped(services => new HttpClient(UiServiceCollectionExtensions.Http(services))
        {
            BaseAddress = new Uri(agentUrl),
            Timeout = WslcAgentApi.RequestTimeout,
        });

        // In every configuration, not only in Debug: on Android this is logcat, and
        // a release client that fails on someone's phone left no trace at all —
        // which is how one yellow error bar cost an evening.
        builder.Logging.AddDebug();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }

    /// <summary>
    /// The WebView serves the UI from an <c>https://</c> origin of its own,
    /// while the agent it talks to is plain HTTP on the LAN. Chromium calls the
    /// exec terminal's <c>ws://</c> socket blockable mixed content and refuses
    /// it, which left the terminal dead in both native clients. The app already
    /// speaks cleartext to that same agent for every request, so the setting
    /// only lets the socket follow the traffic that is already allowed.
    /// </summary>
    private static void AllowPlainAgentConnections()
    {
#if WINDOWS
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--allow-running-insecure-content");
#elif ANDROID
        Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping(
            "WslcAgentMixedContent",
            (handler, _) =>
            {
                if (handler.PlatformView.Settings is { } settings)
                {
                    settings.MixedContentMode = Android.Webkit.MixedContentHandling.AlwaysAllow;
                }
            });
#endif
    }
}
