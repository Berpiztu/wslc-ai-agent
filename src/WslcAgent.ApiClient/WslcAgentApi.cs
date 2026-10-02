using System.Net.Http.Json;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.ApiClient;

/// <summary>
/// Typed client for <c>/api/v1</c>. Both hosts register it over an
/// <see cref="HttpClient"/> whose base address is the agent and whose
/// timeout allows an image pull.
/// </summary>
public sealed class WslcAgentApi(HttpClient http, AgentAccessToken? access = null)
{
    /// <summary>Long enough for an image pull, which the agent runs to completion before answering.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(35);

    public Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<HealthResponse>("api/v1/health", cancellationToken);

    public Task<VersionResponse?> GetVersionAsync(CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<VersionResponse>("api/v1/version", cancellationToken);

    // Home

    public Task<HomeOverview> GetHomeOverviewAsync(CancellationToken cancellationToken = default) =>
        GetAsync<HomeOverview>("api/v1/home", cancellationToken);

    public Task<HomeRuntime> GetHomeRuntimeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<HomeRuntime>("api/v1/home/metrics/runtime", cancellationToken);

    public Task<HomeIo> GetHomeIoAsync(CancellationToken cancellationToken = default) =>
        GetAsync<HomeIo>("api/v1/home/metrics/io", cancellationToken);

    public Task<HomeDisk> GetHomeDiskAsync(CancellationToken cancellationToken = default) =>
        GetAsync<HomeDisk>("api/v1/home/metrics/disk", cancellationToken);

    /// <summary>
    /// The dashboard as the user keeps it with this agent, the client's own
    /// text, sent and read back as it was written, so an older client never
    /// loses what a newer one wrote; the shipped default when they have none yet.
    /// </summary>
    public async Task<string> GetUserDashboardV2Async(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/v1/me/dashboard-v2", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>The user's dashboard, kept as it is written; empty forgets it.</summary>
    public Task SetUserDashboardV2Async(string layout, CancellationToken cancellationToken = default) =>
        PutTextAsync("api/v1/me/dashboard-v2", layout, cancellationToken);

    /// <summary>The dashboard's default as the agent ships it, the client's own text; empty when none is shipped.</summary>
    public async Task<string> GetDefaultDashboardV2Async(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/v1/dashboard-v2/default", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>
    /// The dashboard's default, the one a user with none is given (Save as
    /// default): written back to the repository by a development agent; on an
    /// installed one, the views it changes become the agent's own default.
    /// </summary>
    public Task SetDefaultDashboardV2Async(string layout, CancellationToken cancellationToken = default) =>
        PutTextAsync("api/v1/dashboard-v2/default", layout, cancellationToken);

    /// <summary>How each kind of dashboard object is born, view by view, and whether this agent writes them back (a development build).</summary>
    public Task<ObjectDefaultsResponse> GetObjectDefaultsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<ObjectDefaultsResponse>("api/v1/dashboard/object-defaults", cancellationToken);

    /// <summary>
    /// How one kind of dashboard object is born in one view (desktop, mobile),
    /// merged into the rest: written back to its repository by a development
    /// agent, kept as its own by an installed one.
    /// </summary>
    public Task SetObjectDefaultAsync(string view, string type, string value, CancellationToken cancellationToken = default) =>
        PutTextAsync($"api/v1/dashboard/object-defaults/{Uri.EscapeDataString(view)}/{Uri.EscapeDataString(type)}", value, cancellationToken);

    /// <summary>The two shipped files, the objects' defaults and the default dashboard: shipped, loaded and waiting in the package folder.</summary>
    public Task<DashboardDefaultsStatus> GetDashboardDefaultsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<DashboardDefaultsStatus>("api/v1/dashboard/defaults", cancellationToken);

    /// <summary>What a defaults file holds, and whether this agent can load it.</summary>
    public Task<DefaultsFileInfo> InspectDefaultsFileAsync(string file, CancellationToken cancellationToken = default) =>
        PostTextAsync<DefaultsFileInfo>("api/v1/dashboard/defaults/inspect", file, cancellationToken);

    /// <summary>The objects' defaults of a file, or of the package folder's when <paramref name="file"/> is empty, used from now on.</summary>
    public Task<DashboardDefaultsStatus> LoadObjectDefaultsAsync(string file = "", CancellationToken cancellationToken = default) =>
        PostTextAsync<DashboardDefaultsStatus>("api/v1/dashboard/defaults/object-defaults", file, cancellationToken);

    /// <summary>The objects' defaults in use, as a file another agent loads.</summary>
    public async Task<string> ExportObjectDefaultsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/v1/dashboard/defaults/object-defaults/export", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Every change the user made to the objects' defaults on this agent dropped (the agent backs them up first).</summary>
    public Task<DashboardDefaultsStatus> ClearObjectDefaultChangesAsync(CancellationToken cancellationToken = default) =>
        DeleteAsync<DashboardDefaultsStatus>("api/v1/dashboard/defaults/object-defaults/changes", cancellationToken);

    /// <summary>Conflicts between the user's changes and a newer base settled, kind by kind.</summary>
    public Task<DashboardDefaultsStatus> SettleObjectDefaultConflictsAsync(IEnumerable<ObjectDefaultChoice> choices, CancellationToken cancellationToken = default) =>
        PostJsonAsync<ObjectDefaultChoice[], DashboardDefaultsStatus>("api/v1/dashboard/defaults/object-defaults/conflicts", [.. choices], cancellationToken);

    /// <summary>The objects' defaults the agent ships, the base again; the user's changes stay over it.</summary>
    public Task<DashboardDefaultsStatus> ResetObjectDefaultsAsync(CancellationToken cancellationToken = default) =>
        DeleteAsync<DashboardDefaultsStatus>("api/v1/dashboard/defaults/object-defaults", cancellationToken);

    /// <summary>
    /// The views named of a dashboard file, or of the package folder's when
    /// <paramref name="file"/> is empty, in place of the user's same views; the
    /// agent backs the dashboard up first.
    /// </summary>
    public Task<DashboardDefaultsStatus> ImportUserDashboardV2Async(IEnumerable<string> views, string file = "", CancellationToken cancellationToken = default) =>
        PostTextAsync<DashboardDefaultsStatus>($"api/v1/me/dashboard-v2/import?views={Escape(string.Join(',', views))}", file, cancellationToken);

    /// <summary>The views named of the user's dashboard put back to the default the agent has, after a backup.</summary>
    public Task<DashboardDefaultsStatus> ResetUserDashboardV2ViewsAsync(IEnumerable<string> views, CancellationToken cancellationToken = default) =>
        PostTextAsync<DashboardDefaultsStatus>($"api/v1/me/dashboard-v2/reset?views={Escape(string.Join(',', views))}", "", cancellationToken);

    /// <summary>Every state the user's dashboard was saved in, with its origin and revision (v2.3), the newest first.</summary>
    public Task<IReadOnlyList<DashboardRevision>> GetUserDashboardV2HistoryAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<DashboardRevision>>("api/v1/me/dashboard-v2/history", cancellationToken);

    /// <summary>A state the user's dashboard was saved in brought back, as it was.</summary>
    public Task<DashboardDefaultsStatus> RestoreUserDashboardV2RevisionAsync(int id, CancellationToken cancellationToken = default) =>
        PostTextAsync<DashboardDefaultsStatus>($"api/v1/me/dashboard-v2/history/{id}/restore", "", cancellationToken);

    /// <summary>The whole user's dashboard back to the release, every view at once.</summary>
    public Task<DashboardDefaultsStatus> ResetUserDashboardV2ToReleaseAsync(CancellationToken cancellationToken = default) =>
        PostTextAsync<DashboardDefaultsStatus>("api/v1/me/dashboard-v2/reset-all", "", cancellationToken);

    /// <summary>The views named made the agent's own default, from a file, or the newest shipped or waiting when <paramref name="file"/> is empty.</summary>
    public Task<DashboardDefaultsStatus> LoadDefaultDashboardV2ViewsAsync(IEnumerable<string> views, string file = "", CancellationToken cancellationToken = default) =>
        PostTextAsync<DashboardDefaultsStatus>($"api/v1/dashboard-v2/default/views?views={Escape(string.Join(',', views))}", file, cancellationToken);

    /// <summary>The views named of the agent's default back to the shipped ones.</summary>
    public Task<DashboardDefaultsStatus> ResetDefaultDashboardV2ViewsAsync(IEnumerable<string> views, CancellationToken cancellationToken = default) =>
        DeleteAsync<DashboardDefaultsStatus>($"api/v1/dashboard-v2/default/views?views={Escape(string.Join(',', views))}", cancellationToken);

    /// <summary>The views named of the user's dashboard, as a file another agent loads.</summary>
    public async Task<string> ExportUserDashboardV2Async(IEnumerable<string> views, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/v1/me/dashboard-v2/export?views={Escape(string.Join(',', views))}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>A body that is the client's own text, with an answer; a problem-details error becomes an <see cref="AgentApiException"/>.</summary>
    private async Task<TResult> PostTextAsync<TResult>(string path, string text, CancellationToken cancellationToken)
    {
        using var content = new StringContent(text, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(path, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResult>(cancellationToken)
            ?? throw new AgentApiException((int)response.StatusCode, "Empty answer");
    }

    /// <summary>A body that is the client's own text, sent as it is.</summary>
    private async Task PutTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        using var content = new StringContent(text, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PutAsync(path, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    // System

    public Task<SystemOverview> GetSystemAsync(CancellationToken cancellationToken = default) =>
        GetAsync<SystemOverview>("api/v1/system", cancellationToken);

    /// <summary><c>images</c>, <c>volumes</c> or <c>networks</c>.</summary>
    public Task<CleanupResult> CleanupAsync(string target, CancellationToken cancellationToken = default) =>
        PostJsonAsync<object?, CleanupResult>($"api/v1/system/cleanup/{Escape(target)}", null, cancellationToken);

    // Sign-in

    public Task<LoginStatus> GetLoginStatusAsync(CancellationToken cancellationToken = default) =>
        GetAsync<LoginStatus>("api/v1/login", cancellationToken);

    public Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default) =>
        PostJsonAsync<LoginRequest, LoginResult>("api/v1/login", new LoginRequest(username, password), cancellationToken);

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        PostAsync("api/v1/logout", cancellationToken);

    public Task SetLoginCredentialsAsync(string username, string password, CancellationToken cancellationToken = default) =>
        PutJsonAsync("api/v1/login/credentials", new SetCredentialsRequest(username, password), cancellationToken);

    public Task<ApiTokenResult> RenewApiTokenAsync(CancellationToken cancellationToken = default) =>
        PostJsonAsync<object?, ApiTokenResult>("api/v1/login/api-token", null, cancellationToken);

    // Saved logins (the host browser pane's picker, edited in Settings)

    public Task<IReadOnlyList<SavedLogin>> GetSavedLoginsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SavedLogin>>("api/v1/saved-logins", cancellationToken);

    public Task<SavedLogin> CreateSavedLoginAsync(SavedLoginInput input, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SavedLoginInput, SavedLogin>("api/v1/saved-logins", input, cancellationToken);

    public Task<SavedLogin> UpdateSavedLoginAsync(string id, SavedLoginInput input, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SavedLoginInput, SavedLogin>($"api/v1/saved-logins/{Escape(id)}", input, cancellationToken, HttpMethod.Put);

    public Task DeleteSavedLoginAsync(string id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/saved-logins/{Escape(id)}", cancellationToken);

    // Notifications (Settings → Notifications)

    /// <summary>The notifications raised after <paramref name="after"/>, oldest first, and the last id raised; <c>long.MaxValue</c> asks for none, only where the list stands.</summary>
    public Task<NotificationList> GetNotificationsAsync(long after, CancellationToken cancellationToken = default) =>
        GetAsync<NotificationList>($"api/v1/notifications?after={after}", cancellationToken);

    public Task<NotificationSettings> GetNotificationSettingsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<NotificationSettings>("api/v1/notifications/settings", cancellationToken);

    public Task<NotificationSettings> SetNotificationSettingsAsync(NotificationSettings settings, CancellationToken cancellationToken = default) =>
        PostJsonAsync<NotificationSettings, NotificationSettings>("api/v1/notifications/settings", settings, cancellationToken, HttpMethod.Put);

    /// <summary>Whether the agent can send to phones, and the phones it sends to.</summary>
    public Task<NotificationDevices> GetNotificationDevicesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<NotificationDevices>("api/v1/notifications/devices", cancellationToken);

    /// <summary>This device, and the Firebase token its notifications go to; registering again with the same token only refreshes it.</summary>
    public Task<NotificationDevice> RegisterNotificationDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<RegisterDeviceRequest, NotificationDevice>("api/v1/notifications/devices", request, cancellationToken);

    /// <summary>A device no longer sent to.</summary>
    public Task RemoveNotificationDeviceAsync(string id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/notifications/devices/{Escape(id)}", cancellationToken);

    // Agent update (Settings → Agent update)

    public Task<AgentUpdateStatus> GetAgentUpdateAsync(CancellationToken cancellationToken = default) =>
        GetAsync<AgentUpdateStatus>("api/v1/agent/update", cancellationToken);

    public Task<AgentUpdateStatus> SetAgentUpdateSettingsAsync(AgentUpdateSettings settings, CancellationToken cancellationToken = default) =>
        PostJsonAsync<AgentUpdateSettings, AgentUpdateStatus>("api/v1/agent/update/settings", settings, cancellationToken, HttpMethod.Put);

    /// <summary>Update now: the agent installs the newer installer in its package folder as soon as nothing is being transferred.</summary>
    public Task<AgentUpdateStatus> UpdateAgentAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<AgentUpdateStatus>(HttpMethod.Post, "api/v1/agent/update", cancellationToken);

    /// <summary>Stops an update that has not started: the countdown every client shows, or one waiting for the transfers.</summary>
    public Task<AgentUpdateStatus> CancelAgentUpdateAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<AgentUpdateStatus>(HttpMethod.Post, "api/v1/agent/update/cancel", cancellationToken);

    // MCP server (Settings → MCP server)

    /// <param name="address">How the machine that will run the install command reaches this agent; empty for the agent's own address.</param>
    public Task<McpStatus> GetMcpSettingsAsync(string address = "", CancellationToken cancellationToken = default) =>
        GetAsync<McpStatus>(address.Length > 0 ? $"api/v1/mcp/settings?address={Escape(address)}" : "api/v1/mcp/settings", cancellationToken);

    public Task<McpStatus> SetMcpSettingsAsync(McpSettings settings, CancellationToken cancellationToken = default) =>
        PostJsonAsync<McpSettings, McpStatus>("api/v1/mcp/settings", settings, cancellationToken, HttpMethod.Put);

    /// <summary>Where the browser saves the skill from: the agent hands out its own copy.</summary>
    public Uri McpSkillDownloadUrl() => new(http.BaseAddress!, "api/v1/mcp/skill");

    /// <param name="ssh">The machine to ask, as ssh takes it; empty asks the agent's own.</param>
    public Task<IReadOnlyList<SkillClient>> GetSkillClientsAsync(string ssh = "", CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SkillClient>>(ssh.Length > 0 ? $"api/v1/mcp/skill/clients?ssh={Escape(ssh)}" : "api/v1/mcp/skill/clients", cancellationToken);

    /// <summary>Runs that client's own installer, here or over ssh on the machine the client lives on.</summary>
    public Task<SkillInstallResult> InstallMcpSkillAsync(SkillInstallRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SkillInstallRequest, SkillInstallResult>("api/v1/mcp/skill/install", request, cancellationToken);

    // Testing (Settings → Testing)

    public Task<TestingSettings> GetTestingSettingsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<TestingSettings>("api/v1/testing", cancellationToken);

    public Task<TestingSettings> SetTestingSettingsAsync(TestingSettings settings, CancellationToken cancellationToken = default) =>
        PostJsonAsync<TestingSettings, TestingSettings>("api/v1/testing", settings, cancellationToken, HttpMethod.Put);

    // Publishing (Settings → Publishing, and Publish on a container)

    public Task<PublishingSettings> GetPublishingSettingsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<PublishingSettings>("api/v1/publishing/settings", cancellationToken);

    public Task<PublishingSettings> SetPublishingSettingsAsync(PublishingSettings settings, CancellationToken cancellationToken = default) =>
        PostJsonAsync<PublishingSettings, PublishingSettings>("api/v1/publishing/settings", settings, cancellationToken, HttpMethod.Put);

    public Task<PublishingSetupResult> SetUpPublishingAsync(CancellationToken cancellationToken = default) =>
        PostJsonAsync<object, PublishingSetupResult>("api/v1/publishing/setup", new { }, cancellationToken);

    public async Task<IReadOnlyList<Publication>> ListPublicationsAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync<PublicationList>("api/v1/publications", cancellationToken)).Publications;

    public Task<Publication> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<PublishRequest, Publication>("api/v1/publications", request, cancellationToken);

    public Task UnpublishAsync(string hostname, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/publications/{Escape(hostname)}", cancellationToken);

    // Host terminal

    public Task<TerminalStatus> GetTerminalStatusAsync(CancellationToken cancellationToken = default) =>
        GetAsync<TerminalStatus>("api/v1/terminal/status", cancellationToken);

    /// <summary>The host shell's WebSocket, in the same protocol as a container's exec stream.</summary>
    public Uri HostTerminalStreamUrl() => WebSocketUrl("api/v1/terminal/stream");

    /// <summary>
    /// Where the agent says what changed (<c>ChangeNotice</c> per message), so a
    /// screen reads its list when something happened instead of on a clock.
    /// </summary>
    public Uri ChangeStreamUrl() => WebSocketUrl("api/v1/events/stream");

    /// <summary>Whether the agent is listening to <c>wslc events</c> right now, or the screens are reading on their own clock.</summary>
    public Task<EventStatus> GetEventStatusAsync(CancellationToken cancellationToken = default) =>
        GetAsync<EventStatus>("api/v1/events/status", cancellationToken);

    public Task<NativeTerminalResult> OpenNativeHostTerminalAsync(string command = "", CancellationToken cancellationToken = default) =>
        PostJsonAsync<OpenTerminalRequest, NativeTerminalResult>("api/v1/terminal/open-native", new OpenTerminalRequest(command), cancellationToken);

    public Task<TerminalJob> PrepareTerminalJobAsync(string job, CancellationToken cancellationToken = default) =>
        PostJsonAsync<object?, TerminalJob>($"api/v1/terminal/jobs/{Escape(job)}", null, cancellationToken);

    // Registry

    public Task<IReadOnlyList<RegistryLogin>> GetRegistryLoginsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<RegistryLogin>>("api/v1/registry/logins", cancellationToken);

    public Task RegistryLoginAsync(RegistryLoginRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync("api/v1/registry/login", request, cancellationToken);

    public Task RegistryLogoutAsync(string server = "", CancellationToken cancellationToken = default) =>
        PostJsonAsync("api/v1/registry/logout", new RegistryLogoutRequest(server), cancellationToken);

    // The agent's own log

    /// <summary>The last <paramref name="tail"/> entries, or every entry from <paramref name="from"/> on: what a page anchors itself to so its list only grows at the end.</summary>
    /// <summary>The log, abridged (a command's outcome, not its output): the last <paramref name="tail"/> entries, or the entries after the one with id <paramref name="after"/>.</summary>
    public Task<IReadOnlyList<AgentLogEntry>> GetAgentLogsAsync(int tail = 500, int? after = null, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<AgentLogEntry>>(after is { } last ? $"api/v1/logs?after={last}" : $"api/v1/logs?tail={tail}", cancellationToken);

    /// <summary>The entries with these ids, whole: a command's output entire.</summary>
    public Task<IReadOnlyList<AgentLogEntry>> GetAgentLogEntriesAsync(IReadOnlyList<int> entryIds, CancellationToken cancellationToken = default) =>
        PostJsonAsync<LogEntriesRequest, IReadOnlyList<AgentLogEntry>>("api/v1/logs/entries", new LogEntriesRequest(entryIds), cancellationToken);

    public Task<DeleteLogEntriesResult> DeleteAgentLogEntriesAsync(IReadOnlyList<int> entryIds, CancellationToken cancellationToken = default) =>
        PostJsonAsync<DeleteLogEntriesRequest, DeleteLogEntriesResult>("api/v1/logs/delete", new DeleteLogEntriesRequest(entryIds), cancellationToken);

    /// <summary>The whole log, every file emptied: the entries past the tail included.</summary>
    public Task<DeleteLogEntriesResult> ClearAgentLogsAsync(CancellationToken cancellationToken = default) =>
        DeleteAsync<DeleteLogEntriesResult>("api/v1/logs", cancellationToken);

    // Native clients

    public Task<ClientPackageInfo> GetClientPackageAsync(string platform, CancellationToken cancellationToken = default) =>
        GetAsync<ClientPackageInfo>($"api/v1/clients/{Escape(platform)}", cancellationToken);

    /// <summary>Where a browser downloads the client installer.</summary>
    public Uri ClientPackageDownloadUrl(string platform) => new(http.BaseAddress!, $"api/v1/clients/{Escape(platform)}/download");

    /// <summary>
    /// Streams the client installer to <paramref name="destinationPath"/>. Use
    /// an <see cref="HttpClient"/> without a timeout: the APK is tens of
    /// megabytes and may travel over a slow link — which is why it reports what
    /// it has read so far, and the whole size when the agent tells it, to
    /// <paramref name="progress"/>: the client shows that while it waits.
    /// </summary>
    public async Task DownloadClientPackageAsync(
        string platform,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/v1/clients/{Escape(platform)}/download", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 20, useAsync: true);

        var buffer = new byte[1 << 20];
        long received = 0;
        long reported = 0;
        progress?.Report(new DownloadProgress(0, total));
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            // Every half megabyte: often enough to look alive on a slow link,
            // rarely enough not to repaint a toast on every buffer.
            if (received - reported >= 512 * 1024)
            {
                reported = received;
                progress?.Report(new DownloadProgress(received, total));
            }
        }

        progress?.Report(new DownloadProgress(received, total ?? received));
    }

    // Sessions

    public async Task<SessionsResponse> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<SessionsResponse>("api/v1/sessions", cancellationToken)
        ?? new SessionsResponse([], "");

    public Task SelectSessionAsync(string name, CancellationToken cancellationToken = default) =>
        PostJsonAsync("api/v1/sessions/select", new SelectSessionRequest(name), cancellationToken);

    /// <summary>Starts a session; an empty name means the one the agent targets.</summary>
    public Task<SessionActionResult> StartSessionAsync(string name, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SessionActionRequest, SessionActionResult>("api/v1/sessions/start", new SessionActionRequest(name), cancellationToken);

    /// <summary>Stops a session, and everything running in it; an empty name means the one the agent targets.</summary>
    public Task<SessionActionResult> StopSessionAsync(string name, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SessionActionRequest, SessionActionResult>("api/v1/sessions/stop", new SessionActionRequest(name), cancellationToken);

    // Containers

    public async Task<ContainerListResponse> GetContainersAsync(bool all = true, bool helpers = false, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<ContainerListResponse>($"api/v1/containers?all={Flag(all)}&helpers={Flag(helpers)}", cancellationToken)
        ?? new ContainerListResponse([], new ContainerAggregate(0, 1, 0));

    public Task<ContainerCreated> CreateContainerAsync(ContainerLaunchRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<ContainerLaunchRequest, ContainerCreated>("api/v1/containers", request, cancellationToken);

    public Task<ContainerCreated> RunContainerAsync(ContainerLaunchRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<ContainerLaunchRequest, ContainerCreated>("api/v1/containers/run", request, cancellationToken);

    /// <summary>The form against itself and the machine before a run, create or recreate; <paramref name="source"/> is the container being recreated, if any.</summary>
    public Task<LaunchCheck> CheckContainerLaunchAsync(ContainerLaunchRequest request, string source = "", CancellationToken cancellationToken = default) =>
        PostJsonAsync<LaunchCheckRequest, LaunchCheck>("api/v1/containers/launch-check", new LaunchCheckRequest(request, source), cancellationToken);

    public Task<IReadOnlyList<ContainerLaunch>> GetContainerLaunchesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ContainerLaunch>>("api/v1/containers/launches", cancellationToken);

    /// <summary>Run as a job the agent owns: returns at once, the image is pulled first when it is not local.</summary>
    public Task<ContainerLaunch> LaunchContainerAsync(ContainerLaunchRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<ContainerLaunchRequest, ContainerLaunch>("api/v1/containers/launches", request, cancellationToken);

    public Task<ContainerLaunchRequest> GetContainerLaunchRequestAsync(string id, CancellationToken cancellationToken = default) =>
        GetAsync<ContainerLaunchRequest>($"api/v1/containers/launches/{Escape(id)}/request", cancellationToken);

    public Task CancelContainerLaunchAsync(string id, CancellationToken cancellationToken = default) =>
        PostAsync($"api/v1/containers/launches/{Escape(id)}/cancel", cancellationToken);

    /// <summary>Forgets a run that failed or was cancelled, so its row goes for every client; 409 while it is still on its way.</summary>
    public Task DismissContainerLaunchAsync(string id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/containers/launches/{Escape(id)}", cancellationToken);

    /// <summary>The files on their way in and out of containers, counted by the agent: the ring on the container's row.</summary>
    public Task<IReadOnlyList<ContainerTransfer>> GetContainerTransfersAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ContainerTransfer>>("api/v1/containers/transfers", cancellationToken);

    /// <summary>Stops one transfer wherever it was started from: the request is abandoned and the staged bytes go with it.</summary>
    public Task CancelContainerTransferAsync(string id, CancellationToken cancellationToken = default) =>
        PostAsync($"api/v1/containers/transfers/{Escape(id)}/cancel", cancellationToken);

    /// <summary>Forgets a transfer that failed or was cancelled, so its ring goes for every client; 409 while it is still on its way.</summary>
    public Task DismissContainerTransferAsync(string id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/containers/transfers/{Escape(id)}", cancellationToken);

    /// <summary>
    /// Says what this client is about to move before any of it moves:
    /// the files take their places at the back of
    /// the agent's queue, in this order, and the ids that come back are what
    /// make them this client's own.
    /// </summary>
    public Task<AnnouncedTransfers> AnnounceContainerTransfersAsync(string container, AnnounceTransfersRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<AnnounceTransfersRequest, AnnouncedTransfers>($"api/v1/containers/{Escape(container)}/files/queue", request, cancellationToken);

    /// <summary>Lets a whole announcement go: the files of it still waiting their turn leave the queue, for every client at once.</summary>
    public Task DropContainerTransferBatchAsync(string batch, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/containers/transfers/batch/{Escape(batch)}", cancellationToken);

    public Task<ContainerCreated> RecreateContainerAsync(string container, ContainerLaunchRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<ContainerLaunchRequest, ContainerCreated>($"api/v1/containers/{Escape(container)}/recreate", request, cancellationToken);

    /// <summary>Starts moving the container to another image: the pull started (its console follows it), then a recreate the agent runs on its own.</summary>
    public Task<ImagePullState> UpdateContainerImageAsync(string container, string image, CancellationToken cancellationToken = default) =>
        PostJsonAsync<UpdateContainerImageRequest, ImagePullState>($"api/v1/containers/{Escape(container)}/update-image", new UpdateContainerImageRequest(image), cancellationToken);

    public Task KillContainerAsync(string container, CancellationToken cancellationToken = default) =>
        PostAsync($"api/v1/containers/{Escape(container)}/kill", cancellationToken);

    public Task<ContainerDetails> GetContainerDetailsAsync(string container, CancellationToken cancellationToken = default) =>
        GetAsync<ContainerDetails>($"api/v1/containers/{Escape(container)}/details", cancellationToken);

    public async Task<string> GetContainerLogsAsync(string container, int tail = 200, bool timestamps = false, CancellationToken cancellationToken = default) =>
        (await GetAsync<ContainerLogs>($"api/v1/containers/{Escape(container)}/logs?tail={tail}&timestamps={Flag(timestamps)}", cancellationToken)).Text;

    public Task<ContainerStats> GetContainerStatsAsync(string container, CancellationToken cancellationToken = default) =>
        GetAsync<ContainerStats>($"api/v1/containers/{Escape(container)}/stats", cancellationToken);

    /// <summary>Where the browser downloads the container's inspect JSON (<c>name-inspect.json</c>).</summary>
    public Uri ContainerInspectJsonUrl(string container) =>
        new(http.BaseAddress!, $"api/v1/containers/{Escape(container)}/inspect.json");

    /// <summary>One non-interactive command inside the container; a non-zero exit code is the command's own answer.</summary>
    public Task<ContainerExecResult> ExecInContainerAsync(string container, string command, CancellationToken cancellationToken = default) =>
        PostJsonAsync<ContainerExecRequest, ContainerExecResult>($"api/v1/containers/{Escape(container)}/exec", new ContainerExecRequest(command), cancellationToken);

    /// <summary>The interactive terminal's WebSocket, on the agent's own origin (<c>ws://</c> or <c>wss://</c>).</summary>
    public Uri ContainerExecStreamUrl(string container) => WebSocketUrl($"api/v1/containers/{Escape(container)}/exec/stream");

    /// <summary>
    /// The host browser pane's WebSocket: a new or resumed browser on <paramref name="hostPort"/>,
    /// or, with <paramref name="sessionId"/>, another client's browser joined.
    /// </summary>
    public Uri ContainerBrowseStreamUrl(string container, string hostPort, string sessionId = "") =>
        WebSocketUrl($"api/v1/containers/{Escape(container)}/browse/stream?hostPort={Escape(hostPort)}{(sessionId.Length > 0 ? $"&sessionId={Escape(sessionId)}" : "")}");

    /// <summary>The live host browsers on this container's ports, newest first.</summary>
    public async Task<IReadOnlyList<BrowseSessionInfo>> GetBrowseSessionsAsync(string container, CancellationToken cancellationToken = default) =>
        (await GetAsync<BrowseSessionList>($"api/v1/containers/{Escape(container)}/browse-sessions", cancellationToken)).Sessions;

    /// <summary>
    /// An agent path as a WebSocket address: <c>ws</c> beside http, <c>wss</c> beside https. A native
    /// client's session rides along as <c>access_token</c>, since a WebSocket carries no headers.
    /// </summary>
    private Uri WebSocketUrl(string path)
    {
        var url = new Uri(http.BaseAddress!, path);
        var builder = new UriBuilder(url) { Scheme = url.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" };
        if (access?.Value is { Length: > 0 } token)
        {
            var query = builder.Query.TrimStart('?');
            builder.Query = (query.Length > 0 ? query + "&" : "") + "access_token=" + Escape(token);
        }

        return builder.Uri;
    }

    // Files inside a container

    /// <summary>One directory of the container: its entries, directories first.</summary>
    public Task<ContainerFileListing> ListContainerFilesAsync(string container, string path = "/", CancellationToken cancellationToken = default) =>
        GetAsync<ContainerFileListing>($"api/v1/containers/{Escape(container)}/files?path={Escape(path)}", cancellationToken);

    /// <summary>Where the browser downloads one file of the container.</summary>
    public Uri ContainerFileUrl(string container, string path, string queued = "") =>
        new(http.BaseAddress!, DownloadPath(container, path, queued));

    /// <summary>
    /// The file's bytes into <paramref name="destination"/>, as they arrive: a
    /// native client writes them to the file the user picked, of whatever size
    /// and whatever kind. The browser needs none of this — it downloads from
    /// <see cref="ContainerFileUrl"/> itself — and the agent counts what it
    /// sends either way, so the ring on the container's row is the same one.
    /// </summary>
    public async Task DownloadContainerFileAsync(string container, string path, Stream destination, string queued = "", CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(DownloadPath(container, path, queued), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await source.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>The file's text, for the editor; a directory or a file over a megabyte is refused.</summary>
    public Task<ContainerFileContent> ReadContainerFileAsync(string container, string path, CancellationToken cancellationToken = default) =>
        GetAsync<ContainerFileContent>($"api/v1/containers/{Escape(container)}/files/content?path={Escape(path)}", cancellationToken);

    public Task<ContainerFileEntry> WriteContainerFileAsync(string container, string path, string content, CancellationToken cancellationToken = default) =>
        PostJsonAsync<WriteFileRequest, ContainerFileEntry>($"api/v1/containers/{Escape(container)}/files/content", new WriteFileRequest(path, content), cancellationToken, HttpMethod.Put);

    /// <summary>
    /// Puts a file into a directory of the container, keeping its name.
    /// <paramref name="size"/> is what the file measures, so the agent's job
    /// knows what it is counting towards and the ring on the container's row
    /// has a percentage; without it the row can only say that something is on
    /// its way.
    /// </summary>
    public async Task<ContainerFileEntry> UploadContainerFileAsync(string container, string directory, string name, Stream content, long size = 0, string queued = "", CancellationToken cancellationToken = default)
    {
        using var response = await PostFileAsync(UploadPath(container, directory, size, queued), content, name, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ContainerFileEntry>(cancellationToken)
            ?? throw new AgentApiException((int)response.StatusCode, "Empty answer");
    }

    /// <summary>
    /// Where a browser posts a file itself, which is how the web UI uploads:
    /// .NET in WebAssembly cannot stream a request, so it would hold the whole
    /// file in memory. Same endpoint, same job, same ring.
    /// </summary>
    public Uri ContainerFileUploadUrl(string container, string directory, long size, string queued = "") =>
        new(http.BaseAddress!, UploadPath(container, directory, size, queued));

    private static string DownloadPath(string container, string path, string queued = "") =>
        $"api/v1/containers/{Escape(container)}/files/download?path={Escape(path)}{Queued(queued)}";

    private static string UploadPath(string container, string directory, long size, string queued) =>
        $"api/v1/containers/{Escape(container)}/files/upload?path={Escape(directory)}&size={size}{Queued(queued)}";

    /// <summary>The place in the agent's queue this file was given, when it has one; without it the agent makes a job on the spot.</summary>
    private static string Queued(string queued) => queued.Length > 0 ? $"&queued={Escape(queued)}" : "";

    public Task DeleteContainerFileAsync(string container, string path, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/containers/{Escape(container)}/files?path={Escape(path)}", cancellationToken);

    public Task<ContainerFileEntry> MakeContainerDirectoryAsync(string container, string path, CancellationToken cancellationToken = default) =>
        PostJsonAsync<MakeDirectoryRequest, ContainerFileEntry>($"api/v1/containers/{Escape(container)}/files/mkdir", new MakeDirectoryRequest(path), cancellationToken);

    public Task<ContainerFileEntry> RenameContainerFileAsync(string container, string source, string destination, CancellationToken cancellationToken = default) =>
        PostJsonAsync<MoveFileRequest, ContainerFileEntry>($"api/v1/containers/{Escape(container)}/files/rename", new MoveFileRequest(source, destination), cancellationToken);

    public Task<ContainerFileEntry> CopyContainerFileAsync(string container, string source, string destination, CancellationToken cancellationToken = default) =>
        PostJsonAsync<MoveFileRequest, ContainerFileEntry>($"api/v1/containers/{Escape(container)}/files/copy", new MoveFileRequest(source, destination), cancellationToken);

    /// <summary>Opens a terminal window on the agent's desktop; only the agent's own machine may ask.</summary>
    public Task<NativeTerminalResult> OpenContainerTerminalAsync(string container, string command = "", CancellationToken cancellationToken = default) =>
        PostJsonAsync<OpenTerminalRequest, NativeTerminalResult>($"api/v1/containers/{Escape(container)}/open-terminal", new OpenTerminalRequest(command), cancellationToken);

    /// <summary>The same JSON as text, for a host that saves the file itself instead of letting the browser download it.</summary>
    public async Task<string> GetContainerInspectJsonAsync(string container, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/v1/containers/{Escape(container)}/inspect.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>The launch form read from an inspect JSON (or an exported launch request) the user loaded.</summary>
    public Task<ContainerLaunchRequest> ParseLaunchFormAsync(string json, CancellationToken cancellationToken = default) =>
        PostJsonAsync<LaunchFormSource, ContainerLaunchRequest>("api/v1/containers/launch-form", new LaunchFormSource(json), cancellationToken);

    // Backups

    public Task<BackupJob> StartBackupAsync(string container, CancellationToken cancellationToken = default) =>
        PostJsonAsync<object, BackupJob>($"api/v1/containers/{Escape(container)}/backup", new { }, cancellationToken);

    public Task<BackupJob> GetBackupAsync(string job, CancellationToken cancellationToken = default) =>
        GetAsync<BackupJob>($"api/v1/containers/backups/{Escape(job)}", cancellationToken);

    /// <summary>Where the browser downloads the finished archive; the job moves to <c>saving</c> when it is fetched.</summary>
    public Uri BackupDownloadUrl(string job) =>
        new(http.BaseAddress!, $"api/v1/containers/backups/{Escape(job)}/download");

    /// <summary>Finishes or discards the job: the archive is deleted from the agent.</summary>
    public Task CancelBackupAsync(string job, CancellationToken cancellationToken = default) =>
        PostAsync($"api/v1/containers/backups/{Escape(job)}/cancel", cancellationToken);

    public Task<RestartPolicyInfo> GetRestartPolicyAsync(string container, CancellationToken cancellationToken = default) =>
        GetAsync<RestartPolicyInfo>($"api/v1/containers/{Escape(container)}/restart-policy", cancellationToken);

    public async Task<RestartPolicyInfo> SetRestartPolicyAsync(string container, string policy, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync($"api/v1/containers/{Escape(container)}/restart-policy", new SetRestartPolicyRequest(policy), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RestartPolicyInfo>(cancellationToken) ?? RestartPolicyInfo.None;
    }

    // Host

    public Task<HostFolderListing> GetHostFoldersAsync(string path = "", CancellationToken cancellationToken = default) =>
        GetAsync<HostFolderListing>($"api/v1/host/folders?path={Escape(path)}", cancellationToken);

    public Task<HostFolderListing> CreateHostFolderAsync(string parent, string name, CancellationToken cancellationToken = default) =>
        PostJsonAsync<CreateHostFolderRequest, HostFolderListing>("api/v1/host/folders", new CreateHostFolderRequest(parent, name), cancellationToken);

    public Task StartContainerAsync(string container, CancellationToken cancellationToken = default) =>
        PostAsync($"api/v1/containers/{Escape(container)}/start", cancellationToken);

    public Task StopContainerAsync(string container, CancellationToken cancellationToken = default) =>
        PostAsync($"api/v1/containers/{Escape(container)}/stop", cancellationToken);

    public Task RestartContainerAsync(string container, CancellationToken cancellationToken = default) =>
        PostAsync($"api/v1/containers/{Escape(container)}/restart", cancellationToken);

    public Task RemoveContainerAsync(string container, bool force = false, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/containers/{Escape(container)}?force={Flag(force)}", cancellationToken);

    // Images

    public async Task<ImageListResponse> GetImagesAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<ImageListResponse>("api/v1/images", cancellationToken)
        ?? new ImageListResponse([], new ImageAggregate(0, 0));

    public Task<ImageInspect> GetImageInspectAsync(string reference, CancellationToken cancellationToken = default) =>
        GetAsync<ImageInspect>($"api/v1/images/inspect?reference={Escape(reference)}", cancellationToken);

    public Task PullImageAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default) =>
        PostJsonAsync("api/v1/images/pull", new PullImageRequest(reference, allTags), cancellationToken);

    public Task TagImageAsync(string source, string target, CancellationToken cancellationToken = default) =>
        PostJsonAsync("api/v1/images/tag", new TagImageRequest(source, target), cancellationToken);

    public Task RemoveImageAsync(string reference, bool force = false, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/images?reference={Escape(reference)}&force={Flag(force)}", cancellationToken);

    public Task<IReadOnlyList<ImagePullState>> GetImagePullsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ImagePullState>>("api/v1/images/pulls", cancellationToken);

    /// <summary>Starts a pull the agent owns and returns at once; one already running is joined.</summary>
    public Task<ImagePullState> StartImagePullAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default) =>
        PostJsonAsync<PullImageRequest, ImagePullState>("api/v1/images/pulls", new PullImageRequest(reference, allTags), cancellationToken);

    public Task<ImagePullLog> GetImagePullLogAsync(string image, CancellationToken cancellationToken = default) =>
        GetAsync<ImagePullLog>($"api/v1/images/pulls/log?image={Escape(image)}", cancellationToken);

    public Task<CancelImagePullResult> CancelImagePullAsync(string image, CancellationToken cancellationToken = default) =>
        PostJsonAsync<PullImageRequest, CancelImagePullResult>("api/v1/images/pulls/cancel", new PullImageRequest(image), cancellationToken);

    /// <summary>Forgets a pull that failed or was cancelled, so its row goes for every client; 409 while it is still running.</summary>
    public Task DismissImagePullAsync(string image, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/images/pulls?image={Escape(image)}", cancellationToken);

    public Task<BuildJob> StartImageBuildAsync(BuildImageRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<BuildImageRequest, BuildJob>("api/v1/images/build", request, cancellationToken);

    public Task<BuildJob> GetImageBuildAsync(string job, CancellationToken cancellationToken = default) =>
        GetAsync<BuildJob>($"api/v1/images/build/{Escape(job)}", cancellationToken);

    public Task CancelImageBuildAsync(string job, CancellationToken cancellationToken = default) =>
        PostAsync($"api/v1/images/build/{Escape(job)}/cancel", cancellationToken);

    /// <summary>Uploads an image archive for <c>wslc import</c>; <paramref name="image"/> names the result.</summary>
    public async Task ImportImageAsync(Stream archive, string fileName, string image = "", CancellationToken cancellationToken = default)
    {
        using var response = await PostFileAsync($"api/v1/images/import?image={Escape(image)}", archive, fileName, cancellationToken);
    }

    /// <summary>Uploads an image archive for <c>wslc load</c>.</summary>
    public async Task<LoadImagesResult> LoadImagesAsync(Stream archive, string fileName, CancellationToken cancellationToken = default)
    {
        using var response = await PostFileAsync("api/v1/images/load", archive, fileName, cancellationToken);
        return await response.Content.ReadFromJsonAsync<LoadImagesResult>(cancellationToken)
            ?? throw new AgentApiException((int)response.StatusCode, "Empty answer");
    }

    public Task PushImageAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default) =>
        PostJsonAsync("api/v1/images/push", new PushImageRequest(reference, allTags), cancellationToken);

    /// <summary>Tags the image as a version in a registry and starts its push (and <c>latest</c>'s); returns the pushes started.</summary>
    public Task<IReadOnlyList<ImagePullState>> PublishImageAsync(PublishImageRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<PublishImageRequest, IReadOnlyList<ImagePullState>>("api/v1/images/publish", request, cancellationToken);

    public Task<IReadOnlyList<ImagePullState>> GetImagePushesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ImagePullState>>("api/v1/images/pushes", cancellationToken);

    public Task<ImagePullLog> GetImagePushLogAsync(string image, CancellationToken cancellationToken = default) =>
        GetAsync<ImagePullLog>($"api/v1/images/pushes/log?image={Escape(image)}", cancellationToken);

    public Task<CancelImagePullResult> CancelImagePushAsync(string image, CancellationToken cancellationToken = default) =>
        PostJsonAsync<PushImageRequest, CancelImagePullResult>("api/v1/images/pushes/cancel", new PushImageRequest(image), cancellationToken);

    /// <summary>A tar archive written on the agent's machine; a bare name goes to that user's Downloads.</summary>
    public Task<SavedImage> SaveImageAsync(string reference, string output, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SaveImageRequest, SavedImage>("api/v1/images/save", new SaveImageRequest(reference, output), cancellationToken);

    /// <summary>A temporary container of the image whose files the container files calls browse; close it with <see cref="CloseImageFilesAsync"/>.</summary>
    public Task<FilesSession> OpenImageFilesAsync(string reference, CancellationToken cancellationToken = default) =>
        PostJsonAsync<ImageFilesRequest, FilesSession>("api/v1/images/files-session", new ImageFilesRequest(reference), cancellationToken);

    public Task CloseImageFilesAsync(string container, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/images/files-session/{Escape(container)}", cancellationToken);

    public Task PruneImagesAsync(CancellationToken cancellationToken = default) =>
        PostAsync("api/v1/images/prune", cancellationToken);

    // Volumes

    public async Task<VolumeListResponse> GetVolumesAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<VolumeListResponse>("api/v1/volumes", cancellationToken)
        ?? new VolumeListResponse([], 0, 0, 0);

    public Task CreateVolumeAsync(CreateVolumeRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync("api/v1/volumes", request, cancellationToken);

    public Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/volumes/{Escape(name)}", cancellationToken);

    public Task<VolumeInspect> GetVolumeInspectAsync(string name, CancellationToken cancellationToken = default) =>
        GetAsync<VolumeInspect>($"api/v1/volumes/{Escape(name)}/inspect", cancellationToken);

    public Task<VolumeUsers> GetVolumeUsersAsync(string name, CancellationToken cancellationToken = default) =>
        GetAsync<VolumeUsers>($"api/v1/volumes/{Escape(name)}/containers", cancellationToken);

    /// <summary>A helper container with the volume mounted at the session's root; close it with <see cref="CloseVolumeFilesAsync"/>.</summary>
    public Task<FilesSession> OpenVolumeFilesAsync(string name, CancellationToken cancellationToken = default) =>
        PostJsonAsync<object?, FilesSession>($"api/v1/volumes/{Escape(name)}/files-session", null, cancellationToken);

    public Task CloseVolumeFilesAsync(string name, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/volumes/{Escape(name)}/files-session", cancellationToken);

    public Task PruneVolumesAsync(CancellationToken cancellationToken = default) =>
        PostAsync("api/v1/volumes/prune", cancellationToken);

    // Networks

    public async Task<NetworkListResponse> GetNetworksAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<NetworkListResponse>("api/v1/networks", cancellationToken)
        ?? new NetworkListResponse([], 0, 0, 0);

    public Task CreateNetworkAsync(CreateNetworkRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync("api/v1/networks", request, cancellationToken);

    public Task ConnectNetworkAsync(string network, string container, string ip = "", CancellationToken cancellationToken = default) =>
        PostJsonAsync($"api/v1/networks/{Escape(network)}/connect", new NetworkContainerRequest(container, ip), cancellationToken);

    public Task DisconnectNetworkAsync(string network, string container, CancellationToken cancellationToken = default) =>
        PostJsonAsync($"api/v1/networks/{Escape(network)}/disconnect", new NetworkContainerRequest(container), cancellationToken);

    public Task RemoveNetworkAsync(string name, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/networks/{Escape(name)}", cancellationToken);

    public Task<NetworkTopology> GetNetworkTopologyAsync(CancellationToken cancellationToken = default) =>
        GetAsync<NetworkTopology>("api/v1/networks/topology", cancellationToken);

    public Task<NetworkDetails> GetNetworkDetailsAsync(string name, CancellationToken cancellationToken = default) =>
        GetAsync<NetworkDetails>($"api/v1/networks/{Escape(name)}/details", cancellationToken);

    public Task<RecreateNetworkResult> RecreateNetworkAsync(string source, CreateNetworkRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<CreateNetworkRequest, RecreateNetworkResult>($"api/v1/networks/{Escape(source)}/recreate", request, cancellationToken);

    public Task PruneNetworksAsync(CancellationToken cancellationToken = default) =>
        PostAsync("api/v1/networks/prune", cancellationToken);

    // Plumbing

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string Flag(bool value) => value ? "true" : "false";

    private Task PostAsync(string path, CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, path, cancellationToken);

    /// <summary>A file as the multipart <c>file</c> field; the caller reads and disposes the answer.</summary>
    private async Task<HttpResponseMessage> PostFileAsync(string path, Stream content, string fileName, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);
        form.Add(file, "file", fileName);
        var response = await http.PostAsync(path, form, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Body-less request; a problem-details error becomes an <see cref="AgentApiException"/> carrying its detail.</summary>
    private async Task SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task PostJsonAsync<TBody>(string path, TBody body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(path, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task PutJsonAsync<TBody>(string path, TBody body, CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync(path, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>A POST with a body and an answer; a problem-details error becomes an <see cref="AgentApiException"/>.</summary>
    private async Task<TResult> PostJsonAsync<TBody, TResult>(string path, TBody body, CancellationToken cancellationToken, HttpMethod? method = null)
    {
        using var response = method is null || method == HttpMethod.Post
            ? await http.PostAsJsonAsync(path, body, cancellationToken)
            : await http.PutAsJsonAsync(path, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResult>(cancellationToken)
            ?? throw new AgentApiException((int)response.StatusCode, "Empty answer");
    }

    /// <summary>A GET whose failure carries the problem detail (GetFromJsonAsync would only say the status).</summary>
    private Task<TResult> GetAsync<TResult>(string path, CancellationToken cancellationToken) =>
        ReadAsync<TResult>(HttpMethod.Get, path, cancellationToken);

    /// <summary>A DELETE that answers with a body, such as a count of what went.</summary>
    private Task<TResult> DeleteAsync<TResult>(string path, CancellationToken cancellationToken) =>
        ReadAsync<TResult>(HttpMethod.Delete, path, cancellationToken);

    /// <summary>A body-less request with an answer; a problem-details error becomes an <see cref="AgentApiException"/>.</summary>
    private async Task<TResult> ReadAsync<TResult>(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResult>(cancellationToken)
            ?? throw new AgentApiException((int)response.StatusCode, "Empty answer");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/html")
        {
            // An agent older than this client answers a route it does not know
            // with the application's page, not with 404 (a new Windows client on a 0.2.15 agent failed
            // with "'<' is an invalid start of a value" in Settings).
            throw new AgentApiException(404,
                "The agent does not know this request: it is older than this client. Update the agent.");
        }

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail;
        Dictionary<string, string>? fields = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(cancellationToken);
            detail = problem?.Detail ?? problem?.Title ?? response.ReasonPhrase ?? response.StatusCode.ToString();
            fields = problem?.Fields;
        }
        catch (Exception)
        {
            detail = response.ReasonPhrase ?? response.StatusCode.ToString();
        }

        throw new AgentApiException((int)response.StatusCode, detail, fields);
    }

    /// <summary>The agent's problem details: <c>fields</c> is the launch form's fields a failed launch is about.</summary>
    private sealed record ProblemDetailsBody(string? Title, string? Detail, Dictionary<string, string>? Fields);
}
