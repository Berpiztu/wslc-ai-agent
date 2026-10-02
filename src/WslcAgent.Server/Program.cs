using ModelContextProtocol.AspNetCore;
using WslcAgent.Mcp;
using WslcAgent.Mcp.Tools;
using WslcAgent.Server;
using WslcAgent.Server.Auth;
using WslcAgent.Server.Browse;
using WslcAgent.Server.ClientPackages;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Endpoints;
using WslcAgent.Server.Host;
using WslcAgent.Server.Images;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Mcp;
using WslcAgent.Server.Networks;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Overview;
using WslcAgent.Server.Publishing;
using WslcAgent.Server.Registries;
using WslcAgent.Server.SavedLogins;
using WslcAgent.Server.Sessions;
using WslcAgent.Server.Testing;
using WslcAgent.Server.Updates;
using WslcAgent.Server.Volumes;
using WslcAgent.Server.Wslc;

var builder = WebApplication.CreateBuilder(args);

// What the installer's wizard chose, written beside the executable as plain
// text rather than in the registry: the package folder. A run from the
// checkout has none.
builder.Configuration.AddIniFile(Path.Combine(AppContext.BaseDirectory, "wslc-ai-agent.ini"), optional: true);

// Installed agents listen where the installer said (HKCU); --urls,
// ASPNETCORE_URLS and launchSettings still take precedence.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls(InstalledAgentSettings.ListenUrl());
}

var agentInfo = new AgentInfo();
// The installed agent's version under its registry key, for an older installer to name it.
InstalledAgentSettings.RecordVersion(agentInfo.Version);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IAgentInfo>(agentInfo);
builder.Services.Configure<WslcOptions>(builder.Configuration.GetSection(WslcOptions.Section));
builder.Services.AddSingleton<AgentLogFile>();
builder.Services.AddSingleton<AgentLogs>();
builder.Logging.Services.AddSingleton<ILoggerProvider, AgentFileLoggerProvider>();
builder.Services.AddSingleton<ICliActivity, CliActivity>();
builder.Services.AddSingleton<ICliActivityLog>(services => services.GetRequiredService<AgentLogs>());
builder.Services.AddSingleton<ISelectedSession, SelectedSession>();
builder.Services.AddSingleton<StoppedSessions>();
builder.Services.AddSingleton<WslcRunner>();
builder.Services.AddSingleton<IWslcRunner>(services => ActivatorUtilities.CreateInstance<CachingWslcRunner>(services, services.GetRequiredService<WslcRunner>()));
builder.Services.AddSingleton<ContainerUsageScanner>();
builder.Services.AddSingleton<RestartPolicyStore>();
builder.Services.AddSingleton<WslcAgent.Server.Resources.ResourceRegistry>();
builder.Services.AddSingleton<WslcEvents>();
builder.Services.AddSingleton<NotificationSettingsStore>();
builder.Services.AddSingleton<NotificationHistory>();
builder.Services.AddSingleton<NotificationDeviceStore>();
builder.Services.AddSingleton<FirebasePush>();
builder.Services.AddSingleton<Notifier>();
builder.Services.AddSingleton<INotificationService>(services => services.GetRequiredService<Notifier>());
builder.Services.AddSingleton<ContainerStops>();
builder.Services.AddHostedService<NotificationWatcher>();
builder.Services.AddHostedService<WslcEventReader>();
builder.Services.AddSingleton<RestartPolicyReconciler>();
builder.Services.AddHostedService<RestartReconciler>();
builder.Services.AddSingleton<ContainerRecreations>();
builder.Services.AddSingleton<IContainerService, ContainerService>();
builder.Services.AddSingleton<ILaunchChecks, LaunchChecks>();
builder.Services.AddSingleton<IContainerBackups, ContainerBackups>();
builder.Services.Configure<ExecTerminalOptions>(builder.Configuration.GetSection(ExecTerminalOptions.Section));
builder.Services.AddSingleton<ExecTerminals>();
builder.Services.AddSingleton<NativeTerminals>();
builder.Services.AddSingleton<ContainerFiles>();
builder.Services.AddSingleton<ContainerTransfers>();
builder.Services.AddSingleton<FilesHelpers>();
builder.Services.Configure<BrowseOptions>(builder.Configuration.GetSection(BrowseOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AgentLogin>();
builder.Services.AddSingleton<SavedLoginStore>();
builder.Services.AddSingleton<TestingSettingsStore>();
builder.Services.AddSingleton<IBrowserPageFactory, PlaywrightPages>();
builder.Services.AddSingleton<HostBrowserSessions>();
builder.Services.AddSingleton<BrowseStream>();
builder.Services.AddHostedService<HostBrowserReaper>();
builder.Services.AddSingleton<DashboardV2Store>();
builder.Services.AddSingleton<ObjectDefaultsStore>();
builder.Services.AddSingleton<LoadedDefaults>();
builder.Services.AddSingleton<DefaultsVersions>();
builder.Services.AddSingleton<DashboardDefaults>();
builder.Services.AddSingleton<DashboardHistory>();
builder.Services.AddSingleton<McpSettingsStore>();
builder.Services.AddSingleton<SkillFile>();
builder.Services.AddSingleton<SkillInstaller>();
builder.Services.AddSingleton<SkillClients>();
builder.Services.AddSingleton<IMcpSwitches, SavedMcpSwitches>();
builder.Services.AddSingleton(services => new ApprovalGate(
    () => services.GetRequiredService<IMcpSwitches>().AllowDestructiveTools));
builder.Services.AddSingleton<IImageService, ImageService>();
builder.Services.AddSingleton<ImageBuilds>();
builder.Services.AddSingleton<ImagePulls>();
builder.Services.AddSingleton<ImagePushes>();
builder.Services.AddSingleton<ImagePublisher>();
builder.Services.AddSingleton<ContainerLaunches>();
builder.Services.AddSingleton<ImageArchives>();
builder.Services.AddSingleton<IVolumeService, VolumeService>();
builder.Services.AddSingleton<INetworkService, NetworkService>();
builder.Services.AddSingleton<ContainerRestarter>();
builder.Services.AddSingleton<PublishingSettingsStore>();
builder.Services.AddSingleton<PublicationStore>();
builder.Services.AddSingleton<PublishingService>();
builder.Services.AddSingleton<IPublishingService>(services => services.GetRequiredService<PublishingService>());
builder.Services.AddSingleton<IPublishingSetup, PublishingSetup>();
builder.Services.AddSingleton<NetworkTopologyReader>();
builder.Services.AddSingleton<ISessionService, SessionService>();
builder.Services.AddSingleton<ISystemService, SystemService>();
builder.Services.AddSingleton<IHomeService, HomeService>();
builder.Services.AddSingleton<RegistryLogins>();
builder.Services.AddSingleton<RegistryService>();
builder.Services.AddSingleton<TerminalJobs>();
builder.Services.AddSingleton<PackageFolders>();
builder.Services.AddSingleton<IClientPackageService, ClientPackageService>();
builder.Services.AddSingleton<AgentUpdateSettingsStore>();
builder.Services.AddSingleton<AgentUpdateLauncher>();
builder.Services.AddSingleton<AgentUpdater>();
builder.Services.AddHostedService(services => services.GetRequiredService<AgentUpdater>());
builder.Services
    .AddMcpServer(options => options.ServerInfo = new() { Name = "wslc-ai-agent", Version = agentInfo.Version })
    // Stateful: a destructive tool asks the user through the client's own
    // approval prompt, and a server with no session has no way to ask.
    .WithHttpTransport(transport => transport.SessionMode = HttpServerSessionMode.Stateful)
    .WithToolsFromAssembly(typeof(HealthTools).Assembly)
    .WithOperatorSwitches();

var app = builder.Build();

// Waiting for the installers before anyone asks where to put them.
app.Services.GetRequiredService<PackageFolders>().EnsureExists();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseWebAssemblyDebugging();

    // Nothing of a development build is worth keeping: the .NET SDK stamps each
    // asset's name with a hash of its content, and a rebuild that reuses a name
    // for different bytes leaves the browser serving yesterday's file from its
    // own store — the app then stops at 99 %, since the hash no longer matches.
    // no-store is stronger than the no-cache these assets carry: it forbids
    // keeping a copy at all, so every build is fetched afresh.
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            return Task.CompletedTask;
        });

        await next();
    });
}

