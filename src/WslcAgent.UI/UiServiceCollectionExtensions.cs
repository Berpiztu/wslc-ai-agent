using Berpiztu.Dashboard;
using Berpiztu.Dashboard.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using WslcAgent.ApiClient;
using WslcAgent.UI.Access;
using WslcAgent.UI.Components;
using WslcAgent.UI.Dashboard;
using WslcAgent.UI.Dashboard.Cards;
using WslcAgent.UI.Files;
using WslcAgent.UI.Layout;
using WslcAgent.UI.Updates;

namespace WslcAgent.UI;

/// <summary>Everything the shared UI needs from a host, registered in one call.</summary>
public static class UiServiceCollectionExtensions
{
    /// <summary>
    /// Registers MudBlazor, the typed agent client (over the host's
    /// <see cref="HttpClient"/>), the page chrome and the client update check.
    /// A native host registers its <see cref="IClientUpdates"/> before calling
    /// this; the browser keeps the no-op. The host's <see cref="HttpClient"/> sends calls through
    /// <see cref="Http"/>, which carries a native client's session and reports a refusal.
    /// </summary>
    /// <summary>
    /// The handler a host's <see cref="HttpClient"/> is built on: the session travels
    /// with every call, and every call says whether the agent answers (<see cref="AgentLink"/>).
    /// </summary>
    public static HttpMessageHandler Http(IServiceProvider services) =>
        new AgentLinkHandler(services.GetRequiredService<AgentLink>())
        {
            InnerHandler = new AgentAccessHandler(services.GetRequiredService<AgentAccessToken>()) { InnerHandler = new HttpClientHandler() },
        };

    public static IServiceCollection AddWslcAgentUi(this IServiceCollection services)
    {
        // Toasts top right over the title bar, and errors that
        // wait to be closed (ErrorsStaySnackbar wraps MudBlazor's own service).
        services.AddMudServices(options =>
        {
            options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;
            // The toast slides in over the title bar (wslc-agent-ui.css); MudBlazor's own
            // one-second fade would hold the slide back until it had finished.
            options.SnackbarConfiguration.ShowTransitionDuration = 0;
            // It stays as long as it stayed; what was slow was the leaving. Two
            // seconds of fading read as a toast that will not go away, and they
            // pile up when several verbs answer at once.
            options.SnackbarConfiguration.HideTransitionDuration = 150;
        });
        services.AddScoped<SnackbarService>();
        services.AddScoped<AgentLink>();
        // The last catch: what Blazor's renderer would have put in its yellow bar goes to a toast instead.
        services.AddSingleton<UnhandledErrors>();
        services.AddSingleton<ILoggerProvider, UnhandledErrorLoggerProvider>();
        services.AddScoped<ISnackbar>(sp => new ErrorsStaySnackbar(sp.GetRequiredService<SnackbarService>(), sp.GetRequiredService<IJSRuntime>(), sp.GetRequiredService<AgentLink>()));
        services.TryAddSingleton<IAgentTokenStore, CookieTokenStore>();
        services.TryAddSingleton<IClientServers, NoClientServers>();
        services.AddSingleton(sp => new AgentAccessToken { Value = sp.GetRequiredService<IAgentTokenStore>().Load() });
        services.AddScoped<WslcAgentApi>();
        services.AddScoped<PageChrome>();
        services.AddScoped<HostDevice>();
        services.AddScoped<BusyRows>();
        services.AddScoped<OpenPopups>();
        services.AddScoped<SessionState>();
        services.AddScoped<SessionPower>();
        services.AddScoped<OpenDetails>();
        services.AddScoped<ViewPreference>();
        services.AddScoped<ListOrder>();
        services.AddScoped<LogsPreference>();
        services.AddScoped<ThemePreference>();
        services.AddScoped<WslcAgent.UI.Components.Styles.UiStyle>();
        services.AddSingleton<SimulatedRemote>();
        services.TryAddSingleton<IClientUpdates, NoClientUpdates>();
        services.TryAddSingleton<IClientFiles, NoClientFiles>();
        services.TryAddSingleton<WslcAgent.UI.Links.IClientLinks, WslcAgent.UI.Links.NoClientLinks>();
        services.TryAddSingleton<WslcAgent.UI.Lifetime.IClientLifetime, WslcAgent.UI.Lifetime.NoClientLifetime>();
        services.TryAddSingleton<WslcAgent.UI.Notifications.IClientNotifications, WslcAgent.UI.Notifications.NoClientNotifications>();
        services.AddScoped<ClientUpdateChecker>();
        services.AddScoped<AgentChanges>();
        services.AddScoped<FileTransfers>();
        services.AddScoped<AgentBuild>();
        // The dashboard: Berpiztu's, with WSLC's
        // objects found in this assembly, their container family, and the
        // agent keeping the dashboard.
        services.AddBerpiztuDashboard(typeof(UiServiceCollectionExtensions).Assembly);
        services.AddScoped<ContainerSourceFamily>();
        services.AddScoped<ISourceFamily>(provider => provider.GetRequiredService<ContainerSourceFamily>());
        services.AddScoped<ISourceFamily, SubjectSourceFamily>();
        services.AddScoped<ISourceFamily, ImageSourceFamily>();
        services.AddScoped<ISourceFamily, VolumeSourceFamily>();
        services.AddScoped<ISourceFamily, NetworkSourceFamily>();
        foreach (var card in WslcCards.All)
        {
            services.AddSingleton(card);
        }

        services.AddScoped<ContainerRows>();
        services.AddScoped<ImageRows>();
        services.AddScoped<VolumeRows>();
        services.AddScoped<NetworkRows>();
        services.AddScoped<TransferRows>();
        services.AddScoped<TransferCardFilters>();
        services.AddScoped<ContainerStatsReads>();
        services.AddScoped<HostOverviewRead>();
        services.AddScoped<HostRuntimeRead>();
        services.AddScoped<HostDiskRead>();
        services.AddScoped<HostIoRead>();
        services.AddScoped<AgentHealthRead>();
        services.AddScoped<SystemInfoRead>();
        services.AddScoped<EventStatusRead>();
        services.AddScoped<AgentDashboardStore>();
        services.AddScoped<DeviceDashboardStore>();
        services.AddScoped<DeviceDashboardDrafts>();
        services.AddScoped<DashboardPlace>();
        services.AddScoped<DashboardMeasures>();
        services.AddScoped<AgentObjectDefaults>();
        services.AddScoped<Berpiztu.Dashboard.Storage.IDashboardFiles, AgentDashboardFiles>();
        return services;
    }
}
