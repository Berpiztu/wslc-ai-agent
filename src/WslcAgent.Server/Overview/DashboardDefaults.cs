using System.Text.Json;
using System.Text.Json.Nodes;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.ClientPackages;
using WslcAgent.Server.Resources;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The two files the agent ships besides itself — the objects' defaults and
/// the default dashboard — once they travel on their own
/// (docs/dashboard-defaults.md): which version this agent ships, which one the
/// user loaded, which one waits in the package folder, and loading one, never
/// on its own. The user's dashboard against the release and this agent's own
/// default, view by view, JSON against JSON; its history, any state of which
/// is brought back; and the dashboard carried to another agent. A file needing
/// a newer agent than this one is refused, and says so.
/// </summary>
public sealed class DashboardDefaults(
    DefaultsVersions versions,
    LoadedDefaults loaded,
    ObjectDefaultsStore objectDefaults,
    DashboardV2Store dashboard,
    DashboardHistory history,
    PackageFolders folders,
    ResourceRegistry resources,
    IAgentInfo info,
    ILogger<DashboardDefaults> logger)
{
    private const string PackageFolder = "package folder";

    /// <summary>This agent's version without the build metadata a development build adds (1.0.12+abc → 1.0.12).</summary>
    private string AgentVersion => info.Version.Split('+')[0];

    public DashboardDefaultsStatus Status() =>
        new(AgentVersion, folders.Folder, State(DefaultsKinds.ObjectDefaults), State(DefaultsKinds.Dashboard), Dashboard());

    /// <summary>What a file holds and whether this agent can load it; a refusal for text that is no defaults file.</summary>
    public DefaultsFileInfo Inspect(string text)
    {
        var file = DefaultsFile.Parse(text)
            ?? throw new DefaultsRefusedException("This is not a defaults file: it has no kind, version and content.");
        IReadOnlyList<string> views = file.Kind == DefaultsKinds.Dashboard ? StoredDashboardViews.Present(file.Content) : [];
        return new DefaultsFileInfo(file.Kind, file.Version, file.Revision, file.MinAgentVersion, file.From, file.Created, views, Refusal(file, file.Kind),
            DashboardSources.Missing(file.Content, file.Sources, LocalUid));
    }

    /// <summary>
    /// The objects' defaults of a file — the package folder's when
    /// <paramref name="text"/> is empty — the base from now on instead of those
    /// the agent ships, kept through updates until reset; the user's changes stay over it.
    /// </summary>
    public DashboardDefaultsStatus LoadObjectDefaults(string text)
    {
        if (objectDefaults.Writable)
        {
            throw new DefaultsRefusedException(
                "A development agent reads the objects' defaults of its repository; load a file on an installed agent.", StatusCodes.Status409Conflict);
        }

        var file = Checked(text, DefaultsKinds.ObjectDefaults);
        loaded.Write(file);
        logger.LogInformation("object defaults v{Version} loaded", file.Version);
        return Status();
    }

    /// <summary>The objects' defaults this agent ships, the base again; the user's changes stay over it.</summary>
    public DashboardDefaultsStatus ResetObjectDefaults()
    {
        loaded.Clear(DefaultsKinds.ObjectDefaults);
        logger.LogInformation("object defaults: the base is the shipped one again");
        return Status();
    }

    /// <summary>Every change the user made to the objects' defaults dropped, backed up first: the base alone again.</summary>
    public DashboardDefaultsStatus ClearObjectDefaultChanges()
    {
        objectDefaults.ClearChanges();
        return Status();
    }

    /// <summary>The conflicts between the user's changes and a newer base settled, kind by kind.</summary>
    public DashboardDefaultsStatus SettleObjectDefaultConflicts(IReadOnlyList<ObjectDefaultChoice> choices)
    {
        objectDefaults.Settle(choices);
        return Status();
    }

    /// <summary>
    /// The objects' defaults in use as a file another agent loads: the base
    /// with the user's changes over it, with the base's version — needing this
    /// agent when it carries changes made on it.
    /// </summary>
    public string ExportObjectDefaults()
    {
        var content = Parse(objectDefaults.Get())
            ?? throw new DefaultsRefusedException("This agent has no objects' defaults to export.", StatusCodes.Status404NotFound);
        var own = objectDefaults.Writable ? null : loaded.Read(DefaultsKinds.ObjectDefaults);
        var identity = own?.Identity ?? versions.For(DefaultsKinds.ObjectDefaults);
        var minimum = !objectDefaults.Writable && objectDefaults.ChangeCount > 0 ? AgentVersion : identity.MinAgentVersion;
        return new DefaultsFile(DefaultsKinds.ObjectDefaults, identity.Version, minimum, Environment.MachineName, Now(), content).Write();
    }

    /// <summary>
    /// The views named of a dashboard file — of the release, the installation's
    /// or the package folder's when newer, when <paramref name="text"/> is
    /// empty — put in place of the user's same views; every other view kept.
    /// The dashboard starts again from the file's version, as its revision 0.
    /// </summary>
    public DashboardDefaultsStatus ImportDashboard(string text, IReadOnlyCollection<string> views)
    {
        DashboardOrigin origin;
        JsonObject content;
        if (string.IsNullOrWhiteSpace(text))
        {
            (content, origin) = Release();
        }
        else
        {
            var file = Checked(text, DefaultsKinds.Dashboard);
            content = Here(file);
            origin = new DashboardOrigin(file.Version, DashboardHistory.Import, Named(file));
        }

        RequireViews(views, content, "the file holds");
        var current = Parse(dashboard.Get()) ?? [];
        dashboard.Set(StoredDashboardViews.Merge(current, content, views).ToJsonString(), origin, $"Loaded {Titles(views)}");
        logger.LogInformation("dashboard views loaded from v{Version} ({Source}): {Views}", origin.Version, origin.Source, string.Join(", ", views));
        return Status();
    }

    /// <summary>The views named of the user's dashboard put back to the default this agent has (its own views, else the shipped ones), as a new revision.</summary>
    public DashboardDefaultsStatus ResetDashboardViews(IReadOnlyCollection<string> views)
    {
        var defaults = Parse(dashboard.Default()) ?? [];
        RequireViews(views, defaults, "the default holds");
        var current = Parse(dashboard.Get()) ?? [];
        dashboard.Set(StoredDashboardViews.Merge(current, defaults, views).ToJsonString(), null, $"Reset {Titles(views)} to the default");
        logger.LogInformation("dashboard views reset to the default: {Views}", string.Join(", ", views));
        return Status();
    }

    /// <summary>The whole dashboard back to the release — every view as the installation (or a newer package folder file) has it — as its revision 0.</summary>
    public DashboardDefaultsStatus ResetDashboardToRelease()
    {
        var (content, origin) = Release();
        dashboard.Set(content.ToJsonString(), origin, $"Back to the {origin.Source} v{origin.Version}");
        logger.LogInformation("dashboard back to v{Version} ({Source})", origin.Version, origin.Source);
        return Status();
    }

    /// <summary>Every state the dashboard was saved in, the newest first.</summary>
    public IReadOnlyList<DashboardRevision> History() => history.List();

    /// <summary>A state the dashboard was saved in brought back, as it was and under its own name (v2.3), recorded as the newest.</summary>
    public DashboardDefaultsStatus RestoreRevision(int id)
    {
        var revision = history.List().FirstOrDefault(entry => entry.Id == id)
            ?? throw new DefaultsRefusedException($"There is no revision {id} in the history.", StatusCodes.Status404NotFound);
        var content = history.Content(id)
            ?? throw new DefaultsRefusedException($"Revision {id} cannot be read.", StatusCodes.Status404NotFound);
        dashboard.Set(content, new DashboardOrigin(revision.Version, revision.Source, revision.Name),
            $"Back to v{revision.Version}.{revision.Revision} of {revision.Saved}", revision.Revision);
        logger.LogInformation("dashboard back to v{Version}.{Revision}", revision.Version, revision.Revision);
        return Status();
    }

    /// <summary>
    /// The views named made this agent's own default: from a file, or — the
    /// body empty — the release's, which simply drops the agent's own where it
    /// is the installation's. The user's dashboard is not touched.
    /// </summary>
    public DashboardDefaultsStatus LoadDefaultViews(string text, IReadOnlyCollection<string> views)
    {
        if (dashboard.DefaultWritable)
        {
            throw new DefaultsRefusedException(
                "A development agent's default is its repository's: Save as default writes it.", StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            var (content, origin) = Release();
            if (origin.Source == DashboardHistory.Installation)
            {
                dashboard.ResetOwnDefault(views);
            }
            else
            {
                dashboard.SetOwnDefault(content, views, origin.Version);
            }
        }
        else
        {
            var file = Checked(text, DefaultsKinds.Dashboard);
            RequireViews(views, file.Content, "the file holds");
            dashboard.SetOwnDefault(Here(file), views, file.Version);
        }

        logger.LogInformation("default dashboard views of this agent loaded: {Views}", string.Join(", ", views));
        return Status();
    }

    /// <summary>The views named of this agent's default back to the shipped ones, its own dropped after a backup.</summary>
    public DashboardDefaultsStatus ResetDefaultViews(IReadOnlyCollection<string> views)
    {
        dashboard.ResetOwnDefault(views);
        logger.LogInformation("default dashboard views back to the shipped ones: {Views}", string.Join(", ", views));
        return Status();
    }

    /// <summary>The views named of the user's dashboard as a file another agent loads: its version and revision (v2.3), needing this agent.</summary>
    public string ExportDashboard(IReadOnlyCollection<string> views)
    {
        var current = Parse(dashboard.Get()) ?? [];
        var extract = StoredDashboardViews.Extract(current, views);
        if (StoredDashboardViews.Present(extract).Count == 0)
        {
            throw new DefaultsRefusedException("None of the views chosen holds an object: there is nothing to export.");
        }

        var revision = history.Current();
        return new DefaultsFile(DefaultsKinds.Dashboard, revision?.Version ?? dashboard.ShippedVersion, AgentVersion, Environment.MachineName,
            Now(), extract, revision?.Revision ?? 0, DashboardSources.Describe(extract, resources.Describe)).Write();
    }

    /// <summary>A dashboard file's content with its resources' uids changed for this agent's own (<see cref="DashboardSources"/>).</summary>
    private JsonObject Here(DefaultsFile file) => DashboardSources.Remap(file.Content, file.Sources, LocalUid);

    /// <summary>This agent's uid of a resource, by its kind and name; 0 when it does not have it.</summary>
    private int LocalUid(string kind, string name) => resources.UidOf(kind, "", name);

    /// <summary>The user's dashboard against the release and this agent's own default, view by view.</summary>
    private DashboardState Dashboard()
    {
        var (release, origin) = Release();
        var current = Parse(dashboard.Get()) ?? [];
        var own = dashboard.OwnDefault();
        var ownViews = dashboard.OwnDefaultViews;
        return new DashboardState(history.Current(), origin.Version, origin.Source,
        [
            .. DashboardViewNames.All.Select(view =>
            {
                var hasOwn = ownViews.ContainsKey(view);
                return new DashboardViewState(view, hasOwn, ownViews.GetValueOrDefault(view),
                    DashboardDiff.Compare(release, current, view),
                    hasOwn ? DashboardDiff.Compare(own, current, view) : [],
                    hasOwn ? DashboardDiff.Compare(release, own, view) : []);
            }),
        ]);
    }

    /// <summary>
    /// The release the dashboard is compared with and loaded from: the default
    /// this agent ships, or the package folder's when it is newer and this agent
    /// loads it.
    /// </summary>
    private (JsonObject Content, DashboardOrigin Origin) Release()
    {
        var shipped = dashboard.ShippedVersion;
        if (Waiting(DefaultsKinds.Dashboard) is { } file && file.Version > shipped && Refusal(file, DefaultsKinds.Dashboard).Length == 0)
        {
            return (file.Content, new DashboardOrigin(file.Version, PackageFolder, DefaultsFile.FileName(DefaultsKinds.Dashboard)));
        }

        return (Parse(dashboard.ShippedDefault()) ?? [], new DashboardOrigin(shipped, DashboardHistory.Installation, ""));
    }

    private DefaultsFileState State(string kind)
    {
        var shipped = versions.For(kind);
        var taken = loaded.Read(kind)?.Identity;
        var waiting = Waiting(kind);
        var inUse = taken is not null && taken.Version > shipped.Version ? taken.Version : shipped.Version;
        var objects = kind == DefaultsKinds.ObjectDefaults && !objectDefaults.Writable;
        return new DefaultsFileState(kind, shipped, taken, waiting?.Identity,
            waiting is not null && waiting.Version > inUse, waiting is null ? "" : Refusal(waiting, kind),
            waiting?.Kind == DefaultsKinds.Dashboard ? StoredDashboardViews.Present(waiting.Content) : [],
            objects ? objectDefaults.ChangeCount : 0,
            objects ? objectDefaults.Conflicts() : []);
    }

    /// <summary>A choice of views that are all among those <paramref name="dashboardHolds"/> holds, or the refusal that lists them.</summary>
    private static void RequireViews(IReadOnlyCollection<string> views, JsonObject dashboardHolds, string what)
    {
        var present = StoredDashboardViews.Present(dashboardHolds);
        if (views.Count == 0 || views.Any(view => !present.Contains(view)))
        {
            throw new DefaultsRefusedException(
                $"Choose views {what}: {(present.Count == 0 ? "none" : string.Join(", ", present))}.");
        }
    }

    /// <summary>The kind's file in the package folder; null when there is none, or it is no defaults file.</summary>
    private DefaultsFile? Waiting(string kind) =>
        folders.Locate(DefaultsFile.FileName(kind)) is { } path ? DefaultsFile.Parse(ShippedFile.ReadFile(path)) : null;

    /// <summary>The file of <paramref name="text"/>, or the package folder's when it is empty, once this agent can load it.</summary>
    private DefaultsFile Checked(string text, string kind)
    {
        var file = string.IsNullOrWhiteSpace(text)
            ? Waiting(kind) ?? throw new DefaultsRefusedException($"There is no {DefaultsFile.FileName(kind)} in the package folder ({folders.Folder}).", StatusCodes.Status404NotFound)
            : DefaultsFile.Parse(text) ?? throw new DefaultsRefusedException("This is not a defaults file: it has no kind, version and content.");
        if (Refusal(file, kind) is { Length: > 0 } refusal)
        {
            throw new DefaultsRefusedException(refusal, StatusCodes.Status409Conflict);
        }

        return file;
    }

    /// <summary>Why this agent cannot load the file as <paramref name="kind"/>; empty when it can.</summary>
    private string Refusal(DefaultsFile file, string kind)
    {
        if (file.Kind != kind)
        {
            return $"This file holds {Named(file.Kind)}, not {Named(kind)}.";
        }

        return ClientPackageInfo.CompareDisplayVersion(file.MinAgentVersion, AgentVersion) > 0
            ? $"This file needs agent {file.MinAgentVersion} or later; this agent is {AgentVersion}. Update the agent first."
            : "";
    }

    private static string Named(string kind) => kind == DefaultsKinds.Dashboard ? "a dashboard" : "the objects' defaults";

    /// <summary>An imported file as the history names it: where it was exported and its revision, or the release it is.</summary>
    private static string Named(DefaultsFile file) =>
        file.From.Length > 0 ? $"export of v{file.Version}.{file.Revision} from {file.From}" : $"file v{file.Version}";

    private static string Titles(IEnumerable<string> views) => string.Join(", ", views.Select(DashboardViewNames.Title));

    private static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");

    private static JsonObject? Parse(string? text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A defaults file this agent will not load or write, with the reason the user is shown.</summary>
public sealed class DefaultsRefusedException(string message, int status = StatusCodes.Status400BadRequest) : Exception(message)
{
    public int Status { get; } = status;
}