app.UseWslcProblemDetails();

// The exec terminal and the host browser pane are WebSockets.
app.UseWebSockets();

// Stopping the agent ends them at once instead of waiting for every client to go.
app.UseWebSocketShutdown();

// The agent's own login: every request below needs a session, the API token or a
// caller at the agent's machine, whatever proxy stands in front (docs/api-v1.md).
// After the WebSockets, so a socket's handshake is recognised as one.
app.UseMiddleware<AgentAuthMiddleware>();

// The Blazor WebAssembly UI (WslcAgent.Web) is served by the agent itself.
// MapStaticAssets covers the referenced project's fingerprinted assets,
// including _framework/; the older UseBlazorFrameworkFiles must not be added
// on top, it looks for unfingerprinted file names and fails with a 500.
app.MapStaticAssets();

app.MapGroup("/api/v1")
    .MapAgentEndpoints()
    .MapAgentUpdateEndpoints()
    .MapLoginEndpoints()
    .MapSavedLoginEndpoints()
    .MapTestingEndpoints()
    .MapMcpSettingsEndpoints()
    .MapSessionEndpoints()
    .MapEventEndpoints()
    .MapNotificationEndpoints()
    .MapHomeEndpoints()
    .MapSystemEndpoints()
    .MapLogEndpoints()
    .MapRegistryEndpoints()
    .MapTerminalEndpoints()
    .MapContainerEndpoints()
    .MapBrowseEndpoints()
    .MapImageEndpoints()
    .MapVolumeEndpoints()
    .MapNetworkEndpoints()
    .MapPublishingEndpoints()
    .MapHostEndpoints()
    .MapClientEndpoints();

// The MCP endpoint lives under the API's own version: the protocol version is
// negotiated by the SDK, but the tool surface is ours, and the day a tool
// changes shape the old one has to stay reachable — which a path without a
// version cannot do. One path only: nothing is registered anywhere else.
app.MapMcp("/api/v1/mcp");
app.MapWellKnownSkillEndpoints();
app.MapUiFallback();

app.Run();

// Exposes the entry point to WebApplicationFactory in the test project.
public partial class Program;
