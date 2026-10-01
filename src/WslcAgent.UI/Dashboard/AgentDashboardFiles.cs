using Berpiztu.Dashboard.Storage;
using Microsoft.JSInterop;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The dashboard's files as the agent keeps them (<c>/dashboard/defaults</c>,
/// <c>/dashboard-v2/default/views</c> and <c>/me/dashboard-v2/…</c>,
/// docs/dashboard-defaults.md), for the dashboard's Tools. Objects' defaults
/// loaded, reset or settled are read again at once, so the next object dropped
/// is born with them; a file is handed to the user as the web UI downloads one.
/// </summary>
public sealed class AgentDashboardFiles(WslcAgentApi api, AgentObjectDefaults objectDefaults, IJSRuntime js) : IDashboardFiles
{
    private static readonly IReadOnlyList<DashboardViewName> Names =
        [.. DashboardViewNames.All.Select(view => new DashboardViewName(view, DashboardViewNames.Title(view)))];

    public Task<DashboardFilesStatus> StatusAsync(CancellationToken cancellationToken = default) =>
        AskAsync(async () => Status(await api.GetDashboardDefaultsAsync(cancellationToken)));

    public Task<DashboardFileInfo> InspectAsync(string file, CancellationToken cancellationToken = default) =>
        AskAsync(async () =>
        {
            var info = await api.InspectDefaultsFileAsync(file, cancellationToken);
            return new DashboardFileInfo(info.Kind == DefaultsKinds.Dashboard, info.Version, info.Revision, info.MinAgentVersion,
                info.From, info.Created, info.Views, info.Refusal, info.Missing);
        });

    public Task<DashboardFilesStatus> LoadObjectDefaultsAsync(string file, CancellationToken cancellationToken = default) =>
        ObjectsAsync(() => api.LoadObjectDefaultsAsync(file, cancellationToken), cancellationToken);

    public Task<DashboardFilesStatus> ResetObjectDefaultsAsync(CancellationToken cancellationToken = default) =>
        ObjectsAsync(() => api.ResetObjectDefaultsAsync(cancellationToken), cancellationToken);

    public Task<DashboardFilesStatus> ClearChangesAsync(CancellationToken cancellationToken = default) =>
        ObjectsAsync(() => api.ClearObjectDefaultChangesAsync(cancellationToken), cancellationToken);

    public Task<DashboardFilesStatus> SettleConflictsAsync(IReadOnlyList<DashboardConflictChoice> choices, CancellationToken cancellationToken = default) =>
        ObjectsAsync(() => api.SettleObjectDefaultConflictsAsync(
            choices.Select(choice => new ObjectDefaultChoice(choice.View, choice.Type, choice.KeepMine)), cancellationToken), cancellationToken);

    public Task<string> ExportObjectDefaultsAsync(CancellationToken cancellationToken = default) =>
        AskAsync(() => api.ExportObjectDefaultsAsync(cancellationToken));

    public Task<DashboardFilesStatus> LoadViewsAsync(IReadOnlyCollection<string> views, string file, CancellationToken cancellationToken = default) =>
        AskAsync(async () => Status(await api.ImportUserDashboardV2Async(views, file, cancellationToken)));

    public Task<DashboardFilesStatus> ResetViewsAsync(IReadOnlyCollection<string> views, CancellationToken cancellationToken = default) =>
        AskAsync(async () => Status(await api.ResetUserDashboardV2ViewsAsync(views, cancellationToken)));

    public Task<DashboardFilesStatus> ResetAllAsync(CancellationToken cancellationToken = default) =>
        AskAsync(async () => Status(await api.ResetUserDashboardV2ToReleaseAsync(cancellationToken)));

    public Task<IReadOnlyList<DashboardRevisionInfo>> HistoryAsync(CancellationToken cancellationToken = default) =>
        AskAsync<IReadOnlyList<DashboardRevisionInfo>>(async () => [.. (await api.GetUserDashboardV2HistoryAsync(cancellationToken)).Select(Revision)]);

    public Task<DashboardFilesStatus> RestoreAsync(int revision, CancellationToken cancellationToken = default) =>
        AskAsync(async () => Status(await api.RestoreUserDashboardV2RevisionAsync(revision, cancellationToken)));

    public Task<DashboardFilesStatus> LoadDefaultViewsAsync(IReadOnlyCollection<string> views, string file, CancellationToken cancellationToken = default) =>
        AskAsync(async () => Status(await api.LoadDefaultDashboardV2ViewsAsync(views, file, cancellationToken)));

    public Task<DashboardFilesStatus> ResetDefaultViewsAsync(IReadOnlyCollection<string> views, CancellationToken cancellationToken = default) =>
        AskAsync(async () => Status(await api.ResetDefaultDashboardV2ViewsAsync(views, cancellationToken)));

    public Task<string> ExportViewsAsync(IReadOnlyCollection<string> views, CancellationToken cancellationToken = default) =>
        AskAsync(() => api.ExportUserDashboardV2Async(views, cancellationToken));

    public Task SaveAsync(string name, string text, CancellationToken cancellationToken = default) =>
        AskAsync(async () =>
        {
            await js.InvokeVoidAsync("wslcAgent.download", cancellationToken, $"wslc-{name}", text, "application/json");
            return true;
        });

    /// <summary>A change to the objects' defaults, read again at once so the next object dropped is born with them.</summary>
    private Task<DashboardFilesStatus> ObjectsAsync(Func<Task<DashboardDefaultsStatus>> change, CancellationToken cancellationToken) =>
        AskAsync(async () =>
        {
            var status = await change();
            await objectDefaults.ReloadAsync(cancellationToken);
            return Status(status);
        });

    private static DashboardFilesStatus Status(DashboardDefaultsStatus status) =>
        new(State(status.ObjectDefaults), State(status.DashboardFile), Views(status.Dashboard), Names);

    private static DashboardFileState State(DefaultsFileState state) =>
        new(Version(state.Shipped)!, Version(state.Loaded), Version(state.Available), state.Newer, state.Refusal, state.AvailableViews,
            state.Changes, [.. state.Conflicts.Select(conflict => new DashboardConflict(conflict.View, conflict.Type))]);

    private static DashboardViewsState Views(DashboardState state) =>
        new(state.Current is { } current ? Revision(current) : null, state.ReleaseVersion, state.ReleaseSource,
            [.. state.Views.Select(view => new DashboardViewDiff(view.View, view.OwnDefault, view.OwnBasedOn,
                Differences(view.FromRelease), Differences(view.FromOwnDefault), Differences(view.OwnFromRelease)))]);

    private static IReadOnlyList<DashboardDifference> Differences(IReadOnlyList<DashboardChange> changes) =>
        [.. changes.Select(change => new DashboardDifference(change.Object, change.Change, change.Detail))];

    private static DashboardRevisionInfo Revision(DashboardRevision revision) =>
        new(revision.Id, revision.Version, revision.Revision, revision.Source, revision.Name, revision.Saved, revision.Note);

    private static DashboardFileVersion? Version(DefaultsFileVersion? version) =>
        version is null ? null : new DashboardFileVersion(version.Version, version.MinAgentVersion);

    /// <summary>The agent's answer; its refusal, or no answer at all, as the reason the Tools show.</summary>
    private static async Task<T> AskAsync<T>(Func<Task<T>> ask)
    {
        try
        {
            return await ask();
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException or JSException)
        {
            throw new DashboardFilesException(ex.Message, ex);
        }
    }
}
